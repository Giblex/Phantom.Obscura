using System;
using System.Buffers.Binary;
using System.Buffers;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using GiblexVault.Security.ZK.Models;
using GiblexVault.Security.ZK.Primitives;
using GiblexVault.Security.ZK.Wrapping;
using NSec.Cryptography;

namespace GiblexVault.Security.ZK
{
    public static class VaultFileZk
    {
        private sealed record HeaderDoc(string Type, string Version, CipherSuite Suite, KdfParams Kdf, byte[] WrappedDek, string? Note);

        // File-to-file compatibility API - delegates to stream-based API
        public static async Task EncryptAsync(string inputPath, string outputPath, byte[] masterKey, EngineOptions options, string? note = null)
        {
            await using var fin = File.OpenRead(inputPath);
            await EncryptAsync(fin, outputPath, masterKey, options, note).ConfigureAwait(false);
        }

        // Stream-based encrypt: reads plaintext from inputStream and writes encrypted file to outputPath
        public static async Task EncryptAsync(Stream inputStream, string outputPath, byte[] masterKey, EngineOptions options, string? note = null)
        {
            var salt = new byte[32];
            RandomNumberGenerator.Fill(salt);
            var kdf = new KdfParams { Kdf = "argon2id", Ops = options.ArgonOpsLimit, MemMiB = options.ArgonMemMiB, Parallelism = options.ArgonParallelism, Salt = salt };

            var kek = Hkdf.Sha256(masterKey, salt, Encoding.UTF8.GetBytes("kek"));
            var dek = new byte[32];
            RandomNumberGenerator.Fill(dek);

            var suite = options.Suite;
            var aadObj = new AadPreview("GV-ZKF", "1", suite.ToString());
            var aadPreview = JsonSerializer.SerializeToUtf8Bytes(aadObj, GvZkJsonContext.Default.AadPreview);
            var wrappedDek = KeyWrap.WrapAead(suite, kek, dek, aadPreview);
            var headerDto = new HeaderDocDto("GV-ZKF", "1", suite, kdf, wrappedDek, note);
            var header = JsonSerializer.SerializeToUtf8Bytes(headerDto, GvZkJsonContext.Default.HeaderDocDto);

            await using var fout = File.Create(outputPath);
            var magic = new byte[] { 0x47, 0x56, 0x2D, 0x5A, 0x4B, 0x46, 0x01, 0x00 };
            await fout.WriteAsync(magic).ConfigureAwait(false);
            byte[] l = ArrayPool<byte>.Shared.Rent(4);
            try
            {
                BinaryPrimitives.WriteUInt32LittleEndian(l, (uint)header.Length);
                await fout.WriteAsync(l, 0, 4).ConfigureAwait(false);
                await fout.WriteAsync(header).ConfigureAwait(false);

                var ns = Aead.GetSuite(suite).NonceSize;
                var buf = ArrayPool<byte>.Shared.Rent(options.ChunkSizeBytes);

                // Create reusable cipher instance for the entire stream
                IDisposable? cipherDisposable = null;
                AesGcm? aesGcm = null;
                Key? xChaChaKey = null;

                if (suite == CipherSuite.XChaCha20Poly1305)
                {
                    xChaChaKey = Aead.CreateXChaChaKey(dek);
                    cipherDisposable = xChaChaKey;
                }
                else
                {
                    aesGcm = Aead.CreateAesGcm(dek);
                    cipherDisposable = aesGcm;
                }

                try
                {
                    int r;
                    while ((r = await inputStream.ReadAsync(buf, 0, options.ChunkSizeBytes).ConfigureAwait(false)) > 0)
                    {
                        var nonce = new byte[ns];
                        RandomNumberGenerator.Fill(nonce);

                        // Create an exact-sized chunk for the AEAD API which expects an exact-length array
                        var chunk = new byte[r];
                        Buffer.BlockCopy(buf, 0, chunk, 0, r);

                        byte[] ct;
                        if (suite == CipherSuite.XChaCha20Poly1305)
                            ct = Aead.EncryptWithKey(xChaChaKey!, nonce, header, chunk);
                        else
                            ct = Aead.EncryptWithAesGcm(aesGcm!, nonce, header, chunk);

                        await fout.WriteAsync(nonce, 0, nonce.Length).ConfigureAwait(false);
                        BinaryPrimitives.WriteUInt32LittleEndian(l, (uint)ct.Length);
                        await fout.WriteAsync(l, 0, 4).ConfigureAwait(false);
                        await fout.WriteAsync(ct, 0, ct.Length).ConfigureAwait(false);

                        // Zero sensitive chunk copy
                        CryptographicOperations.ZeroMemory(chunk);
                    }
                }
                finally
                {
                    cipherDisposable?.Dispose();
                    // Zero and return the large read buffer
                    CryptographicOperations.ZeroMemory(buf.AsSpan(0, options.ChunkSizeBytes));
                    ArrayPool<byte>.Shared.Return(buf);
                }
            }
            finally
            {
                // Return the small length buffer (zero it first)
                l[0] = l[1] = l[2] = l[3] = 0;
                ArrayPool<byte>.Shared.Return(l);
            }
            CryptographicOperations.ZeroMemory(kek);
            CryptographicOperations.ZeroMemory(dek);
        }

        // File-to-file decrypt delegates to stream implementation
        public static async Task DecryptAsync(string inputPath, string outputPath, byte[] masterKey, EngineOptions options)
        {
            await using var fin = File.OpenRead(inputPath);
            await using var fout = File.Create(outputPath);
            await DecryptToStreamAsync(fin, fout, masterKey, options).ConfigureAwait(false);
        }

        // Decrypt to byte[] (useful for in-memory viewing)
        public static async Task<byte[]> DecryptToArrayAsync(string inputPath, byte[] masterKey, EngineOptions options)
        {
            await using var fin = File.OpenRead(inputPath);
            await using var ms = new MemoryStream();
            await DecryptToStreamAsync(fin, ms, masterKey, options).ConfigureAwait(false);
            return ms.ToArray();
        }

        // Core decrypt implementation that writes plaintext to the provided output stream
        public static async Task DecryptToStreamAsync(Stream inputStream, Stream outputStream, byte[] masterKey, EngineOptions options)
        {
            var magic = ArrayPool<byte>.Shared.Rent(8);
            var l = ArrayPool<byte>.Shared.Rent(4);
            try
            {
                if (await inputStream.ReadAsync(magic, 0, 8).ConfigureAwait(false) != 8 || magic[0] != 0x47)
                    throw new InvalidOperationException("Bad magic");

                if (await inputStream.ReadAsync(l, 0, 4).ConfigureAwait(false) != 4)
                    throw new InvalidOperationException("Bad header length");

                var hlen = (int)BinaryPrimitives.ReadUInt32LittleEndian(l);
                var header = new byte[hlen];
                if (await inputStream.ReadAsync(header, 0, hlen).ConfigureAwait(false) != hlen)
                    throw new InvalidOperationException("Bad header");

                var docDto = JsonSerializer.Deserialize<HeaderDocDto>(header, GvZkJsonContext.Default.HeaderDocDto)!;
                var doc = new HeaderDoc(docDto.Type, docDto.Version, docDto.Suite, docDto.Kdf, docDto.WrappedDek, docDto.Note);
                var kek = Hkdf.Sha256(masterKey, doc.Kdf.Salt, Encoding.UTF8.GetBytes("kek"));
                var aadObj2 = new AadPreview("GV-ZKF", "1", doc.Suite.ToString());
                var aadBytes2 = JsonSerializer.SerializeToUtf8Bytes(aadObj2, GvZkJsonContext.Default.AadPreview);
                var dek = KeyWrap.UnwrapAead(doc.Suite, kek, doc.WrappedDek, aadBytes2);

                var ns = Aead.GetSuite(doc.Suite).NonceSize;

                // Create reusable cipher instance for the entire stream
                IDisposable? cipherDisposable = null;
                AesGcm? aesGcm = null;
                Key? xChaChaKey = null;

                if (doc.Suite == CipherSuite.XChaCha20Poly1305)
                {
                    xChaChaKey = Aead.CreateXChaChaKey(dek);
                    cipherDisposable = xChaChaKey;
                }
                else
                {
                    aesGcm = Aead.CreateAesGcm(dek);
                    cipherDisposable = aesGcm;
                }

                try
                {
                    while (true)
                    {
                        var nonce = new byte[ns];
                        var rr = await inputStream.ReadAsync(nonce, 0, ns).ConfigureAwait(false);
                        if (rr == 0) break;
                        if (rr != ns) throw new InvalidOperationException("Truncated nonce");

                        if (await inputStream.ReadAsync(l, 0, 4).ConfigureAwait(false) != 4)
                            throw new InvalidOperationException("Truncated length");

                        var clen = (int)BinaryPrimitives.ReadUInt32LittleEndian(l);
                        var ct = new byte[clen];
                        if (await inputStream.ReadAsync(ct, 0, clen).ConfigureAwait(false) != clen)
                            throw new InvalidOperationException("Truncated chunk");

                        byte[] plain;
                        if (doc.Suite == CipherSuite.XChaCha20Poly1305)
                            plain = Aead.DecryptWithKey(xChaChaKey!, nonce, header, ct);
                        else
                            plain = Aead.DecryptWithAesGcm(aesGcm!, nonce, header, ct);

                        await outputStream.WriteAsync(plain, 0, plain.Length).ConfigureAwait(false);
                        CryptographicOperations.ZeroMemory(plain);
                    }
                }
                finally
                {
                    cipherDisposable?.Dispose();
                }

                CryptographicOperations.ZeroMemory(kek);
                CryptographicOperations.ZeroMemory(dek);
            }
            finally
            {
                // Zero and return rented arrays
                CryptographicOperations.ZeroMemory(magic.AsSpan(0, 8));
                ArrayPool<byte>.Shared.Return(magic);
                CryptographicOperations.ZeroMemory(l.AsSpan(0, 4));
                ArrayPool<byte>.Shared.Return(l);
            }
        }
    }
}


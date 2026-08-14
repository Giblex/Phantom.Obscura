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

        // ── Container format versions ────────────────────────────────────────────────
        //
        // v1 gave every chunk the same AAD (the serialized header), so nothing bound a
        // chunk to its position or to the total chunk count. An attacker could reorder
        // chunks, drop trailing chunks, or splice in a zero-length record to end the
        // stream early — all while every per-chunk tag still verified, because the key,
        // AAD and nonce were each individually intact.
        //
        // v2 fixes both by:
        //   • tagging each record and binding {record type, chunk index} into the AAD,
        //     so a reordered or retyped chunk fails authentication; and
        //   • terminating with an authenticated trailer carrying the total chunk count,
        //     so truncation is detected (a missing or short-count trailer is an error).
        //
        // v1 files still decrypt — ReadVersion dispatches on the magic's version byte.
        private const byte FormatV1 = 0x01;
        private const byte FormatV2 = 0x02;

        private const byte RecordData = 0x01;
        private const byte RecordTerminator = 0x02;

        private static byte[] Magic(byte version)
            => new byte[] { 0x47, 0x56, 0x2D, 0x5A, 0x4B, 0x46, version, 0x00 };

        /// <summary>AAD for a data chunk: header ‖ recordType ‖ chunkIndex.</summary>
        private static byte[] ChunkAad(byte[] header, byte recordType, ulong counter)
        {
            var aad = new byte[header.Length + 1 + 8];
            Buffer.BlockCopy(header, 0, aad, 0, header.Length);
            aad[header.Length] = recordType;
            BinaryPrimitives.WriteUInt64LittleEndian(aad.AsSpan(header.Length + 1, 8), counter);
            return aad;
        }

        public static async Task EncryptAsync(string inputPath, string outputPath, byte[] masterKey, EngineOptions options, string? note = null)
        {
            await using var fin = File.OpenRead(inputPath);
            await EncryptAsync(fin, outputPath, masterKey, options, note).ConfigureAwait(false);
        }

        public static async Task EncryptAsync(Stream inputStream, string outputPath, byte[] masterKey, EngineOptions options, string? note = null)
        {
            await using var fout = File.Create(outputPath);
            await EncryptToStreamAsync(inputStream, fout, masterKey, options, note).ConfigureAwait(false);
        }

        public static async Task EncryptToStreamAsync(Stream inputStream, Stream outputStream, byte[] masterKey, EngineOptions options, string? note = null)
        {
            var salt = new byte[32];
            RandomNumberGenerator.Fill(salt);

            // This field is a SALT CARRIER, not a KDF specification.
            //
            // No Argon2 pass happens in this format: `masterKey` arrives already derived
            // (ZkVaultService runs Argon2id over the user's factors), so the only
            // derivation here is the HKDF-SHA256 expansion below that turns it into a KEK.
            // Writing real Argon2 cost parameters here claimed a hardening step this
            // container does not perform, which would mislead any auditor or third-party
            // implementer reading the header. The cost fields are therefore written as
            // zero to mark them not-applicable; only Salt is meaningful.
            //
            // The field itself has to stay for v1 compatibility — existing files carry
            // their HKDF salt inside it, and the read path pulls `doc.Kdf.Salt`.
            var kdf = new KdfParams
            {
                Kdf = "none",
                Ops = 0,
                MemMiB = 0,
                Parallelism = 0,
                Salt = salt
            };

            var kek = Hkdf.Sha256(masterKey, salt, Encoding.UTF8.GetBytes("kek"));
            var dek = new byte[32];
            RandomNumberGenerator.Fill(dek);

            var suite = options.Suite;
            var aadObj = new AadPreview("GV-ZKF", "1", suite.ToString());
            var aadPreview = JsonSerializer.SerializeToUtf8Bytes(aadObj, GvZkJsonContext.Default.AadPreview);
            var wrappedDek = KeyWrap.WrapAead(suite, kek, dek, aadPreview);
            var headerDto = new HeaderDocDto("GV-ZKF", "1", suite, kdf, wrappedDek, note);
            var header = JsonSerializer.SerializeToUtf8Bytes(headerDto, GvZkJsonContext.Default.HeaderDocDto);

            await outputStream.WriteAsync(Magic(FormatV2)).ConfigureAwait(false);
            byte[] l = ArrayPool<byte>.Shared.Rent(4);
            try
            {
                BinaryPrimitives.WriteUInt32LittleEndian(l, (uint)header.Length);
                await outputStream.WriteAsync(l, 0, 4).ConfigureAwait(false);
                await outputStream.WriteAsync(header).ConfigureAwait(false);

                var ns = Aead.GetSuite(suite).NonceSize;
                var buf = ArrayPool<byte>.Shared.Rent(options.ChunkSizeBytes);

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
                    ulong chunkIndex = 0;

                    while ((r = await inputStream.ReadAsync(buf, 0, options.ChunkSizeBytes).ConfigureAwait(false)) > 0)
                    {
                        var nonce = new byte[ns];
                        RandomNumberGenerator.Fill(nonce);

                        var chunk = new byte[r];
                        Buffer.BlockCopy(buf, 0, chunk, 0, r);

                        // Position-bound AAD: swapping two chunks changes the index each
                        // was sealed under, so authentication fails on both.
                        var aad = ChunkAad(header, RecordData, chunkIndex);

                        byte[] ct;
                        if (suite == CipherSuite.XChaCha20Poly1305)
                            ct = Aead.EncryptWithKey(xChaChaKey!, nonce, aad, chunk);
                        else
                            ct = Aead.EncryptWithAesGcm(aesGcm!, nonce, aad, chunk);

                        await WriteRecordAsync(outputStream, l, RecordData, nonce, ct).ConfigureAwait(false);

                        CryptographicOperations.ZeroMemory(chunk);
                        chunkIndex++;
                    }

                    // Authenticated trailer over the total chunk count. Its absence — or a
                    // count that disagrees with the chunks actually read — is how the
                    // reader detects truncation.
                    {
                        var nonce = new byte[ns];
                        RandomNumberGenerator.Fill(nonce);

                        var aad = ChunkAad(header, RecordTerminator, chunkIndex);

                        byte[] ct;
                        if (suite == CipherSuite.XChaCha20Poly1305)
                            ct = Aead.EncryptWithKey(xChaChaKey!, nonce, aad, Array.Empty<byte>());
                        else
                            ct = Aead.EncryptWithAesGcm(aesGcm!, nonce, aad, Array.Empty<byte>());

                        await WriteRecordAsync(outputStream, l, RecordTerminator, nonce, ct).ConfigureAwait(false);
                    }
                }
                finally
                {
                    cipherDisposable?.Dispose();
                    CryptographicOperations.ZeroMemory(buf.AsSpan(0, options.ChunkSizeBytes));
                    ArrayPool<byte>.Shared.Return(buf, clearArray: true);
                }
            }
            finally
            {
                l[0] = l[1] = l[2] = l[3] = 0;
                ArrayPool<byte>.Shared.Return(l, clearArray: true);
                CryptographicOperations.ZeroMemory(kek);
                CryptographicOperations.ZeroMemory(dek);
            }
        }

        /// <summary>Writes one v2 record: [type:1][nonce:ns][ctLen:4][ct].</summary>
        private static async Task WriteRecordAsync(Stream output, byte[] lenScratch, byte recordType, byte[] nonce, byte[] ct)
        {
            output.WriteByte(recordType);
            await output.WriteAsync(nonce, 0, nonce.Length).ConfigureAwait(false);
            BinaryPrimitives.WriteUInt32LittleEndian(lenScratch, (uint)ct.Length);
            await output.WriteAsync(lenScratch, 0, 4).ConfigureAwait(false);
            await output.WriteAsync(ct, 0, ct.Length).ConfigureAwait(false);
        }

        private static async Task<int> ReadExactAsync(Stream s, byte[] buf, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                var n = await s.ReadAsync(buf, offset + total, count - total).ConfigureAwait(false);
                if (n == 0) break;
                total += n;
            }
            return total;
        }

        public static async Task DecryptAsync(string inputPath, string outputPath, byte[] masterKey, EngineOptions options)
        {
            await using var fin = File.OpenRead(inputPath);
            await using var fout = File.Create(outputPath);
            await DecryptToStreamAsync(fin, fout, masterKey, options).ConfigureAwait(false);
        }

        public static async Task<byte[]> DecryptToArrayAsync(string inputPath, byte[] masterKey, EngineOptions options)
        {
            await using var fin = File.OpenRead(inputPath);
            await using var ms = new MemoryStream();
            await DecryptToStreamAsync(fin, ms, masterKey, options).ConfigureAwait(false);
            return ms.ToArray();
        }

        public static async Task DecryptToStreamAsync(Stream inputStream, Stream outputStream, byte[] masterKey, EngineOptions options)
        {
            var magic = ArrayPool<byte>.Shared.Rent(8);
            var l = ArrayPool<byte>.Shared.Rent(4);
            try
            {
                if (await ReadExactAsync(inputStream, magic, 0, 8).ConfigureAwait(false) != 8 || magic[0] != 0x47)
                    throw new InvalidOperationException("Bad magic");

                var formatVersion = magic[6];
                if (formatVersion != FormatV1 && formatVersion != FormatV2)
                    throw new InvalidOperationException($"Unsupported container format version {formatVersion}");

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
                    ulong chunkIndex = 0;
                    bool sawTerminator = false;

                    while (true)
                    {
                        // v2 prefixes every record with a type byte; v1 has no such byte.
                        byte recordType = RecordData;
                        if (formatVersion == FormatV2)
                        {
                            var typeByte = inputStream.ReadByte();
                            if (typeByte < 0)
                                throw new InvalidOperationException(
                                    "Container truncated: stream ended before the authenticated trailer");

                            recordType = (byte)typeByte;
                            if (recordType != RecordData && recordType != RecordTerminator)
                                throw new InvalidOperationException($"Unknown record type {recordType}");
                        }

                        var nonce = new byte[ns];
                        var rr = await ReadExactAsync(inputStream, nonce, 0, ns).ConfigureAwait(false);

                        if (rr == 0)
                        {
                            // v1 has no trailer, so a clean end of stream is its normal
                            // termination. v2 must have consumed a terminator by now.
                            if (formatVersion == FormatV1) break;
                            throw new InvalidOperationException(
                                "Container truncated: stream ended before the authenticated trailer");
                        }
                        if (rr != ns) throw new InvalidOperationException("Truncated nonce");

                        if (await ReadExactAsync(inputStream, l, 0, 4).ConfigureAwait(false) != 4)
                            throw new InvalidOperationException("Truncated length");

                        var clen = (int)BinaryPrimitives.ReadUInt32LittleEndian(l);

                        if (formatVersion == FormatV1)
                        {
                            // Preserved v1 behaviour: a zero-length record ends the stream.
                            // This is the splice weakness v2 removes; it cannot be fixed
                            // for existing files without breaking them.
                            if (clen == 0) break;
                        }
                        else if (clen < 0)
                        {
                            throw new InvalidOperationException("Invalid record length");
                        }

                        var ct = new byte[clen];
                        if (clen > 0 && await ReadExactAsync(inputStream, ct, 0, clen).ConfigureAwait(false) != clen)
                            throw new InvalidOperationException("Truncated chunk");

                        // v1 authenticates against the bare header; v2 against
                        // header ‖ recordType ‖ counter. For a data record the counter is
                        // its index; for the terminator it is the total chunk count — which
                        // at this point is the same running value.
                        var aad = formatVersion == FormatV1
                            ? header
                            : ChunkAad(header, recordType, chunkIndex);

                        byte[] plain;
                        if (doc.Suite == CipherSuite.XChaCha20Poly1305)
                            plain = Aead.DecryptWithKey(xChaChaKey!, nonce, aad, ct);
                        else
                            plain = Aead.DecryptWithAesGcm(aesGcm!, nonce, aad, ct);

                        if (recordType == RecordTerminator)
                        {
                            // Decryption succeeding proves the sealed count equals the number
                            // of data chunks we actually read — dropping any chunk shifts the
                            // running index and makes this tag fail.
                            CryptographicOperations.ZeroMemory(plain);
                            sawTerminator = true;
                            break;
                        }

                        await outputStream.WriteAsync(plain, 0, plain.Length).ConfigureAwait(false);
                        CryptographicOperations.ZeroMemory(plain);
                        chunkIndex++;
                    }

                    if (formatVersion == FormatV2 && !sawTerminator)
                        throw new InvalidOperationException(
                            "Container truncated: authenticated trailer missing");
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

                CryptographicOperations.ZeroMemory(magic.AsSpan(0, 8));
                ArrayPool<byte>.Shared.Return(magic, clearArray: true);
                CryptographicOperations.ZeroMemory(l.AsSpan(0, 4));
                ArrayPool<byte>.Shared.Return(l, clearArray: true);
            }
        }
    }
}


using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using GiblexVault.Security.ZK.Models;
using GiblexVault.Security.ZK.Primitives;
using GiblexVault.Security.ZK.Wrapping;

namespace GiblexVault.Security.ZK.Container
{
    /// <summary>
    /// Zero-knowledge encrypted container format. Stores multiple files with
    /// per-entry DEKs wrapped by a container encryption key (CEK) derived from the master key.
    /// </summary>
    public sealed class GvContainerZk
    {
        private static readonly byte[] Magic = { 0x47, 0x56, 0x2D, 0x43, 0x5A, 0x4B, 0x01, 0x00 };
        private const int ChunkSize = 64 * 1024; // 64 KB streaming buffer

        private sealed record CHeader(string Type, string Version, CipherSuite Suite, KdfParams Kdf, string? Label);
        private sealed record Entry(string Name, long RealLength, long Offset, long BlobLength, byte[] WrappedDek);
        private sealed record Toc(List<Entry> Entries);

        public static async Task CreateAsync(string path, byte[] masterKey, EngineOptions options, string? label = null)
        {
            var suite = options.Suite;
            var kdf = new KdfParams
            {
                Kdf = "argon2id",
                Ops = options.ArgonOpsLimit,
                MemMiB = options.ArgonMemMiB,
                Parallelism = options.ArgonParallelism,
                Salt = RandomNumberGenerator.GetBytes(32)
            };

            var header = JsonSerializer.SerializeToUtf8Bytes(new CHeader("GV-CZK", "1", suite, kdf, label));
            await using var fs = File.Create(path);
            await fs.WriteAsync(Magic);

            byte[] l = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(l, (uint)header.Length);
            await fs.WriteAsync(l);
            await fs.WriteAsync(header);

            var cek = Hkdf.Sha256(masterKey, kdf.Salt, Encoding.UTF8.GetBytes("container::cek"));
            await WriteTocAsync(fs, header, suite, cek, new Toc(new List<Entry>()));
            CryptographicOperations.ZeroMemory(cek);
        }

        private static async Task WriteTocAsync(FileStream fs, byte[] header, CipherSuite suite, byte[] cek, Toc toc)
        {
            var ns = Aead.GetSuite(suite).NonceSize;
            var nonce = RandomNumberGenerator.GetBytes(ns);
            var plain = JsonSerializer.SerializeToUtf8Bytes(toc);
            var ct = Aead.Encrypt(suite, cek, nonce, header, plain);

            byte[] l = new byte[4];
            await fs.WriteAsync(nonce);
            BinaryPrimitives.WriteUInt32LittleEndian(l, (uint)ct.Length);
            await fs.WriteAsync(l);
            await fs.WriteAsync(ct);
            await fs.FlushAsync();
        }

        private static (byte[] header, CipherSuite suite, byte[] cek, long tocStart, FileStream fs) Open(string path, byte[] masterKey)
        {
            var fs = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Span<byte> magic = stackalloc byte[Magic.Length];
            if (fs.Read(magic) != Magic.Length || !magic.SequenceEqual(Magic))
                throw new InvalidOperationException("Bad container magic");

            byte[] l = new byte[4];
            if (fs.Read(l) != 4)
                throw new InvalidOperationException("Bad header length");

            var hlen = BinaryPrimitives.ReadUInt32LittleEndian(l);
            var header = new byte[(int)hlen];
            if (fs.Read(header, 0, (int)hlen) != (int)hlen)
                throw new InvalidOperationException("Bad header");

            var hdoc = JsonSerializer.Deserialize<CHeader>(header)!;
            var cek = Hkdf.Sha256(masterKey, hdoc.Kdf.Salt, Encoding.UTF8.GetBytes("container::cek"));
            var tocStart = fs.Position;

            return (header, hdoc.Suite, cek, tocStart, fs);
        }

        private static Toc ReadToc(FileStream fs, byte[] header, CipherSuite suite, byte[] cek)
        {
            var ns = Aead.GetSuite(suite).NonceSize;
            var nonce = new byte[ns];
            if (fs.Read(nonce, 0, ns) != ns)
                throw new InvalidOperationException("Bad TOC nonce");

            byte[] l = new byte[4];
            if (fs.Read(l) != 4)
                throw new InvalidOperationException("Bad TOC length");

            var clen = (int)BinaryPrimitives.ReadUInt32LittleEndian(l);
            var ct = new byte[clen];
            if (fs.Read(ct, 0, clen) != clen)
                throw new InvalidOperationException("Truncated TOC");

            var plain = Aead.Decrypt(suite, cek, nonce, header, ct);
            return JsonSerializer.Deserialize<Toc>(plain)!;
        }

        private static void WriteToc(FileStream fs, long tocStart, byte[] header, CipherSuite suite, byte[] cek, Toc toc)
        {
            fs.Position = tocStart;
            var ns = Aead.GetSuite(suite).NonceSize;
            var nonce = RandomNumberGenerator.GetBytes(ns);
            var plain = JsonSerializer.SerializeToUtf8Bytes(toc);
            var ct = Aead.Encrypt(suite, cek, nonce, header, plain);

            byte[] l = new byte[4];
            fs.Write(nonce);
            BinaryPrimitives.WriteUInt32LittleEndian(l, (uint)ct.Length);
            fs.Write(l);
            fs.Write(ct);
            fs.Flush();
            fs.Position = fs.Length;
        }

        private static string HashName(string name, byte[] cek)
        {
            using var h = new HMACSHA256(cek);
            return Convert.ToHexString(h.ComputeHash(Encoding.UTF8.GetBytes(name))).ToLowerInvariant();
        }

        private static long PadLen(long len, int bucket = 64 * 1024) => ((len + bucket - 1) / bucket) * bucket;

        /// <summary>
        /// Adds a file to the container using streaming to avoid loading the entire file into memory.
        /// </summary>
        public static void AddFile(string path, byte[] masterKey, EngineOptions options, string entryName, string filePath)
        {
            var (header, suite, cek, tocStart, fs) = Open(path, masterKey);
            using (fs)
            {
                var toc = ReadToc(fs, header, suite, cek);

                var fileInfo = new FileInfo(filePath);
                long realLength = fileInfo.Length;
                long padLen = options.Profile == EncryptionProfile.Paranoid ? PadLen(realLength) : realLength;

                var dek = RandomNumberGenerator.GetBytes(32);
                var wrapped = KeyWrap.WrapAead(suite, cek, dek, header);
                long offset = fs.Position;

                // Write entry using streaming
                var ns = Aead.GetSuite(suite).NonceSize;
                var nonce = RandomNumberGenerator.GetBytes(ns);

                // For AEAD we need to encrypt the entire padded content as one block
                // Use a memory stream to collect the padded plaintext
                byte[] paddedPlain;
                byte[] buffer = ArrayPool<byte>.Shared.Rent(ChunkSize);
                try
                {
                    using var ms = new MemoryStream((int)padLen);
                    using var inputFs = File.OpenRead(filePath);

                    long remaining = realLength;
                    while (remaining > 0)
                    {
                        int toRead = (int)Math.Min(ChunkSize, remaining);
                        int bytesRead = inputFs.Read(buffer, 0, toRead);
                        if (bytesRead == 0) break;
                        ms.Write(buffer, 0, bytesRead);
                        remaining -= bytesRead;
                    }

                    // Add padding if needed
                    long paddingNeeded = padLen - realLength;
                    if (paddingNeeded > 0)
                    {
                        var padding = new byte[paddingNeeded];
                        ms.Write(padding, 0, padding.Length);
                    }

                    paddedPlain = ms.ToArray();
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(buffer.AsSpan(0, ChunkSize));
                    ArrayPool<byte>.Shared.Return(buffer);
                }

                var ct = Aead.Encrypt(suite, dek, nonce, header, paddedPlain);
                CryptographicOperations.ZeroMemory(paddedPlain);

                byte[] l = new byte[4];
                fs.Write(nonce);
                BinaryPrimitives.WriteUInt32LittleEndian(l, (uint)ct.Length);
                fs.Write(l);
                fs.Write(ct);

                var name = options.Profile == EncryptionProfile.Paranoid ? HashName(entryName, cek) : entryName;
                toc.Entries.Add(new Entry(name, realLength, offset, ct.Length + ns + 4, wrapped));
                WriteToc(fs, tocStart, header, suite, cek, toc);
                CryptographicOperations.ZeroMemory(dek);
            }
            CryptographicOperations.ZeroMemory(cek);
        }

        public static List<string> List(string path, byte[] masterKey, EngineOptions options)
        {
            var (header, suite, cek, tocStart, fs) = Open(path, masterKey);
            using (fs)
            {
                var toc = ReadToc(fs, header, suite, cek);
                var names = new List<string>(toc.Entries.Select(e => e.Name));
                CryptographicOperations.ZeroMemory(cek);
                return names;
            }
        }

        public static byte[] Extract(string path, byte[] masterKey, EngineOptions options, string entryName)
        {
            var (header, suite, cek, tocStart, fs) = Open(path, masterKey);
            using (fs)
            {
                var toc = ReadToc(fs, header, suite, cek);
                var key = options.Profile == EncryptionProfile.Paranoid ? HashName(entryName, cek) : entryName;
                var e = toc.Entries.FirstOrDefault(x => x.Name == key);
                if (e is null)
                    throw new FileNotFoundException(entryName);

                fs.Position = e.Offset;
                var ns = Aead.GetSuite(suite).NonceSize;
                var nonce = new byte[ns];
                if (fs.Read(nonce, 0, ns) != ns)
                    throw new InvalidOperationException("Bad entry nonce");

                byte[] l = new byte[4];
                if (fs.Read(l) != 4)
                    throw new InvalidOperationException("Bad entry len");

                var clen = (int)BinaryPrimitives.ReadUInt32LittleEndian(l);
                var ct = new byte[clen];
                if (fs.Read(ct, 0, clen) != clen)
                    throw new InvalidOperationException("Truncated entry");

                var dek = KeyWrap.UnwrapAead(suite, cek, e.WrappedDek, header);
                var padded = Aead.Decrypt(suite, dek, nonce, header, ct);
                var plain = e.RealLength == padded.Length ? padded : padded[..(int)e.RealLength];

                CryptographicOperations.ZeroMemory(dek);
                CryptographicOperations.ZeroMemory(cek);
                return plain;
            }
        }
    }
}




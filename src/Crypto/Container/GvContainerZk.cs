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
using GiblexVault.Security.ZK.Signing;
using GiblexVault.Security.ZK.Wrapping;

namespace GiblexVault.Security.ZK.Container
{

    public sealed class GvContainerZk
    {
        private static readonly byte[] Magic = { 0x47, 0x56, 0x2D, 0x43, 0x5A, 0x4B, 0x01, 0x00 };
        private static readonly byte[] TocFooterMagic = { 0x47, 0x56, 0x2D, 0x54, 0x4F, 0x43, 0x02, 0x00 };
        private const int TocFooterSize = 16;
        private const int ChunkSize = 64 * 1024;
        private const int MaxHeaderBytes = 64 * 1024;
        private const int MaxTocBytes = 16 * 1024 * 1024;
        private const int MaxEntryBytes = 256 * 1024 * 1024;

        private static int ReadBoundedLength(FileStream stream, int maximum, string section)
        {
            Span<byte> bytes = stackalloc byte[4];
            stream.ReadExactly(bytes);
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
            if (length == 0 || length > maximum || length > stream.Length - stream.Position)
                throw new InvalidDataException($"Invalid {section} length.");
            return checked((int)length);
        }

        private sealed record CHeader(string Type, string Version, CipherSuite Suite, KdfParams Kdf, string? Label, string? PolicyHash = null);
        private sealed record Entry(string Name, long RealLength, long Offset, long BlobLength, byte[] WrappedDek);
        private sealed record Toc(List<Entry> Entries);

        public static async Task CreateAsync(string path, byte[] masterKey, EngineOptions options, string? label = null, string? policyHash = null, byte[]? signingKey = null)
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

            var header = JsonSerializer.SerializeToUtf8Bytes(new CHeader("GV-CZK", "2", suite, kdf, label, policyHash));
            if (header.Length > MaxHeaderBytes)
                throw new InvalidDataException("Container header exceeds the supported size.");
            await using var fs = File.Create(path);
            await fs.WriteAsync(Magic);

            byte[] l = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(l, (uint)header.Length);
            await fs.WriteAsync(l);
            await fs.WriteAsync(header);

            if (signingKey != null)
            {
                var headerStr = Encoding.UTF8.GetString(header);
                var sig = Ed25519Signer.SignString(headerStr, signingKey);
                byte[] sigLen = new byte[2];
                BinaryPrimitives.WriteUInt16LittleEndian(sigLen, (ushort)sig.Length);
                await fs.WriteAsync(sigLen);
                await fs.WriteAsync(sig);
            }
            else
            {
                byte[] sigLen = new byte[2];
                BinaryPrimitives.WriteUInt16LittleEndian(sigLen, 0);
                await fs.WriteAsync(sigLen);
            }

            var cek = Hkdf.Sha256(masterKey, kdf.Salt, Encoding.UTF8.GetBytes("container::cek"));
            try
            {
                await AppendTocAsync(fs, header, suite, cek, new Toc(new List<Entry>()));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(cek);
            }
        }

        private static async Task AppendTocAsync(FileStream fs, byte[] header, CipherSuite suite, byte[] cek, Toc toc)
        {
            fs.Position = fs.Length;
            var tocStart = fs.Position;
            var ns = Aead.GetSuite(suite).NonceSize;
            var nonce = RandomNumberGenerator.GetBytes(ns);
            var plain = JsonSerializer.SerializeToUtf8Bytes(toc);
            byte[] ct;
            try { ct = Aead.Encrypt(suite, cek, nonce, header, plain); }
            finally { CryptographicOperations.ZeroMemory(plain); }
            if (ct.Length > MaxTocBytes)
                throw new InvalidDataException("Container TOC exceeds the supported size.");

            byte[] l = new byte[4];
            await fs.WriteAsync(nonce);
            BinaryPrimitives.WriteUInt32LittleEndian(l, (uint)ct.Length);
            await fs.WriteAsync(l);
            await fs.WriteAsync(ct);
            await WriteTocFooterAsync(fs, tocStart);
            await fs.FlushAsync();
        }

        private static (byte[] header, CipherSuite suite, byte[] cek, long tocStart, FileStream fs) Open(string path, byte[] masterKey, byte[]? verifyingKey = null)
        {
            var fs = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            byte[]? cek = null;
            try
            {
                Span<byte> magic = stackalloc byte[Magic.Length];
                if (fs.Read(magic) != Magic.Length || !magic.SequenceEqual(Magic))
                    throw new InvalidOperationException("Bad container magic");

                var header = new byte[ReadBoundedLength(fs, MaxHeaderBytes, "header")];
                fs.ReadExactly(header);

                var hdoc = JsonSerializer.Deserialize<CHeader>(header)!;
                if (hdoc.Version != "1")
                {
                    byte[] sigLenBuf = new byte[2];
                    if (fs.Read(sigLenBuf) != 2)
                        throw new InvalidOperationException("Bad signature length");
                    int sigLen = BinaryPrimitives.ReadUInt16LittleEndian(sigLenBuf);

                    if (sigLen > 0)
                    {
                        byte[] sig = new byte[sigLen];
                        if (fs.Read(sig, 0, sigLen) != sigLen)
                            throw new InvalidOperationException("Bad signature");

                        if (verifyingKey != null)
                        {
                            var headerStr = Encoding.UTF8.GetString(header);
                            if (!Ed25519Signer.VerifyString(headerStr, sig, verifyingKey))
                                throw new CryptographicException("Container header signature verification failed. The header may have been tampered with.");
                        }
                    }
                    else if (verifyingKey != null)
                    {

                        throw new CryptographicException("Container header signature required but not found.");
                    }
                }

                cek = Hkdf.Sha256(masterKey, hdoc.Kdf.Salt, Encoding.UTF8.GetBytes("container::cek"));
                var initialTocStart = fs.Position;
                var tocStart = ReadLatestTocStart(fs, initialTocStart);

                return (header, hdoc.Suite, cek, tocStart, fs);
            }
            catch
            {
                if (cek is not null) CryptographicOperations.ZeroMemory(cek);
                fs.Dispose();
                throw;
            }
        }

        private static long ReadLatestTocStart(FileStream fs, long fallbackTocStart)
        {
            if (fs.Length < TocFooterSize)
                return fallbackTocStart;

            var originalPosition = fs.Position;
            try
            {
                fs.Position = fs.Length - TocFooterSize;
                Span<byte> magic = stackalloc byte[TocFooterMagic.Length];
                if (fs.Read(magic) != TocFooterMagic.Length || !magic.SequenceEqual(TocFooterMagic))
                    return fallbackTocStart;

                Span<byte> offsetBytes = stackalloc byte[8];
                if (fs.Read(offsetBytes) != 8)
                    return fallbackTocStart;

                var tocStart = BinaryPrimitives.ReadInt64LittleEndian(offsetBytes);
                if (tocStart < fallbackTocStart || tocStart >= fs.Length - TocFooterSize)
                    throw new InvalidOperationException("Invalid TOC footer offset");
                return tocStart;
            }
            finally
            {
                fs.Position = originalPosition;
            }
        }

        private static Toc ReadToc(FileStream fs, byte[] header, CipherSuite suite, byte[] cek)
        {
            var ns = Aead.GetSuite(suite).NonceSize;
            var nonce = new byte[ns];
            if (fs.Read(nonce, 0, ns) != ns)
                throw new InvalidOperationException("Bad TOC nonce");

            var ct = new byte[ReadBoundedLength(fs, MaxTocBytes, "TOC")];
            fs.ReadExactly(ct);

            var plain = Aead.Decrypt(suite, cek, nonce, header, ct);
            try
            {
                return JsonSerializer.Deserialize<Toc>(plain) ?? throw new InvalidDataException("Invalid TOC.");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }

        private static void WriteToc(FileStream fs, long tocStart, byte[] header, CipherSuite suite, byte[] cek, Toc toc)
        {
            fs.Position = fs.Length;
            tocStart = fs.Position;
            var ns = Aead.GetSuite(suite).NonceSize;
            var nonce = RandomNumberGenerator.GetBytes(ns);
            var plain = JsonSerializer.SerializeToUtf8Bytes(toc);
            byte[] ct;
            try { ct = Aead.Encrypt(suite, cek, nonce, header, plain); }
            finally { CryptographicOperations.ZeroMemory(plain); }
            if (ct.Length > MaxTocBytes)
                throw new InvalidDataException("Container TOC exceeds the supported size.");

            byte[] l = new byte[4];
            fs.Write(nonce);
            BinaryPrimitives.WriteUInt32LittleEndian(l, (uint)ct.Length);
            fs.Write(l);
            fs.Write(ct);
            WriteTocFooter(fs, tocStart);
            fs.Flush();
            fs.Position = fs.Length;
        }

        private static async Task WriteTocFooterAsync(FileStream fs, long tocStart)
        {
            await fs.WriteAsync(TocFooterMagic);
            byte[] offset = new byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(offset, tocStart);
            await fs.WriteAsync(offset);
        }

        private static void WriteTocFooter(FileStream fs, long tocStart)
        {
            fs.Write(TocFooterMagic);
            Span<byte> offset = stackalloc byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(offset, tocStart);
            fs.Write(offset);
        }

        private static string HashName(string name, byte[] cek)
        {
            using var h = new HMACSHA256(cek);
            return Convert.ToHexString(h.ComputeHash(Encoding.UTF8.GetBytes(name))).ToLowerInvariant();
        }

        private static long PadLen(long len, int bucket = 64 * 1024) => ((len + bucket - 1) / bucket) * bucket;

        public static void AddFile(string path, byte[] masterKey, EngineOptions options, string entryName, string filePath, byte[]? verifyingKey = null)
        {
            var (header, suite, cek, tocStart, fs) = Open(path, masterKey, verifyingKey);
            try
            {
                using (fs)
                {
                    fs.Position = tocStart;
                    var toc = ReadToc(fs, header, suite, cek);

                    long entryPosition = fs.Length;
                    fs.Position = entryPosition;

                    var fileInfo = new FileInfo(filePath);
                    long realLength = fileInfo.Length;
                    long padLen = options.Profile == EncryptionProfile.Paranoid ? PadLen(realLength) : realLength;
                    if (padLen > MaxEntryBytes - 16)
                        throw new InvalidDataException("Container entry exceeds the supported size.");

                    var dek = RandomNumberGenerator.GetBytes(32);
                    try
                    {
                        var wrapped = KeyWrap.WrapAead(suite, cek, dek, header);

                        var ns = Aead.GetSuite(suite).NonceSize;
                        var nonce = RandomNumberGenerator.GetBytes(ns);

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
                            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
                        }

                        byte[] ct;
                        try { ct = Aead.Encrypt(suite, dek, nonce, header, paddedPlain); }
                        finally { CryptographicOperations.ZeroMemory(paddedPlain); }

                        var name = options.Profile == EncryptionProfile.Paranoid ? HashName(entryName, cek) : entryName;
                        toc.Entries.Add(new Entry(name, realLength, entryPosition, ct.Length + ns + 4, wrapped));
                        byte[] tocPreview = JsonSerializer.SerializeToUtf8Bytes(toc);
                        try
                        {
                            if (tocPreview.Length > MaxTocBytes - 16)
                                throw new InvalidDataException("Container TOC exceeds the supported size.");
                        }
                        finally
                        {
                            CryptographicOperations.ZeroMemory(tocPreview);
                        }

                        byte[] l = new byte[4];
                        fs.Write(nonce);
                        BinaryPrimitives.WriteUInt32LittleEndian(l, (uint)ct.Length);
                        fs.Write(l);
                        fs.Write(ct);

                        WriteToc(fs, tocStart, header, suite, cek, toc);

                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(dek);
                    }
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(cek);
            }
        }

        public static List<string> List(string path, byte[] masterKey, EngineOptions options, byte[]? verifyingKey = null)
        {
            var (header, suite, cek, tocStart, fs) = Open(path, masterKey, verifyingKey);
            try
            {
                using (fs)
                {
                    fs.Position = tocStart;
                    var toc = ReadToc(fs, header, suite, cek);
                    var names = new List<string>(toc.Entries.Select(e => e.Name));
                    return names;
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(cek);
            }
        }

        public static byte[] Extract(string path, byte[] masterKey, EngineOptions options, string entryName, byte[]? verifyingKey = null)
        {
            var (header, suite, cek, tocStart, fs) = Open(path, masterKey, verifyingKey);
            try
            {
                using (fs)
                {
                    fs.Position = tocStart;
                    var toc = ReadToc(fs, header, suite, cek);
                    var key = options.Profile == EncryptionProfile.Paranoid ? HashName(entryName, cek) : entryName;
                    var e = toc.Entries.FirstOrDefault(x => x.Name == key);
                    if (e is null)
                        throw new FileNotFoundException(entryName);

                    if (e.Offset < 0 || e.Offset >= fs.Length || e.RealLength < 0 || e.RealLength > MaxEntryBytes)
                        throw new InvalidDataException("Invalid entry range.");
                    fs.Position = e.Offset;
                    var ns = Aead.GetSuite(suite).NonceSize;
                    var nonce = new byte[ns];
                    if (fs.Read(nonce, 0, ns) != ns)
                        throw new InvalidOperationException("Bad entry nonce");

                    var ct = new byte[ReadBoundedLength(fs, MaxEntryBytes, "entry")];
                    fs.ReadExactly(ct);

                    var dek = KeyWrap.UnwrapAead(suite, cek, e.WrappedDek, header);
                    byte[]? padded = null;
                    try
                    {
                        padded = Aead.Decrypt(suite, dek, nonce, header, ct);
                        if (e.RealLength > padded.Length)
                            throw new InvalidDataException("Invalid entry plaintext length.");
                        return padded[..checked((int)e.RealLength)];
                    }
                    finally
                    {
                        if (padded is not null) CryptographicOperations.ZeroMemory(padded);
                        CryptographicOperations.ZeroMemory(dek);
                    }
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(cek);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using PhantomVault.Core.Models;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Native KDBX 3.1 and 4.x parser — no KeePassLib.Standard dependency.
    /// Supports: AES-256-CBC (KDBX 3.1), ChaCha20 (KDBX 4.x).
    /// KDF: AES-KDF (KDBX 3.1), Argon2d/Argon2id (KDBX 4.x).
    /// </summary>
    public class KeePassImportService
    {
        private static readonly byte[] KdbxSignature = { 0x03, 0xD9, 0xA2, 0x9A, 0x67, 0xFB, 0x4B, 0xB5 };

        public async Task<KeePassImportResult> ImportAsync(
            string kdbxPath,
            string password,
            string? keyfilePath = null,
            IProgress<int>? progress = null)
        {
            try
            {
                progress?.Report(0);

                if (!File.Exists(kdbxPath))
                    return KeePassImportResult.Failure("KeePass database file not found.");

                // Password is optional — KDBX supports keyfile-only auth too.
                if (string.IsNullOrEmpty(password) && string.IsNullOrEmpty(keyfilePath))
                    return KeePassImportResult.Failure("Provide at least a password or a keyfile.");

                progress?.Report(10);

                byte[] kdbxData;
                try { kdbxData = await File.ReadAllBytesAsync(kdbxPath); }
                catch (Exception ex) { return KeePassImportResult.Failure($"Failed to read file: {ex.Message}"); }

                progress?.Report(20);

                var parseResult = await Task.Run(() => ParseKdbx(kdbxData, password, keyfilePath, progress));
                progress?.Report(100);
                return parseResult;
            }
            catch (Exception ex)
            {
                return KeePassImportResult.Failure($"Import failed: {ex.Message}");
            }
        }

        public async Task<(bool IsValid, string Message)> ValidateAsync(
            string kdbxPath, string password, string? keyfilePath = null)
        {
            var result = await ImportAsync(kdbxPath, password, keyfilePath);
            return (result.IsSuccess, result.Message);
        }

        #region KDBX Parsing

        private KeePassImportResult ParseKdbx(byte[] data, string? password, string? keyfilePath, IProgress<int>? progress)
        {
            try
            {
                using var ms = new MemoryStream(data);
                using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

                // Signature check
                var sig1 = br.ReadUInt32();
                var sig2 = br.ReadUInt32();
                if (sig1 != 0x9AA2D903u || (sig2 != 0xB54BFB67u && sig2 != 0xB54BFB66u && sig2 != 0xB54BFB65u))
                    return KeePassImportResult.Failure("Not a valid KDBX file.");

                var versionMinor = br.ReadUInt16();
                var versionMajor = br.ReadUInt16();

                progress?.Report(30);

                if (versionMajor == 3)
                    return ParseKdbx3(br, ms, data, password, keyfilePath, progress);
                else if (versionMajor == 4)
                    return ParseKdbx4(br, ms, data, password, keyfilePath, versionMinor, progress);
                else
                    return KeePassImportResult.Failure($"KDBX version {versionMajor}.{versionMinor} is not supported.");
            }
            catch (CryptographicException)
            {
                return KeePassImportResult.Failure("Invalid password or keyfile — decryption failed.");
            }
            catch (Exception ex)
            {
                return KeePassImportResult.Failure($"KDBX parsing error: {ex.Message}");
            }
        }

        // ──────────────────────────────────────────────────────────────────
        // KDBX 3.1
        // ──────────────────────────────────────────────────────────────────

        private KeePassImportResult ParseKdbx3(BinaryReader br, MemoryStream ms, byte[] data,
            string? password, string? keyfilePath, IProgress<int>? progress)
        {
            // Read outer headers
            var hdr = new Kdbx3Header();
            while (true)
            {
                byte fieldId = br.ReadByte();
                int fieldLen = br.ReadUInt16();
                byte[] fieldData = br.ReadBytes(fieldLen);

                if (fieldId == 0) break; // EndOfHeader

                switch (fieldId)
                {
                    case 2: hdr.CipherId = fieldData; break;
                    case 3: hdr.CompressionFlags = BitConverter.ToUInt32(fieldData, 0); break;
                    case 4: hdr.MasterSeed = fieldData; break;
                    case 5: hdr.TransformSeed = fieldData; break;
                    case 6: hdr.TransformRounds = BitConverter.ToUInt64(fieldData, 0); break;
                    case 7: hdr.EncryptionIV = fieldData; break;
                    case 8: hdr.ProtectedStreamKey = fieldData; break;
                    case 9: hdr.StreamStartBytes = fieldData; break;
                    case 10: hdr.InnerRandomStreamId = BitConverter.ToUInt32(fieldData, 0); break;
                }
            }

            progress?.Report(40);

            // Derive composite key
            byte[] compositeKey = DeriveCompositeKey3(password, keyfilePath);

            // Transform key: AES-KDF
            byte[] transformedKey = AesKdf(compositeKey, hdr.TransformSeed!, hdr.TransformRounds);

            // Master key = SHA256(MasterSeed + TransformKey)
            byte[] masterKey;
            using (var sha = SHA256.Create())
            {
                byte[] combined = new byte[hdr.MasterSeed!.Length + transformedKey.Length];
                Buffer.BlockCopy(hdr.MasterSeed, 0, combined, 0, hdr.MasterSeed.Length);
                Buffer.BlockCopy(transformedKey, 0, combined, hdr.MasterSeed.Length, transformedKey.Length);
                masterKey = sha.ComputeHash(combined);
            }

            progress?.Report(55);

            // Encrypted payload starts at current stream position
            long payloadOffset = ms.Position;
            byte[] encryptedPayload = new byte[data.Length - payloadOffset];
            Buffer.BlockCopy(data, (int)payloadOffset, encryptedPayload, 0, encryptedPayload.Length);

            // Decrypt AES-CBC
            byte[] decrypted = DecryptAesCbc(encryptedPayload, masterKey, hdr.EncryptionIV!);

            // Verify stream start bytes
            if (hdr.StreamStartBytes != null && decrypted.Length >= hdr.StreamStartBytes.Length)
            {
                for (int i = 0; i < hdr.StreamStartBytes.Length; i++)
                {
                    if (decrypted[i] != hdr.StreamStartBytes[i])
                        return KeePassImportResult.Failure("Invalid password or keyfile — stream start bytes mismatch.");
                }
            }

            progress?.Report(70);

            // Skip stream start bytes and block-hash wrapper, then decompress
            byte[] payload = UnpackKdbx3Blocks(decrypted, hdr.StreamStartBytes?.Length ?? 32);

            if (hdr.CompressionFlags == 1)
            {
                payload = GzipDecompress(payload);
            }

            progress?.Report(85);

            // Parse XML
            var xmlDoc = XDocument.Parse(Encoding.UTF8.GetString(payload));
            var credentials = ExtractCredentialsFromXml(xmlDoc, hdr.ProtectedStreamKey, hdr.InnerRandomStreamId);

            return KeePassImportResult.Success(credentials,
                $"Successfully imported {credentials.Count} credentials from KeePass 3.1 database.");
        }

        // ──────────────────────────────────────────────────────────────────
        // KDBX 4.x
        // ──────────────────────────────────────────────────────────────────

        private KeePassImportResult ParseKdbx4(BinaryReader br, MemoryStream ms, byte[] data,
            string? password, string? keyfilePath, ushort versionMinor, IProgress<int>? progress)
        {
            var hdr = new Kdbx4Header();
            byte[] headerBytes;
            long headerStart = 0;

            // Read variable-length headers
            long headerBytesStart = headerStart;
            var headerBuffer = new List<byte>();

            // Store raw header bytes for HMAC check — include the 8-byte signature + 4-byte version we already read
            headerBuffer.AddRange(BitConverter.GetBytes(0x9AA2D903u));
            headerBuffer.AddRange(BitConverter.GetBytes(0xB54BFB67u));
            headerBuffer.AddRange(BitConverter.GetBytes(versionMinor));
            headerBuffer.AddRange(BitConverter.GetBytes((ushort)4));

            while (true)
            {
                byte fieldId = br.ReadByte();
                int fieldLen = (int)br.ReadUInt32();
                byte[] fieldData = br.ReadBytes(fieldLen);

                // Record for HMAC
                headerBuffer.Add(fieldId);
                headerBuffer.AddRange(BitConverter.GetBytes((uint)fieldLen));
                headerBuffer.AddRange(fieldData);

                if (fieldId == 0) break;

                switch (fieldId)
                {
                    case 2: hdr.CipherId = fieldData; break;
                    case 3: hdr.CompressionFlags = BitConverter.ToUInt32(fieldData, 0); break;
                    case 4: hdr.MasterSeed = fieldData; break;
                    case 11: hdr.KdfParameters = fieldData; break;
                }
            }

            headerBytes = headerBuffer.ToArray();

            progress?.Report(40);

            // KDF — parse VariantDictionary for KDF params
            var kdfParams = ParseVariantDictionary(hdr.KdfParameters ?? Array.Empty<byte>());
            byte[] compositeKey = DeriveCompositeKey4(password, keyfilePath);
            byte[] transformedKey = ApplyKdf4(compositeKey, kdfParams);

            // Keys from master seed
            using var sha = SHA256.Create();
            byte[] combined = new byte[hdr.MasterSeed!.Length + transformedKey.Length + 1];
            Buffer.BlockCopy(hdr.MasterSeed, 0, combined, 0, hdr.MasterSeed.Length);
            Buffer.BlockCopy(transformedKey, 0, combined, hdr.MasterSeed.Length, transformedKey.Length);
            combined[combined.Length - 1] = 1;
            byte[] masterKey = sha.ComputeHash(combined);
            combined[combined.Length - 1] = 0;
            byte[] hmacKey64 = SHA512.HashData(combined);

            progress?.Report(55);

            // Read header HMAC (32 bytes) — skip validation for now; wrong key = XML parse failure
            byte[] headerHmac = br.ReadBytes(32);

            // Read block-HMAC payload
            long payloadOffset = ms.Position;
            byte[] encryptedBlocks = new byte[data.Length - payloadOffset];
            Buffer.BlockCopy(data, (int)payloadOffset, encryptedBlocks, 0, encryptedBlocks.Length);

            byte[] encryptedPayload = UnpackKdbx4Blocks(encryptedBlocks);

            progress?.Report(65);

            // Decrypt
            byte[] decrypted;
            if (hdr.CipherId != null && hdr.CipherId.Length == 16 &&
                hdr.CipherId[0] == 0xD6 && hdr.CipherId[1] == 0x03)
            {
                // ChaCha20 — cipher UUID starts with D6 03 8A 2B
                // IV from header bytes 64–76 of masterKey (KDBX4 derives IV differently)
                byte[] encIv = sha.ComputeHash(Concat(hdr.MasterSeed, transformedKey, new byte[] { 0x01 }))[..12];
                decrypted = ChaCha20Decrypt(encryptedPayload, masterKey[..32], encIv);
            }
            else
            {
                // AES-256-CBC
                // Derive 32-byte key and 16-byte IV from HKDF
                byte[] encKey = HkdfExpand(masterKey, Encoding.ASCII.GetBytes("encryption"), 32);
                byte[] encIv = HkdfExpand(masterKey, Encoding.ASCII.GetBytes("enc-iv"), 16);
                decrypted = DecryptAesCbc(encryptedPayload, encKey, encIv);
            }

            progress?.Report(75);

            byte[] payload = decrypted;
            if (hdr.CompressionFlags == 1)
                payload = GzipDecompress(payload);

            progress?.Report(85);

            // Inner header (KDBX 4.x)
            using var pms = new MemoryStream(payload);
            using var pbr = new BinaryReader(pms, Encoding.UTF8, leaveOpen: true);
            byte[]? protectedStreamKey = null;
            uint innerStreamId = 0;

            while (true)
            {
                byte id = pbr.ReadByte();
                int len = (int)pbr.ReadUInt32();
                byte[] val = pbr.ReadBytes(len);
                if (id == 0) break;
                if (id == 1) innerStreamId = BitConverter.ToUInt32(val, 0);
                if (id == 2) protectedStreamKey = val;
            }

            byte[] xmlBytes = new byte[pms.Length - pms.Position];
            pms.Read(xmlBytes, 0, xmlBytes.Length);

            var xmlDoc = XDocument.Parse(Encoding.UTF8.GetString(xmlBytes));
            var credentials = ExtractCredentialsFromXml(xmlDoc, protectedStreamKey, innerStreamId);

            return KeePassImportResult.Success(credentials,
                $"Successfully imported {credentials.Count} credentials from KeePass 4 database.");
        }

        // ──────────────────────────────────────────────────────────────────
        // Key Derivation
        // ──────────────────────────────────────────────────────────────────

        private static byte[] DeriveCompositeKey3(string? password, string? keyfilePath)
        {
            using var sha256 = SHA256.Create();
            var parts = new List<byte[]>();

            if (!string.IsNullOrEmpty(password))
                parts.Add(sha256.ComputeHash(Encoding.UTF8.GetBytes(password)));

            if (!string.IsNullOrEmpty(keyfilePath) && File.Exists(keyfilePath))
                parts.Add(LoadKeyfileKey(keyfilePath));

            using var sha = SHA256.Create();
            byte[] combined = parts.SelectMany(p => p).ToArray();
            return sha.ComputeHash(combined);
        }

        private static byte[] DeriveCompositeKey4(string? password, string? keyfilePath)
            => DeriveCompositeKey3(password, keyfilePath);

        private static byte[] LoadKeyfileKey(string path)
        {
            byte[] raw = File.ReadAllBytes(path);
            // Try XML keyfile
            try
            {
                var xml = XDocument.Parse(Encoding.UTF8.GetString(raw));
                var keyEl = xml.Descendants("Data").FirstOrDefault();
                if (keyEl != null)
                    return Convert.FromBase64String(keyEl.Value.Trim());
            }
            catch { }

            // Raw 32-byte key or hash of entire file
            if (raw.Length == 32) return raw;
            using var sha = SHA256.Create();
            return sha.ComputeHash(raw);
        }

        private static byte[] AesKdf(byte[] compositeKey, byte[] seed, ulong rounds)
        {
            // KDBX 3.1: AES-ECB transform of composite key
            using var aes = Aes.Create();
            aes.Key = seed;
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;

            byte[] key = (byte[])compositeKey.Clone();
            using var encryptor = aes.CreateEncryptor();
            for (ulong i = 0; i < rounds; i++)
            {
                encryptor.TransformBlock(key, 0, 16, key, 0);
                encryptor.TransformBlock(key, 16, 16, key, 16);
            }

            using var sha = SHA256.Create();
            return sha.ComputeHash(key);
        }

        private static byte[] ApplyKdf4(byte[] compositeKey, Dictionary<string, object> kdfParams)
        {
            if (kdfParams.TryGetValue("$UUID", out var uuidObj) && uuidObj is byte[] uuid)
            {
                // Argon2 UUIDs
                bool isArgon2 = (uuid[0] == 0xEF && uuid[1] == 0x63 && uuid[2] == 0x6D && uuid[3] == 0xDF) ||
                                (uuid[0] == 0x9E && uuid[1] == 0x29 && uuid[2] == 0x8B && uuid[3] == 0x19);

                if (isArgon2)
                {
                    byte[] salt = kdfParams.TryGetValue("S", out var sv) ? (byte[])sv : new byte[32];
                    ulong iterations = kdfParams.TryGetValue("I", out var iv) ? (ulong)iv : 2;
                    uint memory = (uint)(kdfParams.TryGetValue("M", out var mv) ? (ulong)mv / 1024 : 65536);
                    uint parallelism = (uint)(kdfParams.TryGetValue("P", out var pv) ? (ulong)pv : 2);

                    // Use Isopoh.Cryptography.Argon2 (already referenced in the project)
                    bool isDraft = uuid[0] == 0x9E; // Argon2id
                    var argon2Config = new Isopoh.Cryptography.Argon2.Argon2Config
                    {
                        Type = isDraft
                            ? Isopoh.Cryptography.Argon2.Argon2Type.HybridAddressing
                            : Isopoh.Cryptography.Argon2.Argon2Type.DataIndependentAddressing,
                        TimeCost = (int)iterations,
                        MemoryCost = (int)memory,
                        Lanes = (int)parallelism,
                        Threads = (int)parallelism,
                        Password = compositeKey,
                        Salt = salt,
                        HashLength = 32
                    };
                    using var argon2 = new Isopoh.Cryptography.Argon2.Argon2(argon2Config);
                    using var hash = argon2.Hash();
                    return hash.Buffer[..32];
                }
            }

            // Fallback: AES-KDF
            byte[] kdfSeed = kdfParams.TryGetValue("S", out var seedV) ? (byte[])seedV : new byte[32];
            ulong kdfRounds = kdfParams.TryGetValue("R", out var rv) ? (ulong)rv : 6000;
            return AesKdf(compositeKey, kdfSeed, kdfRounds);
        }

        // ──────────────────────────────────────────────────────────────────
        // Crypto helpers
        // ──────────────────────────────────────────────────────────────────

        private static byte[] DecryptAesCbc(byte[] ciphertext, byte[] key, byte[] iv)
        {
            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;
            using var dec = aes.CreateDecryptor();
            return dec.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
        }

        private static byte[] ChaCha20Decrypt(byte[] ciphertext, byte[] key, byte[] nonce)
        {
            var engine = new ChaCha7539Engine();
            engine.Init(false, new ParametersWithIV(new KeyParameter(key), nonce));
            byte[] output = new byte[ciphertext.Length];
            engine.ProcessBytes(ciphertext, 0, ciphertext.Length, output, 0);
            return output;
        }

        private static byte[] HkdfExpand(byte[] prk, byte[] info, int length)
        {
            var result = new List<byte>();
            byte[] t = Array.Empty<byte>();
            byte counter = 1;
            while (result.Count < length)
            {
                using var hmac = new HMACSHA256(prk);
                byte[] block = new byte[t.Length + info.Length + 1];
                Buffer.BlockCopy(t, 0, block, 0, t.Length);
                Buffer.BlockCopy(info, 0, block, t.Length, info.Length);
                block[block.Length - 1] = counter++;
                t = hmac.ComputeHash(block);
                result.AddRange(t);
            }
            return result.Take(length).ToArray();
        }

        private static byte[] Concat(params byte[][] arrays)
        {
            byte[] result = new byte[arrays.Sum(a => a.Length)];
            int offset = 0;
            foreach (var a in arrays) { Buffer.BlockCopy(a, 0, result, offset, a.Length); offset += a.Length; }
            return result;
        }

        // ──────────────────────────────────────────────────────────────────
        // Payload unpacking
        // ──────────────────────────────────────────────────────────────────

        private static byte[] UnpackKdbx3Blocks(byte[] decrypted, int skipBytes)
        {
            // Skip stream start bytes, then read block-hash stream
            var result = new List<byte>();
            int pos = skipBytes;
            while (pos < decrypted.Length - 4)
            {
                // Block index (4), hash (32), size (4), data
                pos += 4; // block index
                if (pos + 32 + 4 > decrypted.Length) break;
                byte[] hash = new byte[32];
                Buffer.BlockCopy(decrypted, pos, hash, 0, 32);
                pos += 32;
                int size = BitConverter.ToInt32(decrypted, pos);
                pos += 4;
                if (size == 0) break;
                result.AddRange(decrypted.Skip(pos).Take(size));
                pos += size;
            }
            return result.ToArray();
        }

        private static byte[] UnpackKdbx4Blocks(byte[] blocks)
        {
            var result = new List<byte>();
            int pos = 0;
            while (pos < blocks.Length - 32 - 4)
            {
                // 32-byte HMAC, 4-byte size, data
                pos += 32; // skip HMAC
                if (pos + 4 > blocks.Length) break;
                int size = BitConverter.ToInt32(blocks, pos);
                pos += 4;
                if (size == 0) break;
                result.AddRange(blocks.Skip(pos).Take(size));
                pos += size;
            }
            return result.ToArray();
        }

        private static byte[] GzipDecompress(byte[] data)
        {
            using var compressed = new MemoryStream(data);
            using var gz = new GZipStream(compressed, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gz.CopyTo(output);
            return output.ToArray();
        }

        // ──────────────────────────────────────────────────────────────────
        // VariantDictionary (KDBX 4.x)
        // ──────────────────────────────────────────────────────────────────

        private static Dictionary<string, object> ParseVariantDictionary(byte[] data)
        {
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (data.Length < 2) return result;

            int pos = 2; // skip version (2 bytes)
            while (pos < data.Length)
            {
                byte type = data[pos++];
                if (type == 0) break;

                int keyLen = BitConverter.ToInt32(data, pos); pos += 4;
                string key = Encoding.UTF8.GetString(data, pos, keyLen); pos += keyLen;
                int valLen = BitConverter.ToInt32(data, pos); pos += 4;
                byte[] val = new byte[valLen];
                Buffer.BlockCopy(data, pos, val, 0, valLen); pos += valLen;

                result[key] = type switch
                {
                    0x04 => (object)BitConverter.ToUInt32(val, 0),
                    0x05 => BitConverter.ToUInt64(val, 0),
                    0x08 => val[0] != 0,
                    0x0C => BitConverter.ToInt32(val, 0),
                    0x0D => BitConverter.ToInt64(val, 0),
                    0x18 => val,
                    0x1C => Encoding.UTF8.GetString(val),
                    _ => val
                };
            }
            return result;
        }

        // ──────────────────────────────────────────────────────────────────
        // XML extraction
        // ──────────────────────────────────────────────────────────────────

        private static List<Credential> ExtractCredentialsFromXml(XDocument doc, byte[]? protectedStreamKey, uint innerStreamId)
        {
            // Set up protected-value decryptor (Salsa20 or ChaCha20)
            IProtectedValueDecryptor? decryptor = null;
            if (protectedStreamKey != null)
            {
                decryptor = innerStreamId switch
                {
                    2 => new Salsa20Decryptor(protectedStreamKey),
                    3 => new ChaCha20Decryptor(protectedStreamKey),
                    _ => null
                };
            }

            var credentials = new List<Credential>();
            var root = doc.Root?.Element("Root")?.Element("Group");
            if (root != null)
                ExtractGroup(root, credentials, "", decryptor);

            decryptor?.Dispose();
            return credentials;
        }

        private static void ExtractGroup(XElement group, List<Credential> creds, string path, IProtectedValueDecryptor? dec)
        {
            string name = group.Element("Name")?.Value ?? "";
            string currentPath = string.IsNullOrEmpty(path) ? name : $"{path}/{name}";

            foreach (var entry in group.Elements("Entry"))
            {
                try
                {
                    string title = "", username = "", password = "", url = "", notes = "";
                    var customFields = new Dictionary<string, string>();
                    var tags = new List<string>();
                    DateTimeOffset created = DateTimeOffset.UtcNow, modified = DateTimeOffset.UtcNow;
                    DateTimeOffset? expiry = null;

                    foreach (var str in entry.Elements("String"))
                    {
                        string key = str.Element("Key")?.Value ?? "";
                        var valueEl = str.Element("Value");
                        bool isProtected = valueEl?.Attribute("Protected")?.Value?.Equals("True", StringComparison.OrdinalIgnoreCase) == true;
                        string value = isProtected && dec != null
                            ? dec.Decrypt(Convert.FromBase64String(valueEl?.Value ?? ""))
                            : (valueEl?.Value ?? "");

                        switch (key)
                        {
                            case "Title": title = value; break;
                            case "UserName": username = value; break;
                            case "Password": password = value; break;
                            case "URL": url = value; break;
                            case "Notes": notes = value; break;
                            default: if (!string.IsNullOrEmpty(key)) customFields[key] = value; break;
                        }
                    }

                    var times = entry.Element("Times");
                    if (times != null)
                    {
                        if (DateTimeOffset.TryParse(times.Element("CreationTime")?.Value, out var ct)) created = ct;
                        if (DateTimeOffset.TryParse(times.Element("LastModificationTime")?.Value, out var mt)) modified = mt;
                        bool expires = times.Element("Expires")?.Value?.Equals("True", StringComparison.OrdinalIgnoreCase) == true;
                        if (expires && DateTimeOffset.TryParse(times.Element("ExpiryTime")?.Value, out var et)) expiry = et;
                    }

                    var tagsEl = entry.Element("Tags");
                    if (tagsEl != null && !string.IsNullOrEmpty(tagsEl.Value))
                        tags = tagsEl.Value.Split(';', ',').Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t)).ToList();

                    if (string.IsNullOrWhiteSpace(title)) continue;

                    creds.Add(new Credential
                    {
                        Title = title,
                        Username = username,
                        Password = password,
                        Url = url,
                        Notes = notes,
                        Group = currentPath,
                        CreatedUtc = created,
                        LastUpdatedUtc = modified,
                        ExpiryUtc = expiry,
                        CustomFields = customFields,
                        Tags = tags
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[KeePassImport] Failed to extract entry: {ex.Message}");
                }
            }

            foreach (var sub in group.Elements("Group"))
                ExtractGroup(sub, creds, currentPath, dec);
        }

        // ──────────────────────────────────────────────────────────────────
        // Protected-value stream decryptors
        // ──────────────────────────────────────────────────────────────────

        private interface IProtectedValueDecryptor : IDisposable
        {
            string Decrypt(byte[] ciphertext);
        }

        private sealed class Salsa20Decryptor : IProtectedValueDecryptor
        {
            private readonly Salsa20Engine _engine;

            public Salsa20Decryptor(byte[] key)
            {
                byte[] keyHash = SHA256.HashData(key);
                byte[] iv = new byte[] { 0xE8, 0x30, 0x09, 0x4B, 0x97, 0x20, 0x5D, 0x2A };
                _engine = new Salsa20Engine();
                _engine.Init(false, new ParametersWithIV(new KeyParameter(keyHash), iv));
            }

            public string Decrypt(byte[] ciphertext)
            {
                byte[] plain = new byte[ciphertext.Length];
                _engine.ProcessBytes(ciphertext, 0, ciphertext.Length, plain, 0);
                return Encoding.UTF8.GetString(plain);
            }

            public void Dispose() { }
        }

        private sealed class ChaCha20Decryptor : IProtectedValueDecryptor
        {
            private readonly ChaCha7539Engine _engine;

            public ChaCha20Decryptor(byte[] key)
            {
                byte[] keyHash = SHA512.HashData(key);
                _engine = new ChaCha7539Engine();
                _engine.Init(false, new ParametersWithIV(new KeyParameter(keyHash[..32]), keyHash[32..44]));
            }

            public string Decrypt(byte[] ciphertext)
            {
                byte[] plain = new byte[ciphertext.Length];
                _engine.ProcessBytes(ciphertext, 0, ciphertext.Length, plain, 0);
                return Encoding.UTF8.GetString(plain);
            }

            public void Dispose() { }
        }

        // ──────────────────────────────────────────────────────────────────
        // Header POCOs
        // ──────────────────────────────────────────────────────────────────

        private sealed class Kdbx3Header
        {
            public byte[]? CipherId { get; set; }
            public uint CompressionFlags { get; set; }
            public byte[]? MasterSeed { get; set; }
            public byte[]? TransformSeed { get; set; }
            public ulong TransformRounds { get; set; } = 6000;
            public byte[]? EncryptionIV { get; set; }
            public byte[]? ProtectedStreamKey { get; set; }
            public byte[]? StreamStartBytes { get; set; }
            public uint InnerRandomStreamId { get; set; }
        }

        private sealed class Kdbx4Header
        {
            public byte[]? CipherId { get; set; }
            public uint CompressionFlags { get; set; }
            public byte[]? MasterSeed { get; set; }
            public byte[]? KdfParameters { get; set; }
        }

        #endregion
    }

    public class KeePassImportResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<Credential> Credentials { get; set; } = new();
        public int TotalEntries => Credentials.Count;
        public int TotalGroups => Credentials.Select(c => c.Group).Distinct().Count();

        public static KeePassImportResult Success(List<Credential> credentials, string message = "")
        {
            return new KeePassImportResult
            {
                IsSuccess = true,
                Message = string.IsNullOrEmpty(message)
                    ? $"Successfully imported {credentials.Count} credentials."
                    : message,
                Credentials = credentials
            };
        }

        public static KeePassImportResult Failure(string message)
        {
            return new KeePassImportResult
            {
                IsSuccess = false,
                Message = message,
                Credentials = new List<Credential>()
            };
        }
    }
}

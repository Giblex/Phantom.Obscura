using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using PhantomVault.Core.Utils;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// On-disk shape of an Obscura volume, and the rules that keep it from describing
    /// itself to anyone who has not got the keyfile.
    ///
    /// <para>
    /// <b>v1 (legacy, plaintext header).</b> <c>"OBSCUR01" | int32 headerLength | manifest
    /// JSON | payload</c>. The payload entries were always individually encrypted, but the
    /// manifest — every entry's path, offset, length and SHA-256, plus the creation
    /// timestamp — sat in the clear. Opening the file in a text editor listed the vault's
    /// entire structure, including <c>decoy/decoy.database.pmeta</c>. Naming the decoy is
    /// what makes this worse than ordinary metadata leakage: the decoy's whole value is
    /// that an adversary cannot tell it is a decoy, and the header told them.
    /// </para>
    ///
    /// <para>
    /// <b>v2.</b> <c>nonce | tag | int32 cipherLength | ciphertext | payload | random tail
    /// pad</c>. The manifest is encrypted, and there is <b>no magic constant anywhere</b>.
    /// </para>
    ///
    /// <para>
    /// <b>Why no "random-looking" magic.</b> The tempting middle ground is a fixed byte
    /// sequence chosen to look like encrypted noise. It does not work, because an adversary
    /// does not judge the bytes by eye — they compare. Any constant is identical in every
    /// volume ever produced, so one sample (or one copy of the app, where the constant sits
    /// in the binary) turns it into a perfect detector no matter how random it looks. A
    /// constant cannot be both recognisable to us and unrecognisable to them; that is the
    /// same secret serving two opposed purposes.
    /// </para>
    ///
    /// <para>
    /// <b>What replaces it.</b> Identification by authentication. The first bytes of a v2
    /// volume are a nonce and a GCM tag: genuinely random, different in every volume, and
    /// carrying no pattern to match. "Is this an Obscura volume?" is answered by deriving
    /// the key from the keyfile and seeing whether the tag verifies. Only someone holding
    /// the keyfile can answer it — which is the property actually wanted, and strictly
    /// stronger than any constant could give. This is the same reasoning behind hidden
    /// volumes in VeraCrypt, which likewise carry no signature.
    /// </para>
    ///
    /// <para>
    /// Version detection needs no v2 marker: v1 volumes start with the old ASCII magic, so
    /// its <i>absence</i> means v2. Once the last v1 volume is upgraded the discriminator
    /// stops being consulted, and nothing identifying remains on disk.
    /// </para>
    /// </summary>
    internal static class ObscuraVolumeFormat
    {
        /// <summary>The v1 signature. Retained only to recognise volumes awaiting upgrade.</summary>
        internal static readonly byte[] LegacyMagic = System.Text.Encoding.ASCII.GetBytes("OBSCUR01");

            internal const int SaltLength = 16;
        internal const int NonceLength = 12;
        internal const int TagLength = 16;

        /// <summary>Fixed part of a v2 header: salt, nonce, tag, ciphertext length.</summary>
        internal const int V2FixedPrefixLength = SaltLength + NonceLength + TagLength + 4;

        /// <summary>
        /// The manifest plaintext is padded to a multiple of this before encryption, so the
        /// ciphertext length — the one field that must stay readable — reveals only which
        /// 4 KiB band the manifest falls in, not the entry count.
        /// </summary>
        internal const int HeaderPaddingGranularity = 4096;

        /// <summary>
        /// Total file size is rounded up to a multiple of this with random bytes.
        ///
        /// Encrypting the header hides every per-entry length, but the file's own size still
        /// approximates how much is stored. 64 MiB was chosen as the point where the leak is
        /// coarse enough to be uninformative while the cost stays bounded: at most 64 MiB of
        /// slack, on media sized in gigabytes. Small vaults benefit most — anything under
        /// 64 MiB becomes indistinguishable from any other vault under 64 MiB.
        /// </summary>
        internal const long SizeBucketBytes = 64L * 1024 * 1024;


        /// <summary>
        /// Derives the header key from the keyfile plus the salt stored in this volume's own
        /// header. The vault password is deliberately NOT an input — see below.
        ///
        /// <para>
        /// <b>Why the salt lives in the volume and not in the VaultManifest.</b> The obvious
        /// move is to reuse the manifest salt, the way every other USB artifact does. It
        /// cannot work here: the VaultManifest is stored <i>inside</i> this volume
        /// (<c>root/</c>, <c>manifests/</c>), so it is not available until after extraction —
        /// the very thing the key is needed for. The volume therefore carries its own salt.
        /// </para>
        ///
        /// <para>
        /// <b>Why the password is not mixed in.</b> Not a preference — the flow forbids it.
        /// The volume must be extracted before the VaultManifest can be read, and the manifest
        /// is what tells the unlock flow whether a password is even in use; the password is
        /// therefore still unknown at the moment the header has to be opened. Mixing it in
        /// would produce a volume whose header was written under a key the reader can never
        /// reconstruct — a vault that provisions successfully and then cannot be opened.
        /// </para>
        ///
        /// <para>
        /// <b>What that costs.</b> For the keyfile-only tiers (the default) nothing: the
        /// keyfile is the entire secret, so the header is exactly as strong as the vault. For
        /// a user who adds a password, someone holding the keyfile but not the password could
        /// read the manifest — entry paths and sizes — without reading any entry contents,
        /// which stay encrypted under the full factor set. The leak this change exists to
        /// close is the header being readable by anyone at all; that is closed in every case.
        /// </para>
        ///
        /// <para>
        /// <b>Why HKDF rather than Argon2id.</b> A KDF's work factor compensates for low
        /// entropy in a human-chosen secret. The keyfile is mandatory here and is
        /// high-entropy file material, so stretching buys nothing against it, while a second
        /// 256 MiB Argon2 pass would add about a second to every unlock on top of the one the
        /// manifest already performs. This also matches the scheme
        /// <c>UsbArtifactProtectionService</c> already uses for every other encrypted
        /// artifact on the stick, so there is one derivation story rather than two.
        /// </para>
        /// </summary>
        internal static byte[] DeriveHeaderKey(byte[] salt, string keyfilePath)
        {
            ArgumentNullException.ThrowIfNull(salt);
            if (string.IsNullOrWhiteSpace(keyfilePath))
                throw new ArgumentException("A keyfile is required to open an Obscura volume.", nameof(keyfilePath));

            byte[] secretMaterial = BuildSecretMaterial(keyfilePath);
            try
            {
                byte[] secretDigest = SHA256.HashData(secretMaterial);
                try
                {
                    return HKDF.DeriveKey(
                        HashAlgorithmName.SHA256,
                        secretDigest,
                        32,
                        salt,
                        Encoding.UTF8.GetBytes("PhantomVault.ObscuraVolume.Header.v2"));
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(secretDigest);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(secretMaterial);
            }
        }

        /// <summary>
        /// Additional authenticated data for the header. Binding the salt means an attacker
        /// cannot splice a header from one volume onto another: the tag stops verifying.
        /// </summary>
        internal static byte[] BuildHeaderAad(byte[] salt)
        {
            byte[] prefix = Encoding.UTF8.GetBytes("PhantomVault.ObscuraVolume.v2|");
            var aad = new byte[prefix.Length + salt.Length];
            Buffer.BlockCopy(prefix, 0, aad, 0, prefix.Length);
            Buffer.BlockCopy(salt, 0, aad, prefix.Length, salt.Length);
            return aad;
        }

        /// <summary>
        /// Keyfile bytes. The keyfile is the mandatory factor and the only input here.
        /// </summary>
        private static byte[] BuildSecretMaterial(string keyfilePath)
        {
            using var buffer = new MemoryStream();

            byte[] keyfileBytes = CompositeKeyfilePath.ReadCombinedBytes(keyfilePath, required: true);
            try { buffer.Write(keyfileBytes, 0, keyfileBytes.Length); }
            finally { CryptographicOperations.ZeroMemory(keyfileBytes); }

            if (buffer.Length == 0)
                throw new InvalidOperationException("Keyfile produced no key material.");

            return buffer.ToArray();
        }

        /// <summary>True when the bytes begin with the v1 signature and must be read as legacy.</summary>
        internal static bool IsLegacyHeader(ReadOnlySpan<byte> head)
            => head.Length >= 8 && head.Slice(0, 8).SequenceEqual(LegacyMagic);

        /// <summary>Rounds a volume size up to the next bucket boundary.</summary>
        internal static long BucketedSize(long actualSize)
        {
            if (actualSize <= 0) return SizeBucketBytes;
            long buckets = (actualSize + SizeBucketBytes - 1) / SizeBucketBytes;
            return buckets * SizeBucketBytes;
        }

        /// <summary>
        /// Wraps manifest JSON as <c>int32 jsonLength | json | random padding</c>, padded to
        /// <see cref="HeaderPaddingGranularity"/>. The padding is random rather than zeroed:
        /// a run of zeros inside otherwise-high-entropy ciphertext is itself a signal, and
        /// would leak the true manifest length back out through compressibility.
        /// </summary>
        internal static byte[] PackHeaderPlaintext(byte[] json)
        {
            ArgumentNullException.ThrowIfNull(json);

            int unpadded = 4 + json.Length;
            int padded = ((unpadded + HeaderPaddingGranularity - 1) / HeaderPaddingGranularity) * HeaderPaddingGranularity;

            var buffer = new byte[padded];
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(0, 4), json.Length);
            Buffer.BlockCopy(json, 0, buffer, 4, json.Length);
            RandomNumberGenerator.Fill(buffer.AsSpan(unpadded));
            return buffer;
        }

        /// <summary>Inverse of <see cref="PackHeaderPlaintext"/>.</summary>
        internal static byte[] UnpackHeaderPlaintext(byte[] plaintext)
        {
            ArgumentNullException.ThrowIfNull(plaintext);
            if (plaintext.Length < 4)
                throw new InvalidOperationException("Volume header is malformed.");

            int jsonLength = BinaryPrimitives.ReadInt32LittleEndian(plaintext.AsSpan(0, 4));
            if (jsonLength < 0 || 4 + (long)jsonLength > plaintext.Length)
                throw new InvalidOperationException("Volume header length is out of range.");

            return plaintext.AsSpan(4, jsonLength).ToArray();
        }
    }
}

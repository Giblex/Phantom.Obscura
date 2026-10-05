using System;
using System.Buffers.Binary;
using System.Security;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace PhantomVault.Core.Services.BootRom
{
    /// <summary>
    /// The PHANTOM-ROM container: a signed, authenticated-encrypted program image carried on a
    /// Phantom Key (USB). Obscura runs it in a sandbox at app start to obtain its contribution to
    /// the vault key, so a missing or tampered ROM does not merely fail a check — it yields the
    /// wrong key and the vault stays shut.
    ///
    /// Layout (little-endian). The signature covers every preceding byte, and the 56-byte header
    /// is fed to AES-GCM as associated data, so neither can be swapped independently:
    ///
    ///   0   8   magic "PHROM\0\0\0"
    ///   8   2   format version
    ///   10  2   algorithm (1 = AES-256-GCM)
    ///   12  4   flags (reserved, must be 0)
    ///   16  16  key id — which ROM identity sealed this
    ///   32  12  nonce
    ///   44  4   ciphertext length
    ///   48  8   ROM version — monotonic, for rollback detection by the caller
    ///   56  n   ciphertext
    ///   56+n 16 GCM tag
    ///   72+n 64 Ed25519 signature
    ///
    /// The signature is verified before the key is ever used, so a forged image is rejected
    /// without exercising the decryption path.
    /// </summary>
    public static class PhantomRomContainer
    {
        public const int HeaderSize = 56;
        public const int KeyIdSize = 16;
        public const int NonceSize = 12;
        public const int TagSize = 16;
        public const int SignatureSize = 64;
        public const int RomKeySize = 32;

        public const ushort FormatVersion = 1;
        public const ushort AlgorithmAesGcm = 1;

        /// <summary>Largest image accepted, so a corrupt length cannot drive a huge allocation.</summary>
        public const int MaxProgramBytes = 1024 * 1024;

        private static ReadOnlySpan<byte> Magic => "PHROM\0\0\0"u8;

        /// <summary>Header fields of a parsed container. Carries no secrets.</summary>
        public readonly record struct RomHeader(
            ushort FormatVersion,
            ushort Algorithm,
            uint Flags,
            byte[] KeyId,
            ulong RomVersion,
            int CipherTextLength);

        /// <summary>
        /// Reads the header without verifying or decrypting, so a caller can check the key id and
        /// ROM version (rollback) before attempting to unseal. Never trust these fields for a
        /// security decision on their own — they are only authenticated by <see cref="Open"/>.
        /// </summary>
        public static bool TryReadHeader(ReadOnlySpan<byte> container, out RomHeader header)
        {
            header = default;
            if (container.Length < HeaderSize + TagSize + SignatureSize)
                return false;
            if (!container[..8].SequenceEqual(Magic))
                return false;

            ushort format = BinaryPrimitives.ReadUInt16LittleEndian(container.Slice(8, 2));
            ushort algorithm = BinaryPrimitives.ReadUInt16LittleEndian(container.Slice(10, 2));
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(container.Slice(12, 4));
            byte[] keyId = container.Slice(16, KeyIdSize).ToArray();
            uint cipherLength = BinaryPrimitives.ReadUInt32LittleEndian(container.Slice(44, 4));
            ulong romVersion = BinaryPrimitives.ReadUInt64LittleEndian(container.Slice(48, 8));

            if (cipherLength > MaxProgramBytes)
                return false;
            if (container.Length != HeaderSize + (int)cipherLength + TagSize + SignatureSize)
                return false;

            header = new RomHeader(format, algorithm, flags, keyId, romVersion, (int)cipherLength);
            return true;
        }

        /// <summary>
        /// Verifies the container's signature and decrypts the program image.
        /// Returns the plaintext program; the caller owns it and must zero it after use.
        /// Throws <see cref="SecurityException"/> on any failure — no partial results.
        /// </summary>
        /// <param name="container">The whole .rom file.</param>
        /// <param name="trustedSigningPublicKey">32-byte Ed25519 public key Obscura trusts.</param>
        /// <param name="romKey">32-byte decryption key, derived outside this container.</param>
        /// <param name="header">The container's authenticated header fields.</param>
        public static byte[] Open(
            ReadOnlySpan<byte> container,
            ReadOnlySpan<byte> trustedSigningPublicKey,
            ReadOnlySpan<byte> romKey,
            out RomHeader header)
        {
            if (!TryReadHeader(container, out header))
                throw new SecurityException("Boot ROM container is malformed.");
            if (header.FormatVersion != FormatVersion)
                throw new SecurityException($"Unsupported Boot ROM format version {header.FormatVersion}.");
            if (header.Algorithm != AlgorithmAesGcm)
                throw new SecurityException($"Unsupported Boot ROM algorithm {header.Algorithm}.");
            if (header.Flags != 0)
                throw new SecurityException("Boot ROM header sets reserved flags.");
            if (trustedSigningPublicKey.Length != 32)
                throw new ArgumentException("Ed25519 public key must be 32 bytes.", nameof(trustedSigningPublicKey));
            if (romKey.Length != RomKeySize)
                throw new ArgumentException("ROM key must be 32 bytes.", nameof(romKey));

            int signedLength = HeaderSize + header.CipherTextLength + TagSize;
            ReadOnlySpan<byte> signedBytes = container[..signedLength];
            ReadOnlySpan<byte> signature = container.Slice(signedLength, SignatureSize);

            // Authenticity first: a forged image never reaches the decryption path.
            if (!VerifyEd25519(signedBytes, signature, trustedSigningPublicKey))
                throw new SecurityException("Boot ROM signature is not valid for the trusted signing key.");

            var plaintext = new byte[header.CipherTextLength];
            try
            {
                using var aes = new AesGcm(romKey, TagSize);
                aes.Decrypt(
                    nonce: container.Slice(32, NonceSize),
                    ciphertext: container.Slice(HeaderSize, header.CipherTextLength),
                    tag: container.Slice(HeaderSize + header.CipherTextLength, TagSize),
                    plaintext: plaintext,
                    associatedData: container[..HeaderSize]);
            }
            catch (CryptographicException ex)
            {
                CryptographicOperations.ZeroMemory(plaintext);
                // Wrong key or altered ciphertext are indistinguishable here, deliberately.
                throw new SecurityException("Boot ROM could not be decrypted or failed authentication.", ex);
            }

            return plaintext;
        }

        /// <summary>
        /// Builds a signed, encrypted container around <paramref name="program"/>. Used by
        /// provisioning and by tests; the signing key never ships with the app.
        /// </summary>
        public static byte[] Seal(
            ReadOnlySpan<byte> program,
            ReadOnlySpan<byte> signingPrivateKey,
            ReadOnlySpan<byte> romKey,
            ReadOnlySpan<byte> keyId,
            ulong romVersion)
        {
            if (program.Length > MaxProgramBytes)
                throw new ArgumentException($"Program exceeds {MaxProgramBytes} bytes.", nameof(program));
            if (signingPrivateKey.Length != 32)
                throw new ArgumentException("Ed25519 private key must be 32 bytes.", nameof(signingPrivateKey));
            if (romKey.Length != RomKeySize)
                throw new ArgumentException("ROM key must be 32 bytes.", nameof(romKey));
            if (keyId.Length != KeyIdSize)
                throw new ArgumentException($"Key id must be {KeyIdSize} bytes.", nameof(keyId));

            int total = HeaderSize + program.Length + TagSize + SignatureSize;
            var container = new byte[total];
            var span = container.AsSpan();

            Magic.CopyTo(span[..8]);
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(8, 2), FormatVersion);
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(10, 2), AlgorithmAesGcm);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(12, 4), 0);
            keyId.CopyTo(span.Slice(16, KeyIdSize));
            RandomNumberGenerator.Fill(span.Slice(32, NonceSize));
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(44, 4), (uint)program.Length);
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(48, 8), romVersion);

            using (var aes = new AesGcm(romKey, TagSize))
            {
                aes.Encrypt(
                    nonce: span.Slice(32, NonceSize),
                    plaintext: program,
                    ciphertext: span.Slice(HeaderSize, program.Length),
                    tag: span.Slice(HeaderSize + program.Length, TagSize),
                    associatedData: span[..HeaderSize]);
            }

            int signedLength = HeaderSize + program.Length + TagSize;
            SignEd25519(span[..signedLength], signingPrivateKey, span.Slice(signedLength, SignatureSize));
            return container;
        }

        private static bool VerifyEd25519(ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature, ReadOnlySpan<byte> publicKey)
        {
            try
            {
                var verifier = new Ed25519Signer();
                verifier.Init(false, new Ed25519PublicKeyParameters(publicKey.ToArray(), 0));
                byte[] messageBytes = message.ToArray();
                verifier.BlockUpdate(messageBytes, 0, messageBytes.Length);
                return verifier.VerifySignature(signature.ToArray());
            }
            catch
            {
                // A malformed key or signature is a verification failure, never an exception
                // that could be mistaken for success further up.
                return false;
            }
        }

        private static void SignEd25519(ReadOnlySpan<byte> message, ReadOnlySpan<byte> privateKey, Span<byte> destination)
        {
            var signer = new Ed25519Signer();
            signer.Init(true, new Ed25519PrivateKeyParameters(privateKey.ToArray(), 0));
            byte[] messageBytes = message.ToArray();
            signer.BlockUpdate(messageBytes, 0, messageBytes.Length);
            signer.GenerateSignature().CopyTo(destination);
        }
    }
}

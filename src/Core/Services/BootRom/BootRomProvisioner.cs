using System;
using System.IO;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace PhantomVault.Core.Services.BootRom
{
    /// <summary>What provisioning produced. The recovery code is shown once and never stored.</summary>
    public sealed class BootRomProvisionResult
    {
        public required byte[] Contribution { get; init; }
        public required string RecoveryCode { get; init; }
        public required BootRomMarker Marker { get; init; }
    }

    /// <summary>
    /// Creates a vault's Boot ROM: generates the attestation program, seals it to the device, and
    /// writes the marker and the recovery escrow.
    ///
    /// The generated ROM carries a per-vault secret and releases it only when the environment
    /// matches what was recorded here. That secret is the vault's key contribution, so refusing
    /// to release it is the same act as withholding the key — there is no verdict to patch around.
    /// </summary>
    public sealed class BootRomProvisioner
    {
        // Memory map the generated program uses.
        private const int AddrExpectedIntegrity = 0;
        private const int AddrExpectedBinding = 32;
        private const int AddrActualIntegrity = 64;
        private const int AddrActualBinding = 96;
        private const int AddrSecret = 128;

        /// <summary>Verdict the ROM emits when the environment does not match.</summary>
        public const long RefusedVerdict = 1;

        /// <summary>
        /// Provisions Boot ROM binding for the vault on <paramref name="usbRoot"/>.
        /// </summary>
        /// <param name="usbRoot">The vault drive.</param>
        /// <param name="keyfilePath">Vault keyfile material the ROM key is derived from.</param>
        /// <param name="expectedIntegrity">
        /// Integrity digest the ROM will require, or null to skip that check — appropriate when
        /// the privileged helper is not installed, since the ROM would otherwise refuse forever.
        /// </param>
        /// <param name="expectedBinding">Binding digest the ROM will require, or null to skip.</param>
        /// <param name="romVersion">Version stamped into the image, for the rollback floor.</param>
        public BootRomProvisionResult Provision(
            string usbRoot,
            string keyfilePath,
            byte[]? expectedIntegrity,
            byte[]? expectedBinding,
            ulong romVersion = 1)
        {
            if (string.IsNullOrWhiteSpace(usbRoot)) throw new ArgumentException("Drive root required.", nameof(usbRoot));
            if (expectedIntegrity is { Length: not 32 }) throw new ArgumentException("Integrity digest must be 32 bytes.", nameof(expectedIntegrity));
            if (expectedBinding is { Length: not 32 }) throw new ArgumentException("Binding digest must be 32 bytes.", nameof(expectedBinding));

            byte[] secret = RandomNumberGenerator.GetBytes(32);
            byte[] keyId = RandomNumberGenerator.GetBytes(PhantomRomContainer.KeyIdSize);
            byte[] salt = RandomNumberGenerator.GetBytes(32);
            byte[] escrowSalt = RandomNumberGenerator.GetBytes(32);

            var (signingPrivate, signingPublic) = GenerateSigningKeys();
            byte[] program = BuildAttestationProgram(secret, expectedIntegrity, expectedBinding);

            byte[]? romKey = null;
            byte[]? container = null;
            try
            {
                romKey = BootRomKeyDerivation.DeriveRomKey(keyfilePath, salt, keyId);
                container = PhantomRomContainer.Seal(program, signingPrivate, romKey, keyId, romVersion);

                string recoveryCode = BootRomKeyDerivation.GenerateRecoveryCode();
                byte[] escrow = BootRomKeyDerivation.WrapContributionForRecovery(secret, recoveryCode, escrowSalt);

                var marker = new BootRomMarker
                {
                    KeyIdBase64 = Convert.ToBase64String(keyId),
                    MinRomVersion = romVersion,
                    SaltBase64 = Convert.ToBase64String(salt),
                    SigningPublicKeyBase64 = Convert.ToBase64String(signingPublic),
                    EscrowSaltBase64 = Convert.ToBase64String(escrowSalt)
                };

                Directory.CreateDirectory(BootRomMarker.BootDirectory(usbRoot));
                File.WriteAllBytes(BootRomMarker.RomPath(usbRoot), container);
                File.WriteAllBytes(BootRomMarker.EscrowPath(usbRoot), escrow);
                marker.Save(usbRoot);

                return new BootRomProvisionResult
                {
                    // The contribution is the secret the ROM releases on a good environment.
                    Contribution = secret.AsSpan().ToArray(),
                    RecoveryCode = recoveryCode,
                    Marker = marker
                };
            }
            finally
            {
                CryptographicOperations.ZeroMemory(program);
                CryptographicOperations.ZeroMemory(secret);
                CryptographicOperations.ZeroMemory(signingPrivate);
                if (romKey is not null) CryptographicOperations.ZeroMemory(romKey);
                if (container is not null) CryptographicOperations.ZeroMemory(container);
            }
        }

        /// <summary>Removes Boot ROM binding artefacts from a drive, after the vault is re-wrapped.</summary>
        public static void Remove(string usbRoot)
        {
            foreach (var path in new[]
                     {
                         BootRomMarker.MarkerPath(usbRoot),
                         BootRomMarker.RomPath(usbRoot),
                         BootRomMarker.EscrowPath(usbRoot)
                     })
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Leaving a stale artefact behind is harmless: the vault is no longer bound,
                    // and an orphaned marker only causes a failed run that falls back to unbound.
                }
            }
        }

        /// <summary>
        /// Emits the attestation program: compare the environment against what was recorded, and
        /// release the secret only on a match.
        /// </summary>
        internal static byte[] BuildAttestationProgram(
            ReadOnlySpan<byte> secret,
            byte[]? expectedIntegrity,
            byte[]? expectedBinding)
        {
            var builder = new RomProgramBuilder();

            if (expectedIntegrity is not null)
            {
                builder.StoreBytes(AddrExpectedIntegrity, expectedIntegrity);
                builder.Push(AddrActualIntegrity).Syscall(RomProgramBuilder.SysReadIntegrity);
            }

            if (expectedBinding is not null)
            {
                builder.StoreBytes(AddrExpectedBinding, expectedBinding);
                builder.Push(AddrActualBinding).Syscall(RomProgramBuilder.SysReadBinding);
            }

            // Accumulator starts true, then ANDs in each 64-bit chunk comparison.
            builder.Push(1);

            if (expectedIntegrity is not null)
                EmitBlockCompare(builder, AddrExpectedIntegrity, AddrActualIntegrity);
            if (expectedBinding is not null)
                EmitBlockCompare(builder, AddrExpectedBinding, AddrActualBinding);

            builder.Jump(RomProgramBuilder.OpJz, "refuse");

            builder.StoreBytes(AddrSecret, secret);
            builder.Push(AddrSecret).Syscall(RomProgramBuilder.SysEmitContribution);
            builder.Push(0).Syscall(RomProgramBuilder.SysEmitVerdict);
            builder.Op(RomProgramBuilder.OpHalt);

            builder.Label("refuse");
            builder.Push(RefusedVerdict).Syscall(RomProgramBuilder.SysEmitVerdict);
            builder.Op(RomProgramBuilder.OpHalt);

            return builder.Build();
        }

        /// <summary>Compares 32 bytes as four 64-bit words, ANDing the result into the accumulator.</summary>
        private static void EmitBlockCompare(RomProgramBuilder builder, int expectedAddress, int actualAddress)
        {
            for (int offset = 0; offset < 32; offset += 8)
            {
                builder.Push(expectedAddress + offset).Op(RomProgramBuilder.OpLoad64);
                builder.Push(actualAddress + offset).Op(RomProgramBuilder.OpLoad64);
                builder.Op(RomProgramBuilder.OpEq);
                builder.Op(RomProgramBuilder.OpAnd);
            }
        }

        private static (byte[] PrivateKey, byte[] PublicKey) GenerateSigningKeys()
        {
            var generator = new Ed25519KeyPairGenerator();
            generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
            var pair = generator.GenerateKeyPair();
            return (((Ed25519PrivateKeyParameters)pair.Private).GetEncoded(),
                    ((Ed25519PublicKeyParameters)pair.Public).GetEncoded());
        }
    }
}

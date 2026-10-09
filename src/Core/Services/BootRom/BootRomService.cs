using System;
using System.IO;
using System.Security;
using System.Security.Cryptography;

namespace PhantomVault.Core.Services.BootRom
{
    /// <summary>How a Boot ROM run ended. Anything but <see cref="Success"/> leaves the vault shut.</summary>
    public enum BootRomStatus
    {
        /// <summary>No marker on the drive: this vault is not ROM bound, unlock proceeds normally.</summary>
        NotBound,
        Success,
        /// <summary>Marker present but the image is missing or unreadable.</summary>
        RomMissing,
        /// <summary>Signature, header or authentication failed — forged, corrupt, or wrong device.</summary>
        RomRejected,
        /// <summary>Image is older than the vault's floor: a rollback attempt.</summary>
        RollbackBlocked,
        /// <summary>The ROM ran and declined to release its contribution.</summary>
        RomRefused,
        /// <summary>The ROM broke the sandbox's rules.</summary>
        RomFaulted
    }

    /// <summary>Result of a Boot ROM run. <see cref="Contribution"/> is set only on success.</summary>
    public sealed class BootRomOutcome
    {
        public BootRomStatus Status { get; init; }
        public byte[]? Contribution { get; init; }
        public long Verdict { get; init; }
        public string Message { get; init; } = string.Empty;

        public bool IsSuccess => Status == BootRomStatus.Success && Contribution is { Length: 32 };
    }

    /// <summary>
    /// The inputs a Boot ROM is allowed to see. Everything is supplied here, so a run depends on
    /// nothing but what the host chooses to hand over.
    /// </summary>
    public sealed class BootRomHost : IRomHost
    {
        private readonly byte[] _challenge = RandomNumberGenerator.GetBytes(32);
        private readonly byte[] _integrity;
        private readonly byte[] _binding;
        private byte[]? _contribution;
        private long _verdict = -1;
        private bool _verdictEmitted;

        public BootRomHost(ReadOnlySpan<byte> integrityDigest, ReadOnlySpan<byte> bindingDigest)
        {
            if (integrityDigest.Length != 32) throw new ArgumentException("Integrity digest must be 32 bytes.", nameof(integrityDigest));
            if (bindingDigest.Length != 32) throw new ArgumentException("Binding digest must be 32 bytes.", nameof(bindingDigest));
            _integrity = integrityDigest.ToArray();
            _binding = bindingDigest.ToArray();
        }

        public byte[]? Contribution => _contribution;
        public long Verdict => _verdict;
        public bool VerdictEmitted => _verdictEmitted;

        public void ReadChallenge(Span<byte> destination) => _challenge.CopyTo(destination);
        public void ReadIntegrity(Span<byte> destination) => _integrity.CopyTo(destination);
        public void ReadBinding(Span<byte> destination) => _binding.CopyTo(destination);

        public void EmitContribution(ReadOnlySpan<byte> contribution)
        {
            if (_contribution is not null) CryptographicOperations.ZeroMemory(_contribution);
            _contribution = contribution.ToArray();
        }

        public void EmitVerdict(long code)
        {
            _verdict = code;
            _verdictEmitted = true;
        }

        /// <summary>Wipes the host's copies of the challenge, digests and contribution.</summary>
        public void Wipe()
        {
            CryptographicOperations.ZeroMemory(_challenge);
            CryptographicOperations.ZeroMemory(_integrity);
            CryptographicOperations.ZeroMemory(_binding);
            if (_contribution is not null) CryptographicOperations.ZeroMemory(_contribution);
            _contribution = null;
        }
    }

    /// <summary>
    /// Runs a vault's Boot ROM: reads the marker and sealed image from the drive, derives the ROM
    /// key from the vault's keyfile material, verifies and decrypts into memory, executes it in
    /// the sandbox, and returns its contribution.
    ///
    /// The decrypted image never touches disk and is zeroed before this returns, whatever the
    /// outcome. Every failure path is closed: callers register a contribution only on success, so
    /// a failed run simply means the vault key cannot be derived.
    /// </summary>
    public sealed class BootRomService
    {
        private readonly RomVmLimits _limits;

        public BootRomService(RomVmLimits? limits = null) => _limits = limits ?? RomVmLimits.Default;

        /// <summary>True when this drive carries a Boot ROM marker.</summary>
        public static bool IsBound(string usbRoot) => BootRomMarker.TryLoad(usbRoot) is not null;

        /// <summary>
        /// Runs the Boot ROM for a drive. <paramref name="keyfilePath"/> is the vault's keyfile
        /// (composite, including the host companion key when one exists) — the material a cloned
        /// stick cannot supply on its own.
        /// </summary>
        public BootRomOutcome Run(
            string usbRoot,
            string keyfilePath,
            ReadOnlySpan<byte> integrityDigest,
            ReadOnlySpan<byte> bindingDigest)
        {
            var marker = BootRomMarker.TryLoad(usbRoot);
            if (marker is null)
                return new BootRomOutcome { Status = BootRomStatus.NotBound, Message = "This vault is not Boot ROM bound." };

            byte[] container;
            try
            {
                string romPath = BootRomMarker.RomPath(usbRoot);
                if (!File.Exists(romPath))
                    return new BootRomOutcome { Status = BootRomStatus.RomMissing, Message = "The Boot ROM image is missing from this device." };

                using var image = File.Open(romPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                long length = image.Length;
                if (length < PhantomRomContainer.HeaderSize + PhantomRomContainer.TagSize + PhantomRomContainer.SignatureSize ||
                    length > PhantomRomContainer.MaxContainerBytes)
                    return new BootRomOutcome
                    {
                        Status = BootRomStatus.RomRejected,
                        Message = "The Boot ROM image size is outside the supported range."
                    };

                container = new byte[(int)length];
                image.ReadExactly(container);
                if (image.ReadByte() != -1)
                    return new BootRomOutcome
                    {
                        Status = BootRomStatus.RomRejected,
                        Message = "The Boot ROM image changed while being read."
                    };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new BootRomOutcome { Status = BootRomStatus.RomMissing, Message = "The Boot ROM image could not be read." };
            }

            byte[] keyId, salt, signingKey;
            try
            {
                keyId = Convert.FromBase64String(marker.KeyIdBase64);
                salt = Convert.FromBase64String(marker.SaltBase64);
                signingKey = Convert.FromBase64String(marker.SigningPublicKeyBase64);
            }
            catch (FormatException)
            {
                return new BootRomOutcome { Status = BootRomStatus.RomRejected, Message = "The Boot ROM marker is malformed." };
            }
            if (keyId.Length != PhantomRomContainer.KeyIdSize || salt.Length != 32 || signingKey.Length != 32)
                return new BootRomOutcome { Status = BootRomStatus.RomRejected, Message = "The Boot ROM marker is malformed." };

            // Rollback floor, checked before unsealing. The header is authenticated during Open,
            // so a forged version cannot survive — this only avoids wasted work on an obvious one.
            if (PhantomRomContainer.TryReadHeader(container, out var preview) && preview.RomVersion < marker.MinRomVersion)
            {
                return new BootRomOutcome
                {
                    Status = BootRomStatus.RollbackBlocked,
                    Message = $"The Boot ROM image (version {preview.RomVersion}) is older than this vault accepts."
                };
            }

            byte[]? romKey = null;
            byte[]? program = null;
            var host = new BootRomHost(integrityDigest, bindingDigest);
            try
            {
                romKey = BootRomKeyDerivation.DeriveRomKey(keyfilePath, salt, keyId);
                program = PhantomRomContainer.Open(container, signingKey, romKey, out var header);

                if (header.RomVersion < marker.MinRomVersion)
                {
                    return new BootRomOutcome
                    {
                        Status = BootRomStatus.RollbackBlocked,
                        Message = $"The Boot ROM image (version {header.RomVersion}) is older than this vault accepts."
                    };
                }

                var result = PhantomRomVm.Execute(program, host, _limits);

                if (result.Verdict != 0)
                {
                    return new BootRomOutcome
                    {
                        Status = BootRomStatus.RomRefused,
                        Verdict = result.Verdict,
                        Message = $"The Boot ROM declined to authorise this device (code {result.Verdict})."
                    };
                }

                if (!result.ContributionEmitted || host.Contribution is not { Length: 32 })
                {
                    return new BootRomOutcome
                    {
                        Status = BootRomStatus.RomRefused,
                        Verdict = result.Verdict,
                        Message = "The Boot ROM authorised the device but released no key material."
                    };
                }

                return new BootRomOutcome
                {
                    Status = BootRomStatus.Success,
                    Contribution = host.Contribution.AsSpan().ToArray(),
                    Verdict = 0,
                    Message = "Boot ROM verified."
                };
            }
            catch (SecurityException ex)
            {
                return new BootRomOutcome { Status = BootRomStatus.RomRejected, Message = ex.Message };
            }
            catch (RomFaultException ex)
            {
                return new BootRomOutcome { Status = BootRomStatus.RomFaulted, Message = ex.Message };
            }
            finally
            {
                if (program is not null) CryptographicOperations.ZeroMemory(program);
                if (romKey is not null) CryptographicOperations.ZeroMemory(romKey);
                host.Wipe();
            }
        }
    }
}

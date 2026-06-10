using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace PhantomVault.Core.Services.Update
{
    /// <summary>
    /// Default <see cref="IUpdateVerifier"/>. See the interface for the contract.
    /// </summary>
    public sealed class UpdateVerifier : IUpdateVerifier
    {
        private const int Ed25519PublicKeySize = 32;
        private const int Ed25519SignatureSize = 64;

        // Production builds use the embedded UpdatePublicKey. Tests inject a
        // synthetic keypair via the internal constructor and InternalsVisibleTo.
        private readonly byte[]? _testPublicKey;

        public UpdateVerifier()
        {
            _testPublicKey = null;
        }

        internal UpdateVerifier(byte[] testPublicKey)
        {
            if (testPublicKey is null) throw new ArgumentNullException(nameof(testPublicKey));
            if (testPublicKey.Length != Ed25519PublicKeySize)
                throw new ArgumentException($"Test public key must be {Ed25519PublicKeySize} bytes.", nameof(testPublicKey));
            _testPublicKey = (byte[])testPublicKey.Clone();
        }

        private bool IsProvisioned => _testPublicKey is not null || UpdatePublicKey.IsProvisioned;

        private ReadOnlySpan<byte> ActivePublicKey => _testPublicKey is not null
            ? _testPublicKey.AsSpan()
            : UpdatePublicKey.Bytes;

        public UpdateInfo VerifyManifest(
            ReadOnlySpan<byte> manifestBytes,
            ReadOnlySpan<byte> signature,
            UpdateChannel expectedChannel,
            Version installedVersion,
            string expectedAssetHost)
        {
            if (installedVersion is null) throw new ArgumentNullException(nameof(installedVersion));
            if (string.IsNullOrWhiteSpace(expectedAssetHost))
                throw new ArgumentException("Asset host is required.", nameof(expectedAssetHost));

            if (!IsProvisioned)
            {
                throw new UpdateVerificationException(
                    UpdateVerificationFailure.PublicKeyNotProvisioned,
                    "Update verification is disabled: no signing public key is provisioned in this build.");
            }

            if (signature.Length != Ed25519SignatureSize)
            {
                throw new UpdateVerificationException(
                    UpdateVerificationFailure.BadSignature,
                    $"Signature must be {Ed25519SignatureSize} bytes (got {signature.Length}).");
            }
            if (ActivePublicKey.Length != Ed25519PublicKeySize)
            {
                throw new UpdateVerificationException(
                    UpdateVerificationFailure.PublicKeyNotProvisioned,
                    $"Embedded public key must be {Ed25519PublicKeySize} bytes.");
            }

            // 1) Ed25519 — does this manifest come from the offline signing key?
            if (!Ed25519Verify(ActivePublicKey, manifestBytes, signature))
            {
                throw new UpdateVerificationException(
                    UpdateVerificationFailure.BadSignature,
                    "Manifest signature did not verify against the embedded public key.");
            }

            // 2) Parse the now-trusted bytes.
            UpdateManifest manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<UpdateManifest>(
                    manifestBytes,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new UpdateVerificationException(
                        UpdateVerificationFailure.BadManifest,
                        "Manifest JSON was empty.");
            }
            catch (JsonException ex)
            {
                throw new UpdateVerificationException(
                    UpdateVerificationFailure.BadManifest,
                    "Manifest JSON did not parse.",
                    ex);
            }

            if (manifest.Schema != 1)
            {
                throw new UpdateVerificationException(
                    UpdateVerificationFailure.BadManifest,
                    $"Unsupported manifest schema {manifest.Schema}.");
            }

            // 3) Channel pinning.
            UpdateChannel channel = ParseChannel(manifest.Channel);
            if (channel != expectedChannel)
            {
                throw new UpdateVerificationException(
                    UpdateVerificationFailure.ChannelMismatch,
                    $"Manifest is for channel '{channel}'; client is configured for '{expectedChannel}'.");
            }

            // 4) Version monotonicity.
            if (!Version.TryParse(manifest.Version, out var manifestVersion))
            {
                throw new UpdateVerificationException(
                    UpdateVerificationFailure.BadManifest,
                    $"Manifest version '{manifest.Version}' is not a valid Version.");
            }
            if (manifestVersion <= installedVersion)
            {
                throw new UpdateVerificationException(
                    UpdateVerificationFailure.VersionNotNewer,
                    $"Manifest version {manifestVersion} is not newer than installed {installedVersion}.");
            }

            Version? minPrev = null;
            if (!string.IsNullOrWhiteSpace(manifest.MinPreviousVersion))
            {
                if (!Version.TryParse(manifest.MinPreviousVersion, out minPrev))
                {
                    throw new UpdateVerificationException(
                        UpdateVerificationFailure.BadManifest,
                        $"minPreviousVersion '{manifest.MinPreviousVersion}' is not a valid Version.");
                }
                if (installedVersion < minPrev)
                {
                    throw new UpdateVerificationException(
                        UpdateVerificationFailure.UpgradePathBlocked,
                        $"Installed version {installedVersion} is older than required minimum {minPrev} for this update.");
                }
            }

            // 5) Asset URL pinning.
            if (manifest.Asset is null
                || string.IsNullOrWhiteSpace(manifest.Asset.Url)
                || !Uri.TryCreate(manifest.Asset.Url, UriKind.Absolute, out var assetUri)
                || assetUri.Scheme != Uri.UriSchemeHttps)
            {
                throw new UpdateVerificationException(
                    UpdateVerificationFailure.BadManifest,
                    "Manifest asset URL is missing or not an absolute https URL.");
            }
            if (!string.Equals(assetUri.Host, expectedAssetHost, StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdateVerificationException(
                    UpdateVerificationFailure.AssetHostMismatch,
                    $"Manifest asset host '{assetUri.Host}' does not match expected host '{expectedAssetHost}'.");
            }
            if (manifest.Asset.Size <= 0 || manifest.Asset.Size > 1L * 1024 * 1024 * 1024)
            {
                throw new UpdateVerificationException(
                    UpdateVerificationFailure.BadManifest,
                    $"Manifest asset size {manifest.Asset.Size} is out of bounds (must be 1B..1GiB).");
            }
            if (string.IsNullOrWhiteSpace(manifest.Asset.Sha256))
            {
                throw new UpdateVerificationException(
                    UpdateVerificationFailure.BadManifest,
                    "Manifest asset SHA-256 is missing.");
            }

            byte[] assetSha;
            try
            {
                assetSha = Convert.FromBase64String(manifest.Asset.Sha256);
            }
            catch (FormatException ex)
            {
                throw new UpdateVerificationException(
                    UpdateVerificationFailure.BadManifest,
                    "Manifest asset SHA-256 is not valid base64.",
                    ex);
            }
            if (assetSha.Length != 32)
            {
                throw new UpdateVerificationException(
                    UpdateVerificationFailure.BadManifest,
                    $"Manifest asset SHA-256 must be 32 bytes (got {assetSha.Length}).");
            }

            // 6) Authenticode pin (optional).
            byte[]? authThumbprint = null;
            string? authSubject = null;
            if (manifest.Authenticode is not null)
            {
                if (string.IsNullOrWhiteSpace(manifest.Authenticode.Subject))
                {
                    throw new UpdateVerificationException(
                        UpdateVerificationFailure.BadManifest,
                        "Authenticode subject is missing.");
                }
                if (string.IsNullOrWhiteSpace(manifest.Authenticode.ThumbprintSha256)
                    || !TryParseHex(manifest.Authenticode.ThumbprintSha256, out authThumbprint)
                    || authThumbprint.Length != 32)
                {
                    throw new UpdateVerificationException(
                        UpdateVerificationFailure.BadManifest,
                        "Authenticode thumbprint must be a 64-character hex SHA-256.");
                }
                authSubject = manifest.Authenticode.Subject;
            }

            return new UpdateInfo
            {
                Schema = manifest.Schema,
                Channel = channel,
                Version = manifestVersion,
                ReleasedAtUtc = new DateTimeOffset(DateTime.SpecifyKind(manifest.ReleasedAtUtc, DateTimeKind.Utc)),
                MinPreviousVersion = minPrev,
                AssetUrl = assetUri,
                AssetSize = manifest.Asset.Size,
                AssetSha256 = assetSha,
                AuthenticodeSubject = authSubject,
                AuthenticodeThumbprintSha256 = authThumbprint,
            };
        }

        public void VerifyAsset(string assetPath, UpdateInfo info)
        {
            if (assetPath is null) throw new ArgumentNullException(nameof(assetPath));
            if (info is null) throw new ArgumentNullException(nameof(info));
            if (!File.Exists(assetPath))
            {
                throw new UpdateVerificationException(
                    UpdateVerificationFailure.AssetSizeMismatch,
                    "Downloaded asset file does not exist.");
            }

            var len = new FileInfo(assetPath).Length;
            if (len != info.AssetSize)
            {
                throw new UpdateVerificationException(
                    UpdateVerificationFailure.AssetSizeMismatch,
                    $"Downloaded asset is {len} bytes; manifest expected {info.AssetSize}.");
            }

            using (var fs = File.OpenRead(assetPath))
            {
                VerifyAssetStream(fs, info);
            }

            if (info.AuthenticodeSubject is not null && info.AuthenticodeThumbprintSha256 is not null)
            {
                VerifyAuthenticode(assetPath, info.AuthenticodeSubject, info.AuthenticodeThumbprintSha256);
            }
        }

        public void VerifyAssetStream(Stream stream, UpdateInfo info)
        {
            if (stream is null) throw new ArgumentNullException(nameof(stream));
            if (info is null) throw new ArgumentNullException(nameof(info));

            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(stream);
            if (!CryptographicOperations.FixedTimeEquals(hash, info.AssetSha256))
            {
                throw new UpdateVerificationException(
                    UpdateVerificationFailure.AssetHashMismatch,
                    "Downloaded asset SHA-256 does not match the manifest.");
            }
        }

        private static UpdateChannel ParseChannel(string? channel)
        {
            return channel?.Trim().ToLowerInvariant() switch
            {
                "stable" => UpdateChannel.Stable,
                "beta" => UpdateChannel.Beta,
                _ => throw new UpdateVerificationException(
                    UpdateVerificationFailure.BadManifest,
                    $"Manifest channel '{channel}' is not recognised."),
            };
        }

        private static bool Ed25519Verify(ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature)
        {
            // BouncyCastle currently lacks a span-friendly verifier; copy to arrays
            // and zeroize after — the data is public anyway, so this is just hygiene.
            byte[] pkArr = publicKey.ToArray();
            byte[] msgArr = message.ToArray();
            byte[] sigArr = signature.ToArray();
            try
            {
                var verifier = new Ed25519Signer();
                verifier.Init(forSigning: false, new Ed25519PublicKeyParameters(pkArr, 0));
                verifier.BlockUpdate(msgArr, 0, msgArr.Length);
                return verifier.VerifySignature(sigArr);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(pkArr);
                CryptographicOperations.ZeroMemory(msgArr);
                CryptographicOperations.ZeroMemory(sigArr);
            }
        }

        private static bool TryParseHex(string s, out byte[] bytes)
        {
            s = s.Trim();
            if (s.Length % 2 != 0) { bytes = Array.Empty<byte>(); return false; }
            byte[] result = new byte[s.Length / 2];
            for (int i = 0; i < result.Length; i++)
            {
                if (!byte.TryParse(s.AsSpan(i * 2, 2), System.Globalization.NumberStyles.HexNumber, null, out result[i]))
                {
                    bytes = Array.Empty<byte>();
                    return false;
                }
            }
            bytes = result;
            return true;
        }

        private static void VerifyAuthenticode(string filePath, string expectedSubject, byte[] expectedThumbprint)
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new UpdateVerificationException(
                    UpdateVerificationFailure.AuthenticodeFailure,
                    "Authenticode verification is required by the manifest but is only available on Windows.");
            }

            VerifyAuthenticodeWindows(filePath, expectedSubject, expectedThumbprint);
        }

        [SupportedOSPlatform("windows")]
        private static void VerifyAuthenticodeWindows(string filePath, string expectedSubject, byte[] expectedThumbprint)
        {
            // Stage 1: WinVerifyTrust — chain validity, timestamp, revocation.
            uint trustResult = WinVerifyTrustFile(filePath);
            if (trustResult != 0)
            {
                throw new UpdateVerificationException(
                    UpdateVerificationFailure.AuthenticodeFailure,
                    $"WinVerifyTrust rejected the asset (HRESULT 0x{trustResult:X8}).");
            }

            // Stage 2: extract signer cert and pin subject + SHA-256 thumbprint.
            // CreateFromSignedFile is the only API that pulls the Authenticode signer
            // out of a PE; the replacement (X509CertificateLoader) targets PKCS7/PFX,
            // not Authenticode. Suppression is intentional and tightly scoped.
            X509Certificate2 signer;
            try
            {
#pragma warning disable SYSLIB0057
                using var loaded = X509Certificate.CreateFromSignedFile(filePath);
                signer = new X509Certificate2(loaded);
#pragma warning restore SYSLIB0057
            }
            catch (Exception ex)
            {
                throw new UpdateVerificationException(
                    UpdateVerificationFailure.AuthenticodeFailure,
                    "Could not extract Authenticode signer certificate.",
                    ex);
            }

            try
            {
                if (!string.Equals(signer.Subject.Trim(), expectedSubject.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    throw new UpdateVerificationException(
                        UpdateVerificationFailure.AuthenticodePinMismatch,
                        "Authenticode signer subject does not match the manifest pin.");
                }

                byte[] thumb = SHA256.HashData(signer.RawData);
                if (!CryptographicOperations.FixedTimeEquals(thumb, expectedThumbprint))
                {
                    throw new UpdateVerificationException(
                        UpdateVerificationFailure.AuthenticodePinMismatch,
                        "Authenticode signer thumbprint does not match the manifest pin.");
                }
            }
            finally
            {
                signer.Dispose();
            }
        }

        // ─── WinVerifyTrust P/Invoke ────────────────────────────────────────────

        // WinTrust action GUID for generic file-policy verification.
        private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 =
            new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

        private const uint WTD_UI_NONE = 2;
        private const uint WTD_REVOKE_WHOLECHAIN = 1;
        private const uint WTD_CHOICE_FILE = 1;
        private const uint WTD_STATEACTION_VERIFY = 1;
        private const uint WTD_STATEACTION_CLOSE = 2;
        private const uint WTD_SAFER_FLAG = 0x100;

        [SupportedOSPlatform("windows")]
        private static uint WinVerifyTrustFile(string filePath)
        {
            IntPtr unicodeFile = IntPtr.Zero;
            IntPtr fileInfoPtr = IntPtr.Zero;
            IntPtr trustDataPtr = IntPtr.Zero;
            try
            {
                unicodeFile = Marshal.StringToHGlobalUni(filePath);

                var fileInfo = new WINTRUST_FILE_INFO
                {
                    cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
                    pcwszFilePath = unicodeFile,
                    hFile = IntPtr.Zero,
                    pgKnownSubject = IntPtr.Zero,
                };
                fileInfoPtr = Marshal.AllocHGlobal((int)fileInfo.cbStruct);
                Marshal.StructureToPtr(fileInfo, fileInfoPtr, fDeleteOld: false);

                var trustData = new WINTRUST_DATA
                {
                    cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                    pPolicyCallbackData = IntPtr.Zero,
                    pSIPClientData = IntPtr.Zero,
                    dwUIChoice = WTD_UI_NONE,
                    fdwRevocationChecks = WTD_REVOKE_WHOLECHAIN,
                    dwUnionChoice = WTD_CHOICE_FILE,
                    pInfoUnion = fileInfoPtr,
                    dwStateAction = WTD_STATEACTION_VERIFY,
                    hWVTStateData = IntPtr.Zero,
                    pwszURLReference = IntPtr.Zero,
                    dwProvFlags = WTD_SAFER_FLAG,
                    dwUIContext = 0,
                };
                trustDataPtr = Marshal.AllocHGlobal((int)trustData.cbStruct);
                Marshal.StructureToPtr(trustData, trustDataPtr, fDeleteOld: false);

                Guid action = WINTRUST_ACTION_GENERIC_VERIFY_V2;
                uint result = WinVerifyTrust(IntPtr.Zero, ref action, trustDataPtr);

                // Always close to release the cached state, regardless of outcome.
                trustData = Marshal.PtrToStructure<WINTRUST_DATA>(trustDataPtr);
                trustData.dwStateAction = WTD_STATEACTION_CLOSE;
                Marshal.StructureToPtr(trustData, trustDataPtr, fDeleteOld: true);
                _ = WinVerifyTrust(IntPtr.Zero, ref action, trustDataPtr);

                return result;
            }
            finally
            {
                if (unicodeFile != IntPtr.Zero) Marshal.FreeHGlobal(unicodeFile);
                if (fileInfoPtr != IntPtr.Zero) Marshal.FreeHGlobal(fileInfoPtr);
                if (trustDataPtr != IntPtr.Zero) Marshal.FreeHGlobal(trustDataPtr);
            }
        }

        [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern uint WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, IntPtr pWVTData);

        [StructLayout(LayoutKind.Sequential)]
        private struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            public IntPtr pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pInfoUnion;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
        }
    }
}

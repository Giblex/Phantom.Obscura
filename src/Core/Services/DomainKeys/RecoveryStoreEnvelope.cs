using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PhantomVault.Core.Models.DomainStores;

namespace PhantomVault.Core.Services.DomainKeys
{
    /// <summary>
    /// Self-describing envelope for the recovery store that breaks the historical
    /// "recovery needs an unlocked vault" Catch-22.
    ///
    /// The store body is encrypted under a random per-store <c>RecoveryStoreKey</c> (RSK).
    /// The RSK is wrapped independently:
    ///   * once per recovery code (so any single recovery code can open the store while LOCKED), and
    ///   * optionally once under the vault recovery key (so the unlocked app can manage the store).
    ///
    /// Opening with a code requires no vault key and no unlock — exactly what recovery is for.
    /// The body stays AES-256-GCM encrypted at rest, so audit log / device id / timestamps are
    /// never exposed in plaintext on the USB.
    /// </summary>
    public static class RecoveryStoreEnvelope
    {
        private const string FormatMagic = "PHANTOM-RECOVERY-ENVELOPE";
        private const int CurrentVersion = 1;

        private const int RskSize = 32;
        private const int SaltSize = 32;
        private const int NonceSize = 12;
        private const int TagSize = 16;
        private const int WrapSize = NonceSize + RskSize + TagSize;

        private static readonly byte[] EnvelopeInfo = Encoding.UTF8.GetBytes("phantom.recovery.envelope.v1");
        private static readonly byte[] DomainSealInfo = Encoding.UTF8.GetBytes("phantom.recovery.domain-seal.v1");
        private static readonly byte[] BodyAad = Encoding.UTF8.GetBytes("recovery.store.envelope.v1");

        /// <summary>
        /// Result of opening an envelope. The caller MUST dispose to zeroize the RSK once any
        /// follow-up <see cref="ResealBody"/> (e.g. after marking a code used) is complete.
        /// </summary>
        public sealed class OpenedStore : IDisposable
        {
            private byte[] _rsk;
            private bool _disposed;

            internal OpenedStore(RecoveryStore store, byte[] rsk, byte[] envelopeBytes)
            {
                Store = store;
                _rsk = rsk;
                EnvelopeBytes = envelopeBytes;
            }

            public RecoveryStore Store { get; }

            /// <summary>The original envelope bytes (header is preserved across re-seals).</summary>
            public byte[] EnvelopeBytes { get; }

            /// <summary>
            /// The recovered store key. Valid until <see cref="Dispose"/>. Used by higher layers to
            /// derive the domain-seal KEK so any recovery code (not just the seed-time one) can
            /// unseal the per-domain keys.
            /// </summary>
            public ReadOnlySpan<byte> RecoveryStoreKey => Rsk;

            internal ReadOnlySpan<byte> Rsk
            {
                get
                {
                    if (_disposed)
                        throw new ObjectDisposedException(nameof(OpenedStore));
                    return _rsk;
                }
            }

            /// <summary>
            /// Re-encrypt the (possibly mutated) store body under the SAME RSK, preserving the
            /// existing header (salt + code wraps + vault wrap). Used after the recovery session
            /// marks a code used / appends audit entries while the vault is still locked.
            /// </summary>
            public byte[] ResealBody(RecoveryStore updatedStore)
                => RecoveryStoreEnvelope.ResealBody(EnvelopeBytes, updatedStore, Rsk);

            public void Dispose()
            {
                if (_disposed)
                    return;
                CryptographicOperations.ZeroMemory(_rsk);
                _rsk = Array.Empty<byte>();
                _disposed = true;
            }
        }

        public static bool IsEnvelope(ReadOnlySpan<byte> fileBytes)
        {
            if (fileBytes.Length < FormatMagic.Length)
                return false;
            try
            {
                var dto = JsonSerializer.Deserialize<EnvelopeDto>(fileBytes, JsonOptions);
                return dto != null && string.Equals(dto.Format, FormatMagic, StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Create a fresh envelope from a recovery store and its plaintext recovery codes.
        /// Optionally also wraps the RSK with the vault recovery key for in-app management.
        /// </summary>
        public static byte[] Seal(
            RecoveryStore store,
            IReadOnlyList<string> plaintextCodes,
            ReadOnlySpan<byte> vaultRecoveryKey = default)
        {
            var rsk = RandomNumberGenerator.GetBytes(RskSize);
            try
            {
                return SealWithStoreKey(store, plaintextCodes, rsk, vaultRecoveryKey);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(rsk);
            }
        }

        /// <summary>
        /// Seal using a caller-supplied RecoveryStoreKey (RSK). This lets the seeder derive the
        /// per-domain seal KEK from the SAME RSK that every recovery code can unwrap, so all codes
        /// (not just the seed-time one) can later unseal the domain keys.
        /// </summary>
        public static byte[] SealWithStoreKey(
            RecoveryStore store,
            IReadOnlyList<string> plaintextCodes,
            ReadOnlySpan<byte> rsk,
            ReadOnlySpan<byte> vaultRecoveryKey = default)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (plaintextCodes == null || plaintextCodes.Count == 0)
                throw new ArgumentException("At least one recovery code is required to seal the store.", nameof(plaintextCodes));
            if (rsk.Length != RskSize)
                throw new ArgumentException($"RSK must be {RskSize} bytes.", nameof(rsk));

            var headerSalt = RandomNumberGenerator.GetBytes(SaltSize);
            var rskArray = rsk.ToArray();
            try
            {
                var codeWraps = new List<string>(plaintextCodes.Count);
                foreach (var code in plaintextCodes)
                {
                    var envelopeKey = DeriveEnvelopeKey(code, headerSalt);
                    try
                    {
                        codeWraps.Add(Convert.ToBase64String(WrapRsk(envelopeKey, rskArray)));
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(envelopeKey);
                    }
                }

                string? vaultWrap = null;
                if (vaultRecoveryKey.Length > 0)
                    vaultWrap = Convert.ToBase64String(WrapRsk(vaultRecoveryKey, rskArray));

                var body = EncryptBody(store, rskArray);

                var dto = new EnvelopeDto
                {
                    Format = FormatMagic,
                    Version = CurrentVersion,
                    HeaderSalt = Convert.ToBase64String(headerSalt),
                    CodeWraps = codeWraps,
                    VaultWrap = vaultWrap,
                    Body = Convert.ToBase64String(body)
                };

                return JsonSerializer.SerializeToUtf8Bytes(dto, JsonOptions);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(rskArray);
            }
        }

        /// <summary>
        /// Derive the per-domain seal KEK from the RecoveryStoreKey. Used identically at seed time
        /// (to seal domain keys) and at recovery time (to unseal them), so the KEK is independent of
        /// which recovery code was entered.
        /// </summary>
        public static byte[] DeriveDomainSealKey(ReadOnlySpan<byte> rsk, ReadOnlySpan<byte> storeSalt)
        {
            if (rsk.Length != RskSize)
                throw new ArgumentException($"RSK must be {RskSize} bytes.", nameof(rsk));

            var salt = storeSalt.Length > 0 ? storeSalt.ToArray() : new byte[SaltSize];
            return HKDF.DeriveKey(HashAlgorithmName.SHA256, rsk.ToArray(), RskSize, salt, DomainSealInfo);
        }

        /// <summary>
        /// Open the store using a single recovery code — no vault key, no unlock required.
        /// Throws <see cref="UnauthorizedAccessException"/> if the code matches no wrap.
        /// </summary>
        public static OpenedStore OpenWithCode(byte[] envelopeBytes, string recoveryCode)
        {
            if (envelopeBytes == null) throw new ArgumentNullException(nameof(envelopeBytes));
            if (string.IsNullOrWhiteSpace(recoveryCode)) throw new ArgumentException("Recovery code is required.", nameof(recoveryCode));

            var dto = ParseAndValidate(envelopeBytes);
            var headerSalt = Convert.FromBase64String(dto.HeaderSalt);

            var envelopeKey = DeriveEnvelopeKey(recoveryCode, headerSalt);
            try
            {
                foreach (var wrapB64 in dto.CodeWraps)
                {
                    var wrap = Convert.FromBase64String(wrapB64);
                    var rsk = TryUnwrapRsk(envelopeKey, wrap);
                    if (rsk == null)
                        continue;

                    return BuildOpenedStore(dto, rsk, envelopeBytes);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(envelopeKey);
            }

            throw new UnauthorizedAccessException("Recovery code did not match any stored recovery slot.");
        }

        /// <summary>
        /// Open the store using the vault recovery key (in-app management path; requires unlock).
        /// </summary>
        public static OpenedStore OpenWithVaultKey(byte[] envelopeBytes, ReadOnlySpan<byte> vaultRecoveryKey)
        {
            if (envelopeBytes == null) throw new ArgumentNullException(nameof(envelopeBytes));
            if (vaultRecoveryKey.Length == 0) throw new ArgumentException("Vault recovery key is required.", nameof(vaultRecoveryKey));

            var dto = ParseAndValidate(envelopeBytes);
            if (string.IsNullOrEmpty(dto.VaultWrap))
                throw new UnauthorizedAccessException("This recovery store has no vault-key wrap.");

            var wrap = Convert.FromBase64String(dto.VaultWrap);
            var rsk = TryUnwrapRsk(vaultRecoveryKey, wrap)
                ?? throw new UnauthorizedAccessException("Vault recovery key did not unwrap the recovery store.");

            return BuildOpenedStore(dto, rsk, envelopeBytes);
        }

        /// <summary>
        /// Re-encrypt the body under the existing RSK while preserving the header. Used after
        /// the store has been mutated (code marked used, audit entry appended).
        /// </summary>
        public static byte[] ResealBody(byte[] envelopeBytes, RecoveryStore updatedStore, ReadOnlySpan<byte> rsk)
        {
            if (updatedStore == null) throw new ArgumentNullException(nameof(updatedStore));
            if (rsk.Length != RskSize) throw new ArgumentException("RSK must be 32 bytes.", nameof(rsk));

            var dto = ParseAndValidate(envelopeBytes);
            dto.Body = Convert.ToBase64String(EncryptBody(updatedStore, rsk));
            return JsonSerializer.SerializeToUtf8Bytes(dto, JsonOptions);
        }

        private static OpenedStore BuildOpenedStore(EnvelopeDto dto, byte[] rsk, byte[] envelopeBytes)
        {
            try
            {
                var store = DecryptBody(Convert.FromBase64String(dto.Body), rsk);
                return new OpenedStore(store, rsk, envelopeBytes);
            }
            catch
            {
                CryptographicOperations.ZeroMemory(rsk);
                throw;
            }
        }

        private static EnvelopeDto ParseAndValidate(byte[] envelopeBytes)
        {
            EnvelopeDto? dto;
            try
            {
                dto = JsonSerializer.Deserialize<EnvelopeDto>(envelopeBytes, JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("Recovery store is corrupted or not a recognized envelope.", ex);
            }

            if (dto == null || !string.Equals(dto.Format, FormatMagic, StringComparison.Ordinal))
                throw new InvalidOperationException("Recovery store is not a recognized envelope.");
            if (dto.Version != CurrentVersion)
                throw new InvalidOperationException($"Unsupported recovery store version {dto.Version}.");
            if (string.IsNullOrEmpty(dto.HeaderSalt) || dto.CodeWraps == null || dto.CodeWraps.Count == 0 || string.IsNullOrEmpty(dto.Body))
                throw new InvalidOperationException("Recovery store envelope is missing required fields.");

            return dto;
        }

        private static byte[] DeriveEnvelopeKey(string recoveryCode, byte[] headerSalt)
        {
            var normalized = recoveryCode.Replace("-", string.Empty).Replace(" ", string.Empty).ToUpperInvariant();
            var codeBytes = Encoding.UTF8.GetBytes(normalized);
            try
            {
                return HKDF.DeriveKey(HashAlgorithmName.SHA256, codeBytes, RskSize, headerSalt, EnvelopeInfo);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(codeBytes);
            }
        }

        private static byte[] WrapRsk(ReadOnlySpan<byte> key, byte[] rsk)
        {
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var ct = new byte[RskSize];
            var tag = new byte[TagSize];
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, rsk, ct, tag);

            var wrap = new byte[WrapSize];
            Buffer.BlockCopy(nonce, 0, wrap, 0, NonceSize);
            Buffer.BlockCopy(ct, 0, wrap, NonceSize, RskSize);
            Buffer.BlockCopy(tag, 0, wrap, NonceSize + RskSize, TagSize);
            return wrap;
        }

        private static byte[]? TryUnwrapRsk(ReadOnlySpan<byte> key, byte[] wrap)
        {
            if (wrap.Length != WrapSize)
                return null;

            var nonce = wrap.AsSpan(0, NonceSize);
            var ct = wrap.AsSpan(NonceSize, RskSize);
            var tag = wrap.AsSpan(NonceSize + RskSize, TagSize);
            var rsk = new byte[RskSize];
            try
            {
                using var aes = new AesGcm(key, TagSize);
                aes.Decrypt(nonce, ct, tag, rsk);
                return rsk;
            }
            catch (CryptographicException)
            {
                CryptographicOperations.ZeroMemory(rsk);
                return null;
            }
        }

        private static byte[] EncryptBody(RecoveryStore store, ReadOnlySpan<byte> rsk)
        {
            var plaintext = JsonSerializer.SerializeToUtf8Bytes(store, JsonOptions);
            try
            {
                var nonce = RandomNumberGenerator.GetBytes(NonceSize);
                var ct = new byte[plaintext.Length];
                var tag = new byte[TagSize];
                using var aes = new AesGcm(rsk, TagSize);
                aes.Encrypt(nonce, plaintext, ct, tag, BodyAad);

                var body = new byte[NonceSize + ct.Length + TagSize];
                Buffer.BlockCopy(nonce, 0, body, 0, NonceSize);
                Buffer.BlockCopy(ct, 0, body, NonceSize, ct.Length);
                Buffer.BlockCopy(tag, 0, body, NonceSize + ct.Length, TagSize);
                return body;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }

        private static RecoveryStore DecryptBody(byte[] body, ReadOnlySpan<byte> rsk)
        {
            if (body.Length < NonceSize + TagSize + 1)
                throw new InvalidOperationException("Recovery store body is corrupted.");

            var nonce = body.AsSpan(0, NonceSize);
            var tag = body.AsSpan(body.Length - TagSize, TagSize);
            var ct = body.AsSpan(NonceSize, body.Length - NonceSize - TagSize);
            var plaintext = new byte[ct.Length];
            try
            {
                using var aes = new AesGcm(rsk, TagSize);
                aes.Decrypt(nonce, ct, tag, plaintext, BodyAad);
                return JsonSerializer.Deserialize<RecoveryStore>(plaintext, JsonOptions)
                    ?? throw new InvalidOperationException("Recovery store body deserialized to null.");
            }
            catch (CryptographicException ex)
            {
                throw new UnauthorizedAccessException("Recovery store body failed authentication (tampered or wrong key).", ex);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private sealed class EnvelopeDto
        {
            [JsonPropertyName("format")] public string Format { get; set; } = string.Empty;
            [JsonPropertyName("version")] public int Version { get; set; }
            [JsonPropertyName("header_salt")] public string HeaderSalt { get; set; } = string.Empty;
            [JsonPropertyName("code_wraps")] public List<string> CodeWraps { get; set; } = new();
            [JsonPropertyName("vault_wrap")] public string? VaultWrap { get; set; }
            [JsonPropertyName("body")] public string Body { get; set; } = string.Empty;
        }
    }
}

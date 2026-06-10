using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using PhantomVault.Core.Models.DomainStores;
using PhantomVault.Core.Services.DomainKeys;
using Xunit;

namespace PhantomVault.Core.Tests
{
    public sealed class RecoveryStoreEnvelopeTests
    {
        private static readonly string[] Codes =
        {
            "ABCD-EFGH-JKLM-NPQR",
            "1111-2222-3333-4444",
            "ZZZZ-YYYY-XXXX-WWWW"
        };

        private static RecoveryStore MakeStore()
        {
            return new RecoveryStore
            {
                StoreId = "store-under-test",
                Salt = RandomNumberGenerator.GetBytes(32),
                DeviceId = "device-abc",
                RecoveryCodes = new List<RecoveryCodeEntry>
                {
                    new() { CodeNumber = 0, Hash = "h0" },
                    new() { CodeNumber = 1, Hash = "h1" }
                }
            };
        }

        [Fact]
        public void OpenWithCode_RoundTrips_ForEveryCode_WithoutAnyVaultKey()
        {
            var store = MakeStore();
            var envelope = RecoveryStoreEnvelope.Seal(store, Codes);

            foreach (var code in Codes)
            {
                using var opened = RecoveryStoreEnvelope.OpenWithCode(envelope, code);
                Assert.Equal(store.StoreId, opened.Store.StoreId);
                Assert.Equal(store.DeviceId, opened.Store.DeviceId);
                Assert.Equal(store.Salt, opened.Store.Salt);
            }
        }

        [Fact]
        public void OpenWithCode_IsCaseAndDashInsensitive()
        {
            var store = MakeStore();
            var envelope = RecoveryStoreEnvelope.Seal(store, Codes);

            using var opened = RecoveryStoreEnvelope.OpenWithCode(envelope, "abcdefghjklmnpqr");
            Assert.Equal(store.StoreId, opened.Store.StoreId);
        }

        [Fact]
        public void OpenWithCode_WrongCode_Throws()
        {
            var envelope = RecoveryStoreEnvelope.Seal(MakeStore(), Codes);
            Assert.Throws<UnauthorizedAccessException>(
                () => RecoveryStoreEnvelope.OpenWithCode(envelope, "0000-0000-0000-0000"));
        }

        [Fact]
        public void OpenWithVaultKey_RoundTrips_WhenWrapped()
        {
            var store = MakeStore();
            var vaultKey = RandomNumberGenerator.GetBytes(32);
            var envelope = RecoveryStoreEnvelope.Seal(store, Codes, vaultKey);

            using var opened = RecoveryStoreEnvelope.OpenWithVaultKey(envelope, vaultKey);
            Assert.Equal(store.StoreId, opened.Store.StoreId);
        }

        [Fact]
        public void OpenWithVaultKey_WrongKey_Throws()
        {
            var envelope = RecoveryStoreEnvelope.Seal(MakeStore(), Codes, RandomNumberGenerator.GetBytes(32));
            Assert.Throws<UnauthorizedAccessException>(
                () => RecoveryStoreEnvelope.OpenWithVaultKey(envelope, RandomNumberGenerator.GetBytes(32)));
        }

        [Fact]
        public void OpenWithVaultKey_NoVaultWrap_Throws()
        {
            var envelope = RecoveryStoreEnvelope.Seal(MakeStore(), Codes); // no vault key
            Assert.Throws<UnauthorizedAccessException>(
                () => RecoveryStoreEnvelope.OpenWithVaultKey(envelope, RandomNumberGenerator.GetBytes(32)));
        }

        [Fact]
        public void TamperedEnvelope_FailsAuthentication()
        {
            var envelope = RecoveryStoreEnvelope.Seal(MakeStore(), Codes);

            // Flip a byte well inside the JSON (base64 body region) to simulate tampering.
            var tampered = (byte[])envelope.Clone();
            tampered[tampered.Length / 2] ^= 0xFF;

            Assert.ThrowsAny<Exception>(() => RecoveryStoreEnvelope.OpenWithCode(tampered, Codes[0]));
        }

        [Fact]
        public void ResealBody_PreservesAllCodeAccess_AndPersistsMutation()
        {
            var store = MakeStore();
            var envelope = RecoveryStoreEnvelope.Seal(store, Codes);

            byte[] resealed;
            using (var opened = RecoveryStoreEnvelope.OpenWithCode(envelope, Codes[0]))
            {
                opened.Store.RecoveryCodes[0].Used = true;
                opened.Store.AuditLog.Add(new RecoveryAuditEntry { Success = true });
                resealed = opened.ResealBody(opened.Store);
            }

            // A DIFFERENT code must still open the resealed envelope and observe the mutation.
            using var reopened = RecoveryStoreEnvelope.OpenWithCode(resealed, Codes[2]);
            Assert.True(reopened.Store.RecoveryCodes[0].Used);
            Assert.Single(reopened.Store.AuditLog);
        }

        [Fact]
        public void IsEnvelope_DistinguishesFormats()
        {
            var envelope = RecoveryStoreEnvelope.Seal(MakeStore(), Codes);
            Assert.True(RecoveryStoreEnvelope.IsEnvelope(envelope));
            Assert.False(RecoveryStoreEnvelope.IsEnvelope(RandomNumberGenerator.GetBytes(64)));
        }

        [Fact]
        public void Seal_WithoutCodes_Throws()
        {
            Assert.Throws<ArgumentException>(
                () => RecoveryStoreEnvelope.Seal(MakeStore(), Array.Empty<string>()));
        }

        [Fact]
        public void EachSeal_ProducesDistinctCiphertext()
        {
            var store = MakeStore();
            var a = RecoveryStoreEnvelope.Seal(store, Codes);
            var b = RecoveryStoreEnvelope.Seal(store, Codes);
            Assert.NotEqual(Convert.ToBase64String(a), Convert.ToBase64String(b));
        }
    }
}

#nullable enable
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using PhantomVault.Core.Models.DomainStores;
using PhantomVault.Core.Services;
using PhantomVault.Core.Services.DomainKeys;
using Xunit;

namespace PhantomVault.Core.Tests
{
    public sealed class RecoveryStoreFactoryTests
    {
        private static readonly string[] Codes =
        {
            "AAAA-BBBB-CCCC-DDDD",
            "EEEE-FFFF-GGGG-HHHH",
            "JJJJ-KKKK-LLLL-MMMM"
        };

        private static RecoveryStoreFactory MakeFactory()
            => new(new ScopedRecoveryService(new EncryptionService()));

        private static RecoveryStoreFactory.SeedInput MakeInput(
            byte[] obscuraKey,
            byte[] attestorKey,
            byte[]? vaultKey = null)
        {
            return new RecoveryStoreFactory.SeedInput
            {
                DomainRecoveryKeys = new Dictionary<CryptoDomain, byte[]>
                {
                    [CryptoDomain.Obscura] = obscuraKey,
                    [CryptoDomain.Attestor] = attestorKey
                },
                RecoveryCodes = Codes,
                VaultRecoveryKey = vaultKey,
                DeviceId = "device-xyz",
                SetupAppVersion = "1.0.0"
            };
        }

        [Fact]
        public void Seed_ThenRecover_RoundTripsEachDomain_ForEveryCode_WhileLocked()
        {
            var obscuraKey = RandomNumberGenerator.GetBytes(32);
            var attestorKey = RandomNumberGenerator.GetBytes(32);
            var factory = MakeFactory();

            var envelope = factory.Seed(MakeInput(obscuraKey, attestorKey), out var store);

            Assert.NotNull(store.ObscuraSealed);
            Assert.NotNull(store.AttestorSealed);
            Assert.Equal(Codes.Length, store.RecoveryCodes.Count);

            foreach (var code in Codes)
            {
                var recoveredObscura = factory.RecoverDomainKey(envelope, code, CryptoDomain.Obscura);
                var recoveredAttestor = factory.RecoverDomainKey(envelope, code, CryptoDomain.Attestor);

                Assert.Equal(obscuraKey, recoveredObscura);
                Assert.Equal(attestorKey, recoveredAttestor);
            }
        }

        [Fact]
        public void RecoverDomainKey_WrongCode_Throws()
        {
            var factory = MakeFactory();
            var envelope = factory.Seed(
                MakeInput(RandomNumberGenerator.GetBytes(32), RandomNumberGenerator.GetBytes(32)), out _);

            Assert.Throws<UnauthorizedAccessException>(
                () => factory.RecoverDomainKey(envelope, "9999-9999-9999-9999", CryptoDomain.Obscura));
        }

        [Fact]
        public void Seed_PopulatesPolicyDeviceAndVersionMetadata()
        {
            var factory = MakeFactory();
            factory.Seed(MakeInput(RandomNumberGenerator.GetBytes(32), RandomNumberGenerator.GetBytes(32)), out var store);

            Assert.Equal("device-xyz", store.DeviceId);
            Assert.Equal("1.0.0", store.SetupAppVersion);
            Assert.NotNull(store.Policy);
            Assert.Equal(32, store.Salt.Length);
        }

        [Fact]
        public void Seed_WithVaultKey_AllowsVaultKeyOpen()
        {
            var vaultKey = RandomNumberGenerator.GetBytes(32);
            var factory = MakeFactory();
            var envelope = factory.Seed(
                MakeInput(RandomNumberGenerator.GetBytes(32), RandomNumberGenerator.GetBytes(32), vaultKey), out _);

            using var opened = RecoveryStoreEnvelope.OpenWithVaultKey(envelope, vaultKey);
            Assert.NotNull(opened.Store.ObscuraSealed);
        }

        [Fact]
        public void RecoverDomainKey_AcrossCodes_AllYieldIdenticalKey()
        {
            var obscuraKey = RandomNumberGenerator.GetBytes(32);
            var factory = MakeFactory();
            var envelope = factory.Seed(MakeInput(obscuraKey, RandomNumberGenerator.GetBytes(32)), out _);

            var fromFirst = factory.RecoverDomainKey(envelope, Codes[0], CryptoDomain.Obscura);
            var fromLast = factory.RecoverDomainKey(envelope, Codes[^1], CryptoDomain.Obscura);

            Assert.Equal(fromFirst, fromLast);
            Assert.Equal(obscuraKey, fromFirst);
        }

        [Fact]
        public void Seed_NoCodes_Throws()
        {
            var factory = MakeFactory();
            var input = new RecoveryStoreFactory.SeedInput
            {
                DomainRecoveryKeys = new Dictionary<CryptoDomain, byte[]>
                {
                    [CryptoDomain.Obscura] = RandomNumberGenerator.GetBytes(32)
                },
                RecoveryCodes = Array.Empty<string>()
            };

            Assert.Throws<ArgumentException>(() => factory.Seed(input, out _));
        }

        [Fact]
        public void Seed_NoDomains_Throws()
        {
            var factory = MakeFactory();
            var input = new RecoveryStoreFactory.SeedInput
            {
                DomainRecoveryKeys = new Dictionary<CryptoDomain, byte[]>(),
                RecoveryCodes = Codes
            };

            Assert.Throws<ArgumentException>(() => factory.Seed(input, out _));
        }

        [Fact]
        public void SeedKeyfileOnly_ThenRecover_RoundTrips_ForEveryCode_WhileLocked()
        {
            var keyfile = RandomNumberGenerator.GetBytes(96);
            var factory = MakeFactory();

            var input = new RecoveryStoreFactory.SeedInput
            {
                ProtectedKeyfile = keyfile,
                RecoveryCodes = Codes,
                DeviceId = "device-xyz",
                SetupAppVersion = "1.0.0"
            };

            var envelope = factory.Seed(input, out var store);

            Assert.Null(store.ObscuraSealed);
            Assert.Null(store.AttestorSealed);
            Assert.NotNull(store.ProtectedKeyfile);

            foreach (var code in Codes)
            {
                var recovered = factory.RecoverProtectedKeyfile(envelope, code);
                Assert.Equal(keyfile, recovered);
            }
        }

        [Fact]
        public void RecoverProtectedKeyfile_WrongCode_Throws()
        {
            var factory = MakeFactory();
            var envelope = factory.Seed(new RecoveryStoreFactory.SeedInput
            {
                ProtectedKeyfile = RandomNumberGenerator.GetBytes(64),
                RecoveryCodes = Codes
            }, out _);

            Assert.Throws<UnauthorizedAccessException>(
                () => factory.RecoverProtectedKeyfile(envelope, "9999-9999-9999-9999"));
        }

        [Fact]
        public void RecoverProtectedKeyfile_WhenOnlyDomainsSeeded_Throws()
        {
            var factory = MakeFactory();
            var envelope = factory.Seed(
                MakeInput(RandomNumberGenerator.GetBytes(32), RandomNumberGenerator.GetBytes(32)), out _);

            Assert.Throws<InvalidOperationException>(
                () => factory.RecoverProtectedKeyfile(envelope, Codes[0]));
        }

        [Fact]
        public void Seed_KeyfileAndDomains_BothRecoverable()
        {
            var keyfile = RandomNumberGenerator.GetBytes(80);
            var obscuraKey = RandomNumberGenerator.GetBytes(32);
            var factory = MakeFactory();

            var envelope = factory.Seed(new RecoveryStoreFactory.SeedInput
            {
                ProtectedKeyfile = keyfile,
                DomainRecoveryKeys = new Dictionary<CryptoDomain, byte[]>
                {
                    [CryptoDomain.Obscura] = obscuraKey
                },
                RecoveryCodes = Codes
            }, out _);

            Assert.Equal(keyfile, factory.RecoverProtectedKeyfile(envelope, Codes[1]));
            Assert.Equal(obscuraKey, factory.RecoverDomainKey(envelope, Codes[1], CryptoDomain.Obscura));
        }
    }
}

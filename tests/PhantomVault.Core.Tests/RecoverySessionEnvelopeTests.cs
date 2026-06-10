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
    public sealed class RecoverySessionEnvelopeTests
    {
        private static readonly string[] Codes =
        {
            "AAAA-BBBB-CCCC-DDDD",
            "EEEE-FFFF-GGGG-HHHH",
            "JJJJ-KKKK-LLLL-MMMM"
        };

        private static (ScopedRecoveryService scoped, RecoveryAuditService audit, RecoveryStoreFactory factory) MakeServices()
        {
            var scoped = new ScopedRecoveryService(new EncryptionService());
            return (scoped, new RecoveryAuditService(), new RecoveryStoreFactory(scoped));
        }

        private static byte[] SeedKeyfileEnvelope(RecoveryStoreFactory factory, byte[] keyfile)
            => factory.Seed(new RecoveryStoreFactory.SeedInput
            {
                ProtectedKeyfile = keyfile,
                RecoveryCodes = Codes
            }, out _);

        [Fact]
        public void OpenFromEnvelope_RecoversKeyfile_ForEveryCode_WhileLocked()
        {
            var (scoped, audit, factory) = MakeServices();
            var keyfile = RandomNumberGenerator.GetBytes(4096);
            var envelope = SeedKeyfileEnvelope(factory, keyfile);

            foreach (var code in Codes)
            {
                using var session = RecoverySession.OpenFromEnvelope(envelope, code, scoped, audit);
                Assert.Equal(RecoverySessionState.AwaitingDomainSelection, session.State);

                var recovered = session.RecoverKeyfile();
                Assert.Equal(keyfile, recovered);
            }
        }

        [Fact]
        public void OpenFromEnvelope_WrongCode_Throws()
        {
            var (scoped, audit, factory) = MakeServices();
            var envelope = SeedKeyfileEnvelope(factory, RandomNumberGenerator.GetBytes(256));

            Assert.Throws<UnauthorizedAccessException>(
                () => RecoverySession.OpenFromEnvelope(envelope, "0000-0000-0000-0000", scoped, audit));
        }

        [Fact]
        public void OpenFromEnvelope_RecoversDomainKey_ForEveryCode_ViaRskDerivedKek()
        {
            var (scoped, audit, factory) = MakeServices();
            var obscuraKey = RandomNumberGenerator.GetBytes(32);

            var envelope = factory.Seed(new RecoveryStoreFactory.SeedInput
            {
                DomainRecoveryKeys = new Dictionary<CryptoDomain, byte[]>
                {
                    [CryptoDomain.Obscura] = obscuraKey
                },
                RecoveryCodes = Codes
            }, out _);

            foreach (var code in Codes)
            {
                using var session = RecoverySession.OpenFromEnvelope(envelope, code, scoped, audit);
                var recovered = session.RecoverDomain(CryptoDomain.Obscura);
                Assert.Equal(obscuraKey, recovered);
            }
        }

        [Fact]
        public void RecoverKeyfile_WhenNoKeyfileSealed_Throws()
        {
            var (scoped, audit, factory) = MakeServices();
            var envelope = factory.Seed(new RecoveryStoreFactory.SeedInput
            {
                DomainRecoveryKeys = new Dictionary<CryptoDomain, byte[]>
                {
                    [CryptoDomain.Obscura] = RandomNumberGenerator.GetBytes(32)
                },
                RecoveryCodes = Codes
            }, out _);

            using var session = RecoverySession.OpenFromEnvelope(envelope, Codes[0], scoped, audit);
            Assert.Throws<InvalidOperationException>(() => session.RecoverKeyfile());
        }
    }
}

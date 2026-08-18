using System;
using System.IO;
using System.Security.Cryptography;
using PhantomVault.Core.Services;
using Xunit;

namespace PhantomVault.Core.Tests
{
    public class ObscuraVolumeHeaderKeyTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _keyfileA;
        private readonly string _keyfileB;

        public ObscuraVolumeHeaderKeyTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ObscuraKeyTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _keyfileA = Path.Combine(_dir, "a.key");
            _keyfileB = Path.Combine(_dir, "b.key");
            File.WriteAllBytes(_keyfileA, RandomNumberGenerator.GetBytes(64));
            File.WriteAllBytes(_keyfileB, RandomNumberGenerator.GetBytes(64));
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { }
        }

        private static byte[] Salt(byte fill) => Enumerable_Repeat(fill);
        private static byte[] Enumerable_Repeat(byte fill)
        {
            var s = new byte[ObscuraVolumeFormat.SaltLength];
            Array.Fill(s, fill);
            return s;
        }

        [Fact]
        public void Derivation_is_deterministic()
        {
            var one = ObscuraVolumeFormat.DeriveHeaderKey(Salt(1), _keyfileA);
            var two = ObscuraVolumeFormat.DeriveHeaderKey(Salt(1), _keyfileA);

            Assert.Equal(one, two);
            Assert.Equal(32, one.Length);
        }

        [Fact]
        public void A_different_salt_gives_a_different_key()
        {
            // This is what makes two volumes built from the same keyfile share no header key.
            Assert.NotEqual(
                ObscuraVolumeFormat.DeriveHeaderKey(Salt(1), _keyfileA),
                ObscuraVolumeFormat.DeriveHeaderKey(Salt(2), _keyfileA));
        }

        [Fact]
        public void A_different_keyfile_gives_a_different_key()
        {
            Assert.NotEqual(
                ObscuraVolumeFormat.DeriveHeaderKey(Salt(1), _keyfileA),
                ObscuraVolumeFormat.DeriveHeaderKey(Salt(1), _keyfileB));
        }

        [Fact]
        public void The_key_depends_on_the_keyfile_alone()
        {
            // The vault password is deliberately NOT an input. The volume must be opened
            // before the VaultManifest can be read, and the manifest is what says whether a
            // password is in use — so the password is still unknown at this point. Mixing it
            // in would write a header under a key the reader could never reconstruct.
            var derived = ObscuraVolumeFormat.DeriveHeaderKey(Salt(1), _keyfileA);

            Assert.Equal(32, derived.Length);
            Assert.Equal(derived, ObscuraVolumeFormat.DeriveHeaderKey(Salt(1), _keyfileA));
        }

        [Fact]
        public void A_missing_keyfile_is_rejected()
        {
            // The vault is keyfile-first: a password alone must never produce a usable key.
            Assert.Throws<ArgumentException>(
                () => ObscuraVolumeFormat.DeriveHeaderKey(Salt(1), ""));
            Assert.Throws<ArgumentException>(
                () => ObscuraVolumeFormat.DeriveHeaderKey(Salt(1), null!));
        }

        [Fact]
        public void Aad_binds_the_salt_so_headers_cannot_be_spliced_between_volumes()
        {
            Assert.NotEqual(
                ObscuraVolumeFormat.BuildHeaderAad(Salt(1)),
                ObscuraVolumeFormat.BuildHeaderAad(Salt(2)));
        }
    }
}

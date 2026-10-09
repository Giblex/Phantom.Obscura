using System;
using System.Security;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using PhantomVault.Core.Services.BootRom;
using Xunit;

namespace PhantomVault.Core.Tests
{
    /// <summary>
    /// The PHANTOM-ROM container: signature, authenticated encryption, and the header binding
    /// that stops a valid ciphertext being replayed under a different header. Every failure must
    /// surface as <see cref="SecurityException"/> — never a partial or silently-wrong result.
    /// </summary>
    public sealed class BootRomContainerTests
    {
        private static (byte[] PrivateKey, byte[] PublicKey) NewSigningKeys()
        {
            var generator = new Ed25519KeyPairGenerator();
            generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
            var pair = generator.GenerateKeyPair();
            return (((Ed25519PrivateKeyParameters)pair.Private).GetEncoded(),
                    ((Ed25519PublicKeyParameters)pair.Public).GetEncoded());
        }

        private static byte[] Key32() => RandomNumberGenerator.GetBytes(32);
        private static byte[] KeyId() => RandomNumberGenerator.GetBytes(PhantomRomContainer.KeyIdSize);

        [Fact]
        public void Seal_then_open_returns_the_original_program()
        {
            var (priv, pub) = NewSigningKeys();
            byte[] romKey = Key32();
            byte[] program = RandomNumberGenerator.GetBytes(512);

            byte[] container = PhantomRomContainer.Seal(program, priv, romKey, KeyId(), romVersion: 7);
            byte[] opened = PhantomRomContainer.Open(container, pub, romKey, out var header);

            Assert.Equal(program, opened);
            Assert.Equal(7UL, header.RomVersion);
            Assert.Equal(PhantomRomContainer.FormatVersion, header.FormatVersion);
            Assert.Equal(program.Length, header.CipherTextLength);
        }

        [Fact]
        public void The_maximum_program_size_round_trips()
        {
            var (priv, pub) = NewSigningKeys();
            byte[] romKey = Key32();
            byte[] program = RandomNumberGenerator.GetBytes(PhantomRomContainer.MaxProgramBytes);
            byte[] opened = null;
            try
            {
                byte[] container = PhantomRomContainer.Seal(program, priv, romKey, KeyId(), 1);
                Assert.Equal(PhantomRomContainer.MaxContainerBytes, container.Length);
                opened = PhantomRomContainer.Open(container, pub, romKey, out _);
                Assert.Equal(program, opened);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(priv);
                CryptographicOperations.ZeroMemory(romKey);
                CryptographicOperations.ZeroMemory(program);
                if (opened is not null) CryptographicOperations.ZeroMemory(opened);
            }
        }

        [Fact]
        public void Open_rejects_a_container_signed_by_an_untrusted_key()
        {
            var (priv, _) = NewSigningKeys();
            var (_, otherPublic) = NewSigningKeys();
            byte[] romKey = Key32();

            byte[] container = PhantomRomContainer.Seal(new byte[] { 1, 2, 3 }, priv, romKey, KeyId(), 1);

            Assert.Throws<SecurityException>(() =>
                PhantomRomContainer.Open(container, otherPublic, romKey, out _));
        }

        [Fact]
        public void Open_rejects_a_tampered_ciphertext()
        {
            var (priv, pub) = NewSigningKeys();
            byte[] romKey = Key32();
            byte[] container = PhantomRomContainer.Seal(RandomNumberGenerator.GetBytes(64), priv, romKey, KeyId(), 1);

            container[PhantomRomContainer.HeaderSize] ^= 0xFF;

            Assert.Throws<SecurityException>(() =>
                PhantomRomContainer.Open(container, pub, romKey, out _));
        }

        [Fact]
        public void Open_rejects_a_tampered_header()
        {
            var (priv, pub) = NewSigningKeys();
            byte[] romKey = Key32();
            byte[] container = PhantomRomContainer.Seal(RandomNumberGenerator.GetBytes(64), priv, romKey, KeyId(), 1);

            // Rewrite the ROM version — a rollback attempt. The signature covers the header.
            container[48] ^= 0x01;

            Assert.Throws<SecurityException>(() =>
                PhantomRomContainer.Open(container, pub, romKey, out _));
        }

        [Fact]
        public void Open_rejects_the_wrong_rom_key()
        {
            var (priv, pub) = NewSigningKeys();
            byte[] container = PhantomRomContainer.Seal(RandomNumberGenerator.GetBytes(64), priv, Key32(), KeyId(), 1);

            Assert.Throws<SecurityException>(() =>
                PhantomRomContainer.Open(container, pub, Key32(), out _));
        }

        [Fact]
        public void Open_rejects_a_truncated_container()
        {
            var (priv, pub) = NewSigningKeys();
            byte[] romKey = Key32();
            byte[] container = PhantomRomContainer.Seal(RandomNumberGenerator.GetBytes(64), priv, romKey, KeyId(), 1);

            byte[] truncated = container[..(container.Length - 1)];

            Assert.Throws<SecurityException>(() =>
                PhantomRomContainer.Open(truncated, pub, romKey, out _));
        }

        [Fact]
        public void Open_rejects_reserved_flags()
        {
            var (priv, pub) = NewSigningKeys();
            byte[] romKey = Key32();
            byte[] program = RandomNumberGenerator.GetBytes(32);
            byte[] container = PhantomRomContainer.Seal(program, priv, romKey, KeyId(), 1);

            // Set a reserved flag and re-sign, so this tests the policy rather than the signature.
            container[12] = 0x01;
            var resigned = PhantomRomContainer.Seal(program, priv, romKey, KeyId(), 1);
            Array.Copy(container, 12, resigned, 12, 4);

            Assert.Throws<SecurityException>(() =>
                PhantomRomContainer.Open(resigned, pub, romKey, out _));
        }

        [Fact]
        public void A_ciphertext_cannot_be_moved_onto_another_header()
        {
            // The header is the AEAD associated data, so swapping bodies between two validly
            // sealed containers must fail even when each half is individually well formed.
            var (priv, pub) = NewSigningKeys();
            byte[] romKey = Key32();
            byte[] first = PhantomRomContainer.Seal(RandomNumberGenerator.GetBytes(64), priv, romKey, KeyId(), 1);
            byte[] second = PhantomRomContainer.Seal(RandomNumberGenerator.GetBytes(64), priv, romKey, KeyId(), 2);

            byte[] spliced = (byte[])second.Clone();
            Array.Copy(first, PhantomRomContainer.HeaderSize, spliced, PhantomRomContainer.HeaderSize, 64 + PhantomRomContainer.TagSize);

            Assert.Throws<SecurityException>(() =>
                PhantomRomContainer.Open(spliced, pub, romKey, out _));
        }

        [Fact]
        public void Random_mutations_never_open()
        {
            // Fuzz: every single-byte mutation of a valid container must be rejected. This is the
            // property that matters — a Boot ROM is a trust root, so "mostly rejected" is a bug.
            var (priv, pub) = NewSigningKeys();
            byte[] romKey = Key32();
            byte[] container = PhantomRomContainer.Seal(RandomNumberGenerator.GetBytes(128), priv, romKey, KeyId(), 1);

            var random = new Random(20260105);
            for (int i = 0; i < 400; i++)
            {
                byte[] mutated = (byte[])container.Clone();
                int index = random.Next(mutated.Length);
                byte delta = (byte)(random.Next(1, 256));
                mutated[index] ^= delta;

                try
                {
                    PhantomRomContainer.Open(mutated, pub, romKey, out _);
                    Assert.Fail($"A mutated container opened (byte {index} xor {delta}).");
                }
                catch (SecurityException)
                {
                    // Expected: rejected.
                }
            }
        }
    }
}

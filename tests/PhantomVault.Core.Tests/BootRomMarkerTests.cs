using System;
using System.IO;
using System.Security.Cryptography;
using PhantomVault.Core.Services.BootRom;
using Xunit;

namespace PhantomVault.Core.Tests
{
    /// <summary>
    /// The Boot ROM marker. Its canonical bytes are hashed and pinned inside the encrypted
    /// manifest, so that hash has to survive a save/load round-trip unchanged — otherwise every
    /// unlock would report a false mismatch.
    /// </summary>
    public sealed class BootRomMarkerTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"PhantomVault_Marker_{Guid.NewGuid():N}");

        public BootRomMarkerTests() => Directory.CreateDirectory(_root);

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { /* best-effort cleanup */ }
        }

        private static BootRomMarker NewMarker() => new()
        {
            KeyIdBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(PhantomRomContainer.KeyIdSize)),
            MinRomVersion = 3,
            SaltBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            SigningPublicKeyBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            EscrowSaltBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        };

        [Fact]
        public void A_saved_marker_loads_back_identically()
        {
            var marker = NewMarker();
            marker.Save(_root);

            var loaded = BootRomMarker.TryLoad(_root);

            Assert.NotNull(loaded);
            Assert.Equal(marker.KeyIdBase64, loaded!.KeyIdBase64);
            Assert.Equal(marker.MinRomVersion, loaded.MinRomVersion);
            Assert.Equal(marker.SaltBase64, loaded.SaltBase64);
            Assert.Equal(marker.SigningPublicKeyBase64, loaded.SigningPublicKeyBase64);
            Assert.Equal(marker.EscrowSaltBase64, loaded.EscrowSaltBase64);
        }

        [Fact]
        public void The_pinned_hash_survives_a_round_trip()
        {
            // This is what the manifest pin compares against on every unlock of a bound vault.
            var marker = NewMarker();
            byte[] before = SHA256.HashData(marker.CanonicalBytes());

            marker.Save(_root);
            byte[] after = SHA256.HashData(BootRomMarker.TryLoad(_root)!.CanonicalBytes());

            Assert.Equal(before, after);
        }

        [Fact]
        public void Editing_the_marker_changes_its_hash()
        {
            var marker = NewMarker();
            marker.Save(_root);
            byte[] original = SHA256.HashData(BootRomMarker.TryLoad(_root)!.CanonicalBytes());

            var edited = BootRomMarker.TryLoad(_root)!;
            edited.MinRomVersion = 99;
            edited.Save(_root);

            byte[] tampered = SHA256.HashData(BootRomMarker.TryLoad(_root)!.CanonicalBytes());

            Assert.NotEqual(original, tampered);
        }

        [Fact]
        public void An_absent_marker_reads_as_unbound()
        {
            Assert.Null(BootRomMarker.TryLoad(_root));
            Assert.Null(BootRomMarker.TryLoad(Path.Combine(_root, "nope")));
            Assert.Null(BootRomMarker.TryLoad(string.Empty));
        }

        [Fact]
        public void A_corrupt_or_incomplete_marker_reads_as_unbound()
        {
            Directory.CreateDirectory(BootRomMarker.BootDirectory(_root));

            File.WriteAllText(BootRomMarker.MarkerPath(_root), "{ not json");
            Assert.Null(BootRomMarker.TryLoad(_root));

            // Well-formed JSON but missing the fields unlock needs.
            File.WriteAllText(BootRomMarker.MarkerPath(_root), "{\"version\":1}");
            Assert.Null(BootRomMarker.TryLoad(_root));
        }

        [Fact]
        public void A_marker_from_a_future_version_is_not_honoured()
        {
            var marker = NewMarker();
            marker.Version = BootRomMarker.CurrentVersion + 1;
            marker.Save(_root);

            Assert.Null(BootRomMarker.TryLoad(_root));
        }
    }
}

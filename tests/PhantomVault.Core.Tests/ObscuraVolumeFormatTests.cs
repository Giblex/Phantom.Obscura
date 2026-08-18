using System;
using System.Linq;
using System.Text;
using PhantomVault.Core.Services;
using Xunit;

namespace PhantomVault.Core.Tests
{
    public class ObscuraVolumeFormatTests
    {
        [Fact]
        public void Header_plaintext_round_trips()
        {
            var json = Encoding.UTF8.GetBytes("{\"Version\":1,\"Entries\":[]}");

            var packed = ObscuraVolumeFormat.PackHeaderPlaintext(json);

            Assert.Equal(json, ObscuraVolumeFormat.UnpackHeaderPlaintext(packed));
        }

        [Fact]
        public void Header_plaintext_is_padded_to_the_granularity()
        {
            // The point of the padding: the ciphertext length is the one header field that
            // has to stay readable, so it must not track the manifest's true size.
            var small = ObscuraVolumeFormat.PackHeaderPlaintext(Encoding.UTF8.GetBytes("{}"));
            var larger = ObscuraVolumeFormat.PackHeaderPlaintext(Encoding.UTF8.GetBytes(new string('x', 900)));

            Assert.Equal(0, small.Length % ObscuraVolumeFormat.HeaderPaddingGranularity);
            Assert.Equal(0, larger.Length % ObscuraVolumeFormat.HeaderPaddingGranularity);

            // Two manifests of very different sizes inside one band are indistinguishable.
            Assert.Equal(small.Length, larger.Length);
        }

        [Fact]
        public void Header_padding_is_random_not_zeroed()
        {
            // A run of zeros inside otherwise-high-entropy ciphertext is itself a signal,
            // and would leak the true manifest length back out via compressibility.
            var json = Encoding.UTF8.GetBytes("{}");

            var a = ObscuraVolumeFormat.PackHeaderPlaintext(json);
            var b = ObscuraVolumeFormat.PackHeaderPlaintext(json);

            Assert.Equal(a.Length, b.Length);
            Assert.NotEqual(a, b);

            var tail = a.Skip(4 + json.Length).ToArray();
            Assert.Contains(tail, byteValue => byteValue != 0);
        }

        [Fact]
        public void Oversized_declared_length_is_rejected()
        {
            // Guards against a corrupt or hostile header steering a slice past the buffer.
            var packed = ObscuraVolumeFormat.PackHeaderPlaintext(Encoding.UTF8.GetBytes("{}"));
            packed[0] = 0xFF; packed[1] = 0xFF; packed[2] = 0xFF; packed[3] = 0x7F;

            Assert.Throws<InvalidOperationException>(() => ObscuraVolumeFormat.UnpackHeaderPlaintext(packed));
        }

        [Fact]
        public void Negative_declared_length_is_rejected()
        {
            var packed = ObscuraVolumeFormat.PackHeaderPlaintext(Encoding.UTF8.GetBytes("{}"));
            packed[3] = 0xFF; // sign bit set

            Assert.Throws<InvalidOperationException>(() => ObscuraVolumeFormat.UnpackHeaderPlaintext(packed));
        }

        [Fact]
        public void Legacy_volumes_are_recognised_by_their_signature()
        {
            var legacy = Encoding.ASCII.GetBytes("OBSCUR01").Concat(new byte[] { 1, 2, 3, 4 }).ToArray();

            Assert.True(ObscuraVolumeFormat.IsLegacyHeader(legacy));
        }

        [Fact]
        public void A_v2_header_is_not_mistaken_for_a_legacy_one()
        {
            // v2 opens with a random nonce. There is no v2 marker to look for — the absence
            // of the v1 signature IS the discriminator, which is what leaves nothing
            // identifying on disk once the last legacy volume has been upgraded.
            var v2Head = new byte[16];
            System.Security.Cryptography.RandomNumberGenerator.Fill(v2Head);
            v2Head[0] = (byte)'X'; // ensure it cannot collide with the ASCII magic

            Assert.False(ObscuraVolumeFormat.IsLegacyHeader(v2Head));
        }

        [Fact]
        public void A_truncated_head_is_not_treated_as_legacy()
        {
            Assert.False(ObscuraVolumeFormat.IsLegacyHeader(new byte[] { 0x4F, 0x42 }));
        }

        [Theory]
        [InlineData(1, 64L * 1024 * 1024)]
        [InlineData(64L * 1024 * 1024, 64L * 1024 * 1024)]
        [InlineData(64L * 1024 * 1024 + 1, 128L * 1024 * 1024)]
        [InlineData(268_443_252L, 320L * 1024 * 1024)]
        public void Sizes_round_up_to_the_next_bucket(long actual, long expected)
        {
            Assert.Equal(expected, ObscuraVolumeFormat.BucketedSize(actual));
        }

        [Fact]
        public void Bucketing_never_shrinks_a_volume()
        {
            foreach (var size in new long[] { 1, 4096, 1_000_000, 268_443_252, 3_000_000_000 })
                Assert.True(ObscuraVolumeFormat.BucketedSize(size) >= size);
        }

        [Fact]
        public void Small_vaults_become_indistinguishable_from_each_other()
        {
            // The property that matters for a decoy: a nearly-empty vault and a moderately
            // full one must not be tellable apart by file size alone.
            Assert.Equal(
                ObscuraVolumeFormat.BucketedSize(12_000),
                ObscuraVolumeFormat.BucketedSize(40L * 1024 * 1024));
        }
    }
}

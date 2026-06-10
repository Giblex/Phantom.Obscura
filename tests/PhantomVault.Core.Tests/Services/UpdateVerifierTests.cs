#nullable enable
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using PhantomVault.Core.Services.Update;
using Xunit;

namespace PhantomVault.Core.Tests.Services;

/// <summary>
/// Covers every <see cref="UpdateVerificationFailure"/> path and the happy path,
/// using a synthetic Ed25519 keypair injected via the internal test constructor.
///
/// Authenticode (failures 10 + 11) is exercised in a Windows-only sibling fixture;
/// those paths exit before the P/Invoke when subject is null.
/// </summary>
public sealed class UpdateVerifierTests
{
    private const string AssetHost = "updates.giblex.com";
    private static readonly Version Installed = new(1, 0, 0, 0);
    private static readonly Version Newer = new(1, 1, 0, 0);

    private static readonly byte[] FakeAsset = Encoding.UTF8.GetBytes("synthetic update payload v1.1.0.0");
    private static readonly string FakeAssetSha256Base64 = Convert.ToBase64String(
        System.Security.Cryptography.SHA256.HashData(FakeAsset));

    private static (Ed25519PublicKeyParameters Pub, Ed25519PrivateKeyParameters Priv) NewKeyPair()
    {
        var gen = new Org.BouncyCastle.Crypto.Generators.Ed25519KeyPairGenerator();
        gen.Init(new Org.BouncyCastle.Crypto.KeyGenerationParameters(new SecureRandom(), 256));
        var pair = gen.GenerateKeyPair();
        return ((Ed25519PublicKeyParameters)pair.Public, (Ed25519PrivateKeyParameters)pair.Private);
    }

    private static byte[] Sign(Ed25519PrivateKeyParameters priv, byte[] message)
    {
        var signer = new Ed25519Signer();
        signer.Init(forSigning: true, priv);
        signer.BlockUpdate(message, 0, message.Length);
        return signer.GenerateSignature();
    }

    private static byte[] ManifestBytes(object manifest)
        => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest));

    private static object ValidManifest(
        int schema = 1,
        string channel = "stable",
        string version = "1.1.0.0",
        string? minPrev = null,
        string? assetUrl = "https://updates.giblex.com/v1.1.0.0/PhantomVault.zip",
        long assetSize = 33,
        string? assetSha = null,
        object? authenticode = null) => new
        {
            schema,
            channel,
            version,
            releasedAtUtc = DateTime.UtcNow,
            minPreviousVersion = minPrev,
            asset = new
            {
                url = assetUrl,
                size = assetSize,
                sha256 = assetSha ?? FakeAssetSha256Base64,
            },
            authenticode,
        };

    // ─── Happy path ──────────────────────────────────────────────────────────

    [Fact]
    public void VerifyManifest_HappyPath_ReturnsParsedInfo()
    {
        var (pub, priv) = NewKeyPair();
        var sut = new UpdateVerifier(pub.GetEncoded());
        byte[] manifest = ManifestBytes(ValidManifest());
        byte[] sig = Sign(priv, manifest);

        UpdateInfo info = sut.VerifyManifest(manifest, sig, UpdateChannel.Stable, Installed, AssetHost);

        Assert.Equal(1, info.Schema);
        Assert.Equal(UpdateChannel.Stable, info.Channel);
        Assert.Equal(Newer, info.Version);
        Assert.Equal(AssetHost, info.AssetUrl.Host);
        Assert.Equal(FakeAsset.Length, info.AssetSize);
        Assert.Equal(32, info.AssetSha256.Length);
        Assert.Null(info.AuthenticodeSubject);
    }

    // ─── PublicKeyNotProvisioned (1) ─────────────────────────────────────────

    [Fact]
    public void VerifyManifest_DefaultCtorWithPlaceholderKey_ThrowsPublicKeyNotProvisioned()
    {
        // The shipped UpdatePublicKey is still the all-zero placeholder.
        var sut = new UpdateVerifier();
        byte[] manifest = ManifestBytes(ValidManifest());
        byte[] sig = new byte[64];

        var ex = Assert.Throws<UpdateVerificationException>(
            () => sut.VerifyManifest(manifest, sig, UpdateChannel.Stable, Installed, AssetHost));
        Assert.Equal(UpdateVerificationFailure.PublicKeyNotProvisioned, ex.Reason);
    }

    // ─── BadSignature (2) ────────────────────────────────────────────────────

    [Fact]
    public void VerifyManifest_WrongSize_ThrowsBadSignature()
    {
        var (pub, _) = NewKeyPair();
        var sut = new UpdateVerifier(pub.GetEncoded());
        byte[] manifest = ManifestBytes(ValidManifest());

        var ex = Assert.Throws<UpdateVerificationException>(
            () => sut.VerifyManifest(manifest, new byte[63], UpdateChannel.Stable, Installed, AssetHost));
        Assert.Equal(UpdateVerificationFailure.BadSignature, ex.Reason);
    }

    [Fact]
    public void VerifyManifest_TamperedManifest_ThrowsBadSignature()
    {
        var (pub, priv) = NewKeyPair();
        var sut = new UpdateVerifier(pub.GetEncoded());
        byte[] manifest = ManifestBytes(ValidManifest());
        byte[] sig = Sign(priv, manifest);
        manifest[manifest.Length - 5] ^= 0xFF; // bit flip after signing

        var ex = Assert.Throws<UpdateVerificationException>(
            () => sut.VerifyManifest(manifest, sig, UpdateChannel.Stable, Installed, AssetHost));
        Assert.Equal(UpdateVerificationFailure.BadSignature, ex.Reason);
    }

    [Fact]
    public void VerifyManifest_SignatureFromDifferentKey_ThrowsBadSignature()
    {
        var (pubTrusted, _) = NewKeyPair();
        var (_, privAttacker) = NewKeyPair();
        var sut = new UpdateVerifier(pubTrusted.GetEncoded());
        byte[] manifest = ManifestBytes(ValidManifest());
        byte[] sig = Sign(privAttacker, manifest);

        var ex = Assert.Throws<UpdateVerificationException>(
            () => sut.VerifyManifest(manifest, sig, UpdateChannel.Stable, Installed, AssetHost));
        Assert.Equal(UpdateVerificationFailure.BadSignature, ex.Reason);
    }

    // ─── BadManifest (3) — various sub-cases ─────────────────────────────────

    [Fact]
    public void VerifyManifest_NotJson_ThrowsBadManifest()
    {
        var (pub, priv) = NewKeyPair();
        var sut = new UpdateVerifier(pub.GetEncoded());
        byte[] manifest = Encoding.UTF8.GetBytes("not json {{{");
        byte[] sig = Sign(priv, manifest);

        var ex = Assert.Throws<UpdateVerificationException>(
            () => sut.VerifyManifest(manifest, sig, UpdateChannel.Stable, Installed, AssetHost));
        Assert.Equal(UpdateVerificationFailure.BadManifest, ex.Reason);
    }

    [Fact]
    public void VerifyManifest_UnsupportedSchema_ThrowsBadManifest()
    {
        var (pub, priv) = NewKeyPair();
        var sut = new UpdateVerifier(pub.GetEncoded());
        byte[] manifest = ManifestBytes(ValidManifest(schema: 99));
        byte[] sig = Sign(priv, manifest);

        var ex = Assert.Throws<UpdateVerificationException>(
            () => sut.VerifyManifest(manifest, sig, UpdateChannel.Stable, Installed, AssetHost));
        Assert.Equal(UpdateVerificationFailure.BadManifest, ex.Reason);
    }

    [Fact]
    public void VerifyManifest_UnknownChannel_ThrowsBadManifest()
    {
        var (pub, priv) = NewKeyPair();
        var sut = new UpdateVerifier(pub.GetEncoded());
        byte[] manifest = ManifestBytes(ValidManifest(channel: "nightly"));
        byte[] sig = Sign(priv, manifest);

        var ex = Assert.Throws<UpdateVerificationException>(
            () => sut.VerifyManifest(manifest, sig, UpdateChannel.Stable, Installed, AssetHost));
        Assert.Equal(UpdateVerificationFailure.BadManifest, ex.Reason);
    }

    [Fact]
    public void VerifyManifest_GarbageVersion_ThrowsBadManifest()
    {
        var (pub, priv) = NewKeyPair();
        var sut = new UpdateVerifier(pub.GetEncoded());
        byte[] manifest = ManifestBytes(ValidManifest(version: "v-not-a-version"));
        byte[] sig = Sign(priv, manifest);

        var ex = Assert.Throws<UpdateVerificationException>(
            () => sut.VerifyManifest(manifest, sig, UpdateChannel.Stable, Installed, AssetHost));
        Assert.Equal(UpdateVerificationFailure.BadManifest, ex.Reason);
    }

    [Fact]
    public void VerifyManifest_NonHttpsAssetUrl_ThrowsBadManifest()
    {
        var (pub, priv) = NewKeyPair();
        var sut = new UpdateVerifier(pub.GetEncoded());
        byte[] manifest = ManifestBytes(ValidManifest(assetUrl: "http://updates.giblex.com/x.zip"));
        byte[] sig = Sign(priv, manifest);

        var ex = Assert.Throws<UpdateVerificationException>(
            () => sut.VerifyManifest(manifest, sig, UpdateChannel.Stable, Installed, AssetHost));
        Assert.Equal(UpdateVerificationFailure.BadManifest, ex.Reason);
    }

    [Fact]
    public void VerifyManifest_OversizedAsset_ThrowsBadManifest()
    {
        var (pub, priv) = NewKeyPair();
        var sut = new UpdateVerifier(pub.GetEncoded());
        byte[] manifest = ManifestBytes(ValidManifest(assetSize: 2L * 1024 * 1024 * 1024));
        byte[] sig = Sign(priv, manifest);

        var ex = Assert.Throws<UpdateVerificationException>(
            () => sut.VerifyManifest(manifest, sig, UpdateChannel.Stable, Installed, AssetHost));
        Assert.Equal(UpdateVerificationFailure.BadManifest, ex.Reason);
    }

    [Fact]
    public void VerifyManifest_BadSha256Length_ThrowsBadManifest()
    {
        var (pub, priv) = NewKeyPair();
        var sut = new UpdateVerifier(pub.GetEncoded());
        byte[] manifest = ManifestBytes(ValidManifest(assetSha: Convert.ToBase64String(new byte[16])));
        byte[] sig = Sign(priv, manifest);

        var ex = Assert.Throws<UpdateVerificationException>(
            () => sut.VerifyManifest(manifest, sig, UpdateChannel.Stable, Installed, AssetHost));
        Assert.Equal(UpdateVerificationFailure.BadManifest, ex.Reason);
    }

    // ─── ChannelMismatch (4) ─────────────────────────────────────────────────

    [Fact]
    public void VerifyManifest_BetaManifestStableClient_ThrowsChannelMismatch()
    {
        var (pub, priv) = NewKeyPair();
        var sut = new UpdateVerifier(pub.GetEncoded());
        byte[] manifest = ManifestBytes(ValidManifest(channel: "beta"));
        byte[] sig = Sign(priv, manifest);

        var ex = Assert.Throws<UpdateVerificationException>(
            () => sut.VerifyManifest(manifest, sig, UpdateChannel.Stable, Installed, AssetHost));
        Assert.Equal(UpdateVerificationFailure.ChannelMismatch, ex.Reason);
    }

    // ─── VersionNotNewer (5) ─────────────────────────────────────────────────

    [Fact]
    public void VerifyManifest_SameVersion_ThrowsVersionNotNewer()
    {
        var (pub, priv) = NewKeyPair();
        var sut = new UpdateVerifier(pub.GetEncoded());
        byte[] manifest = ManifestBytes(ValidManifest(version: Installed.ToString()));
        byte[] sig = Sign(priv, manifest);

        var ex = Assert.Throws<UpdateVerificationException>(
            () => sut.VerifyManifest(manifest, sig, UpdateChannel.Stable, Installed, AssetHost));
        Assert.Equal(UpdateVerificationFailure.VersionNotNewer, ex.Reason);
    }

    [Fact]
    public void VerifyManifest_OlderVersion_ThrowsVersionNotNewer()
    {
        var (pub, priv) = NewKeyPair();
        var sut = new UpdateVerifier(pub.GetEncoded());
        byte[] manifest = ManifestBytes(ValidManifest(version: "0.9.0.0"));
        byte[] sig = Sign(priv, manifest);

        var ex = Assert.Throws<UpdateVerificationException>(
            () => sut.VerifyManifest(manifest, sig, UpdateChannel.Stable, Installed, AssetHost));
        Assert.Equal(UpdateVerificationFailure.VersionNotNewer, ex.Reason);
    }

    // ─── UpgradePathBlocked (6) ──────────────────────────────────────────────

    [Fact]
    public void VerifyManifest_BelowMinPreviousVersion_ThrowsUpgradePathBlocked()
    {
        var (pub, priv) = NewKeyPair();
        var sut = new UpdateVerifier(pub.GetEncoded());
        byte[] manifest = ManifestBytes(ValidManifest(version: "2.0.0.0", minPrev: "1.5.0.0"));
        byte[] sig = Sign(priv, manifest);

        var ex = Assert.Throws<UpdateVerificationException>(
            () => sut.VerifyManifest(manifest, sig, UpdateChannel.Stable, Installed, AssetHost));
        Assert.Equal(UpdateVerificationFailure.UpgradePathBlocked, ex.Reason);
    }

    [Fact]
    public void VerifyManifest_AtOrAboveMinPreviousVersion_Succeeds()
    {
        var (pub, priv) = NewKeyPair();
        var sut = new UpdateVerifier(pub.GetEncoded());
        byte[] manifest = ManifestBytes(ValidManifest(version: "2.0.0.0", minPrev: "1.0.0.0"));
        byte[] sig = Sign(priv, manifest);

        UpdateInfo info = sut.VerifyManifest(manifest, sig, UpdateChannel.Stable, Installed, AssetHost);
        Assert.Equal(new Version(2, 0, 0, 0), info.Version);
        Assert.Equal(new Version(1, 0, 0, 0), info.MinPreviousVersion);
    }

    // ─── AssetHostMismatch (7) ───────────────────────────────────────────────

    [Fact]
    public void VerifyManifest_WrongAssetHost_ThrowsAssetHostMismatch()
    {
        var (pub, priv) = NewKeyPair();
        var sut = new UpdateVerifier(pub.GetEncoded());
        byte[] manifest = ManifestBytes(ValidManifest(
            assetUrl: "https://evil.example.com/v1.1.0.0/PhantomVault.zip"));
        byte[] sig = Sign(priv, manifest);

        var ex = Assert.Throws<UpdateVerificationException>(
            () => sut.VerifyManifest(manifest, sig, UpdateChannel.Stable, Installed, AssetHost));
        Assert.Equal(UpdateVerificationFailure.AssetHostMismatch, ex.Reason);
    }

    // ─── VerifyAsset / VerifyAssetStream ─────────────────────────────────────

    [Fact]
    public void VerifyAsset_HappyPath_Succeeds()
    {
        var (pub, priv) = NewKeyPair();
        var sut = new UpdateVerifier(pub.GetEncoded());
        byte[] manifest = ManifestBytes(ValidManifest());
        byte[] sig = Sign(priv, manifest);
        UpdateInfo info = sut.VerifyManifest(manifest, sig, UpdateChannel.Stable, Installed, AssetHost);

        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, FakeAsset);
            sut.VerifyAsset(path, info); // does not throw
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void VerifyAsset_WrongSize_ThrowsAssetSizeMismatch()
    {
        var (pub, priv) = NewKeyPair();
        var sut = new UpdateVerifier(pub.GetEncoded());
        byte[] manifest = ManifestBytes(ValidManifest());
        byte[] sig = Sign(priv, manifest);
        UpdateInfo info = sut.VerifyManifest(manifest, sig, UpdateChannel.Stable, Installed, AssetHost);

        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, new byte[FakeAsset.Length - 1]);
            var ex = Assert.Throws<UpdateVerificationException>(() => sut.VerifyAsset(path, info));
            Assert.Equal(UpdateVerificationFailure.AssetSizeMismatch, ex.Reason);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void VerifyAsset_MissingFile_ThrowsAssetSizeMismatch()
    {
        var (pub, priv) = NewKeyPair();
        var sut = new UpdateVerifier(pub.GetEncoded());
        byte[] manifest = ManifestBytes(ValidManifest());
        byte[] sig = Sign(priv, manifest);
        UpdateInfo info = sut.VerifyManifest(manifest, sig, UpdateChannel.Stable, Installed, AssetHost);

        string path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.bin");
        var ex = Assert.Throws<UpdateVerificationException>(() => sut.VerifyAsset(path, info));
        Assert.Equal(UpdateVerificationFailure.AssetSizeMismatch, ex.Reason);
    }

    [Fact]
    public void VerifyAsset_WrongContent_ThrowsAssetHashMismatch()
    {
        var (pub, priv) = NewKeyPair();
        var sut = new UpdateVerifier(pub.GetEncoded());
        byte[] manifest = ManifestBytes(ValidManifest());
        byte[] sig = Sign(priv, manifest);
        UpdateInfo info = sut.VerifyManifest(manifest, sig, UpdateChannel.Stable, Installed, AssetHost);

        string path = Path.GetTempFileName();
        try
        {
            // Same length, different bytes.
            byte[] tampered = new byte[FakeAsset.Length];
            for (int i = 0; i < tampered.Length; i++) tampered[i] = (byte)(FakeAsset[i] ^ 0xAA);
            File.WriteAllBytes(path, tampered);

            var ex = Assert.Throws<UpdateVerificationException>(() => sut.VerifyAsset(path, info));
            Assert.Equal(UpdateVerificationFailure.AssetHashMismatch, ex.Reason);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void VerifyAssetStream_HappyPath_Succeeds()
    {
        var (pub, priv) = NewKeyPair();
        var sut = new UpdateVerifier(pub.GetEncoded());
        byte[] manifest = ManifestBytes(ValidManifest());
        byte[] sig = Sign(priv, manifest);
        UpdateInfo info = sut.VerifyManifest(manifest, sig, UpdateChannel.Stable, Installed, AssetHost);

        using var ms = new MemoryStream(FakeAsset);
        sut.VerifyAssetStream(ms, info); // does not throw
    }

    [Fact]
    public void VerifyAssetStream_WrongContent_ThrowsAssetHashMismatch()
    {
        var (pub, priv) = NewKeyPair();
        var sut = new UpdateVerifier(pub.GetEncoded());
        byte[] manifest = ManifestBytes(ValidManifest());
        byte[] sig = Sign(priv, manifest);
        UpdateInfo info = sut.VerifyManifest(manifest, sig, UpdateChannel.Stable, Installed, AssetHost);

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes("different payload, same-ish length OK"));
        var ex = Assert.Throws<UpdateVerificationException>(() => sut.VerifyAssetStream(ms, info));
        Assert.Equal(UpdateVerificationFailure.AssetHashMismatch, ex.Reason);
    }
}

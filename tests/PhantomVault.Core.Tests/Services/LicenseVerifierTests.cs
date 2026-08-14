#nullable enable
using System;
using PhantomVault.Core.Models.Licensing;
using PhantomVault.Core.Services.Licensing;
using Xunit;

namespace PhantomVault.Core.Tests.Services;

/// <summary>
/// Covers the <see cref="LicenseVerifier"/> failsafe model and every
/// <see cref="LicenseFailureReason"/> path, using a synthetic Ed25519 keypair
/// injected via the public test/dev constructor.
/// </summary>
public sealed class LicenseVerifierTests
{
    private const string Binding = "USB-BINDING-AAAA";
    private static readonly TimeSpan Grace = TimeSpan.FromDays(7);

    private static (byte[] pub, byte[] priv) NewKeys() => LicenseTokenCodec.GenerateKeyPair();

    private static string Token(byte[] priv, PremiumTier tier, DateTimeOffset expires, string? binding)
    {
        var claims = new LicenseClaims
        {
            LicenseId = "TEST-001",
            Tier = tier,
            UsbBindingId = binding,
            IssuedUtc = DateTimeOffset.UtcNow.AddDays(-1),
            ExpiresUtc = expires
        };
        return LicenseTokenCodec.CreateToken(claims, priv);
    }

    [Fact]
    public void ValidPremiumToken_Unlocks()
    {
        var (pub, priv) = NewKeys();
        var verifier = new LicenseVerifier(pub);
        var token = Token(priv, PremiumTier.Premium, DateTimeOffset.UtcNow.AddDays(20), Binding);

        var status = verifier.Verify(token, Binding, Grace);

        Assert.True(status.IsValid);
        Assert.Equal(PremiumTier.Premium, status.Tier);
        Assert.True(status.Has(PremiumFeature.CustomThemes));
        Assert.False(status.InGracePeriod);
    }

    [Fact]
    public void EmptyToken_ReturnsFree()
    {
        var (pub, _) = NewKeys();
        var verifier = new LicenseVerifier(pub);

        var status = verifier.Verify(null, Binding, Grace);

        Assert.False(status.IsValid);
        Assert.Equal(LicenseFailureReason.Empty, status.Reason);
    }

    [Fact]
    public void DefaultVerifier_ForeignSignedToken_ReturnsFree()
    {
        // This test used to assert NotProvisioned on the premise that the embedded
        // key was still the all-zero placeholder. LicensePublicKey now carries a real
        // production key, so IsProvisioned is true and the verifier proceeds to the
        // signature check. What still matters — and is what this asserts — is that a
        // token signed by anything other than the licensing authority never unlocks
        // premium. The NotProvisioned branch is unreachable in a provisioned build.
        var (_, priv) = NewKeys();
        var verifier = new LicenseVerifier();
        var token = Token(priv, PremiumTier.Premium, DateTimeOffset.UtcNow.AddDays(20), Binding);

        var status = verifier.Verify(token, Binding, Grace);

        Assert.False(status.IsValid);
        Assert.Equal(LicenseFailureReason.BadSignature, status.Reason);
    }

    [Fact]
    public void EmbeddedLicenseKey_IsProvisioned()
    {
        // Guards the premise of the test above: if the embedded key is ever reverted
        // to the all-zero placeholder, this fails loudly rather than silently turning
        // the signature test into a NotProvisioned test.
        Assert.True(LicensePublicKey.IsProvisioned);
    }

    [Fact]
    public void WrongKey_FailsSignature()
    {
        var (_, priv) = NewKeys();
        var (otherPub, _) = NewKeys();
        var verifier = new LicenseVerifier(otherPub);
        var token = Token(priv, PremiumTier.Premium, DateTimeOffset.UtcNow.AddDays(20), Binding);

        var status = verifier.Verify(token, Binding, Grace);

        Assert.False(status.IsValid);
        Assert.Equal(LicenseFailureReason.BadSignature, status.Reason);
    }

    [Fact]
    public void TamperedToken_FailsSignature()
    {
        var (pub, priv) = NewKeys();
        var verifier = new LicenseVerifier(pub);
        var token = Token(priv, PremiumTier.Premium, DateTimeOffset.UtcNow.AddDays(20), Binding);
        var tampered = token.Substring(0, token.Length - 2) + (token.EndsWith("A") ? "B" : "A");

        var status = verifier.Verify(tampered, Binding, Grace);

        Assert.False(status.IsValid);
        Assert.Equal(LicenseFailureReason.BadSignature, status.Reason);
    }

    [Fact]
    public void WrongBinding_IsRejected()
    {
        var (pub, priv) = NewKeys();
        var verifier = new LicenseVerifier(pub);
        var token = Token(priv, PremiumTier.Premium, DateTimeOffset.UtcNow.AddDays(20), Binding);

        var status = verifier.Verify(token, "USB-BINDING-DIFFERENT", Grace);

        Assert.False(status.IsValid);
        Assert.Equal(LicenseFailureReason.BindingMismatch, status.Reason);
    }

    [Fact]
    public void ExpiredWithinGrace_StillValid()
    {
        var (pub, priv) = NewKeys();
        var verifier = new LicenseVerifier(pub);
        var token = Token(priv, PremiumTier.Premium, DateTimeOffset.UtcNow.AddDays(-2), Binding);

        var status = verifier.Verify(token, Binding, Grace);

        Assert.True(status.IsValid);
        Assert.True(status.InGracePeriod);
    }

    [Fact]
    public void ExpiredBeyondGrace_IsRejected()
    {
        var (pub, priv) = NewKeys();
        var verifier = new LicenseVerifier(pub);
        var token = Token(priv, PremiumTier.Premium, DateTimeOffset.UtcNow.AddDays(-30), Binding);

        var status = verifier.Verify(token, Binding, Grace);

        Assert.False(status.IsValid);
        Assert.Equal(LicenseFailureReason.Expired, status.Reason);
    }

    [Fact]
    public void UnboundToken_AcceptedRegardlessOfBinding()
    {
        var (pub, priv) = NewKeys();
        var verifier = new LicenseVerifier(pub);
        var token = Token(priv, PremiumTier.Premium, DateTimeOffset.UtcNow.AddDays(20), binding: null);

        var status = verifier.Verify(token, "any-binding", Grace);

        Assert.True(status.IsValid);
    }
}

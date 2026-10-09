using System;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PhantomVault.Core.Services.Network;
using Xunit;

namespace PhantomVault.Core.Tests;

public sealed class SpkiPinTests
{
    [Fact]
    public void ValidatedIntermediatePin_SurvivesLeafKeyRotation_ButRootAndTlsErrorsAreRejected()
    {
        using var rootKey = RSA.Create(2048);
        using var intermediateKey = RSA.Create(2048);
        using var leafKey = RSA.Create(2048);
        using var rotatedKey = RSA.Create(2048);
        var now = DateTimeOffset.UtcNow;
        var rootRequest = Request("CN=Test Root", rootKey, true);
        using var root = rootRequest.CreateSelfSigned(now.AddDays(-1), now.AddDays(30));
        var intermediateRequest = Request("CN=Test Intermediate", intermediateKey, true);
        using var intermediatePublic = intermediateRequest.Create(root, now.AddHours(-1), now.AddDays(20), RandomNumberGenerator.GetBytes(16));
        using var intermediate = intermediatePublic.CopyWithPrivateKey(intermediateKey);
        using var leaf = Request("CN=site.test", leafKey, false).Create(
            intermediate, now.AddMinutes(-30), now.AddDays(10), RandomNumberGenerator.GetBytes(16));
        using var rotated = Request("CN=site.test", rotatedKey, false).Create(
            intermediate, now.AddMinutes(-30), now.AddDays(10), RandomNumberGenerator.GetBytes(16));
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(root);
        chain.ChainPolicy.ExtraStore.Add(intermediate);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.DisableCertificateDownloads = true;
        string backup = SpkiPin.ComputePinBase64(intermediate);
        Assert.True(chain.Build(leaf));
        Assert.True(SpkiPin.MatchesValidatedChain(leaf, chain, SslPolicyErrors.None, backup));
        Assert.True(chain.Build(rotated));
        Assert.True(SpkiPin.MatchesValidatedChain(rotated, chain, SslPolicyErrors.None, backup));
        Assert.False(SpkiPin.MatchesValidatedChain(rotated, chain, SslPolicyErrors.None, SpkiPin.ComputePinBase64(leaf)));
        Assert.False(SpkiPin.MatchesValidatedChain(rotated, chain, SslPolicyErrors.None, SpkiPin.ComputePinBase64(root)));
        Assert.False(SpkiPin.MatchesValidatedChain(rotated, chain, SslPolicyErrors.RemoteCertificateNameMismatch, backup));
        Assert.False(SpkiPin.MatchesValidatedChain(rotated, chain, SslPolicyErrors.RemoteCertificateChainErrors, backup));
        Assert.False(SpkiPin.MatchesValidatedChain(leaf, chain, SslPolicyErrors.None, backup));
        Assert.False(SpkiPin.MatchesValidatedChain(rotated, null, SslPolicyErrors.None, backup));
    }

    private static CertificateRequest Request(string subject, RSA key, bool ca)
    {
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(ca, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            ca ? X509KeyUsageFlags.KeyCertSign : X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        return request;
    }
}

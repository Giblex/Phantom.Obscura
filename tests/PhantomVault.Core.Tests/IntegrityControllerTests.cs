#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Services.Integrity;
using Xunit;

namespace PhantomVault.Core.Tests;

public sealed class IntegrityControllerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "obscura-integrity-" + Guid.NewGuid().ToString("N"));

    public IntegrityControllerTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void SignedManifest_DetectsModifiedUnexpectedAndDeletedFiles()
    {
        File.WriteAllText(Path.Combine(_root, "stable.dll"), "original");
        File.WriteAllText(Path.Combine(_root, "deleted.json"), "delete-me");
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifests = new IntegrityManifestService();
        var manifest = manifests.Create(_root, "test", signingKey, "release-key-1");
        using var publicKey = ECDsa.Create(signingKey.ExportParameters(false));
        var log = new TamperEvidentIntegrityLog(Path.Combine(_root, "state", "events.jsonl"), RandomNumberGenerator.GetBytes(32));
        using var controller = new IntegrityController(Options(), manifest, manifests, publicKey, log);

        File.WriteAllText(Path.Combine(_root, "stable.dll"), "modified");
        File.Delete(Path.Combine(_root, "deleted.json"));
        File.WriteAllText(Path.Combine(_root, "intruder.dll"), "unexpected");
        IntegrityScanResult result = controller.Scan();

        Assert.True(result.ManifestSignatureValid);
        Assert.Contains(result.Changes, x => x.Kind == IntegrityChangeKind.Modified && x.RelativePath == "stable.dll");
        Assert.Contains(result.Changes, x => x.Kind == IntegrityChangeKind.Deleted && x.RelativePath == "deleted.json");
        Assert.Contains(result.Changes, x => x.Kind == IntegrityChangeKind.Unexpected && x.RelativePath == "intruder.dll");
        Assert.All(result.Changes, x => Assert.Equal(IntegrityChangeOrigin.ExternalOrUnknown, x.Origin));
    }

    [Fact]
    public void AuthorizedWrite_IsAttributedOnce_AndAuditLogDetectsTampering()
    {
        File.WriteAllText(Path.Combine(_root, "settings.json"), "v1");
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifests = new IntegrityManifestService();
        var manifest = manifests.Create(_root, "test", signingKey, "release-key-1");
        using var publicKey = ECDsa.Create(signingKey.ExportParameters(false));
        byte[] auditKey = RandomNumberGenerator.GetBytes(32);
        string logPath = Path.Combine(_root, "state", "events.jsonl");
        var log = new TamperEvidentIntegrityLog(logPath, auditKey);
        using var controller = new IntegrityController(Options(), manifest, manifests, publicKey, log);

        string oldHash = Convert.ToHexString(SHA256.HashData("v1"u8.ToArray())).ToLowerInvariant();
        string newHash = Convert.ToHexString(SHA256.HashData("v2"u8.ToArray())).ToLowerInvariant();
        AuthorizedWrite authorization = controller.AuthorizeWrite(new IntegrityWriteIntent(
            "settings.json", IntegrityChangeKind.Modified, oldHash, newHash, MaximumLength: 2));
        File.WriteAllText(Path.Combine(_root, "settings.json"), "v2");
        IntegrityEvent change = Assert.Single(controller.Scan().Changes);

        Assert.Equal(IntegrityChangeOrigin.AuthorizedApplicationWrite, change.Origin);
        Assert.Equal(authorization.Id, change.AuthorizationId);
        Assert.Single(log.ReadAndVerify());

        string text = File.ReadAllText(logPath);
        File.WriteAllText(logPath, text.Replace("settings.json", "other.json", StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(() => log.ReadAndVerify());
    }

    [Fact]
    public void AuthorizedWrite_WithUnexpectedContent_IsClassifiedExternal()
    {
        File.WriteAllText(Path.Combine(_root, "policy.json"), "safe");
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifests = new IntegrityManifestService();
        var manifest = manifests.Create(_root, "test", signingKey, "release-key-1");
        using var publicKey = ECDsa.Create(signingKey.ExportParameters(false));
        var log = new TamperEvidentIntegrityLog(Path.Combine(_root, "state", "events.jsonl"), RandomNumberGenerator.GetBytes(32));
        using var controller = new IntegrityController(Options(), manifest, manifests, publicKey, log);
        string expected = Convert.ToHexString(SHA256.HashData("approved"u8.ToArray())).ToLowerInvariant();
        controller.AuthorizeWrite(new IntegrityWriteIntent("policy.json", IntegrityChangeKind.Modified,
            ExpectedNewSha256: expected, MaximumLength: 32));

        File.WriteAllText(Path.Combine(_root, "policy.json"), "attacker-content");
        IntegrityEvent change = Assert.Single(controller.Scan().Changes);

        Assert.Equal(IntegrityChangeOrigin.ExternalOrUnknown, change.Origin);
        Assert.Null(change.AuthorizationId);
    }

    [Fact]
    public void ManifestSignature_FailsAfterManifestMutation()
    {
        File.WriteAllText(Path.Combine(_root, "app.dll"), "binary");
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var service = new IntegrityManifestService();
        var manifest = service.Create(_root, "test", signingKey, "release-key-1");
        var changed = manifest with { RootLabel = "attacker" };
        using var publicKey = ECDsa.Create(signingKey.ExportParameters(false));

        Assert.False(service.Verify(changed, publicKey));
    }

    [Fact]
    public void RollbackGuard_RejectsOlderSignedReleaseSequence()
    {
        string statePath = Path.Combine(_root, "state", "highest-release.txt");
        var guard = new IntegrityRollbackGuard(statePath);
        var files = Array.Empty<IntegrityFileRecord>();
        var current = new IntegrityManifest(1, "test", DateTimeOffset.UtcNow, files,
            "ECDSA-P256-SHA256", "key", "signature", ReleaseSequence: 200);
        var older = current with { ReleaseSequence = 199 };

        guard.AcceptOrThrow(current);

        Assert.Equal(200, guard.ReadHighest());
        Assert.Throws<InvalidDataException>(() => guard.AcceptOrThrow(older));
    }

    [Fact]
    public async Task PhantomKeyAnchor_IsIndependentlyVerified_AndDetectsReceiptMutation()
    {
        byte[] auditKey = RandomNumberGenerator.GetBytes(32);
        string logPath = Path.Combine(_root, "state", "events.jsonl");
        var log = new TamperEvidentIntegrityLog(logPath, auditKey);
        log.Append(new IntegrityEvent(0, DateTimeOffset.UtcNow, IntegrityChangeKind.Modified,
            IntegrityChangeOrigin.ExternalOrUnknown, "app.dll", null, "aa", null, "", ""));
        using var provider = new TestPhantomKeyAnchorProvider();
        string receiptsPath = Path.Combine(_root, "state", "anchors.jsonl");
        var coordinator = new IntegrityAnchorCoordinator(log, provider, receiptsPath, provider.KeyId);

        IntegrityAnchorReceipt? receipt = await coordinator.AnchorCurrentHeadAsync("release-key", 3);

        Assert.NotNull(receipt);
        Assert.Equal("PhantomKey", receipt.Proof.Provider);
        byte[] relabeledEnvelope = Convert.FromBase64String(receipt.Proof.SignatureEnvelopeB64);
        relabeledEnvelope[4] = 1; // attempt to relabel TPM+USB Tier 3 proof as Tier 1
        var relabeled = receipt with
        {
            Proof = receipt.Proof with { SignatureEnvelopeB64 = Convert.ToBase64String(relabeledEnvelope) }
        };
        Assert.False(IntegrityAnchorCoordinator.VerifyReceipt(relabeled));
        Assert.Single(coordinator.ReadAndVerifyReceipts());
        string serialized = File.ReadAllText(receiptsPath);
        File.WriteAllText(receiptsPath, serialized.Replace("release-key", "attacker-key", StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(() => coordinator.ReadAndVerifyReceipts());
    }

    private IntegrityControllerOptions Options() => new()
    {
        ProtectedRoot = _root,
        StateDirectory = Path.Combine(_root, "state"),
        PeriodicScanInterval = TimeSpan.FromHours(1)
    };

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private sealed class TestPhantomKeyAnchorProvider : IIntegrityAnchorProvider, IDisposable
    {
        private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly byte[] _publicKey;

        public TestPhantomKeyAnchorProvider()
        {
            _publicKey = _key.ExportSubjectPublicKeyInfo();
            KeyId = Convert.ToHexString(SHA256.HashData(_publicKey)).ToLowerInvariant();
        }

        public string KeyId { get; }

        public Task<IntegrityAnchorProof> SignDigestAsync(byte[] sha256Digest, int tier, CancellationToken cancellationToken)
        {
            int macLength = tier >= 3 ? 32 : 0;
            byte[] mac = RandomNumberGenerator.GetBytes(macLength);
            byte[] domain = System.Text.Encoding.UTF8.GetBytes("PhantomKey:TxnSignature:v2\0");
            byte[] bindingInput = [.. domain, (byte)tier, .. sha256Digest, .. mac];
            byte[] signature = _key.SignHash(SHA256.HashData(bindingInput));
            byte[] envelope = new byte[9 + signature.Length + macLength];
            envelope[0] = (byte)'P'; envelope[1] = (byte)'K'; envelope[2] = (byte)'T'; envelope[3] = (byte)'2';
            envelope[4] = (byte)tier;
            envelope[5] = (byte)signature.Length; envelope[6] = (byte)(signature.Length >> 8);
            envelope[7] = (byte)macLength; envelope[8] = (byte)(macLength >> 8);
            signature.CopyTo(envelope, 9);
            mac.CopyTo(envelope, 9 + signature.Length);
            return Task.FromResult(new IntegrityAnchorProof("PhantomKey", KeyId, "ECDSA-P256-SHA256",
                Convert.ToBase64String(_publicKey), Convert.ToBase64String(envelope)));
        }

        public void Dispose() => _key.Dispose();
    }
}

using System.Security.Cryptography;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using PhantomVault.Core.Services.Integrity;

namespace PhantomVault.PrivilegedBroker;

internal sealed class IntegrityWatchdogWorker : BackgroundService
{
    private const string ManifestName = "integrity-manifest.json";
    private const string PublicKeyName = "integrity-public-key.pem";
    private IntegrityController? _controller;
    private IntegrityAnchorCoordinator? _anchors;
    private WindowsUsnJournalMonitor? _usn;
    private long _releaseSequence;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_controller is null) TryStartController();
                else
                {
                    ReconcileUsn();
                    VerifyLoadedModules();
                    if (_anchors is not null) await TryAnchorAsync(stoppingToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Program.TryLog($"[integrity] watchdog cycle failed: {ex.GetType().Name}: {ex.Message}");
                WriteHealth("degraded", ex.Message, null);
            }
            await Task.Delay(_controller is null ? TimeSpan.FromSeconds(30) : TimeSpan.FromMinutes(5), stoppingToken)
                .ConfigureAwait(false);
        }
    }

    private void TryStartController()
    {
        string? uiPath = BrokerConfig.LoadAllowedClientPath();
        if (string.IsNullOrWhiteSpace(uiPath) || !File.Exists(uiPath)) return;
        string root = Path.GetDirectoryName(Path.GetFullPath(uiPath))!;
        string manifestPath = Path.Combine(root, ManifestName);
        string publicKeyPath = Path.Combine(root, PublicKeyName);
        if (!File.Exists(manifestPath) || !File.Exists(publicKeyPath))
        {
            WriteHealth("unprovisioned", "Signed integrity manifest/public key not installed.", null);
            return;
        }

        var manifests = new IntegrityManifestService();
        IntegrityManifest manifest = manifests.Read(manifestPath);
        using var verificationKey = ECDsa.Create();
        verificationKey.ImportFromPem(File.ReadAllText(publicKeyPath));
        if (!manifests.Verify(manifest, verificationKey))
            throw new CryptographicException("Installed integrity manifest signature is invalid.");
        string? pinnedManifestKey = BrokerConfig.LoadManifestKeyPin();
        if (string.IsNullOrWhiteSpace(pinnedManifestKey) ||
            !string.Equals(pinnedManifestKey, manifest.KeyId, StringComparison.OrdinalIgnoreCase))
            throw new CryptographicException("Installed manifest key does not match the independently pinned installer trust root.");

        WindowsProtectedTreeValidator.Validate(root, ManifestName, PublicKeyName);

        string state = BrokerConfig.IntegrityStateDirectory;
        new IntegrityRollbackGuard(Path.Combine(state, "highest-release.txt")).AcceptOrThrow(manifest);
        _releaseSequence = manifest.ReleaseSequence;
        byte[] auditKey = ProtectedIntegrityKeyStore.LoadOrCreate(Path.Combine(state, "audit-key.dpapi"));
        try
        {
            var log = new TamperEvidentIntegrityLog(Path.Combine(state, "events.jsonl"), auditKey);
            log.Append(new IntegrityEvent(0, DateTimeOffset.UtcNow, IntegrityChangeKind.BaselineVerified,
                IntegrityChangeOrigin.AuthorizedApplicationWrite, $"[release:{manifest.ReleaseSequence}]", null,
                manifest.KeyId, null, string.Empty, string.Empty));
            var key = ECDsa.Create();
            key.ImportFromPem(File.ReadAllText(publicKeyPath));
            _controller = new IntegrityController(new IntegrityControllerOptions
            {
                ProtectedRoot = root,
                StateDirectory = state,
                ExcludedRelativePaths = [ManifestName, PublicKeyName],
                PeriodicScanInterval = TimeSpan.FromMinutes(5)
            }, manifest, manifests, key, log);
            _controller.ChangeDetected += OnChangeDetected;
            IntegrityScanResult initial = _controller.Scan();
            _controller.Start();
            try { _usn = new WindowsUsnJournalMonitor(root); }
            catch (Exception ex) { Program.TryLog($"[integrity] USN journal unavailable; periodic scans remain active: {ex.Message}"); }
            _anchors = new IntegrityAnchorCoordinator(log, new PhantomKeyWatchdogAnchorProvider(),
                Path.Combine(state, "phantom-key-anchors.jsonl"), LoadPinnedPhantomKeyId());
            WriteHealth(initial.IsClean ? "healthy" : "tampered",
                initial.IsClean ? "Initial scan verified." : $"Initial scan found {initial.Changes.Count} change(s).",
                initial.Changes.LastOrDefault());
            Program.TryLog($"[integrity] watchdog started for '{root}', release {manifest.ReleaseSequence}.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(auditKey);
        }
    }

    private static string? LoadPinnedPhantomKeyId()
    {
        string path = Path.Combine(BrokerConfig.IntegrityStateDirectory, "phantom-key-id.txt");
        try { return File.Exists(path) ? File.ReadAllText(path).Trim() : null; }
        catch { return null; }
    }

    private void ReconcileUsn()
    {
        if (_usn is null || _controller is null) return;
        var observation = _usn.Observe();
        if (!observation.Advanced) return;
        IntegrityScanResult result = _controller.Scan();
        if (observation.ContinuityLost)
        {
            Program.TryLog("[integrity] USN journal continuity lost; completed authoritative full scan.");
            WriteHealth(result.IsClean ? "warning" : "tampered",
                result.IsClean ? "USN journal reset detected; full scan completed cleanly."
                               : $"USN reset reconciliation found {result.Changes.Count} change(s).",
                result.Changes.LastOrDefault());
        }
    }

    private static void VerifyLoadedModules()
    {
        string? uiPath = BrokerConfig.LoadAllowedClientPath();
        if (string.IsNullOrWhiteSpace(uiPath)) return;
        string root = Path.GetDirectoryName(Path.GetFullPath(uiPath))!.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string processName = Path.GetFileNameWithoutExtension(uiPath);
        foreach (Process process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                try
                {
                    foreach (ProcessModule module in process.Modules)
                    {
                        string path = Path.GetFullPath(module.FileName);
                        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                        string? expectedSigner = BrokerConfig.LoadAllowedClientSignerSha256();
                        if (string.IsNullOrWhiteSpace(expectedSigner) ||
                            !AuthenticodeTrust.TryGetTrustedSignerSha256(path, out string? actualSigner) ||
                            !string.Equals(expectedSigner, actualSigner, StringComparison.OrdinalIgnoreCase))
                        {
                            WriteHealth("critical", $"Loaded module has no trusted Authenticode signature: {Path.GetFileName(path)}", null);
                            return;
                        }
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    Program.TryLog($"[integrity] loaded-module inspection deferred: {ex.Message}");
                }
            }
        }
    }

    private async Task TryAnchorAsync(CancellationToken cancellationToken)
    {
        if (_anchors is null) return;
        try
        {
            IntegrityAnchorReceipt? receipt = await _anchors.AnchorCurrentHeadAsync(
                BrokerConfig.LoadManifestKeyPin() ?? "unknown-release-key", tier: 1, cancellationToken).ConfigureAwait(false);
            if (receipt is not null) PinOrVerifyPhantomKey(receipt.Proof.KeyId);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException)
        {
            Program.TryLog($"[integrity] Phantom Key anchor deferred: {ex.Message}");
        }
    }

    private static void PinOrVerifyPhantomKey(string keyId)
    {
        string path = Path.Combine(BrokerConfig.IntegrityStateDirectory, "phantom-key-id.txt");
        if (File.Exists(path))
        {
            if (!string.Equals(File.ReadAllText(path).Trim(), keyId, StringComparison.Ordinal))
                throw new CryptographicException("Phantom Key transaction identity changed.");
            return;
        }
        string temporary = path + ".tmp." + Guid.NewGuid().ToString("N");
        File.WriteAllText(temporary, keyId);
        File.Move(temporary, path, false);
    }

    public string GetVerdict(string challenge)
    {
        if (challenge.Length is < 32 or > 256) throw new ArgumentException("Invalid verdict challenge.");
        string healthPath = Path.Combine(BrokerConfig.IntegrityStateDirectory, "health.json");
        string health = File.Exists(healthPath) ? File.ReadAllText(healthPath) : "{}";
        return JsonSerializer.Serialize(new
        {
            Protocol = 1,
            Challenge = challenge,
            TimestampUtc = DateTimeOffset.UtcNow,
            ReleaseSequence = _releaseSequence,
            ManifestKeyId = BrokerConfig.LoadManifestKeyPin(),
            PhantomKeyId = LoadPinnedPhantomKeyId(),
            ControllerReady = _controller is not null,
            Health = JsonDocument.Parse(health).RootElement.Clone()
        });
    }

    public string AuthorizeWrite(string relativePath, int changeKind, string? oldHash, string? newHash, long maximumLength)
    {
        var controller = _controller ?? throw new InvalidOperationException("Integrity controller is not ready.");
        if (!Enum.IsDefined(typeof(IntegrityChangeKind), changeKind)) throw new ArgumentOutOfRangeException(nameof(changeKind));
        var authorization = controller.AuthorizeWrite(new IntegrityWriteIntent(relativePath,
            (IntegrityChangeKind)changeKind, oldHash, newHash, maximumLength));
        return authorization.Id;
    }

    private void OnChangeDetected(object? sender, IntegrityEvent change)
    {
        bool executable = change.RelativePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                          change.RelativePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        string severity = change.Origin == IntegrityChangeOrigin.ExternalOrUnknown && executable
            ? "critical" : change.Origin == IntegrityChangeOrigin.ExternalOrUnknown ? "warning" : "healthy";
        if (executable && change.Kind is not IntegrityChangeKind.Deleted)
        {
            string? root = BrokerConfig.LoadAllowedClientPath() is { } ui ? Path.GetDirectoryName(ui) : null;
            string path = root is null ? string.Empty : Path.Combine(root, change.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            string? expectedSigner = BrokerConfig.LoadAllowedClientSignerSha256();
            if (!File.Exists(path) || string.IsNullOrWhiteSpace(expectedSigner) ||
                !AuthenticodeTrust.TryGetTrustedSignerSha256(path, out string? actualSigner) ||
                !string.Equals(expectedSigner, actualSigner, StringComparison.OrdinalIgnoreCase)) severity = "critical";
        }
        WriteHealth(severity, $"{change.Kind}: {change.RelativePath} ({change.Origin})", change);
    }

    private static void WriteHealth(string status, string message, IntegrityEvent? change)
    {
        string path = Path.Combine(BrokerConfig.IntegrityStateDirectory, "health.json");
        string temporary = path + ".tmp." + Guid.NewGuid().ToString("N");
        File.WriteAllText(temporary, JsonSerializer.Serialize(new
        {
            Status = status,
            Message = message,
            TimestampUtc = DateTimeOffset.UtcNow,
            Change = change
        }, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, true);
    }

    public override void Dispose()
    {
        if (_controller is not null) _controller.ChangeDetected -= OnChangeDetected;
        _controller?.Dispose();
        _usn?.Dispose();
        base.Dispose();
    }
}

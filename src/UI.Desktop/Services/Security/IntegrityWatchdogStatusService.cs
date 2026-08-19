using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using PhantomVault.Core.Services.Security;
using PhantomVault.Core.Services.Privileged;
using PhantomVault.UI.Services.Privileged;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace PhantomVault.UI.Services.Security;

/// <summary>Bridges the independent watchdog's health state into the defence engine.</summary>
public sealed class IntegrityWatchdogStatusService : IDisposable
{
    private readonly IDefenceEngine _defenceEngine;
    private readonly string _healthPath;
    private FileSystemWatcher? _watcher;
    private string? _lastCriticalFingerprint;
    private int _reading;
    private readonly NamedPipeBrokerClient _broker;

    public IntegrityWatchdogStatusService(IDefenceEngine defenceEngine, NamedPipeBrokerClient broker)
    {
        _defenceEngine = defenceEngine ?? throw new ArgumentNullException(nameof(defenceEngine));
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
        _healthPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "PhantomObscura", "Broker", "integrity", "health.json");
    }

    public async Task<(bool Allowed, string Reason)> IsUnlockAllowedAsync()
    {
        string challenge = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        try
        {
            string json = await _broker.GetIntegrityVerdictAsync(challenge).ConfigureAwait(false);
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.GetProperty("Challenge").GetString() != challenge)
                return (false, "Integrity watchdog response failed freshness validation.");
            DateTimeOffset timestamp = root.GetProperty("TimestampUtc").GetDateTimeOffset();
            if ((DateTimeOffset.UtcNow - timestamp).Duration() > TimeSpan.FromSeconds(30))
                return (false, "Integrity watchdog response is stale.");
            string status = root.GetProperty("Health").TryGetProperty("Status", out var node)
                ? node.GetString() ?? "unknown" : "unknown";
            bool controllerReady = root.GetProperty("ControllerReady").GetBoolean();
#if DEBUG
            // Local Debug output is intentionally unsigned and the release private key
            // must never be present on a developer machine. The authenticated broker can
            // therefore report "unprovisioned" even though the pipe, nonce and freshness
            // checks above all succeeded. Permit only that exact state in Debug builds.
            // Release compilation excludes this branch and remains strictly fail-closed.
            if (!controllerReady && status == "unprovisioned")
            {
                Serilog.Log.Warning("Integrity controller is unprovisioned for an unsigned Debug build; authenticated broker connectivity verified");
                return (true, "debug-unprovisioned");
            }
#endif
            if (!controllerReady)
                return (false, "Integrity watchdog is not provisioned or ready.");
            return status is "healthy" or "warning"
                ? (true, status)
                : (false, $"Integrity watchdog status is {status}.");
        }
        catch (Exception ex) when (ex is IOException
            or InvalidOperationException
            or JsonException
            or UnauthorizedAccessException
            or TimeoutException
            or PrivilegedBrokerUnavailableException)
        {
            Serilog.Log.Warning(ex, "Integrity watchdog verdict unavailable");
            return (false, "Integrity watchdog verification is unavailable. Unlock remains blocked.");
        }
    }

    public void Start()
    {
        if (_watcher is not null) return;
        string directory = Path.GetDirectoryName(_healthPath)!;
        try
        {
            Directory.CreateDirectory(directory);
            var watcher = new FileSystemWatcher(directory, Path.GetFileName(_healthPath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
            };
            watcher.Changed += OnHealthChanged;
            watcher.Created += OnHealthChanged;
            watcher.Renamed += OnHealthChanged;
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
            ReadAndRaise();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The broker owns this ProgramData directory and may not yet be installed or
            // may replace it during startup. Monitoring is advisory; unlock still fails
            // closed through IsUnlockAllowedAsync, so a missing watcher must not kill the UI.
            _watcher?.Dispose();
            _watcher = null;
            Serilog.Log.Warning(ex, "Integrity status directory is unavailable; live monitoring is deferred");
        }
    }

    private void OnHealthChanged(object sender, FileSystemEventArgs e) => ReadAndRaise();

    private void ReadAndRaise()
    {
        if (Interlocked.Exchange(ref _reading, 1) != 0) return;
        try
        {
            if (!File.Exists(_healthPath)) return;
            byte[] bytes = File.ReadAllBytes(_healthPath);
            if (bytes.Length > 256 * 1024) return;
            using JsonDocument document = JsonDocument.Parse(bytes);
            string status = document.RootElement.TryGetProperty("Status", out var statusNode)
                ? statusNode.GetString() ?? string.Empty : string.Empty;
            if (status is not ("critical" or "tampered")) return;
            string message = document.RootElement.TryGetProperty("Message", out var messageNode)
                ? messageNode.GetString() ?? "Watchdog reported an integrity violation."
                : "Watchdog reported an integrity violation.";
            string fingerprint = status + "\n" + message;
            if (string.Equals(fingerprint, _lastCriticalFingerprint, StringComparison.Ordinal)) return;
            _lastCriticalFingerprint = fingerprint;
            _defenceEngine.RaiseThreat(new ThreatEvent(ThreatType.IntegrityMismatch, ThreatLevel.Critical, message));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Atomic writer replacement can briefly race notification delivery; the
            // next watcher event or startup read retries without weakening service state.
        }
        finally
        {
            Volatile.Write(ref _reading, 0);
        }
    }

    public void Dispose()
    {
        if (_watcher is null) return;
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _watcher = null;
    }
}

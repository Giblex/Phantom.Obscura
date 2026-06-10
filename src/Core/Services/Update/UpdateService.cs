using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Services.Network;
using PhantomVault.Core.Services.Update.Internal;

namespace PhantomVault.Core.Services.Update
{
    /// <summary>
    /// Default <see cref="IUpdateService"/>. See the interface for the contract.
    /// Sealed; one instance per app (singleton). Concurrent calls are serialised
    /// by an async lock so the state machine has a single writer.
    /// </summary>
    public sealed class UpdateService : IUpdateService
    {
        private const int MaxManifestBytes = 64 * 1024;
        private const int Ed25519SignatureSize = 64;

        private readonly IInternetGateway _gateway;
        private readonly IUpdateVerifier _verifier;
        private readonly UpdateChannelSettings _settings;
        private readonly Func<Version> _installedVersionProvider;
        private readonly string _stageRoot;
        private readonly SemaphoreSlim _stateLock = new(initialCount: 1, maxCount: 1);

        public UpdateService(
            IInternetGateway gateway,
            IUpdateVerifier verifier,
            UpdateChannelSettings settings)
            : this(gateway, verifier, settings, DefaultInstalledVersion, DefaultStageRoot())
        {
        }

        // Test-only constructor; visible via InternalsVisibleTo("PhantomVault.Core.Tests").
        internal UpdateService(
            IInternetGateway gateway,
            IUpdateVerifier verifier,
            UpdateChannelSettings settings,
            Func<Version> installedVersionProvider,
            string stageRoot)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _installedVersionProvider = installedVersionProvider ?? throw new ArgumentNullException(nameof(installedVersionProvider));
            _stageRoot = stageRoot ?? throw new ArgumentNullException(nameof(stageRoot));
        }

        public UpdateState State { get; private set; } = UpdateState.Idle;
        public UpdateInfo? LatestInfo { get; private set; }
        public string? StagedDirectory { get; private set; }
        public UpdateVerificationFailure? LastFailureReason { get; private set; }
        public string? LastFailureMessage { get; private set; }

        public event EventHandler<UpdateStateChangedEventArgs>? StateChanged;

        public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
        {
            await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                TransitionTo(UpdateState.Checking);

                var grant = await _gateway.RequestAccessAsync(
                    UpdateGatewayPolicy.CreateRequest(), cancellationToken).ConfigureAwait(false);
                if (grant is null)
                {
                    Fail(UpdateVerificationFailure.PublicKeyNotProvisioned,
                        "Internet access was not granted; update check cancelled.");
                    return null;
                }

                using var client = _gateway.CreateClient(grant);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("PhantomVault/1.0 (update-check)");

                try
                {
                    var manifestUri = UpdateGatewayPolicy.ManifestUrl(_settings.Channel);
                    var signatureUri = UpdateGatewayPolicy.SignatureUrl(_settings.Channel);

                    byte[] manifestBytes = await DownloadCappedAsync(client, manifestUri, MaxManifestBytes, cancellationToken).ConfigureAwait(false);
                    byte[] signature = await DownloadCappedAsync(client, signatureUri, Ed25519SignatureSize * 4, cancellationToken).ConfigureAwait(false);

                    if (signature.Length != Ed25519SignatureSize)
                    {
                        Fail(UpdateVerificationFailure.BadSignature,
                            $"Update signature must be {Ed25519SignatureSize} bytes (got {signature.Length}).");
                        return null;
                    }

                    UpdateInfo info;
                    try
                    {
                        info = _verifier.VerifyManifest(
                            manifestBytes,
                            signature,
                            _settings.Channel,
                            _installedVersionProvider(),
                            UpdateGatewayPolicy.UpdateHost);
                    }
                    catch (UpdateVerificationException ex)
                        when (ex.Reason == UpdateVerificationFailure.VersionNotNewer)
                    {
                        TransitionTo(UpdateState.UpToDate);
                        return null;
                    }
                    catch (UpdateVerificationException ex)
                    {
                        Fail(ex.Reason, ex.Message);
                        return null;
                    }

                    // Stash raw bytes alongside the parsed info so stage-time can
                    // persist exactly what was signed (no re-canonicalization).
                    LatestInfo = info;
                    _pendingManifestBytes = manifestBytes;
                    _pendingSignature = signature;
                    TransitionTo(UpdateState.UpdateAvailable);
                    return info;
                }
                finally
                {
                    _gateway.Revoke(grant, "Update check complete.");
                }
            }
            catch (OperationCanceledException)
            {
                TransitionTo(UpdateState.Idle);
                throw;
            }
            catch (Exception ex)
            {
                Fail(UpdateVerificationFailure.BadManifest, $"Update check failed: {ex.Message}");
                return null;
            }
            finally
            {
                _stateLock.Release();
            }
        }

        public async Task<string?> DownloadAndStageAsync(
            UpdateInfo info,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (info is null) throw new ArgumentNullException(nameof(info));

            await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (LatestInfo is null
                    || _pendingManifestBytes is null
                    || _pendingSignature is null
                    || !ReferenceEquals(info, LatestInfo))
                {
                    Fail(UpdateVerificationFailure.BadManifest,
                        "DownloadAndStageAsync requires the UpdateInfo returned by the most recent CheckAsync.");
                    return null;
                }

                TransitionTo(UpdateState.Downloading);

                var grant = await _gateway.RequestAccessAsync(
                    UpdateGatewayPolicy.CreateRequest(), cancellationToken).ConfigureAwait(false);
                if (grant is null)
                {
                    Fail(UpdateVerificationFailure.PublicKeyNotProvisioned,
                        "Internet access was not granted; update download cancelled.");
                    return null;
                }

                string stageDir = Path.Combine(_stageRoot, info.Version.ToString());
                string assetFileName = SafeAssetFileName(info.AssetUrl);
                string assetPath = Path.Combine(stageDir, assetFileName);

                try
                {
                    using var client = _gateway.CreateClient(grant);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("PhantomVault/1.0 (update-download)");

                    // Fresh, atomic-ish stage dir: wipe any half-staged previous attempt.
                    if (Directory.Exists(stageDir))
                        Directory.Delete(stageDir, recursive: true);
                    Directory.CreateDirectory(stageDir);

                    using (var response = await client.GetAsync(
                        info.AssetUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();

                        long? contentLength = response.Content.Headers.ContentLength;
                        if (contentLength.HasValue && contentLength.Value != info.AssetSize)
                        {
                            Fail(UpdateVerificationFailure.AssetSizeMismatch,
                                $"Server advertised {contentLength} bytes; manifest expected {info.AssetSize}.");
                            return null;
                        }

                        await using var src = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                        await using var dst = new FileStream(
                            assetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                            bufferSize: 81920, useAsync: true);

                        await CopyWithCapAsync(src, dst, info.AssetSize, progress, cancellationToken).ConfigureAwait(false);
                    }

                    TransitionTo(UpdateState.Verifying);

                    try
                    {
                        _verifier.VerifyAsset(assetPath, info);
                    }
                    catch (UpdateVerificationException ex)
                    {
                        Fail(ex.Reason, ex.Message);
                        return null;
                    }

                    // Persist exactly what we received — the installer re-verifies.
                    await File.WriteAllBytesAsync(
                        Path.Combine(stageDir, "manifest.json"), _pendingManifestBytes, cancellationToken).ConfigureAwait(false);
                    await File.WriteAllBytesAsync(
                        Path.Combine(stageDir, "manifest.json.sig"), _pendingSignature, cancellationToken).ConfigureAwait(false);

                    // Convenience handoff for the installer (Phase C) — re-derivable
                    // from the manifest+sig, but saves a parse.
                    var handoff = new StagedUpdateHandoff
                    {
                        Schema = info.Schema,
                        Channel = info.Channel.ToString(),
                        Version = info.Version.ToString(),
                        ReleasedAtUtc = info.ReleasedAtUtc,
                        AssetFileName = assetFileName,
                        AssetSize = info.AssetSize,
                        AssetSha256Base64 = Convert.ToBase64String(info.AssetSha256),
                        AuthenticodeSubject = info.AuthenticodeSubject,
                        AuthenticodeThumbprintSha256Hex = info.AuthenticodeThumbprintSha256 is null
                            ? null
                            : Convert.ToHexString(info.AuthenticodeThumbprintSha256),
                    };
                    await File.WriteAllTextAsync(
                        Path.Combine(stageDir, "info.json"),
                        JsonSerializer.Serialize(handoff, new JsonSerializerOptions { WriteIndented = true }),
                        cancellationToken).ConfigureAwait(false);

                    StagedDirectory = stageDir;
                    TransitionTo(UpdateState.Staged);
                    return stageDir;
                }
                catch (OperationCanceledException)
                {
                    TryCleanup(stageDir);
                    TransitionTo(UpdateState.Idle);
                    throw;
                }
                catch (Exception ex)
                {
                    TryCleanup(stageDir);
                    Fail(UpdateVerificationFailure.BadManifest, $"Update download failed: {ex.Message}");
                    return null;
                }
                finally
                {
                    _gateway.Revoke(grant, "Update download complete.");
                }
            }
            finally
            {
                _stateLock.Release();
            }
        }

        public void Reset()
        {
            LastFailureReason = null;
            LastFailureMessage = null;
            TransitionTo(UpdateState.Idle);
        }

        public void HandOffToInstaller(string installerExePath, string targetDir, string? relaunchExe = null)
        {
            if (string.IsNullOrWhiteSpace(installerExePath)) throw new ArgumentException("installerExePath is required.", nameof(installerExePath));
            if (string.IsNullOrWhiteSpace(targetDir)) throw new ArgumentException("targetDir is required.", nameof(targetDir));
            if (State != UpdateState.Staged) throw new InvalidOperationException($"Cannot hand off in state {State}; expected Staged.");
            if (string.IsNullOrWhiteSpace(StagedDirectory)) throw new InvalidOperationException("StagedDirectory is null; nothing to apply.");
            if (!File.Exists(installerExePath)) throw new FileNotFoundException("Installer executable not found.", installerExePath);

            int pid = Environment.ProcessId;

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = installerExePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(installerExePath) ?? Environment.CurrentDirectory,
            };
            psi.ArgumentList.Add("--apply-update");
            psi.ArgumentList.Add("--staged"); psi.ArgumentList.Add(StagedDirectory!);
            psi.ArgumentList.Add("--target"); psi.ArgumentList.Add(targetDir);
            psi.ArgumentList.Add("--pid");    psi.ArgumentList.Add(pid.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(relaunchExe))
            {
                psi.ArgumentList.Add("--relaunch"); psi.ArgumentList.Add(relaunchExe!);
            }

            _ = System.Diagnostics.Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start the Giblex Installer for apply-update.");
        }

        // ─── internals ──────────────────────────────────────────────────────────

        private byte[]? _pendingManifestBytes;
        private byte[]? _pendingSignature;

        private static Version DefaultInstalledVersion()
            => Assembly.GetEntryAssembly()?.GetName().Version
               ?? typeof(UpdateService).Assembly.GetName().Version
               ?? new Version(0, 0, 0, 0);

        private static string DefaultStageRoot()
            => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PhantomVault", "Update");

        private static string SafeAssetFileName(Uri assetUrl)
        {
            // Strip path; allow only [A-Za-z0-9._-]. Fallback to a synthesized name.
            string raw = Path.GetFileName(assetUrl.AbsolutePath);
            if (string.IsNullOrEmpty(raw)) return "update.bin";
            var clean = new System.Text.StringBuilder(raw.Length);
            foreach (char c in raw)
            {
                clean.Append(char.IsLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_');
            }
            string result = clean.ToString();
            return string.IsNullOrEmpty(result) || result is "." or ".." ? "update.bin" : result;
        }

        private static async Task<byte[]> DownloadCappedAsync(
            HttpClient client, Uri uri, int capBytes, CancellationToken ct)
        {
            using var response = await client.GetAsync(
                uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            long? contentLength = response.Content.Headers.ContentLength;
            if (contentLength.HasValue && contentLength.Value > capBytes)
            {
                throw new InvalidOperationException(
                    $"Resource exceeds cap of {capBytes} bytes (Content-Length={contentLength}).");
            }

            await using var src = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var ms = new MemoryStream(capacity: contentLength.HasValue ? (int)contentLength.Value : 4096);

            byte[] buf = new byte[8192];
            int total = 0;
            int read;
            while ((read = await src.ReadAsync(buf.AsMemory(), ct).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > capBytes)
                    throw new InvalidOperationException($"Resource exceeds cap of {capBytes} bytes.");
                ms.Write(buf, 0, read);
            }
            return ms.ToArray();
        }

        private static async Task CopyWithCapAsync(
            Stream src, Stream dst, long expectedSize, IProgress<double>? progress, CancellationToken ct)
        {
            byte[] buf = new byte[81920];
            long total = 0;
            int read;
            while ((read = await src.ReadAsync(buf.AsMemory(), ct).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > expectedSize)
                {
                    throw new UpdateVerificationException(
                        UpdateVerificationFailure.AssetSizeMismatch,
                        $"Download exceeded manifest-declared size of {expectedSize} bytes.");
                }
                await dst.WriteAsync(buf.AsMemory(0, read), ct).ConfigureAwait(false);
                progress?.Report(expectedSize > 0 ? (double)total / expectedSize : 0);
            }
            if (total != expectedSize)
            {
                throw new UpdateVerificationException(
                    UpdateVerificationFailure.AssetSizeMismatch,
                    $"Download finished at {total} bytes; manifest expected {expectedSize}.");
            }
            await dst.FlushAsync(ct).ConfigureAwait(false);
        }

        private static void TryCleanup(string stageDir)
        {
            try { if (Directory.Exists(stageDir)) Directory.Delete(stageDir, recursive: true); }
            catch { /* best-effort */ }
        }

        private void Fail(UpdateVerificationFailure reason, string message)
        {
            LastFailureReason = reason;
            LastFailureMessage = message;
            TransitionTo(UpdateState.Failed);
        }

        private void TransitionTo(UpdateState newState)
        {
            var old = State;
            if (old == newState) return;
            State = newState;
            StateChanged?.Invoke(this, new UpdateStateChangedEventArgs { OldState = old, NewState = newState });
        }
    }
}

namespace PhantomVault.Core.Services.Update.Internal
{
    /// <summary>
    /// JSON shape written to <c>info.json</c> in the staged update directory.
    /// Read by the Giblex Installer at apply-time as a fast-path summary. The
    /// installer still re-verifies the raw manifest + signature alongside it —
    /// <c>info.json</c> is convenience, not authority.
    /// </summary>
    internal sealed class StagedUpdateHandoff
    {
        public int Schema { get; set; }
        public string Channel { get; set; } = "";
        public string Version { get; set; } = "";
        public DateTimeOffset ReleasedAtUtc { get; set; }
        public string AssetFileName { get; set; } = "";
        public long AssetSize { get; set; }
        public string AssetSha256Base64 { get; set; } = "";
        public string? AuthenticodeSubject { get; set; }
        public string? AuthenticodeThumbprintSha256Hex { get; set; }
    }
}

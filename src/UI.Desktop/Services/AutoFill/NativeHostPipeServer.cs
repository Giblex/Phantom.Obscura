using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services.AutoInject;
using PhantomVault.Core.Services.Autofill;
using Serilog;

namespace PhantomVault.UI.Services.AutoFill
{
    public interface INativeHostPipeServer
    {
        void SetCredentialProvider(ICredentialProvider provider, VaultManifest manifest);
        void ClearCredentialProvider();
        void Start();
        void Stop();
    }

    /// <summary>
    /// Named pipe server that bridges the running desktop vault to
    /// <c>PhantomVault.UI.exe --native-messaging</c> subprocesses spawned by browsers.
    /// Listens on <c>\\.\pipe\PhantomVaultAutofill</c> and answers NDJSON credential
    /// queries from the native messaging subprocess.
    /// </summary>
    public sealed class NativeHostPipeServer : INativeHostPipeServer, IDisposable
    {
        public const string PipeName = "PhantomVaultAutofill";

        private readonly IAutofillVaultContext _vaultContext;
        private ICredentialProvider? _credentialProvider;
        private VaultManifest? _manifest;
        private CancellationTokenSource? _cts;
        private readonly object _credLock = new();

        public NativeHostPipeServer(IAutofillVaultContext vaultContext)
        {
            _vaultContext = vaultContext ?? throw new ArgumentNullException(nameof(vaultContext));
        }

        public void SetCredentialProvider(ICredentialProvider provider, VaultManifest manifest)
        {
            lock (_credLock)
            {
                _credentialProvider = provider;
                _manifest = manifest;
            }
        }

        public void ClearCredentialProvider()
        {
            lock (_credLock)
            {
                _credentialProvider = null;
                _manifest = null;
            }
        }

        public void Start()
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            _ = AcceptLoopAsync(_cts.Token);
            Log.Information("NativeHostPipeServer listening on pipe: {PipeName}", PipeName);
        }

        public void Stop()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                NamedPipeServerStream? server = null;
                try
                {
                    server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(ct);
                    _ = HandleConnectionAsync(server, ct);
                    server = null; // ownership transferred
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "NativeHostPipeServer: error in accept loop");
                    server?.Dispose();
                    try { await Task.Delay(500, ct); } catch (OperationCanceledException) { break; }
                }
            }
        }

        private async Task HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken ct)
        {
            using (pipe)
            {
                try
                {
                    using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                    using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

                    while (pipe.IsConnected && !ct.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync(ct);
                        if (line == null) break;
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        var response = ProcessRequest(line);
                        await writer.WriteLineAsync(response.AsMemory(), ct);
                    }
                }
                catch (OperationCanceledException) { }
                catch (IOException) { }
                catch (Exception ex)
                {
                    Log.Warning(ex, "NativeHostPipeServer: connection handler error");
                }
            }
        }

        private string ProcessRequest(string requestJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(requestJson);
                var root = doc.RootElement;
                var action = root.TryGetProperty("action", out var a) ? a.GetString() ?? string.Empty : string.Empty;

                return action switch
                {
                    "getVaultState" => BuildVaultStateResponse(),
                    "getCredentials" => BuildCredentialsResponse(root),
                    // Credential creation from the browser is intentionally refused:
                    // the native messaging host has no way to obtain explicit user
                    // confirmation, validate the origin against vault policy, or
                    // capture an AAGUID/passkey attestation. New credentials must
                    // be added from inside the Phantom desktop app (fail-closed).
                    "saveCredential" => JsonSerializer.Serialize(new { success = false, error = "Save from browser is not permitted; add credentials inside the Phantom app." }),
                    _ => Fail($"Unknown action: {action}")
                };
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "NativeHostPipeServer: request parse error for: {Json}", requestJson);
                return Fail("Invalid request");
            }
        }

        private string BuildVaultStateResponse()
        {
            var locked = !_vaultContext.IsUnlocked;
            var autofillEnabled = _vaultContext.CurrentManifest?.AutoFillEnabled ?? false;
            return JsonSerializer.Serialize(new { success = true, locked, autofillEnabled });
        }

        private string BuildCredentialsResponse(JsonElement root)
        {
            if (!_vaultContext.IsUnlocked)
                return Fail("Vault is locked");

            ICredentialProvider? provider;
            lock (_credLock) { provider = _credentialProvider; }

            if (provider == null)
                return Fail("Vault not ready");

            var domain = root.TryGetProperty("domain", out var d) ? d.GetString() ?? string.Empty : string.Empty;
            var normalizedSearch = NormalizeDomain(domain);

            var credentials = provider.GetCredentials()
                .Where(c => normalizedSearch.Length == 0 ||
                            NormalizeDomain(ExtractDomain(c.Url)).Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase))
                .Select(c => new { title = c.Title, username = c.Username, url = c.Url })
                .ToList();

            return JsonSerializer.Serialize(new { success = true, credentials });
        }

        private static string Fail(string error) =>
            JsonSerializer.Serialize(new { success = false, error });

        private static string NormalizeDomain(string domain) =>
            domain.ToLowerInvariant().Replace("www.", "").Trim();

        private static string ExtractDomain(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return string.Empty;
            try { return new Uri(url).Host; }
            catch { return url; }
        }

        public void Dispose() => Stop();
    }
}

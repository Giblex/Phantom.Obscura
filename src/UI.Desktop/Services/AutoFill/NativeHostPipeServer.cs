using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using PhantomVault.Core.Models;
using PhantomVault.Core.Services.AutoInject;
using PhantomVault.Core.Services.Autofill;
using Serilog;

namespace PhantomVault.UI.Services.AutoFill
{
    /// <summary>
    /// Credentials the user submitted to a login form, relayed from the native
    /// messaging host process so the desktop app can offer to save them.
    /// </summary>
    public sealed class CredentialSubmittedEventArgs : EventArgs
    {
        public string Url { get; init; } = string.Empty;
        public string Username { get; init; } = string.Empty;
        public string Password { get; init; } = string.Empty;
    }

    public interface INativeHostPipeServer
    {
        void SetCredentialProvider(ICredentialProvider provider, VaultManifest manifest);
        void ClearCredentialProvider();
        void Start();
        void Stop();

        /// <summary>
        /// Raised when the browser reports a submitted login form. The extension has
        /// always emitted this and the host has always re-raised it, but nothing
        /// carried it across the process boundary, so passwords were never offered
        /// for saving.
        /// </summary>
        event EventHandler<CredentialSubmittedEventArgs>? CredentialSubmitted;
    }

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
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                    await server.WaitForConnectionAsync(ct);

                    if (!IsClientProcessAllowed(server, out var rejectReason))
                    {
                        Log.Warning("NativeHostPipeServer: rejected client connection — {Reason}", rejectReason);
                        try { server.Disconnect(); } catch { /* ignore */ }
                        server.Dispose();
                        continue;
                    }

                    _ = HandleConnectionAsync(server, ct);
                    server = null;
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

                    "saveCredential" => JsonSerializer.Serialize(new { success = false, error = "Save from browser is not permitted; add credentials inside the Phantom app." }),

                    // Not a save — it only surfaces a prompt inside the app, where the
                    // user decides. Nothing reaches the vault without that consent, so
                    // this stays distinct from the rejected "saveCredential".
                    "credentialSubmitted" => HandleCredentialSubmitted(root),

                    _ => Fail($"Unknown action: {action}")
                };
            }
            catch (Exception ex)
            {
                // Never log the raw request body — it may contain credential lookup
                // data or partial secrets. Log a content hash + size only.
                Log.Warning(ex, "NativeHostPipeServer: request parse error (sha256={Hash}, bytes={Length})",
                    HashForLog(requestJson), Encoding.UTF8.GetByteCount(requestJson ?? string.Empty));
                return Fail("Invalid request");
            }
        }

        public event EventHandler<CredentialSubmittedEventArgs>? CredentialSubmitted;

        /// <summary>
        /// Relays a submitted login to the app. Deliberately does not touch the vault:
        /// it raises an event so the UI can ask the user first.
        /// </summary>
        private string HandleCredentialSubmitted(JsonElement root)
        {
            // Only meaningful while unlocked — there is nothing to compare against or
            // save into otherwise.
            if (!_vaultContext.IsUnlocked)
                return JsonSerializer.Serialize(new { success = true, handled = false });

            string Get(string name) =>
                root.TryGetProperty(name, out var p) ? p.GetString() ?? string.Empty : string.Empty;

            var url = Get("url");
            var password = Get("password");

            // No password means this was not a login submission worth surfacing.
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(password))
                return JsonSerializer.Serialize(new { success = true, handled = false });

            try
            {
                CredentialSubmitted?.Invoke(this, new CredentialSubmittedEventArgs
                {
                    Url = url,
                    Username = Get("username"),
                    Password = password
                });
            }
            catch (Exception ex)
            {
                // Never include the payload — it contains a live password.
                Log.Warning(ex, "NativeHostPipeServer: credentialSubmitted handler threw");
            }

            return JsonSerializer.Serialize(new { success = true, handled = true });
        }

        private static string HashForLog(string? value)
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
                var hash = System.Security.Cryptography.SHA256.HashData(bytes);
                return Convert.ToHexString(hash, 0, 8); // first 8 bytes is enough to correlate
            }
            catch
            {
                return "unavailable";
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
                .OrderByDescending(c => string.Equals(NormalizeDomain(ExtractDomain(c.Url)), normalizedSearch, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(c => c.LastUsedUtc ?? DateTimeOffset.MinValue)
                .Take(3)
                .Select(c => new { title = c.Title, username = c.Username, password = c.Password, url = c.Url })
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

        // Allowed browser process names (lowercased, no extension). Anything
        // else connecting to the pipe is rejected. Combined with the Authenticode
        // check below this is defence-in-depth on top of PipeOptions.CurrentUserOnly;
        // the OS identity boundary remains the primary security guarantee.
        private static readonly string[] AllowedBrowserProcessNames =
        {
            "chrome",
            "msedge",
            "firefox",
            "brave",
            "opera",
            "vivaldi",
            "arc",
            "chromium",
        };

        [SupportedOSPlatform("windows")]
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetNamedPipeClientProcessId(SafePipeHandle Pipe, out uint ClientProcessId);

        private static bool IsClientProcessAllowed(NamedPipeServerStream pipe, out string reason)
        {
            if (!OperatingSystem.IsWindows())
            {
                reason = "non-windows host";
                return false;
            }

            uint pid;
            try
            {
                if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out pid) || pid == 0)
                {
                    reason = "could not resolve client PID";
                    return false;
                }
            }
            catch (Exception ex)
            {
                reason = $"PID query threw: {ex.Message}";
                return false;
            }

            try
            {
                using var proc = Process.GetProcessById((int)pid);
                var name = (proc.ProcessName ?? string.Empty).ToLowerInvariant();
                if (string.IsNullOrEmpty(name))
                {
                    reason = $"empty process name for PID {pid}";
                    return false;
                }

                if (!AllowedBrowserProcessNames.Contains(name))
                {
                    reason = $"process '{name}' (PID {pid}) is not an allowed browser";
                    return false;
                }

                // Process names are trivially spoofable (copy any exe as chrome.exe), so
                // additionally require a valid Authenticode signature on the client image.
                string? imagePath;
                try
                {
                    imagePath = proc.MainModule?.FileName;
                }
                catch (Exception ex)
                {
                    reason = $"could not resolve image path for PID {pid}: {ex.Message}";
                    return false;
                }

                if (string.IsNullOrEmpty(imagePath))
                {
                    reason = $"empty image path for PID {pid}";
                    return false;
                }

                if (!IsImageSignatureTrusted(imagePath))
                {
#if DEBUG
                    // Permit unsigned dev/Chromium builds in Debug only.
                    Log.Warning("AutofillPipe: '{Path}' has no valid Authenticode signature; allowed in DEBUG build only", imagePath);
#else
                    reason = $"image '{imagePath}' (PID {pid}) failed Authenticode verification";
                    return false;
#endif
                }

                reason = string.Empty;
                return true;
            }
            catch (ArgumentException)
            {
                reason = $"client PID {pid} no longer alive";
                return false;
            }
            catch (Exception ex)
            {
                reason = $"process inspection failed: {ex.Message}";
                return false;
            }
        }

        // Cached Authenticode verdicts keyed by image path + last-write time so a
        // swapped binary at the same path is re-verified.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime Stamp, bool Trusted)> SignatureCache = new(StringComparer.OrdinalIgnoreCase);

        [SupportedOSPlatform("windows")]
        private static bool IsImageSignatureTrusted(string imagePath)
        {
            try
            {
                var stamp = File.GetLastWriteTimeUtc(imagePath);
                if (SignatureCache.TryGetValue(imagePath, out var cached) && cached.Stamp == stamp)
                    return cached.Trusted;

                var trusted = WinVerifyTrustFile(imagePath);
                SignatureCache[imagePath] = (stamp, trusted);
                return trusted;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "AutofillPipe: Authenticode verification threw for {Path}", imagePath);
                return false;
            }
        }

        private static readonly Guid WintrustActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern int WinVerifyTrust(IntPtr hwnd, [In] ref Guid pgActionID, IntPtr pWVTData);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WintrustFileInfo
        {
            public uint cbStruct;
            public IntPtr pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WintrustData
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;              // 2 = WTD_UI_NONE
            public uint fdwRevocationChecks;     // 0 = WTD_REVOKE_NONE
            public uint dwUnionChoice;           // 1 = WTD_CHOICE_FILE
            public IntPtr pFile;
            public uint dwStateAction;           // 0 = WTD_STATEACTION_IGNORE
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;             // 0x10 = WTD_REVOCATION_CHECK_NONE
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [SupportedOSPlatform("windows")]
        private static bool WinVerifyTrustFile(string filePath)
        {
            var pathPtr = Marshal.StringToCoTaskMemUni(filePath);
            var fileInfo = new WintrustFileInfo
            {
                cbStruct = (uint)Marshal.SizeOf<WintrustFileInfo>(),
                pcwszFilePath = pathPtr,
                hFile = IntPtr.Zero,
                pgKnownSubject = IntPtr.Zero,
            };

            var fileInfoPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf<WintrustFileInfo>());
            var dataPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf<WintrustData>());
            try
            {
                Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);

                var data = new WintrustData
                {
                    cbStruct = (uint)Marshal.SizeOf<WintrustData>(),
                    dwUIChoice = 2,          // WTD_UI_NONE
                    fdwRevocationChecks = 0, // WTD_REVOKE_NONE — offline-friendly; chain validity still enforced
                    dwUnionChoice = 1,       // WTD_CHOICE_FILE
                    pFile = fileInfoPtr,
                    dwProvFlags = 0x10,      // WTD_REVOCATION_CHECK_NONE
                };
                Marshal.StructureToPtr(data, dataPtr, false);

                var action = WintrustActionGenericVerifyV2;
                return WinVerifyTrust(IntPtr.Zero, ref action, dataPtr) == 0;
            }
            finally
            {
                Marshal.FreeCoTaskMem(dataPtr);
                Marshal.FreeCoTaskMem(fileInfoPtr);
                Marshal.FreeCoTaskMem(pathPtr);
            }
        }
    }
}


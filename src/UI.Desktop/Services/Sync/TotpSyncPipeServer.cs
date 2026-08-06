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
using Serilog;

namespace PhantomVault.UI.Services.Sync
{
    public interface ITotpSyncPipeServer
    {
        void Start();
        void Stop();
    }

    /// <summary>
    /// Hosts the Obscura-side end of the live TOTP sync pipe. Accepts pushes of
    /// <see cref="SharedTotpEntry"/> lists from PhantomAttestor and merges them into
    /// the unlocked vault via <see cref="ITotpSyncVaultContext.Bridge"/> — reusing the
    /// same merge path the totp-sync.json file watcher already uses. This pipe is a
    /// fast, additive notification channel; the file-based sync remains the durable
    /// fallback when the other app isn't running.
    /// </summary>
    public sealed class TotpSyncPipeServer : ITotpSyncPipeServer, IDisposable
    {
        public const string PipeName = "PhantomObscuraTotpSync";

        private readonly ITotpSyncVaultContext _vaultContext;
        private CancellationTokenSource? _cts;

        public TotpSyncPipeServer(ITotpSyncVaultContext vaultContext)
        {
            _vaultContext = vaultContext ?? throw new ArgumentNullException(nameof(vaultContext));
        }

        public void Start()
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            _ = AcceptLoopAsync(_cts.Token);
            Log.Information("TotpSyncPipeServer listening on pipe: {PipeName}", PipeName);
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
                        Log.Warning("TotpSyncPipeServer: rejected client connection — {Reason}", rejectReason);
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
                    Log.Warning(ex, "TotpSyncPipeServer: error in accept loop");
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

                        var response = await ProcessRequestAsync(line);
                        await writer.WriteLineAsync(response.AsMemory(), ct);
                    }
                }
                catch (OperationCanceledException) { }
                catch (IOException) { }
                catch (Exception ex)
                {
                    Log.Warning(ex, "TotpSyncPipeServer: connection handler error");
                }
            }
        }

        private async Task<string> ProcessRequestAsync(string requestJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(requestJson);
                var root = doc.RootElement;
                var action = root.TryGetProperty("action", out var a) ? a.GetString() ?? string.Empty : string.Empty;

                switch (action)
                {
                    case "ping":
                        return JsonSerializer.Serialize(new
                        {
                            success = true,
                            locked = !_vaultContext.IsUnlocked,
                            syncEnabled = _vaultContext.SyncTotpEnabled
                        });

                    case "pushEntries":
                        return await HandlePushEntriesAsync(root);

                    default:
                        return Fail($"Unknown action: {action}");
                }
            }
            catch (Exception ex)
            {
                // Never log the raw request body — it carries TOTP seed material.
                Log.Warning(ex, "TotpSyncPipeServer: request parse error (sha256={Hash}, bytes={Length})",
                    HashForLog(requestJson), Encoding.UTF8.GetByteCount(requestJson ?? string.Empty));
                return Fail("Invalid request");
            }
        }

        private async Task<string> HandlePushEntriesAsync(JsonElement root)
        {
            if (!_vaultContext.IsUnlocked)
                return Fail("locked");

            if (!_vaultContext.SyncTotpEnabled)
                return Fail("sync disabled");

            var bridge = _vaultContext.Bridge;
            if (bridge == null)
                return Fail("not ready");

            if (!root.TryGetProperty("entries", out var entriesElement))
                return Fail("missing entries");

            var entries = JsonSerializer.Deserialize<System.Collections.Generic.List<SharedTotpEntry>>(entriesElement.GetRawText())
                           ?? new System.Collections.Generic.List<SharedTotpEntry>();

            await bridge.ApplyIncomingEntriesAsync(entries);

            return JsonSerializer.Serialize(new { success = true });
        }

        private static string HashForLog(string? value)
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
                var hash = System.Security.Cryptography.SHA256.HashData(bytes);
                return Convert.ToHexString(hash, 0, 8);
            }
            catch
            {
                return "unavailable";
            }
        }

        private static string Fail(string reason) =>
            JsonSerializer.Serialize(new { success = false, reason });

        public void Dispose() => Stop();

        // Allowed counterpart process (lowercased, no extension). Combined with
        // PipeOptions.CurrentUserOnly + Authenticode verification below; the OS
        // identity boundary remains the primary security guarantee.
        private static readonly string[] AllowedClientProcessNames =
        {
            "phantomattestor.app",
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

                if (!AllowedClientProcessNames.Contains(name))
                {
                    reason = $"process '{name}' (PID {pid}) is not PhantomAttestor";
                    return false;
                }

                // Process names are trivially spoofable, so additionally require a
                // valid Authenticode signature on the client image.
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
                    Log.Warning("TotpSyncPipe: '{Path}' has no valid Authenticode signature; allowed in DEBUG build only", imagePath);
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
                Log.Warning(ex, "TotpSyncPipe: Authenticode verification threw for {Path}", imagePath);
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

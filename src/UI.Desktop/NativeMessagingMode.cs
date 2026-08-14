using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Services.Autofill;
using PhantomVault.UI.Services;
using PhantomVault.UI.Services.AutoFill;
using Serilog;
using Serilog.Events;

namespace PhantomVault.UI
{

    internal static class NativeMessagingMode
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Run(string[] args)
        {

            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PhantomVault", "logs");
            Directory.CreateDirectory(logDir);

            // Default to Warning on disk; raise to Information only when the user
            // has opted into diagnostic logging in Privacy settings.
            bool verboseLogging;
            try { verboseLogging = SettingsService.Load().EnableDebugLogging; }
            catch { verboseLogging = false; }

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Is(verboseLogging ? LogEventLevel.Information : LogEventLevel.Warning)
                .WriteTo.File(
                    Path.Combine(logDir, "nativehost-.log"),
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            try
            {
                RunInternalAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "NativeMessagingMode fatal error");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        private static async Task RunInternalAsync()
        {
            var origins = LoadAllowedOrigins();
            Log.Information("NativeMessagingMode starting — {Count} allowed origins loaded", origins.Count);

            using var pipeClient = new PipeNativeHostClient();
            var connected = await pipeClient.ConnectAsync(timeoutMs: 5000);

            if (!connected)
            {
                Log.Warning(
                    "Could not connect to PhantomVault desktop app pipe '{PipeName}'. " +
                    "Vault queries will return locked state until the app is running.",
                    NativeHostPipeServer.PipeName);
            }
            else
            {
                Log.Information("Connected to desktop app pipe '{PipeName}'", NativeHostPipeServer.PipeName);
            }

            var repo = new PipeBackedCredentialRepository(pipeClient);
            var ctx = new PipeBackedVaultContext(pipeClient);

            // Non-secret sync bridge: mirrors theme / UI prefs / origin allowlist with
            // the extension via the same %APPDATA% file the desktop app already watches.
            var syncDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PhantomVault");
            using var syncBridge = new FileSyncStateBridge(syncDir, origins);

            var host = new NativeMessagingHostService(repo, ctx, origins, syncBridge);

            // Relay submitted logins to the desktop app so it can offer to save them.
            // The extension has always emitted submitForm and this host has always
            // re-raised it as FormSubmitted, but nothing carried it across the process
            // boundary, so the event fired into the void and nothing was ever offered.
            host.FormSubmitted += (_, e) =>
            {
                try
                {
                    var password = e.Fields.FirstOrDefault(f =>
                        string.Equals(f.Type, "password", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrEmpty(f.Value))?.Value;

                    // Without a password this was a search or first-stage form.
                    if (string.IsNullOrEmpty(password)) return;

                    var username = e.Fields.FirstOrDefault(f =>
                        !string.Equals(f.Type, "password", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrEmpty(f.Value)
                        && (string.Equals(f.Type, "email", StringComparison.OrdinalIgnoreCase)
                            || LooksLikeUsername(f)))?.Value
                        ?? e.Fields.FirstOrDefault(f =>
                            !string.Equals(f.Type, "password", StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrEmpty(f.Value))?.Value
                        ?? string.Empty;

                    var payload = JsonSerializer.Serialize(new
                    {
                        action = "credentialSubmitted",
                        url = e.Url,
                        username,
                        password
                    });

                    // Fire and forget: the browser must not be held up waiting for a
                    // prompt the user may never answer.
                    _ = pipeClient.SendAsync(payload);
                }
                catch (Exception ex)
                {
                    // Never log the payload — it holds a live password.
                    Log.Warning(ex, "NativeMessagingMode: failed to relay submitted credential");
                }
            };

            Log.Information("NativeMessagingMode: starting stdin/stdout message loop");
            await host.StartAsync(CancellationToken.None);
            Log.Information("NativeMessagingMode: message loop exited");
        }

        /// <summary>Descriptor heuristics for the account field on a login form.</summary>
        private static bool LooksLikeUsername(PhantomVault.Core.Services.Autofill.FormFieldInfo f)
        {
            var d = $"{f.Id} {f.Name} {f.AutoComplete} {f.Label} {f.Placeholder}".ToLowerInvariant();
            return d.Contains("user") || d.Contains("email") || d.Contains("login")
                   || d.Contains("account") || d.Contains("identifier");
        }

        // DPAPI-sealed origins live next to the legacy plaintext path. On first run after
        // upgrade we read the plaintext file, seal it to the .dpapi sibling, and best-effort
        // delete the plaintext copy. Subsequent loads come straight from the sealed file.
        private const string OriginsJsonName = "autofill-origins.json";
        private const string OriginsSealedName = "autofill-origins.dpapi";
        private static readonly byte[] OriginsEntropy = Encoding.UTF8.GetBytes("PhantomVault.autofill-origins.v1");

        [SupportedOSPlatform("windows")]
        private static List<string> LoadAllowedOrigins()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PhantomVault");
            var sealedPath = Path.Combine(dir, OriginsSealedName);
            var plaintextPath = Path.Combine(dir, OriginsJsonName);

            try
            {
                if (File.Exists(sealedPath))
                    return ParseOrigins(UnsealOrigins(File.ReadAllBytes(sealedPath)));

                if (!File.Exists(plaintextPath)) return new List<string>();

                var json = File.ReadAllText(plaintextPath, Encoding.UTF8);
                var origins = ParseOrigins(json);
                TryMigratePlaintextToSealed(json, plaintextPath, sealedPath);
                return origins;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "NativeMessagingMode: failed to load allowed origins");
                return new List<string>();
            }
        }

        private static List<string> ParseOrigins(string json)
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("origins", out var arr))
                return new List<string>();

            var list = new List<string>();
            foreach (var item in arr.EnumerateArray())
            {
                var origin = item.GetString();
                if (!string.IsNullOrWhiteSpace(origin))
                    list.Add(origin);
            }
            return list;
        }

        [SupportedOSPlatform("windows")]
        private static string UnsealOrigins(byte[] sealed_)
        {
            var plain = ProtectedData.Unprotect(sealed_, OriginsEntropy, DataProtectionScope.CurrentUser);
            try { return Encoding.UTF8.GetString(plain); }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }

        [SupportedOSPlatform("windows")]
        private static void TryMigratePlaintextToSealed(string json, string plaintextPath, string sealedPath)
        {
            try
            {
                var plain = Encoding.UTF8.GetBytes(json);
                try
                {
                    var sealed_ = ProtectedData.Protect(plain, OriginsEntropy, DataProtectionScope.CurrentUser);
                    File.WriteAllBytes(sealedPath, sealed_);
                }
                finally { CryptographicOperations.ZeroMemory(plain); }

                try { File.Delete(plaintextPath); } catch { /* best-effort */ }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "NativeMessagingMode: failed to seal autofill-origins to DPAPI");
            }
        }
    }
}


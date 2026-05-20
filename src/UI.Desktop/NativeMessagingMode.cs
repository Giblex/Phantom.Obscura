using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PhantomVault.Core.Services.Autofill;
using PhantomVault.UI.Services.AutoFill;
using Serilog;
using Serilog.Events;

namespace PhantomVault.UI
{
    /// <summary>
    /// Entry point for <c>PhantomVault.UI.exe --native-messaging</c> mode.
    /// Browsers register this executable as the native messaging host for
    /// <c>com.phantomvault.autofill</c>. When launched:
    /// <list type="bullet">
    ///   <item>Serilog is configured to file only — stdout must stay clean.</item>
    ///   <item>A <see cref="PipeNativeHostClient"/> connects to the running desktop
    ///         app's named pipe to access vault state and credentials.</item>
    ///   <item><see cref="NativeMessagingHostService"/> reads stdin / writes stdout
    ///         until the browser disconnects.</item>
    /// </list>
    /// </summary>
    internal static class NativeMessagingMode
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Run(string[] args)
        {
            // File-only logging — any output on stdout corrupts the native messaging framing.
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PhantomVault", "logs");
            Directory.CreateDirectory(logDir);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Is(LogEventLevel.Information)
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
            var host = new NativeMessagingHostService(repo, ctx, origins);

            Log.Information("NativeMessagingMode: starting stdin/stdout message loop");
            await host.StartAsync(CancellationToken.None);
            Log.Information("NativeMessagingMode: message loop exited");
        }

        private static List<string> LoadAllowedOrigins()
        {
            try
            {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "PhantomVault", "autofill-origins.json");

                if (!File.Exists(path)) return new List<string>();

                using var stream = File.OpenRead(path);
                using var doc = JsonDocument.Parse(stream);

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
            catch (Exception ex)
            {
                Log.Warning(ex, "NativeMessagingMode: failed to load allowed origins");
                return new List<string>();
            }
        }
    }
}

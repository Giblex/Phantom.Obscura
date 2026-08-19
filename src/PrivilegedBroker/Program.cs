using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PhantomVault.Core.Services.Privileged;

namespace PhantomVault.PrivilegedBroker
{
    /// <summary>
    /// Entry point for the Phantom Obscura privileged helper.
    ///
    /// Usage:
    ///   PhantomVault.PrivilegedBroker.exe --install "C:\path\to\PhantomVault.UI.exe"   (elevated)
    ///   PhantomVault.PrivilegedBroker.exe --start                                       (elevated)
    ///   PhantomVault.PrivilegedBroker.exe --uninstall                                   (elevated)
    ///   PhantomVault.PrivilegedBroker.exe --run | --console
    /// </summary>
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            try
            {
                if (args.Length > 0)
                {
                    switch (args[0].ToLowerInvariant())
                    {
                        case "--install":
                            return ServiceInstaller.Install(args.Length > 1 ? args[1] : null);
                        case "--uninstall":
                            return ServiceInstaller.Uninstall();
                        case "--start":
                            return ServiceInstaller.Start();
                        case "--console":
                            await RunConsoleAsync().ConfigureAwait(false);
                            return 0;
                        case "--run":
                            break;
                        default:
                            Console.Error.WriteLine($"Unknown argument '{args[0]}'.");
                            return 2;
                    }
                }

                await RunServiceAsync().ConfigureAwait(false);
                return 0;
            }
            catch (Exception ex)
            {
                TryLog($"Fatal: {ex}");
                return 1;
            }
        }

        private static async Task RunServiceAsync()
        {
            var builder = Host.CreateApplicationBuilder();
            builder.Services.AddWindowsService(options =>
            {
                options.ServiceName = BrokerProtocol.ServiceName;
            });
            builder.Services.AddSingleton<IntegrityWatchdogWorker>();
            builder.Services.AddHostedService(sp => sp.GetRequiredService<IntegrityWatchdogWorker>());
            builder.Services.AddHostedService<BrokerWorker>();
            await builder.Build().RunAsync().ConfigureAwait(false);
        }

        private static async Task RunConsoleAsync()
        {
            Console.WriteLine("Phantom Obscura privileged helper (console mode). Ctrl+C to exit.");
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
            var server = new BrokerPipeServer(Console.WriteLine, new IntegrityWatchdogWorker());
            try
            {
                await server.RunAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        internal static void TryLog(string message)
        {
            try
            {
                string path = Path.Combine(BrokerConfig.LogDirectory, "broker.log");
                File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}");
            }
            catch
            {
            }
        }
    }
}

using Avalonia;
using Avalonia.ReactiveUI;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using PhantomVault.Core;
using PhantomVault.Core.Services;
using PhantomVault.Core.Services.Security;
using Serilog;
using Serilog.Events;

namespace PhantomVault.UI
{
    public static class Program
    {
        private static PolicyService? _policyService;

        /// <summary>
        /// Gets the global PolicyService instance.
        /// </summary>
        public static PolicyService PolicyService
        {
            get
            {
                if (_policyService == null)
                    throw new InvalidOperationException("PolicyService not initialized. Call InitializePolicyService first.");
                return _policyService;
            }
        }

        /// <summary>
        /// Main entry point. Configures and starts the Avalonia app.
        /// </summary>
        [STAThread]
        public static void Main(string[] args)
        {
            // SECURITY PHASE 0: Suppress crash dumps before any sensitive data is loaded
            bool dumpSuppressionActive = CrashDumpSuppression.Initialize();

            // Configure Serilog logging
            var logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhantomVault", "logs");
            Directory.CreateDirectory(logDirectory);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.File(
                    Path.Combine(logDirectory, "phantomvault-.log"),
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss}] [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                    restrictedToMinimumLevel: LogEventLevel.Warning)
                .CreateLogger();

            Trace.Listeners.Add(new TextWriterTraceListener(Console.Out));
            Trace.AutoFlush = true;

            // Log crash dump suppression status
            if (dumpSuppressionActive)
            {
                Log.Information("🔒 Crash dump suppression active - memory protection enabled");
            }
            else
            {
                Log.Warning("⚠️ Crash dump suppression not available on this platform");
            }

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                if (ex != null)
                {
                    Log.Fatal(ex, "Unhandled exception occurred");
                }
                else
                {
                    Log.Fatal("Unhandled exception raised with non-exception payload");
                }
            };

            Log.Information("Starting PhantomVault with args: {Args}", string.Join(" ", args));

            try
            {
                // SECURITY PHASE 1: Verify build integrity
                var buildMetadata = BuildIntegrityVerifier.GetBuildMetadata();
                Log.Information("Build Info: {BuildMetadata}", buildMetadata.ToString());

                // WARNING: For production deployment, change allowDebugBuilds to false
                // This will prevent DEBUG builds from running
                BuildIntegrityVerifier.EnforceProductionBuild(allowDebugBuilds: true);

                // PHASE 2: Initialize and verify security policy
                // SECURITY: Policy verification is ALWAYS enforced regardless of build type
                InitializePolicyService();
                Log.Information("Policy verification succeeded");

                // PHASE 3: Enforce policy rules
                if (_policyService != null)
                {
#if DEBUG
                    // SECURITY: Developer bypass ONLY works in DEBUG builds
                    var devBypass = Environment.GetEnvironmentVariable("PHANTOM_DEV_BYPASS_POLICY");
                    if (devBypass == "1")
                    {
                        Log.Warning("⚠️ DEVELOPMENT MODE: Policy bypass enabled!");
                        Log.Warning("USB policy enforcement: BYPASSED");
                        Log.Warning("Policy synchronization: BYPASSED");
                        // Skip all policy enforcement in dev mode
                    }
                    else
                    {
                        _policyService.EnforceDebuggerPolicy();
                        _policyService.EnforceVirtualMachinePolicy();

                        var usbResult = _policyService.EnforceUsbPolicy();
                        Log.Information("USB policy enforced. Drive={DriveName} Serial={VolumeSerial}",
                            usbResult.drive?.Name ?? "none", usbResult.volumeSerial ?? "n/a");

                        var syncResult = _policyService.SynchronizeAndValidate();
                        if (!syncResult.Success)
                        {
                            throw new PolicyViolationException(
                                PolicyViolationCode.PolicySyncFailed,
                                $"Policy sync failed: {string.Join("; ", syncResult.Errors)}");
                        }

                        Log.Information("All startup policy checks passed");
                    }
#else
                    // RELEASE builds ALWAYS enforce policies
                    _policyService.EnforceDebuggerPolicy();
                    _policyService.EnforceVirtualMachinePolicy();

                    var usbResult = _policyService.EnforceUsbPolicy();
                    Log.Information("USB policy enforced. Drive={DriveName} Serial={VolumeSerial}",
                        usbResult.drive?.Name ?? "none", usbResult.volumeSerial ?? "n/a");

                    var syncResult = _policyService.SynchronizeAndValidate();
                    if (!syncResult.Success)
                    {
                        throw new PolicyViolationException(
                            PolicyViolationCode.PolicySyncFailed,
                            $"Policy sync failed: {string.Join("; ", syncResult.Errors)}");
                    }

                    Log.Information("All startup policy checks passed");
#endif
                }

                // PHASE 4: Start application
                var exitCode = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
                Log.Information("Application exited with code {ExitCode}", exitCode);
                Environment.ExitCode = exitCode;
            }
            catch (System.Security.SecurityException secEx)
            {
                Log.Fatal(secEx, "Security policy violation");
                Environment.Exit(1);
            }
            catch (System.Security.Cryptography.CryptographicException cryptoEx)
            {
                Log.Fatal(cryptoEx, "Policy signature verification failed");
                Environment.Exit(2);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Startup exception");
                throw;
            }
            finally
            {
                _policyService?.Dispose();
                Trace.Flush();
                Log.CloseAndFlush();
            }
        }

        /// <summary>
        /// Initializes the PolicyService by loading embedded policy files and verifying signatures.
        /// </summary>
        private static void InitializePolicyService()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var rootCertResource = "PhantomVault.UI.Assets.Policies.obscura_root.crt";
            var signedPolicyResource = "PhantomVault.UI.Assets.Policies.base_policy.signed.json";

            // Create root verifier from embedded certificate
            using var rootVerifier = RootTrust.CreateRootVerifierFromEmbeddedCert(rootCertResource, assembly);

            string policyJson = LoadEmbeddedTextResource(assembly, signedPolicyResource);

            // Production builds ALWAYS require signed policies
            _policyService = new PolicyService(rootVerifier, policyJson, requireSignature: true);
        }

        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace()
                .UseReactiveUI();
        }

        private static string LoadEmbeddedTextResource(Assembly assembly, string resourceName)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                throw new FileNotFoundException($"Embedded resource not found: {resourceName}");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}

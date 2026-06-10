using Avalonia;
using Avalonia.ReactiveUI;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Giblex.AssetShield;
using PhantomVault.Core;
using PhantomVault.Core.Services;
using PhantomVault.Core.Services.Security;
using PhantomVault.UI.Services;
using Serilog;
using Serilog.Events;

namespace PhantomVault.UI
{
    public static class Program
    {
        private static PolicyService? _policyService;

        public static PolicyService PolicyService
        {
            get
            {
                if (_policyService == null)
                    throw new InvalidOperationException("PolicyService not initialized. Call InitializePolicyService first.");
                return _policyService;
            }
        }

        [STAThread]
        public static void Main(string[] args)
        {

            ShieldedAssemblyLoadContext.Install(Assembly.GetExecutingAssembly());

            if (args != null && Array.IndexOf(args, "--native-messaging") >= 0)
            {
                NativeMessagingMode.Run(args);
                return;
            }

            AppMain(args ?? Array.Empty<string>());
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void AppMain(string[] args)
        {
#if DEBUG
            const LogEventLevel buildFloorLevel = LogEventLevel.Debug;
            const bool allowDebugBuilds = true;
#else
            const LogEventLevel buildFloorLevel = LogEventLevel.Warning;
            const bool allowDebugBuilds = false;
#endif

            // Verbose (Information+) on-disk logging is opt-in via the privacy
            // setting so a default Release install writes only warnings/errors.
            bool verboseLogging;
            try { verboseLogging = SettingsService.Load().EnableDebugLogging; }
            catch { verboseLogging = false; }

            var minimumLogLevel = verboseLogging ? LogEventLevel.Information : buildFloorLevel;

            bool dumpSuppressionActive = CrashDumpSuppression.Initialize();

            var logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhantomVault", "logs");
            Directory.CreateDirectory(logDirectory);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Is(minimumLogLevel)
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

            Log.Information("Starting PhantomVault");

            try
            {

                var buildMetadata = BuildIntegrityVerifier.GetBuildMetadata();
                Log.Information("Build Info: {BuildMetadata}", buildMetadata.ToString());

                BuildIntegrityVerifier.EnforceProductionBuild(allowDebugBuilds);

                InitializePolicyService();
                Log.Information("Policy verification succeeded");

                if (_policyService != null)
                {
#if DEBUG

                    var devBypass = Environment.GetEnvironmentVariable("PHANTOM_DEV_BYPASS_POLICY");
                    if (devBypass == "1")
                    {
                        Log.Warning("⚠️ DEVELOPMENT MODE: Policy bypass enabled!");
                        Log.Warning("Desktop policy enforcement: BYPASSED");
                        Log.Warning("Policy synchronization: BYPASSED");

                    }
                    else
                    {
                        _policyService.EnforceDebuggerPolicy();
                        _policyService.EnforceVirtualMachinePolicy();

                        Log.Information("USB policy enforcement deferred to vault access stage");

                        var syncResult = _policyService.SynchronizeAndValidate();
                        if (!syncResult.Success)
                        {
                            throw new PolicyViolationException(
                                PolicyViolationCode.PolicySyncFailed,
                                $"Policy sync failed: {string.Join("; ", syncResult.Errors)}");
                        }

                        Log.Information("All startup policy checks passed (USB deferred)");
                    }
#else

                    _policyService.EnforceDebuggerPolicy();
                    _policyService.EnforceVirtualMachinePolicy();

                    Log.Information("USB policy enforcement deferred to vault access stage");

                    var syncResult = _policyService.SynchronizeAndValidate();
                    if (!syncResult.Success)
                    {
                        throw new PolicyViolationException(
                            PolicyViolationCode.PolicySyncFailed,
                            $"Policy sync failed: {string.Join("; ", syncResult.Errors)}");
                    }

                    Log.Information("All startup policy checks passed (USB deferred)");
#endif
                }

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

        private static void InitializePolicyService()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var rootCertResource = "PhantomVault.UI.Assets.Policies.obscura_root.crt";
            var signedPolicyResource = "PhantomVault.UI.Assets.Policies.base_policy.signed.json";

            using var rootVerifier = RootTrust.CreateRootVerifierFromEmbeddedCert(rootCertResource, assembly);

            string policyJson = LoadEmbeddedTextResource(assembly, signedPolicyResource);

            _policyService = new PolicyService(rootVerifier, policyJson, requireSignature: true);
        }

        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .With(new Win32PlatformOptions
                {
                    RenderingMode = new[]
                    {
                        Win32RenderingMode.AngleEgl,
                        Win32RenderingMode.Software
                    }
                })
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


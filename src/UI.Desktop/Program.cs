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
        /// Main entry point. Installs the AssetShield encrypted-assembly resolver,
        /// then delegates to AppMain which references types from encrypted assemblies.
        /// </summary>
        [STAThread]
        public static void Main(string[] args)
        {
            // Install AssetShield encrypted assembly resolver BEFORE any
            // encrypted assembly types are referenced. Giblex.AssetShield.dll
            // itself is exempt from encryption and loads normally.
            ShieldedAssemblyLoadContext.Install(Assembly.GetExecutingAssembly());
            // ── Native messaging subprocess mode ───────────────────────────────────
            // Browsers spawn `PhantomVault.UI.exe --native-messaging` as a child
            // process to relay autofill requests over stdin/stdout. In that mode
            // we MUST NOT initialize Avalonia, the policy service, or write
            // anything to stdout — the pipe is reserved for length-prefixed JSON
            // frames. Hand off to a dedicated entry point and exit when it returns.
            if (args != null && Array.IndexOf(args, "--native-messaging") >= 0)
            {
                NativeMessagingMode.Run(args);
                return;
            }
            // ───────────────────────────────────────────────────────────────────────
            AppMain(args ?? Array.Empty<string>());
        }

        /// <summary>
        /// Real application entry point. Separated from Main so the JIT does not
        /// attempt to resolve encrypted assembly types before ShieldedAssemblyLoadContext
        /// is installed as the fallback resolver.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void AppMain(string[] args)
        {
#if DEBUG
            const LogEventLevel minimumLogLevel = LogEventLevel.Debug;
            const bool allowDebugBuilds = true;
#else
            const LogEventLevel minimumLogLevel = LogEventLevel.Information;
            const bool allowDebugBuilds = false;
#endif

            // SECURITY PHASE 0: Suppress crash dumps before any sensitive data is loaded
            bool dumpSuppressionActive = CrashDumpSuppression.Initialize();

            // Configure Serilog logging
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

            Log.Information("Starting PhantomVault");

            try
            {
                // SECURITY PHASE 1: Verify build integrity
                var buildMetadata = BuildIntegrityVerifier.GetBuildMetadata();
                Log.Information("Build Info: {BuildMetadata}", buildMetadata.ToString());

                BuildIntegrityVerifier.EnforceProductionBuild(allowDebugBuilds);

                // PHASE 2: Initialize and verify security policy
                // SECURITY: Policy verification is ALWAYS enforced regardless of build type
                InitializePolicyService();
                Log.Information("Policy verification succeeded");

                // PHASE 3: Enforce startup policy rules
                // NOTE: USB policy is NOT enforced here. The front page must always be
                // reachable (first install, re-download, update, no USB bound).
                // USB policy is enforced later when the user actually interacts with a
                // vault (ProvisionViewModel for vault creation, SecurityCheckScreen for
                // vault opening). This keeps the welcome page accessible while still
                // gating all sensitive operations on the correct USB policy.
                if (_policyService != null)
                {
#if DEBUG
                    // SECURITY: Developer bypass ONLY works in DEBUG builds
                    var devBypass = Environment.GetEnvironmentVariable("PHANTOM_DEV_BYPASS_POLICY");
                    if (devBypass == "1")
                    {
                        Log.Warning("⚠️ DEVELOPMENT MODE: Policy bypass enabled!");
                        Log.Warning("Desktop policy enforcement: BYPASSED");
                        Log.Warning("Policy synchronization: BYPASSED");
                        // Skip all policy enforcement in dev mode
                    }
                    else
                    {
                        _policyService.EnforceDebuggerPolicy();
                        _policyService.EnforceVirtualMachinePolicy();

                        // USB enforcement deferred — runs at vault-open / vault-create time
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
                    // RELEASE builds ALWAYS enforce desktop/VM policies at startup
                    _policyService.EnforceDebuggerPolicy();
                    _policyService.EnforceVirtualMachinePolicy();

                    // USB enforcement deferred — runs at vault-open / vault-create time
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

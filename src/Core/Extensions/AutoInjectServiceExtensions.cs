using Microsoft.Extensions.DependencyInjection;
using PhantomVault.Core.Services;
using PhantomVault.Core.Services.AutoInject;
using PhantomVault.Core.Services.Platform;

namespace PhantomVault.Core.Extensions
{
    /// <summary>
    /// Extension methods for registering auto-inject services in DI container
    /// </summary>
    public static class AutoInjectServiceExtensions
    {
        /// <summary>
        /// Registers all USB auto-inject services
        /// </summary>
        /// <param name="dataDirectory">Directory for policy storage</param>
        public static IServiceCollection AddUsbAutoInject(
            this IServiceCollection services,
            string dataDirectory)
        {
            // USB detection
            services.AddSingleton<IUsbDetector, UsbDetector>();

            // Platform-specific services (must be registered by platform project)
            // services.AddSingleton<IActiveWindowDetector, WindowsActiveWindowDetector>();
            // services.AddSingleton<IAutoTypeService, WindowsAutoTypeService>();

            // Credential matching
            services.AddSingleton<ICredentialMatchingEngine, CredentialMatchingEngine>();

            // Policy engine
            services.AddSingleton<IAutoInjectPolicyEngine>(sp =>
                new AutoInjectPolicyEngine(dataDirectory));

            // Main orchestrator
            services.AddSingleton<IUsbAutoInjectService, UsbAutoInjectService>();

            return services;
        }
    }
}

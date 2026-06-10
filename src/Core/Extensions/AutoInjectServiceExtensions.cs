using Microsoft.Extensions.DependencyInjection;
using PhantomVault.Core.Services;
using PhantomVault.Core.Services.AutoInject;
using PhantomVault.Core.Services.Platform;

namespace PhantomVault.Core.Extensions
{

    public static class AutoInjectServiceExtensions
    {

        public static IServiceCollection AddUsbAutoInject(
            this IServiceCollection services,
            string dataDirectory)
        {

            services.AddSingleton<IUsbDetector, UsbDetector>();

            services.AddSingleton<ICredentialMatchingEngine, CredentialMatchingEngine>();

            services.AddSingleton<IAutoInjectPolicyEngine>(sp =>
                new AutoInjectPolicyEngine(dataDirectory));

            services.AddSingleton<IUsbAutoInjectService, UsbAutoInjectService>();

            return services;
        }
    }
}


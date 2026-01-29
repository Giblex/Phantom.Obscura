using Microsoft.Extensions.Logging;
using PhantomVault.Core.Services;
using PhantomVault.UI.Mobile.ViewModels;
using PhantomVault.UI.Mobile.Views;
using PhantomVault.UI.Mobile.Services;

namespace PhantomVault.UI.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // Auto-configure developer mode for Android emulators
#if DEBUG && ANDROID
        MobileDeveloperMode.AutoConfigureForEmulator();
        
        if (MobileDeveloperMode.IsEnabled)
        {
            MobileDeveloperMode.Log($"Developer mode enabled");
            MobileDeveloperMode.Log($"Device Info: {MobileDeveloperMode.GetDeviceInfo()}");
            
            // Log emulator detection details
            Platforms.Android.EmulatorDetector.LogDeviceInfo();
        }
#endif

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>();
            // Fonts removed - causing crash on missing font files

        // Register platform-specific USB detector
        RegisterPlatformServices(builder.Services);

        // Register Core services
        builder.Services.AddSingleton<EncryptionService>();
        builder.Services.AddSingleton<ManifestService>();
        builder.Services.AddSingleton<VaultService>();
        builder.Services.AddSingleton<UsbBindingService>();
        builder.Services.AddSingleton<TotpService>();
        builder.Services.AddSingleton<BackupService>();

        // Register ViewModels
        builder.Services.AddTransient<VaultUnlockViewModel>();

        // Register Views
        builder.Services.AddTransient<VaultUnlockPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    private static void RegisterPlatformServices(IServiceCollection services)
    {
        // Register platform-specific IUsbDetector implementation
#if ANDROID
        services.AddSingleton<IUsbDetector>(sp =>
        {
            // Use Application.Context instead of Platform.CurrentActivity
            // CurrentActivity is null during startup, but Application.Context is always available
            var context = global::Android.App.Application.Context;
            return new PhantomVault.UI.Mobile.Platforms.Android.AndroidUsbDetector(context);
        });
#elif IOS
        services.AddSingleton<IUsbDetector, PhantomVault.UI.Mobile.Platforms.iOS.iOSUsbDetector>();
#else
        // Desktop fallback (if building for Windows/macOS/Linux)
        services.AddSingleton<IUsbDetector, UsbDetector>();
#endif
    }
}

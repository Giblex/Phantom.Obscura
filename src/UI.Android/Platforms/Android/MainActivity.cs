using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.OS.Storage;
using Microsoft.Extensions.DependencyInjection;
using PhantomVault.Android.Services;
using PhantomVault.Core.Services.Platform.Android;

namespace PhantomVault.Android;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode
        | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    private MediaMountReceiver? _mountReceiver;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Wire the platform USB/removable-storage detector. Phantom Obscura's
        // mobile build is gated by USB OTG presence — without this hookup the
        // Welcome page would never advance to Unlock/Setup.
        var detector = (Microsoft.Maui.Controls.Application.Current as App)?
            .Handler?.MauiContext?.Services?.GetService<AndroidUsbDetector>();
        var locator = (Microsoft.Maui.Controls.Application.Current as App)?
            .Handler?.MauiContext?.Services?.GetService<UsbVaultLocator>();

        if (detector is not null)
        {
            _mountReceiver = new MediaMountReceiver(detector);

            // ACTION_MEDIA_MOUNTED carries a data Uri pointing at the mount path.
            var filter = new IntentFilter();
            filter.AddAction(Intent.ActionMediaMounted);
            filter.AddAction(Intent.ActionMediaUnmounted);
            filter.AddAction(Intent.ActionMediaRemoved);
            filter.AddAction(Intent.ActionMediaBadRemoval);
            filter.AddDataScheme("file");
            RegisterReceiver(_mountReceiver, filter);

            // Bootstrap with already-mounted removable volumes (we missed the
            // broadcasts that fired before our receiver was registered).
            BootstrapMountedVolumes(detector);
        }

#if DEBUG
        // Emulator has no OTG — give the gate a simulated drive so the flow
        // is testable. No-op on hardware (path is already a real Android path).
        locator?.EnableEmulatorSimulatedUsb();
#endif
    }

    protected override void OnDestroy()
    {
        if (_mountReceiver is not null)
        {
            try { UnregisterReceiver(_mountReceiver); } catch { }
            _mountReceiver = null;
        }
        base.OnDestroy();
    }

    private void BootstrapMountedVolumes(AndroidUsbDetector detector)
    {
        try
        {
            var sm = (StorageManager?)GetSystemService(StorageService);
            if (sm is null) return;

            foreach (var volume in sm.StorageVolumes)
            {
                if (volume is null) continue;
                if (!volume.IsRemovable) continue;
                var state = volume.State;
                if (state != global::Android.OS.Environment.MediaMounted) continue;

                // Prefer the public directory API where available.
                string? path = null;
                if (OperatingSystem.IsAndroidVersionAtLeast(30))
                {
                    path = volume.Directory?.AbsolutePath;
                }
                if (string.IsNullOrEmpty(path))
                {
                    // Fall back to reflection on older surfaces — left null is fine,
                    // the broadcast receiver will pick it up on the next mount.
                }
                if (!string.IsNullOrEmpty(path))
                    detector.NotifyDriveInserted(path);
            }
        }
        catch
        {
            // Best-effort enumeration only — the BroadcastReceiver is the source of truth.
        }
    }

    /// <summary>
    /// BroadcastReceiver bridge: translates Android media-mount intents into
    /// <see cref="AndroidUsbDetector"/> notifications so the cross-platform
    /// gate logic can react.
    /// </summary>
    private sealed class MediaMountReceiver : BroadcastReceiver
    {
        private readonly AndroidUsbDetector _detector;
        public MediaMountReceiver(AndroidUsbDetector detector) => _detector = detector;

        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action is null) return;
            var path = intent.Data?.Path;
            if (string.IsNullOrEmpty(path)) return;

            switch (intent.Action)
            {
                case Intent.ActionMediaMounted:
                    _detector.NotifyDriveInserted(path);
                    break;
                case Intent.ActionMediaUnmounted:
                case Intent.ActionMediaRemoved:
                case Intent.ActionMediaBadRemoval:
                    _detector.NotifyDriveRemoved(path);
                    break;
            }
        }
    }
}

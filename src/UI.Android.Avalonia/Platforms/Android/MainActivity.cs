using System;
using System.IO;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.OS.Storage;
using Avalonia;
using Avalonia.Android;
using Avalonia.ReactiveUI;
using PhantomVault.UI.Services;

namespace PhantomVault.UI.Android;

[Activity(
    Label = "Phantom Obscura",
    Theme = "@style/MyTheme.NoActionBar",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleInstance,
    ConfigurationChanges = ConfigChanges.Orientation
        | ConfigChanges.ScreenSize
        | ConfigChanges.UiMode
        | ConfigChanges.ScreenLayout
        | ConfigChanges.SmallestScreenSize
        | ConfigChanges.Density
        | ConfigChanges.Keyboard
        | ConfigChanges.KeyboardHidden)]
public class MainActivity : AvaloniaMainActivity<App>
{
    private MediaMountReceiver? _mountReceiver;

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        => base.CustomizeAppBuilder(builder)
            .UseReactiveUI();

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // ── USB / removable-storage detection ──────────────────────────────
        // Phantom Obscura is USB-bound. The WelcomePage subscribes to
        // UsbDriveMonitor.Instance and updates its visible state when a drive
        // mounts / unmounts. Two feeds:
        //   (1) BroadcastReceiver for ACTION_MEDIA_MOUNTED / *_UNMOUNTED /
        //       *_REMOVED / *_BAD_REMOVAL — covers OTG insert / eject events
        //       at runtime.
        //   (2) Bootstrap enumeration via StorageManager.StorageVolumes — catches
        //       drives that were already mounted before we registered.
        try
        {
            _mountReceiver = new MediaMountReceiver();
            var filter = new IntentFilter();
            filter.AddAction(Intent.ActionMediaMounted);
            filter.AddAction(Intent.ActionMediaUnmounted);
            filter.AddAction(Intent.ActionMediaRemoved);
            filter.AddAction(Intent.ActionMediaBadRemoval);
            filter.AddDataScheme("file");
            RegisterReceiver(_mountReceiver, filter);

            BootstrapMountedVolumes();
        }
        catch
        {
            // Best-effort — the monitor falls back to its empty state and the
            // Welcome page stays in the "waiting for USB drive" pose.
        }

#if DEBUG
        // The Android emulator has no OTG. Provide a simulated mount point so
        // the gate can be driven end-to-end on the AVD. Path lives under
        // /storage/emulated/0/ which the app can read without extra permission
        // grants once first-run STORAGE consent is given.
        try
        {
            const string simRoot = "/storage/emulated/0/PhantomVaultSimUSB";
            Directory.CreateDirectory(Path.Combine(simRoot, ".phantom"));
            UsbDriveMonitor.Instance.NotifyDriveMounted(simRoot, "Simulated USB (DEBUG)");
        }
        catch
        {
            // Either we don't have write perm to /storage/emulated/0 yet, or
            // we're on hardware where the sim isn't appropriate. Either way,
            // skip silently.
        }
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

    private void BootstrapMountedVolumes()
    {
        try
        {
            var sm = (StorageManager?)GetSystemService(StorageService);
            if (sm is null) return;

            var seeded = new System.Collections.Generic.List<(string, string?)>();
            foreach (var volume in sm.StorageVolumes)
            {
                if (volume is null || !volume.IsRemovable) continue;
                if (volume.State != global::Android.OS.Environment.MediaMounted) continue;

                string? path = null;
                if (OperatingSystem.IsAndroidVersionAtLeast(30))
                    path = volume.Directory?.AbsolutePath;
                if (string.IsNullOrEmpty(path)) continue;

                seeded.Add((path, volume.GetDescription(this)));
            }
            if (seeded.Count > 0)
                UsbDriveMonitor.Instance.ReplaceAll(seeded);
        }
        catch
        {
            // Best-effort — the BroadcastReceiver is the live source of truth.
        }
    }

    /// <summary>
    /// Bridges Android <c>ACTION_MEDIA_*</c> broadcasts into the
    /// platform-agnostic <see cref="UsbDriveMonitor"/>.
    /// </summary>
    private sealed class MediaMountReceiver : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action is null) return;
            var path = intent.Data?.Path;
            if (string.IsNullOrEmpty(path)) return;

            switch (intent.Action)
            {
                case Intent.ActionMediaMounted:
                    UsbDriveMonitor.Instance.NotifyDriveMounted(path);
                    break;
                case Intent.ActionMediaUnmounted:
                case Intent.ActionMediaRemoved:
                case Intent.ActionMediaBadRemoval:
                    UsbDriveMonitor.Instance.NotifyDriveRemoved(path);
                    break;
            }
        }
    }
}

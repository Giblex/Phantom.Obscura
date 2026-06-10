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

        }

#if DEBUG

        try
        {
            const string simRoot = "/storage/emulated/0/PhantomVaultSimUSB";
            Directory.CreateDirectory(Path.Combine(simRoot, ".phantom"));
            UsbDriveMonitor.Instance.NotifyDriveMounted(simRoot, "Simulated USB (DEBUG)");
        }
        catch
        {

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

        }
    }

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


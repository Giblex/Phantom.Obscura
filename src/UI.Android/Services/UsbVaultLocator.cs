using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PhantomVault.Core.Services;
using PhantomVault.Core.Services.Platform.Android;

namespace PhantomVault.Android.Services;

/// <summary>
/// Phantom Obscura's USB-gate brain on Android.
/// Watches the platform <see cref="AndroidUsbDetector"/>, discovers Phantom vaults
/// on every mounted removable drive, and exposes a single observable "current
/// mounted vault" notion the UI binds to.
///
/// In DEBUG builds it can also surface a simulated USB path so the flow can be
/// driven on the emulator (which has no OTG).
///
/// Drive layout we recognise:
///   {driveRoot}/.phantom/phantom.pvmobile     ← encrypted vault payload
///   {driveRoot}/.phantom/device_ids.json      ← multi-device binding allowlist
/// </summary>
public sealed class UsbVaultLocator : IDisposable
{
    public const string PhantomDirName       = ".phantom";
    public const string VaultFileName        = "phantom.pvmobile";
    public const string DeviceIdsFileName    = "device_ids.json";

#if DEBUG
    /// <summary>
    /// When the emulator is running we expose this path as a simulated USB drive
    /// so the full gate flow can be exercised without OTG hardware. Created on
    /// demand by <see cref="EnableEmulatorSimulatedUsb"/>.
    /// </summary>
    public const string EmulatorSimulatedDrivePath = "/storage/emulated/0/PhantomVaultSimUSB";
#endif

    private readonly AndroidUsbDetector _detector;
    private readonly List<string> _knownDrives = new();
    private readonly object _gate = new();
    private string? _currentDrive;

    public UsbVaultLocator(AndroidUsbDetector detector)
    {
        _detector = detector;
        _detector.RemovableDriveInserted += OnDriveInserted;
        _detector.RemovableDriveRemoved  += OnDriveRemoved;
    }

    /// <summary>Fires when a removable drive containing Phantom infra is now usable.</summary>
    public event Action<UsbVaultState>? StateChanged;

    /// <summary>Snapshot of current gate state.</summary>
    public UsbVaultState CurrentState
    {
        get
        {
            lock (_gate)
            {
                return BuildState(_currentDrive);
            }
        }
    }

    /// <summary>Convenience for the UI: returns the drive root or null when no drive is mounted.</summary>
    public string? CurrentDriveRoot
    {
        get { lock (_gate) return _currentDrive; }
    }

    public IReadOnlyList<string> KnownDrives
    {
        get { lock (_gate) return _knownDrives.ToArray(); }
    }

    // ── Paths ────────────────────────────────────────────────────────────────

    public static string PhantomDir(string driveRoot)     => Path.Combine(driveRoot, PhantomDirName);
    public static string VaultPath(string driveRoot)      => Path.Combine(PhantomDir(driveRoot), VaultFileName);
    public static string DeviceIdsPath(string driveRoot)  => Path.Combine(PhantomDir(driveRoot), DeviceIdsFileName);

    public static bool HasVault(string driveRoot)         => File.Exists(VaultPath(driveRoot));

    /// <summary>
    /// Forces an immediate rescan of currently-known drives. Useful right after
    /// permission grants or after manually pushing a simulated drive.
    /// </summary>
    public void Rescan()
    {
        lock (_gate)
        {
            var live = _detector.GetRemovableDrives().Where(Directory.Exists).Distinct().ToList();
            _knownDrives.Clear();
            _knownDrives.AddRange(live);
            _currentDrive = PickPrimaryDrive(live);
        }
        RaiseStateChanged();
    }

#if DEBUG
    /// <summary>
    /// Creates a sham external-storage directory and registers it with the
    /// detector. DEBUG-only and meant for the Android emulator where OTG is
    /// unavailable.
    /// </summary>
    public void EnableEmulatorSimulatedUsb()
    {
        try
        {
            Directory.CreateDirectory(EmulatorSimulatedDrivePath);
            Directory.CreateDirectory(PhantomDir(EmulatorSimulatedDrivePath));
            _detector.NotifyDriveInserted(EmulatorSimulatedDrivePath);
        }
        catch
        {
            // Best-effort: emulator may not have rights to write /sdcard until perms granted.
        }
    }
#endif

    // ── Device binding ───────────────────────────────────────────────────────

    /// <summary>
    /// Stable per-device hashed ID. Derived from Android Settings.Secure.ANDROID_ID
    /// when available, falling back to a host-derived fingerprint. Never stored
    /// in plaintext on the drive — only its SHA-256 hex.
    /// </summary>
    public static string ComputeDeviceIdHash()
    {
        string raw;
#if ANDROID
        try
        {
            var resolver = global::Android.App.Application.Context.ContentResolver;
            raw = global::Android.Provider.Settings.Secure.GetString(resolver, global::Android.Provider.Settings.Secure.AndroidId)
                  ?? global::Android.OS.Build.Fingerprint
                  ?? "phantom-unknown-device";
        }
        catch
        {
            raw = global::Android.OS.Build.Fingerprint ?? "phantom-unknown-device";
        }
#else
        raw = Environment.MachineName + "|" + Environment.OSVersion;
#endif
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("phantom-obscura|" + raw));
        return Convert.ToHexString(bytes);
    }

    public DeviceBindingState ReadBindingState(string driveRoot)
    {
        var path = DeviceIdsPath(driveRoot);
        if (!File.Exists(path)) return new DeviceBindingState(Array.Empty<string>());
        try
        {
            var json = File.ReadAllText(path);
            var doc  = JsonSerializer.Deserialize<DeviceIdsFile>(json) ?? new DeviceIdsFile();
            return new DeviceBindingState(doc.DeviceIdHashes ?? Array.Empty<string>());
        }
        catch
        {
            return new DeviceBindingState(Array.Empty<string>());
        }
    }

    /// <summary>Returns true if THIS device's hashed ID is in the drive's allowlist.</summary>
    public bool IsCurrentDeviceBound(string driveRoot)
    {
        var binding = ReadBindingState(driveRoot);
        if (binding.DeviceIdHashes.Count == 0) return false;
        return binding.DeviceIdHashes.Contains(ComputeDeviceIdHash(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Adds THIS device's hashed ID to the allowlist (creates the file if missing).</summary>
    public void BindCurrentDevice(string driveRoot)
    {
        Directory.CreateDirectory(PhantomDir(driveRoot));
        var path = DeviceIdsPath(driveRoot);

        DeviceIdsFile doc;
        try   { doc = File.Exists(path) ? JsonSerializer.Deserialize<DeviceIdsFile>(File.ReadAllText(path)) ?? new() : new(); }
        catch { doc = new(); }

        var id = ComputeDeviceIdHash();
        var set = new HashSet<string>(doc.DeviceIdHashes ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        if (set.Add(id))
        {
            doc.DeviceIdHashes = set.ToArray();
            doc.LastUpdatedUtc = DateTimeOffset.UtcNow;
            File.WriteAllText(path, JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private void OnDriveInserted(string path)
    {
        lock (_gate)
        {
            if (!_knownDrives.Contains(path)) _knownDrives.Add(path);
            _currentDrive ??= PickPrimaryDrive(_knownDrives);
        }
        RaiseStateChanged();
    }

    private void OnDriveRemoved(string path)
    {
        lock (_gate)
        {
            _knownDrives.Remove(path);
            if (string.Equals(_currentDrive, path, StringComparison.OrdinalIgnoreCase))
                _currentDrive = PickPrimaryDrive(_knownDrives);
        }
        RaiseStateChanged();
    }

    /// <summary>
    /// Picks the first drive that already hosts a Phantom vault; if none do,
    /// picks the first drive that at least has the ".phantom" directory; otherwise
    /// the first writable drive (so the Setup wizard has somewhere to write).
    /// </summary>
    private static string? PickPrimaryDrive(IReadOnlyList<string> drives)
    {
        if (drives.Count == 0) return null;
        var withVault = drives.FirstOrDefault(HasVault);
        if (withVault is not null) return withVault;
        var withPhantomDir = drives.FirstOrDefault(d => Directory.Exists(PhantomDir(d)));
        return withPhantomDir ?? drives[0];
    }

    private UsbVaultState BuildState(string? drive)
    {
        if (drive is null) return UsbVaultState.NoDrive;
        if (!Directory.Exists(drive)) return UsbVaultState.NoDrive;

        var hasVault = HasVault(drive);
        var bound    = hasVault && IsCurrentDeviceBound(drive);

        return new UsbVaultState(
            HasDrive:           true,
            DriveRoot:          drive,
            HasVault:           hasVault,
            IsCurrentDeviceBound: bound);
    }

    private void RaiseStateChanged()
    {
        UsbVaultState s;
        lock (_gate) { s = BuildState(_currentDrive); }
        StateChanged?.Invoke(s);
    }

    public void Dispose()
    {
        _detector.RemovableDriveInserted -= OnDriveInserted;
        _detector.RemovableDriveRemoved  -= OnDriveRemoved;
    }

    // ── DTOs ─────────────────────────────────────────────────────────────────

    private sealed class DeviceIdsFile
    {
        public string[]? DeviceIdHashes { get; set; }
        public DateTimeOffset LastUpdatedUtc { get; set; }
    }
}

/// <summary>Immutable snapshot of what the gate sees right now.</summary>
public sealed record UsbVaultState(
    bool   HasDrive,
    string? DriveRoot,
    bool   HasVault,
    bool   IsCurrentDeviceBound)
{
    public static readonly UsbVaultState NoDrive = new(false, null, false, false);
}

public sealed record DeviceBindingState(IReadOnlyList<string> DeviceIdHashes);

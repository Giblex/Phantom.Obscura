using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PhantomVault.UI.Services;

/// <summary>
/// Android-side USB / removable-storage monitor for the Avalonia.Android port.
/// Pure managed surface; the Android-platform layer (MainActivity) feeds it
/// mount / unmount events from <c>StorageManager</c> + <c>ACTION_MEDIA_*</c>
/// broadcasts, plus an emulator simulation hook for DEBUG builds.
///
/// Decoupled from Avalonia so the WelcomePage VM can subscribe to a single
/// process-wide singleton without taking a dependency on Android namespaces.
/// </summary>
public sealed class UsbDriveMonitor
{
    /// <summary>Process-wide singleton — MainActivity initialises this on boot.</summary>
    public static UsbDriveMonitor Instance { get; } = new();

    private readonly object _lock = new();
    private readonly List<UsbDriveInfo> _drives = new();

    /// <summary>Fires on the .NET thread whenever the set of drives changes.</summary>
    public event Action<IReadOnlyList<UsbDriveInfo>>? DrivesChanged;

    /// <summary>Current snapshot — safe to read from any thread.</summary>
    public IReadOnlyList<UsbDriveInfo> CurrentDrives
    {
        get { lock (_lock) return _drives.ToArray(); }
    }

    /// <summary>Returns the first drive that has a Phantom vault on it (or null).</summary>
    public UsbDriveInfo? GetVaultDrive()
    {
        lock (_lock)
            return _drives.FirstOrDefault(d => d.HasVault);
    }

    // ── Mutation surface (called from MainActivity bridge) ───────────────────

    public void NotifyDriveMounted(string mountPath, string? label = null)
    {
        if (string.IsNullOrWhiteSpace(mountPath)) return;
        lock (_lock)
        {
            // De-dupe by path
            if (_drives.Any(d => string.Equals(d.MountPath, mountPath, StringComparison.OrdinalIgnoreCase)))
                return;
            _drives.Add(BuildInfo(mountPath, label));
        }
        Raise();
    }

    public void NotifyDriveRemoved(string mountPath)
    {
        if (string.IsNullOrWhiteSpace(mountPath)) return;
        bool changed;
        lock (_lock)
        {
            changed = _drives.RemoveAll(d =>
                string.Equals(d.MountPath, mountPath, StringComparison.OrdinalIgnoreCase)) > 0;
        }
        if (changed) Raise();
    }

    /// <summary>Replace the entire set (used by MainActivity's bootstrap enumeration).</summary>
    public void ReplaceAll(IEnumerable<(string Path, string? Label)> drives)
    {
        lock (_lock)
        {
            _drives.Clear();
            foreach (var (path, label) in drives)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                if (_drives.Any(d => string.Equals(d.MountPath, path, StringComparison.OrdinalIgnoreCase))) continue;
                _drives.Add(BuildInfo(path, label));
            }
        }
        Raise();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static UsbDriveInfo BuildInfo(string mountPath, string? label)
    {
        var hasVault = Directory.Exists(Path.Combine(mountPath, ".phantom"))
            && File.Exists(Path.Combine(mountPath, ".phantom", "phantom.pvmobile"));
        return new UsbDriveInfo(
            MountPath: mountPath,
            Label:     string.IsNullOrWhiteSpace(label) ? Path.GetFileName(mountPath.TrimEnd('/')) : label!,
            HasVault:  hasVault);
    }

    private void Raise()
    {
        IReadOnlyList<UsbDriveInfo> snapshot;
        lock (_lock) snapshot = _drives.ToArray();
        // Marshal to UI thread when possible — the WelcomePage VM does its own
        // thread-hop via MainThread.BeginInvokeOnMainThread, but raising plain
        // here keeps the monitor framework-agnostic.
        DrivesChanged?.Invoke(snapshot);
    }
}

public sealed record UsbDriveInfo(string MountPath, string Label, bool HasVault);

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PhantomVault.UI.Services;

public sealed class UsbDriveMonitor
{

    public static UsbDriveMonitor Instance { get; } = new();

    private readonly object _lock = new();
    private readonly List<UsbDriveInfo> _drives = new();

    public event Action<IReadOnlyList<UsbDriveInfo>>? DrivesChanged;

    public IReadOnlyList<UsbDriveInfo> CurrentDrives
    {
        get { lock (_lock) return _drives.ToArray(); }
    }

    public UsbDriveInfo? GetVaultDrive()
    {
        lock (_lock)
            return _drives.FirstOrDefault(d => d.HasVault);
    }

    public void NotifyDriveMounted(string mountPath, string? label = null)
    {
        if (string.IsNullOrWhiteSpace(mountPath)) return;
        lock (_lock)
        {

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

        DrivesChanged?.Invoke(snapshot);
    }
}

public sealed record UsbDriveInfo(string MountPath, string Label, bool HasVault);


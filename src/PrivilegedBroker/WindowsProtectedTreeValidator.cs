using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PhantomVault.PrivilegedBroker;

internal static class WindowsProtectedTreeValidator
{
    private const uint GenericRead = 0x80000000;
    private const uint ShareAll = 1 | 2 | 4;
    private const uint OpenExisting = 3;
    private const uint BackupSemantics = 0x02000000;

    public static void Validate(string protectedRoot, params string[] excludedNames)
    {
        string root = Path.GetFullPath(protectedRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var excluded = new HashSet<string>(excludedNames, StringComparer.OrdinalIgnoreCase);
        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (excluded.Contains(relative)) continue;
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Protected file is a reparse point: {relative}");
            using SafeFileHandle handle = CreateFile(path, GenericRead, ShareAll, IntPtr.Zero,
                OpenExisting, BackupSemantics, IntPtr.Zero);
            if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), $"Cannot securely open {relative}.");
            if (!GetFileInformationByHandle(handle, out var info))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Cannot inspect file identity for {relative}.");
            if (info.NumberOfLinks != 1)
                throw new InvalidDataException($"Protected file has {info.NumberOfLinks} hard links: {relative}");
            var resolved = new StringBuilder(32768);
            uint length = GetFinalPathNameByHandle(handle, resolved, (uint)resolved.Capacity, 0);
            if (length == 0 || length >= resolved.Capacity)
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Cannot resolve final path for {relative}.");
            string finalPath = resolved.ToString().StartsWith(@"\\?\", StringComparison.Ordinal)
                ? resolved.ToString()[4..] : resolved.ToString();
            if (!finalPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException($"Protected file resolves outside installation root: {relative}");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation information);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint length, uint flags);
}

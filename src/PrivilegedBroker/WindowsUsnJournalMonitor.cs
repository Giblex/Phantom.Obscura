using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PhantomVault.PrivilegedBroker;

/// <summary>
/// Tracks NTFS journal identity and high-water mark. Any advancement triggers an
/// authoritative controller scan; reset, deletion or wrap is reported as lost
/// continuity and also forces a scan.
/// </summary>
internal sealed class WindowsUsnJournalMonitor : IDisposable
{
    private const uint GenericRead = 0x80000000;
    private const uint FileShareRead = 1;
    private const uint FileShareWrite = 2;
    private const uint FileShareDelete = 4;
    private const uint OpenExisting = 3;
    private const uint FsctlQueryUsnJournal = 0x000900f4;
    private readonly SafeFileHandle _volume;
    private ulong? _journalId;
    private long _nextUsn;

    public WindowsUsnJournalMonitor(string protectedRoot)
    {
        string root = Path.GetPathRoot(Path.GetFullPath(protectedRoot))
            ?? throw new ArgumentException("Protected root has no volume.", nameof(protectedRoot));
        string volumePath = @"\\.\" + root.TrimEnd(Path.DirectorySeparatorChar);
        _volume = CreateFile(volumePath, GenericRead, FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (_volume.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to open NTFS volume for USN monitoring.");
    }

    public UsnJournalObservation Observe()
    {
        int size = Marshal.SizeOf<UsnJournalData>();
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!DeviceIoControl(_volume, FsctlQueryUsnJournal, IntPtr.Zero, 0, buffer, size, out _, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to query NTFS USN journal.");
            var data = Marshal.PtrToStructure<UsnJournalData>(buffer);
            bool first = _journalId is null;
            bool continuityLost = !first && (_journalId != data.UsnJournalId || data.NextUsn < _nextUsn);
            bool advanced = first || continuityLost || data.NextUsn > _nextUsn;
            _journalId = data.UsnJournalId;
            _nextUsn = data.NextUsn;
            return new UsnJournalObservation(advanced, continuityLost, data.UsnJournalId, data.NextUsn);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    public void Dispose() => _volume.Dispose();

    internal sealed record UsnJournalObservation(bool Advanced, bool ContinuityLost, ulong JournalId, long NextUsn);

    [StructLayout(LayoutKind.Sequential)]
    private struct UsnJournalData
    {
        public ulong UsnJournalId;
        public long FirstUsn;
        public long NextUsn;
        public long LowestValidUsn;
        public long MaxUsn;
        public ulong MaximumSize;
        public ulong AllocationDelta;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint controlCode, IntPtr input,
        int inputSize, IntPtr output, int outputSize, out int bytesReturned, IntPtr overlapped);
}

#if WINFSP
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using Microsoft.Win32.SafeHandles;
using PhantomVault.Core.Services;
using Fsp;
using FileInfo = Fsp.Interop.FileInfo;
using VolumeInfo = Fsp.Interop.VolumeInfo;

namespace PhantomVault.UI.Services.Mount
{
    /// <summary>
    /// Read-only WinFsp file system that projects an OBSCUR01 packed volume as a
    /// Windows drive letter, reading entries directly from the container with
    /// random-access seeks (no temp extraction). The drive exists only while the
    /// host is mounted; unmounting makes it disappear. Writes are denied at two
    /// levels: a read/execute-only security descriptor (WinFsp rejects write opens)
    /// and explicit STATUS_MEDIA_WRITE_PROTECTED returns from the mutating handlers.
    /// </summary>
    internal sealed class PhantomReadOnlyFileSystem : FileSystemBase
    {
        private sealed class Node
        {
            public string Name = string.Empty;
            public bool IsDirectory;
            public long Offset;
            public long Length;
            public FileInfo Info;
            public readonly List<Node> Children = new();
        }

        private sealed class DirEnum
        {
            public List<KeyValuePair<string, FileInfo>> Items = new();
            public int Index;
        }

        private readonly string _volumePath;
        private readonly long _payloadStart;
        private readonly Dictionary<string, Node> _byPath = new(StringComparer.OrdinalIgnoreCase);
        private readonly Node _root;
        private readonly byte[] _securityDescriptor;
        private readonly ulong _totalSize;
        private SafeFileHandle? _handle;

        public PhantomReadOnlyFileSystem(string volumePath, ObscuraVolumeManifest manifest, long payloadStart)
        {
            _volumePath = volumePath;
            _payloadStart = payloadStart;

            ulong now = (ulong)DateTime.UtcNow.ToFileTimeUtc();
            _root = new Node { Name = string.Empty, IsDirectory = true, Info = MakeDirInfo(now) };
            _byPath["\\"] = _root;

            long total = 0;
            foreach (var entry in manifest.Entries)
            {
                total += entry.Length;
                AddEntry(entry, now);
            }
            _totalSize = (ulong)total;

            // Owner/group = BUILTIN\Administrators; DACL grants Everyone read + execute
            // only (FRFX). With no write right, WinFsp denies any write-intent open.
            var raw = new RawSecurityDescriptor("O:BAG:BAD:P(A;OICI;FRFX;;;WD)");
            _securityDescriptor = new byte[raw.BinaryLength];
            raw.GetBinaryForm(_securityDescriptor, 0);
        }

        private void AddEntry(ObscuraVolumeEntry entry, ulong now)
        {
            var parts = entry.Path.Replace('/', '\\')
                .Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;

            var current = _root;
            string path = string.Empty;
            for (int i = 0; i < parts.Length; i++)
            {
                bool last = i == parts.Length - 1;
                path += "\\" + parts[i];
                if (!_byPath.TryGetValue(path, out var node))
                {
                    node = new Node { Name = parts[i], IsDirectory = !last };
                    if (last)
                    {
                        node.Offset = entry.Offset;
                        node.Length = entry.Length;
                        node.Info = MakeFileInfo(entry.Length, now);
                    }
                    else
                    {
                        node.Info = MakeDirInfo(now);
                    }
                    current.Children.Add(node);
                    _byPath[path] = node;
                }
                current = node;
            }
        }

        private static FileInfo MakeDirInfo(ulong time) => new FileInfo
        {
            FileAttributes = (uint)System.IO.FileAttributes.Directory,
            FileSize = 0,
            AllocationSize = 0,
            CreationTime = time,
            LastAccessTime = time,
            LastWriteTime = time,
            ChangeTime = time,
        };

        private static FileInfo MakeFileInfo(long length, ulong time) => new FileInfo
        {
            FileAttributes = (uint)System.IO.FileAttributes.ReadOnly,
            FileSize = (ulong)length,
            AllocationSize = (ulong)((length + 4095) / 4096 * 4096),
            CreationTime = time,
            LastAccessTime = time,
            LastWriteTime = time,
            ChangeTime = time,
        };

        public override int Init(object Host)
        {
            _handle = File.OpenHandle(_volumePath, FileMode.Open, FileAccess.Read,
                FileShare.Read, FileOptions.RandomAccess);
            return STATUS_SUCCESS;
        }

        public override void Unmounted(object Host)
        {
            _handle?.Dispose();
            _handle = null;
        }

        public override int GetVolumeInfo(out VolumeInfo VolumeInfo)
        {
            VolumeInfo = default;
            VolumeInfo.TotalSize = _totalSize;
            VolumeInfo.FreeSize = 0;
            return STATUS_SUCCESS;
        }

        public override int GetSecurityByName(string FileName, out uint FileAttributes, ref byte[] SecurityDescriptor)
        {
            if (!_byPath.TryGetValue(Normalize(FileName), out var node))
            {
                FileAttributes = 0;
                return STATUS_OBJECT_NAME_NOT_FOUND;
            }
            FileAttributes = node.Info.FileAttributes;
            if (SecurityDescriptor != null)
                SecurityDescriptor = _securityDescriptor;
            return STATUS_SUCCESS;
        }

        public override int Open(string FileName, uint CreateOptions, uint GrantedAccess,
            out object FileNode, out object FileDesc, out FileInfo FileInfo, out string NormalizedName)
        {
            FileNode = null!;
            FileDesc = null!;
            FileInfo = default;
            NormalizedName = null!;

            if (!_byPath.TryGetValue(Normalize(FileName), out var node))
                return STATUS_OBJECT_NAME_NOT_FOUND;

            FileDesc = node;
            FileInfo = node.Info;
            NormalizedName = Normalize(FileName);
            return STATUS_SUCCESS;
        }

        public override int GetFileInfo(object FileNode, object FileDesc, out FileInfo FileInfo)
        {
            FileInfo = ((Node)FileDesc).Info;
            return STATUS_SUCCESS;
        }

        public override int GetSecurity(object FileNode, object FileDesc, ref byte[] SecurityDescriptor)
        {
            SecurityDescriptor = _securityDescriptor;
            return STATUS_SUCCESS;
        }

        public override int Read(object FileNode, object FileDesc, IntPtr Buffer,
            ulong Offset, uint Length, out uint BytesTransferred)
        {
            BytesTransferred = 0;
            var node = (Node)FileDesc;
            if (node.IsDirectory)
                return STATUS_FILE_IS_A_DIRECTORY;

            if ((long)Offset >= node.Length)
                return STATUS_END_OF_FILE;

            long remaining = node.Length - (long)Offset;
            int toRead = (int)Math.Min((long)Length, remaining);
            long fileOffset = _payloadStart + node.Offset + (long)Offset;

            byte[] scratch = ArrayPool<byte>.Shared.Rent(toRead);
            try
            {
                int got = RandomAccess.Read(_handle!, scratch.AsSpan(0, toRead), fileOffset);
                if (got > 0)
                    Marshal.Copy(scratch, 0, Buffer, got);
                BytesTransferred = (uint)got;
                return STATUS_SUCCESS;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(scratch);
            }
        }

        public override bool ReadDirectoryEntry(object FileNode, object FileDesc, string Pattern,
            string Marker, ref object Context, out string FileName, out FileInfo FileInfo)
        {
            if (Context is not DirEnum state)
            {
                state = BuildDirEnum((Node)FileDesc, Marker);
                Context = state;
            }

            if (state.Index < state.Items.Count)
            {
                var item = state.Items[state.Index++];
                FileName = item.Key;
                FileInfo = item.Value;
                return true;
            }

            FileName = null!;
            FileInfo = default;
            return false;
        }

        private DirEnum BuildDirEnum(Node dir, string? marker)
        {
            var items = new List<KeyValuePair<string, FileInfo>>();
            if (!ReferenceEquals(dir, _root))
            {
                items.Add(new KeyValuePair<string, FileInfo>(".", dir.Info));
                items.Add(new KeyValuePair<string, FileInfo>("..", _root.Info));
            }
            foreach (var child in dir.Children)
                items.Add(new KeyValuePair<string, FileInfo>(child.Name, child.Info));

            if (!string.IsNullOrEmpty(marker))
            {
                int idx = items.FindIndex(kv => string.Equals(kv.Key, marker, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                    items = items.GetRange(idx + 1, items.Count - (idx + 1));
            }
            return new DirEnum { Items = items, Index = 0 };
        }

        // --- Write-protection: deny every mutating operation explicitly. ---

        public override int Create(string FileName, uint CreateOptions, uint GrantedAccess, uint FileAttributes,
            byte[] SecurityDescriptor, ulong AllocationSize, out object FileNode, out object FileDesc,
            out FileInfo FileInfo, out string NormalizedName)
        {
            FileNode = null!;
            FileDesc = null!;
            FileInfo = default;
            NormalizedName = null!;
            return STATUS_MEDIA_WRITE_PROTECTED;
        }

        public override int Overwrite(object FileNode, object FileDesc, uint FileAttributes,
            bool ReplaceFileAttributes, ulong AllocationSize, out FileInfo FileInfo)
        {
            FileInfo = default;
            return STATUS_MEDIA_WRITE_PROTECTED;
        }

        public override int Write(object FileNode, object FileDesc, IntPtr Buffer, ulong Offset, uint Length,
            bool WriteToEndOfFile, bool ConstrainedIo, out uint BytesTransferred, out FileInfo FileInfo)
        {
            BytesTransferred = 0;
            FileInfo = default;
            return STATUS_MEDIA_WRITE_PROTECTED;
        }

        public override int SetBasicInfo(object FileNode, object FileDesc, uint FileAttributes,
            ulong CreationTime, ulong LastAccessTime, ulong LastWriteTime, ulong ChangeTime, out FileInfo FileInfo)
        {
            FileInfo = default;
            return STATUS_MEDIA_WRITE_PROTECTED;
        }

        public override int SetFileSize(object FileNode, object FileDesc, ulong NewSize,
            bool SetAllocationSize, out FileInfo FileInfo)
        {
            FileInfo = default;
            return STATUS_MEDIA_WRITE_PROTECTED;
        }

        public override int CanDelete(object FileNode, object FileDesc, string FileName)
            => STATUS_MEDIA_WRITE_PROTECTED;

        public override int Rename(object FileNode, object FileDesc, string FileName, string NewFileName, bool ReplaceIfExists)
            => STATUS_MEDIA_WRITE_PROTECTED;

        private static string Normalize(string fileName)
        {
            if (string.IsNullOrEmpty(fileName) || fileName == "\\")
                return "\\";
            return fileName.TrimEnd('\\');
        }
    }
}
#endif

#if WINFSP
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using PhantomVault.Core.Services;
using Fsp;
using FileInfo = Fsp.Interop.FileInfo;
using VolumeInfo = Fsp.Interop.VolumeInfo;

namespace PhantomVault.UI.Services.Mount
{
    /// <summary>
    /// Writable WinFsp file system over an OBSCUR01 container using a copy-on-write
    /// in-memory overlay. Reads come straight from the encrypted container; the first
    /// write to any file copies its bytes into an in-memory buffer (so decrypted
    /// plaintext is never staged to disk). On unmount the overlay is repacked back into
    /// the container atomically via <see cref="ObscuraVolumeService.CreateVolumeFromSourcesAsync"/>,
    /// and a commit callback is fired so the host can bump the rollback save-sequence
    /// and re-baseline the volume trust anchor.
    /// </summary>
    internal sealed class PhantomWritableFileSystem : FileSystemBase
    {
        private const long DefaultOverlayCap = 1024L * 1024 * 1024; // 1 GiB in-memory ceiling

        private sealed class Node
        {
            public string Name = string.Empty;
            public bool IsDirectory;
            public Node? Parent;
            public long ContainerOffset;
            public long ContainerLength;
            public byte[]? Buffer;       // non-null => memory-backed (dirty/new)
            public long BufferLength;    // logical length within Buffer
            public FileInfo Info;
            public readonly Dictionary<string, Node> Children = new(StringComparer.OrdinalIgnoreCase);
            public bool IsMemoryBacked => Buffer != null || IsNew;
            public bool IsNew;
        }

        private sealed class DirEnum
        {
            public List<KeyValuePair<string, FileInfo>> Items = new();
            public int Index;
        }

        private readonly string _volumePath;
        private readonly long _payloadStart;
        private readonly Node _root;
        private readonly object _lock = new();
        private readonly Action? _onCommitted;
        private SafeFileHandle? _handle;
        private long _overlayBytes;
        private bool _dirty;
        private bool _committed;

        public PhantomWritableFileSystem(string volumePath, ObscuraVolumeManifest manifest,
            long payloadStart, Action? onCommitted)
        {
            _volumePath = volumePath;
            _payloadStart = payloadStart;
            _onCommitted = onCommitted;

            ulong now = (ulong)DateTime.UtcNow.ToFileTimeUtc();
            _root = new Node { Name = string.Empty, IsDirectory = true, Info = MakeDirInfo(now) };
            foreach (var entry in manifest.Entries)
                AddContainerEntry(entry, now);
        }

        private void AddContainerEntry(ObscuraVolumeEntry entry, ulong now)
        {
            var parts = entry.Path.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;

            var current = _root;
            for (int i = 0; i < parts.Length; i++)
            {
                bool last = i == parts.Length - 1;
                if (!current.Children.TryGetValue(parts[i], out var node))
                {
                    node = new Node { Name = parts[i], Parent = current, IsDirectory = !last };
                    if (last)
                    {
                        node.ContainerOffset = entry.Offset;
                        node.ContainerLength = entry.Length;
                        node.Info = MakeFileInfo(entry.Length, now);
                    }
                    else
                    {
                        node.Info = MakeDirInfo(now);
                    }
                    current.Children[parts[i]] = node;
                }
                current = node;
            }
        }

        private static FileInfo MakeDirInfo(ulong time) => new FileInfo
        {
            FileAttributes = (uint)System.IO.FileAttributes.Directory,
            CreationTime = time,
            LastAccessTime = time,
            LastWriteTime = time,
            ChangeTime = time,
        };

        private static FileInfo MakeFileInfo(long length, ulong time) => new FileInfo
        {
            FileAttributes = (uint)System.IO.FileAttributes.Archive,
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

        public override int GetVolumeInfo(out VolumeInfo VolumeInfo)
        {
            VolumeInfo = default;
            VolumeInfo.TotalSize = (ulong)DefaultOverlayCap;
            VolumeInfo.FreeSize = (ulong)Math.Max(0, DefaultOverlayCap - Interlocked.Read(ref _overlayBytes));
            return STATUS_SUCCESS;
        }

        public override int GetSecurityByName(string FileName, out uint FileAttributes, ref byte[] SecurityDescriptor)
        {
            lock (_lock)
            {
                var node = Resolve(FileName);
                if (node == null) { FileAttributes = 0; return STATUS_OBJECT_NAME_NOT_FOUND; }
                FileAttributes = node.Info.FileAttributes;
                return STATUS_SUCCESS;
            }
        }

        public override int Open(string FileName, uint CreateOptions, uint GrantedAccess,
            out object FileNode, out object FileDesc, out FileInfo FileInfo, out string NormalizedName)
        {
            FileNode = null!; FileDesc = null!; FileInfo = default; NormalizedName = null!;
            lock (_lock)
            {
                var node = Resolve(FileName);
                if (node == null) return STATUS_OBJECT_NAME_NOT_FOUND;
                FileDesc = node;
                FileInfo = node.Info;
                NormalizedName = FullPath(node);
                return STATUS_SUCCESS;
            }
        }

        public override int Create(string FileName, uint CreateOptions, uint GrantedAccess, uint FileAttributes,
            byte[] SecurityDescriptor, ulong AllocationSize, out object FileNode, out object FileDesc,
            out FileInfo FileInfo, out string NormalizedName)
        {
            FileNode = null!; FileDesc = null!; FileInfo = default; NormalizedName = null!;
            lock (_lock)
            {
                var (parent, leaf) = Split(FileName);
                if (parent == null) return STATUS_OBJECT_PATH_NOT_FOUND;
                if (string.IsNullOrEmpty(leaf)) return STATUS_OBJECT_NAME_INVALID;
                if (parent.Children.ContainsKey(leaf)) return STATUS_OBJECT_NAME_COLLISION;

                bool isDir = (CreateOptions & FILE_DIRECTORY_FILE) != 0;
                ulong now = (ulong)DateTime.UtcNow.ToFileTimeUtc();
                var node = new Node
                {
                    Name = leaf,
                    Parent = parent,
                    IsDirectory = isDir,
                    IsNew = true,
                    Info = isDir ? MakeDirInfo(now) : MakeFileInfo(0, now),
                };
                if (!isDir)
                {
                    node.Buffer = Array.Empty<byte>();
                    node.BufferLength = 0;
                    if (FileAttributes != 0) node.Info.FileAttributes = FileAttributes;
                }
                parent.Children[leaf] = node;
                _dirty = true;

                FileDesc = node;
                FileInfo = node.Info;
                NormalizedName = FullPath(node);
                return STATUS_SUCCESS;
            }
        }

        public override int Overwrite(object FileNode, object FileDesc, uint FileAttributes,
            bool ReplaceFileAttributes, ulong AllocationSize, out FileInfo FileInfo)
        {
            FileInfo = default;
            lock (_lock)
            {
                var node = (Node)FileDesc;
                if (node.IsDirectory) return STATUS_FILE_IS_A_DIRECTORY;
                Materialize(node);
                ResizeBuffer(node, 0);
                if (ReplaceFileAttributes)
                    node.Info.FileAttributes = FileAttributes != 0 ? FileAttributes : (uint)System.IO.FileAttributes.Archive;
                else
                    node.Info.FileAttributes |= FileAttributes;
                Touch(node);
                _dirty = true;
                FileInfo = node.Info;
                return STATUS_SUCCESS;
            }
        }

        public override int Read(object FileNode, object FileDesc, IntPtr Buffer,
            ulong Offset, uint Length, out uint BytesTransferred)
        {
            BytesTransferred = 0;
            lock (_lock)
            {
                var node = (Node)FileDesc;
                if (node.IsDirectory) return STATUS_FILE_IS_A_DIRECTORY;
                long size = CurrentLength(node);
                if ((long)Offset >= size) return STATUS_END_OF_FILE;

                int toRead = (int)Math.Min((long)Length, size - (long)Offset);
                if (node.IsMemoryBacked)
                {
                    Marshal.Copy(node.Buffer!, (int)Offset, Buffer, toRead);
                    BytesTransferred = (uint)toRead;
                    return STATUS_SUCCESS;
                }

                long fileOffset = _payloadStart + node.ContainerOffset + (long)Offset;
                byte[] scratch = ArrayPool<byte>.Shared.Rent(toRead);
                try
                {
                    int got = RandomAccess.Read(_handle!, scratch.AsSpan(0, toRead), fileOffset);
                    if (got > 0) Marshal.Copy(scratch, 0, Buffer, got);
                    BytesTransferred = (uint)got;
                    return STATUS_SUCCESS;
                }
                finally { ArrayPool<byte>.Shared.Return(scratch); }
            }
        }

        public override int Write(object FileNode, object FileDesc, IntPtr Buffer, ulong Offset, uint Length,
            bool WriteToEndOfFile, bool ConstrainedIo, out uint BytesTransferred, out FileInfo FileInfo)
        {
            BytesTransferred = 0; FileInfo = default;
            lock (_lock)
            {
                var node = (Node)FileDesc;
                if (node.IsDirectory) return STATUS_FILE_IS_A_DIRECTORY;
                Materialize(node);

                long writeOffset = WriteToEndOfFile ? node.BufferLength : (long)Offset;
                if (ConstrainedIo)
                {
                    if (writeOffset >= node.BufferLength) { FileInfo = node.Info; return STATUS_SUCCESS; }
                    if (writeOffset + Length > node.BufferLength)
                        Length = (uint)(node.BufferLength - writeOffset);
                }

                long required = writeOffset + Length;
                if (required > node.BufferLength)
                {
                    if (Interlocked.Read(ref _overlayBytes) + (required - node.BufferLength) > DefaultOverlayCap)
                        return STATUS_DISK_FULL;
                    ResizeBuffer(node, required);
                }

                if (Length > 0)
                {
                    Marshal.Copy(Buffer, node.Buffer!, (int)writeOffset, (int)Length);
                    BytesTransferred = Length;
                }
                Touch(node);
                _dirty = true;
                FileInfo = node.Info;
                return STATUS_SUCCESS;
            }
        }

        public override int Flush(object FileNode, object FileDesc, out FileInfo FileInfo)
        {
            // Durability is at unmount (commit-on-eject), matching removable-volume semantics.
            FileInfo = FileDesc is Node n ? n.Info : default;
            return STATUS_SUCCESS;
        }

        public override int GetFileInfo(object FileNode, object FileDesc, out FileInfo FileInfo)
        {
            FileInfo = ((Node)FileDesc).Info;
            return STATUS_SUCCESS;
        }

        public override int SetBasicInfo(object FileNode, object FileDesc, uint FileAttributes,
            ulong CreationTime, ulong LastAccessTime, ulong LastWriteTime, ulong ChangeTime, out FileInfo FileInfo)
        {
            lock (_lock)
            {
                var node = (Node)FileDesc;
                if (FileAttributes != unchecked((uint)(-1)) && FileAttributes != 0) node.Info.FileAttributes = FileAttributes;
                if (CreationTime != 0) node.Info.CreationTime = CreationTime;
                if (LastAccessTime != 0) node.Info.LastAccessTime = LastAccessTime;
                if (LastWriteTime != 0) node.Info.LastWriteTime = LastWriteTime;
                if (ChangeTime != 0) node.Info.ChangeTime = ChangeTime;
                FileInfo = node.Info;
                return STATUS_SUCCESS;
            }
        }

        public override int SetFileSize(object FileNode, object FileDesc, ulong NewSize,
            bool SetAllocationSize, out FileInfo FileInfo)
        {
            lock (_lock)
            {
                var node = (Node)FileDesc;
                Materialize(node);
                if (!SetAllocationSize)
                {
                    if ((long)NewSize > node.BufferLength &&
                        Interlocked.Read(ref _overlayBytes) + ((long)NewSize - node.BufferLength) > DefaultOverlayCap)
                    {
                        FileInfo = node.Info; return STATUS_DISK_FULL;
                    }
                    ResizeBuffer(node, (long)NewSize);
                    Touch(node);
                    _dirty = true;
                }
                FileInfo = node.Info;
                return STATUS_SUCCESS;
            }
        }

        public override int CanDelete(object FileNode, object FileDesc, string FileName)
        {
            lock (_lock)
            {
                var node = (Node)FileDesc;
                if (node.IsDirectory && node.Children.Count > 0) return STATUS_DIRECTORY_NOT_EMPTY;
                return STATUS_SUCCESS;
            }
        }

        public override int Rename(object FileNode, object FileDesc, string FileName, string NewFileName, bool ReplaceIfExists)
        {
            lock (_lock)
            {
                var node = Resolve(FileName);
                if (node == null) return STATUS_OBJECT_NAME_NOT_FOUND;
                var (newParent, newLeaf) = Split(NewFileName);
                if (newParent == null) return STATUS_OBJECT_PATH_NOT_FOUND;
                if (string.IsNullOrEmpty(newLeaf)) return STATUS_OBJECT_NAME_INVALID;

                if (newParent.Children.TryGetValue(newLeaf, out var existing) && !ReferenceEquals(existing, node))
                {
                    if (!ReplaceIfExists) return STATUS_OBJECT_NAME_COLLISION;
                    RemoveNode(existing);
                }

                node.Parent?.Children.Remove(node.Name);
                node.Name = newLeaf;
                node.Parent = newParent;
                newParent.Children[newLeaf] = node;
                _dirty = true;
                return STATUS_SUCCESS;
            }
        }

        public override void Cleanup(object FileNode, object FileDesc, string FileName, uint Flags)
        {
            lock (_lock)
            {
                if ((Flags & CleanupDelete) != 0 && FileDesc is Node node)
                {
                    if (node.IsDirectory && node.Children.Count > 0) return;
                    RemoveNode(node);
                    _dirty = true;
                }
            }
        }

        public override bool ReadDirectoryEntry(object FileNode, object FileDesc, string Pattern,
            string Marker, ref object Context, out string FileName, out FileInfo FileInfo)
        {
            if (Context is not DirEnum state)
            {
                lock (_lock) { state = BuildDirEnum((Node)FileDesc, Marker); }
                Context = state;
            }
            if (state.Index < state.Items.Count)
            {
                var item = state.Items[state.Index++];
                FileName = item.Key; FileInfo = item.Value;
                return true;
            }
            FileName = null!; FileInfo = default;
            return false;
        }

        public override void Unmounted(object Host)
        {
            Commit();
            _handle?.Dispose();
            _handle = null;
        }

        /// <summary>Repack the overlay back into the container. Safe to call once.</summary>
        public void Commit()
        {
            lock (_lock)
            {
                if (_committed) return;
                _committed = true;
                if (!_dirty) return;

                // Pull every surviving container-backed file into memory so nothing
                // references system.bin during File.Replace.
                var sources = new List<ObscuraVolumeSource>();
                CollectSources(_root, string.Empty, sources);

                _handle?.Dispose();
                _handle = null;

                new ObscuraVolumeService()
                    .CreateVolumeFromSourcesAsync(_volumePath, sources)
                    .GetAwaiter().GetResult();
            }

            _onCommitted?.Invoke();
        }

        private void CollectSources(Node dir, string prefix, List<ObscuraVolumeSource> sources)
        {
            foreach (var child in dir.Children.Values)
            {
                string path = prefix.Length == 0 ? child.Name : prefix + "/" + child.Name;
                if (child.IsDirectory)
                {
                    CollectSources(child, path, sources);
                }
                else if (child.IsMemoryBacked)
                {
                    byte[] data = new byte[child.BufferLength];
                    if (child.BufferLength > 0)
                        Array.Copy(child.Buffer!, data, child.BufferLength);
                    sources.Add(new ObscuraVolumeSource(path, () => new MemoryStream(data, writable: false)));
                }
                else
                {
                    long len = child.ContainerLength;
                    long off = _payloadStart + child.ContainerOffset;
                    byte[] data = new byte[len];
                    if (len > 0)
                        RandomAccess.Read(_handle!, data, off);
                    sources.Add(new ObscuraVolumeSource(path, () => new MemoryStream(data, writable: false)));
                }
            }
        }

        // --- helpers (all called under _lock) ---

        private Node? Resolve(string path)
        {
            if (string.IsNullOrEmpty(path) || path == "\\") return _root;
            var parts = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            var current = _root;
            foreach (var part in parts)
            {
                if (!current.Children.TryGetValue(part, out var next)) return null;
                current = next;
            }
            return current;
        }

        private (Node? parent, string leaf) Split(string path)
        {
            var parts = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return (null, string.Empty);
            var current = _root;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (!current.Children.TryGetValue(parts[i], out var next) || !next.IsDirectory) return (null, string.Empty);
                current = next;
            }
            return (current, parts[^1]);
        }

        private static string FullPath(Node node)
        {
            if (node.Parent == null) return "\\";
            var stack = new Stack<string>();
            var cur = node;
            while (cur?.Parent != null) { stack.Push(cur.Name); cur = cur.Parent; }
            return "\\" + string.Join('\\', stack);
        }

        private long CurrentLength(Node node) => node.IsMemoryBacked ? node.BufferLength : node.ContainerLength;

        private void Materialize(Node node)
        {
            if (node.IsMemoryBacked)
            {
                node.Buffer ??= Array.Empty<byte>();
                return;
            }
            long len = node.ContainerLength;
            var buffer = new byte[len];
            if (len > 0)
                RandomAccess.Read(_handle!, buffer, _payloadStart + node.ContainerOffset);
            node.Buffer = buffer;
            node.BufferLength = len;
            node.IsNew = true; // now overlay-backed
            Interlocked.Add(ref _overlayBytes, len);
        }

        private void ResizeBuffer(Node node, long newLength)
        {
            long old = node.BufferLength;
            if (newLength == old) return;
            var buffer = node.Buffer ?? Array.Empty<byte>();
            if (newLength > buffer.Length)
            {
                long cap = Math.Max(newLength, buffer.Length == 0 ? 4096 : buffer.Length * 2L);
                Array.Resize(ref buffer, (int)Math.Min(cap, int.MaxValue));
                node.Buffer = buffer;
            }
            else if (newLength < old)
            {
                Array.Clear(buffer, (int)newLength, (int)(old - newLength));
            }
            node.BufferLength = newLength;
            node.Info.FileSize = (ulong)newLength;
            node.Info.AllocationSize = (ulong)((newLength + 4095) / 4096 * 4096);
            Interlocked.Add(ref _overlayBytes, newLength - old);
        }

        private static void Touch(Node node)
        {
            ulong now = (ulong)DateTime.UtcNow.ToFileTimeUtc();
            node.Info.LastWriteTime = now;
            node.Info.ChangeTime = now;
            node.Info.LastAccessTime = now;
        }

        private void RemoveNode(Node node)
        {
            node.Parent?.Children.Remove(node.Name);
            if (node.IsMemoryBacked)
                Interlocked.Add(ref _overlayBytes, -node.BufferLength);
        }

        private DirEnum BuildDirEnum(Node dir, string? marker)
        {
            var items = new List<KeyValuePair<string, FileInfo>>();
            if (!ReferenceEquals(dir, _root))
            {
                items.Add(new KeyValuePair<string, FileInfo>(".", dir.Info));
                items.Add(new KeyValuePair<string, FileInfo>("..", dir.Parent?.Info ?? _root.Info));
            }
            foreach (var child in dir.Children.Values.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
                items.Add(new KeyValuePair<string, FileInfo>(child.Name, child.Info));

            if (!string.IsNullOrEmpty(marker))
            {
                int idx = items.FindIndex(kv => string.Equals(kv.Key, marker, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0) items = items.GetRange(idx + 1, items.Count - (idx + 1));
            }
            return new DirEnum { Items = items, Index = 0 };
        }
    }
}
#endif

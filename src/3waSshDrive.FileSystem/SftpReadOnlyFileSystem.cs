using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Fsp;
using ThreeWa.SshDrive.Core.Paths;
using ThreeWa.SshDrive.Core.Remote;
using FileInfo = Fsp.Interop.FileInfo;
using VolumeInfo = Fsp.Interop.VolumeInfo;

namespace ThreeWa.SshDrive.FileSystem
{
    public sealed class SftpReadOnlyFileSystem : FileSystemBase
    {
        private sealed class CacheItem<T>
        {
            public T Value { get; }
            public DateTime ExpiresAtUtc { get; }

            public CacheItem(T value, TimeSpan ttl)
            {
                Value = value;
                ExpiresAtUtc = DateTime.UtcNow + ttl;
            }

            public bool IsExpired => DateTime.UtcNow > ExpiresAtUtc;
        }

        // ponytail: short-lived in-memory metadata cache (2s TTL) absorbs bursts of Explorer / IDE queries without SFTP round-trips.
        private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(2);
        // A directory enumeration can contain thousands of SFTP entries. Keep the
        // complete result a little longer so each Windows directory handle does
        // not re-fetch the same listing during an Explorer/IDE metadata burst.
        private static readonly TimeSpan DirectoryEntriesCacheTtl = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan NegativeCacheTtl = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan VolumeInfoCacheTtl = TimeSpan.FromSeconds(30);

        private readonly ConcurrentDictionary<string, CacheItem<RemoteEntry>> _entryCache =
            new ConcurrentDictionary<string, CacheItem<RemoteEntry>>(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentDictionary<string, CacheItem<HashSet<string>>> _dirChildrenCache =
            new ConcurrentDictionary<string, CacheItem<HashSet<string>>>(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentDictionary<string, CacheItem<IReadOnlyList<RemoteEntry>>> _directoryEntriesCache =
            new ConcurrentDictionary<string, CacheItem<IReadOnlyList<RemoteEntry>>>(StringComparer.OrdinalIgnoreCase);

        private CacheItem<RemoteVolumeInfo> _volumeInfoCache;
        private readonly object _volumeInfoLock = new object();

        private const uint FILE_WRITE_DATA = 0x0002;
        private const uint FILE_APPEND_DATA = 0x0004;
        private const uint GENERIC_WRITE = 0x40000000;

        private readonly IRemoteFileSystem _remote;
        private readonly string _remoteRoot;

        public bool ReadOnly { get; }

        public SftpReadOnlyFileSystem(
            IRemoteFileSystem remote,
            string remoteRoot,
            bool readOnly = false)
        {
            _remote = remote ?? throw new ArgumentNullException(nameof(remote));
            _remoteRoot = RemotePathMapper.NormalizeRoot(remoteRoot);
            ReadOnly = readOnly;
        }

        private RemoteEntry GetCachedEntry(string mappedPath)
        {
            if (_entryCache.TryGetValue(mappedPath, out var cached) && !cached.IsExpired)
            {
                if (cached.Value == null)
                    throw new RemotePathNotFoundException(mappedPath);
                return cached.Value;
            }

            var parent = GetParentPath(mappedPath);
            var name = GetName(mappedPath);
            if (_dirChildrenCache.TryGetValue(parent, out var dirChildren) && !dirChildren.IsExpired)
            {
                if (!dirChildren.Value.Contains(name))
                {
                    _entryCache[mappedPath] = new CacheItem<RemoteEntry>(null, NegativeCacheTtl);
                    throw new RemotePathNotFoundException(mappedPath);
                }
            }

            try
            {
                var entry = _remote.GetEntry(mappedPath);
                _entryCache[mappedPath] = new CacheItem<RemoteEntry>(entry, CacheTtl);
                return entry;
            }
            catch (RemotePathNotFoundException)
            {
                _entryCache[mappedPath] = new CacheItem<RemoteEntry>(null, NegativeCacheTtl);
                throw;
            }
        }

        private void InvalidateCache(string mappedPath)
        {
            if (string.IsNullOrEmpty(mappedPath))
                return;

            _entryCache.TryRemove(mappedPath, out _);
            _directoryEntriesCache.TryRemove(mappedPath, out _);
            var parent = GetParentPath(mappedPath);
            _dirChildrenCache.TryRemove(parent, out _);
            _directoryEntriesCache.TryRemove(parent, out _);
            _entryCache.TryRemove(parent, out _);
        }

        private void UpdateCachedEntry(RemoteEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.FullPath))
                return;

            _entryCache[entry.FullPath] = new CacheItem<RemoteEntry>(entry, CacheTtl);
            var parent = GetParentPath(entry.FullPath);
            _dirChildrenCache.TryRemove(parent, out _);
            _directoryEntriesCache.TryRemove(parent, out _);
        }

        private static string GetParentPath(string path)
        {
            var normalized = (path ?? string.Empty).TrimEnd('/');
            var lastIndex = normalized.LastIndexOf('/');
            if (lastIndex <= 0)
                return "/";
            return normalized.Substring(0, lastIndex);
        }

        public override int Init(object hostObject)
        {
            var host = (FileSystemHost)hostObject;
            host.SectorSize = 4096;
            host.SectorsPerAllocationUnit = 1;
            host.MaxComponentLength = 255;
            host.FileInfoTimeout = 2000;
            host.DirInfoTimeout = 2000;
            host.SecurityTimeout = 2000;
            host.VolumeInfoTimeout = 60000;
            host.CaseSensitiveSearch = true;
            host.CasePreservedNames = true;
            host.UnicodeOnDisk = true;
            host.PersistentAcls = false;
            host.PassQueryDirectoryPattern = false;
            host.VolumeCreationTime = 0;
            host.VolumeSerialNumber = 0x33574153;
            host.FileSystemName = "3waSshDrive";
            return STATUS_SUCCESS;
        }

        public override int ExceptionHandler(Exception exception)
        {
            if (exception is RemotePathNotFoundException)
                return STATUS_OBJECT_NAME_NOT_FOUND;
            if (exception is RemoteAccessDeniedException)
                return STATUS_ACCESS_DENIED;
            if (exception is RemoteConnectionException)
                return STATUS_DEVICE_NOT_READY;
            return STATUS_UNEXPECTED_IO_ERROR;
        }

        public override int GetVolumeInfo(out VolumeInfo volumeInfo)
        {
            volumeInfo = default(VolumeInfo);
            try
            {
                CacheItem<RemoteVolumeInfo> cached;
                lock (_volumeInfoLock)
                {
                    cached = _volumeInfoCache;
                }

                if (cached == null || cached.IsExpired)
                {
                    var remoteInfo = _remote.GetVolumeInfo(_remoteRoot);
                    if (remoteInfo != null && remoteInfo.TotalSize > 0)
                    {
                        cached = new CacheItem<RemoteVolumeInfo>(remoteInfo, VolumeInfoCacheTtl);
                        lock (_volumeInfoLock)
                        {
                            _volumeInfoCache = cached;
                        }
                    }
                }

                if (cached != null && cached.Value != null)
                {
                    volumeInfo.TotalSize = cached.Value.TotalSize;
                    volumeInfo.FreeSize = cached.Value.FreeSize;
                    return STATUS_SUCCESS;
                }
            }
            catch
            {
                // Fall back gracefully to default volume info
            }

            // Fallback default: 100 GB total, 50 GB free
            volumeInfo.TotalSize = 100UL * 1024 * 1024 * 1024;
            volumeInfo.FreeSize = 50UL * 1024 * 1024 * 1024;
            return STATUS_SUCCESS;
        }

        public override int GetSecurityByName(
            string fileName,
            out uint fileAttributes,
            ref byte[] securityDescriptor)
        {
            fileAttributes = 0;
            try
            {
                var entry = GetCachedEntry(MapPath(fileName));
                fileAttributes = WinFspFileInfoMapper.GetAttributes(entry);
                securityDescriptor = null;
                return STATUS_SUCCESS;
            }
            catch (Exception exception)
            {
                return ExceptionHandler(exception);
            }
        }

        public override int Create(
            string fileName,
            uint createOptions,
            uint grantedAccess,
            uint fileAttributes,
            byte[] securityDescriptor,
            ulong allocationSize,
            out object fileNode,
            out object fileDesc,
            out FileInfo fileInfo,
            out string normalizedName)
        {
            fileNode = null;
            fileDesc = null;
            fileInfo = default(FileInfo);
            normalizedName = null;

            if (ReadOnly)
                return STATUS_MEDIA_WRITE_PROTECTED;

            try
            {
                var mappedPath = MapPath(fileName);
                RemoteEntry entry;
                Stream stream = null;

                if ((createOptions & FILE_DIRECTORY_FILE) != 0)
                {
                    _remote.CreateDirectory(mappedPath);
                    entry = _remote.GetEntry(mappedPath);
                }
                else
                {
                    stream = _remote.OpenFile(mappedPath, FileMode.CreateNew, FileAccess.ReadWrite);
                    entry = new RemoteEntry(
                        GetName(mappedPath),
                        mappedPath,
                        false,
                        0,
                        DateTime.UtcNow,
                        DateTime.UtcNow);
                }

                UpdateCachedEntry(entry);

                var handle = new RemoteFileHandle(entry, stream);
                if ((createOptions & FILE_DELETE_ON_CLOSE) != 0)
                    handle.DeleteOnClose = true;

                fileNode = entry;
                fileDesc = handle;
                fileInfo = WinFspFileInfoMapper.Map(entry);
                return STATUS_SUCCESS;
            }
            catch (Exception exception)
            {
                return ExceptionHandler(exception);
            }
        }

        public override int Open(
            string fileName,
            uint createOptions,
            uint grantedAccess,
            out object fileNode,
            out object fileDesc,
            out FileInfo fileInfo,
            out string normalizedName)
        {
            fileNode = null;
            fileDesc = null;
            fileInfo = default(FileInfo);
            normalizedName = null;

            try
            {
                var mappedPath = MapPath(fileName);
                var entry = GetCachedEntry(mappedPath);
                Stream stream = null;

                if (!entry.IsDirectory)
                {
                    stream = !ReadOnly
                        ? _remote.OpenFile(entry.FullPath, FileMode.Open, FileAccess.ReadWrite)
                        : _remote.OpenRead(entry.FullPath);
                }

                var handle = new RemoteFileHandle(entry, stream);
                if ((createOptions & FILE_DELETE_ON_CLOSE) != 0)
                    handle.DeleteOnClose = true;

                fileNode = entry;
                fileDesc = handle;
                fileInfo = WinFspFileInfoMapper.Map(entry);
                return STATUS_SUCCESS;
            }
            catch (Exception exception)
            {
                return ExceptionHandler(exception);
            }
        }

        public override int Overwrite(
            object fileNode,
            object fileDesc,
            uint fileAttributes,
            bool replaceFileAttributes,
            ulong allocationSize,
            out FileInfo fileInfo)
        {
            fileInfo = default(FileInfo);
            if (ReadOnly)
                return STATUS_MEDIA_WRITE_PROTECTED;

            try
            {
                var handle = (RemoteFileHandle)fileDesc;
                if (handle.Entry.IsDirectory)
                    return STATUS_FILE_IS_A_DIRECTORY;

                lock (_remote.SyncRoot)
                {
                    handle.Stream.SetLength(0);
                }

                handle.Entry = new RemoteEntry(
                    handle.Entry.Name,
                    handle.Entry.FullPath,
                    false,
                    0,
                    DateTime.UtcNow,
                    DateTime.UtcNow);

                UpdateCachedEntry(handle.Entry);

                fileInfo = WinFspFileInfoMapper.Map(handle.Entry);
                return STATUS_SUCCESS;
            }
            catch (Exception exception)
            {
                return ExceptionHandler(exception);
            }
        }

        public override void Close(object fileNode, object fileDesc)
        {
            var handle = fileDesc as RemoteFileHandle;
            if (handle != null)
            {
                lock (_remote.SyncRoot)
                {
                    handle.Dispose();
                }
            }
        }

        public override void Cleanup(
            object fileNode,
            object fileDesc,
            string fileName,
            uint flags)
        {
            var handle = fileDesc as RemoteFileHandle;
            if (handle == null)
                return;

            var deleteOnCleanup =
                !ReadOnly && ((flags & CleanupDelete) != 0 || handle.DeleteOnClose);
            var mappedPath = deleteOnCleanup ? MapPath(fileName) : null;

            try
            {
                lock (_remote.SyncRoot)
                {
                    // WinFsp invokes Cleanup when a handle stops accepting I/O.
                    // Releasing the SFTP stream here prevents thousands of closed
                    // Windows handles from consuming server-side SFTP handles
                    // until the later Close callback is dispatched.
                    handle.Dispose();
                    if (deleteOnCleanup)
                    {
                        if (handle.Entry.IsDirectory)
                            _remote.DeleteDirectory(mappedPath);
                        else
                            _remote.DeleteFile(mappedPath);
                    }
                }

                if (deleteOnCleanup)
                    InvalidateCache(mappedPath);
            }
            catch
            {
                // WinFsp ignores exceptions during cleanup.
            }
        }

        public override int Read(
            object fileNode,
            object fileDesc,
            IntPtr buffer,
            ulong offset,
            uint length,
            out uint bytesTransferred)
        {
            bytesTransferred = 0;
            try
            {
                var handle = (RemoteFileHandle)fileDesc;
                if (handle.Entry.IsDirectory)
                    return STATUS_FILE_IS_A_DIRECTORY;
                if (offset >= (ulong)handle.Entry.Length)
                    return STATUS_END_OF_FILE;

                var remaining = (ulong)handle.Entry.Length - offset;
                var requested = (int)Math.Min(Math.Min(remaining, length), int.MaxValue);
                var bytes = new byte[requested];
                var total = 0;

                lock (_remote.SyncRoot)
                {
                    if (handle.Stream.Position != (long)offset)
                        handle.Stream.Seek((long)offset, SeekOrigin.Begin);

                    while (total < requested)
                    {
                        var read = handle.Stream.Read(bytes, total, requested - total);
                        if (read == 0)
                            break;
                        total += read;
                    }
                }

                if (total > 0)
                    Marshal.Copy(bytes, 0, buffer, total);
                bytesTransferred = (uint)total;
                return STATUS_SUCCESS;
            }
            catch (Exception exception)
            {
                return ExceptionHandler(exception);
            }
        }

        public override int Write(
            object fileNode,
            object fileDesc,
            IntPtr buffer,
            ulong offset,
            uint length,
            bool writeToEndOfFile,
            bool constrainedIo,
            out uint bytesTransferred,
            out FileInfo fileInfo)
        {
            bytesTransferred = 0;
            fileInfo = default(FileInfo);
            if (ReadOnly)
                return STATUS_MEDIA_WRITE_PROTECTED;

            try
            {
                var handle = (RemoteFileHandle)fileDesc;
                if (handle.Entry.IsDirectory)
                    return STATUS_FILE_IS_A_DIRECTORY;

                var bytes = new byte[length];
                Marshal.Copy(buffer, bytes, 0, (int)length);

                lock (_remote.SyncRoot)
                {
                    if (constrainedIo)
                    {
                        var currentLength = (ulong)handle.Stream.Length;
                        if (offset >= currentLength)
                            return STATUS_SUCCESS;
                        if (offset + length > currentLength)
                            length = (uint)(currentLength - offset);
                    }

                    if (writeToEndOfFile)
                    {
                        if (handle.Stream.Position != handle.Stream.Length)
                            handle.Stream.Seek(0, SeekOrigin.End);
                    }
                    else if (handle.Stream.Position != (long)offset)
                    {
                        handle.Stream.Seek((long)offset, SeekOrigin.Begin);
                    }

                    handle.Stream.Write(bytes, 0, (int)length);

                    var newLength = handle.Stream.Length;
                    if (newLength != handle.Entry.Length)
                    {
                        handle.Entry = new RemoteEntry(
                            handle.Entry.Name,
                            handle.Entry.FullPath,
                            false,
                            newLength,
                            DateTime.UtcNow,
                            DateTime.UtcNow);
                        UpdateCachedEntry(handle.Entry);
                    }
                }

                bytesTransferred = length;
                fileInfo = WinFspFileInfoMapper.Map(handle.Entry);
                return STATUS_SUCCESS;
            }
            catch (Exception exception)
            {
                return ExceptionHandler(exception);
            }
        }

        public override int Flush(
            object fileNode,
            object fileDesc,
            out FileInfo fileInfo)
        {
            var handle = (RemoteFileHandle)fileDesc;
            if (handle?.Stream != null)
            {
                try
                {
                    lock (_remote.SyncRoot)
                    {
                        handle.Stream.Flush();
                    }
                    fileInfo = WinFspFileInfoMapper.Map(handle.Entry);
                    return STATUS_SUCCESS;
                }
                catch (Exception exception)
                {
                    fileInfo = default(FileInfo);
                    return ExceptionHandler(exception);
                }
            }

            fileInfo = default(FileInfo);
            return STATUS_SUCCESS;
        }

        public override int SetFileSize(
            object fileNode,
            object fileDesc,
            ulong newSize,
            bool setAllocationSize,
            out FileInfo fileInfo)
        {
            fileInfo = default(FileInfo);
            if (ReadOnly)
                return STATUS_MEDIA_WRITE_PROTECTED;

            try
            {
                var handle = (RemoteFileHandle)fileDesc;
                if (handle.Entry.IsDirectory)
                    return STATUS_FILE_IS_A_DIRECTORY;

                lock (_remote.SyncRoot)
                {
                    if (!setAllocationSize || (ulong)handle.Stream.Length > newSize)
                    {
                        handle.Stream.SetLength((long)newSize);
                    }

                    handle.Entry = new RemoteEntry(
                        handle.Entry.Name,
                        handle.Entry.FullPath,
                        false,
                        handle.Stream.Length,
                        DateTime.UtcNow,
                        DateTime.UtcNow);
                    UpdateCachedEntry(handle.Entry);
                }

                fileInfo = WinFspFileInfoMapper.Map(handle.Entry);
                return STATUS_SUCCESS;
            }
            catch (Exception exception)
            {
                return ExceptionHandler(exception);
            }
        }

        public override int SetBasicInfo(
            object fileNode,
            object fileDesc,
            uint fileAttributes,
            ulong creationTime,
            ulong lastAccessTime,
            ulong lastWriteTime,
            ulong changeTime,
            out FileInfo fileInfo)
        {
            var handle = (RemoteFileHandle)fileDesc;
            fileInfo = WinFspFileInfoMapper.Map(handle.Entry);
            return STATUS_SUCCESS;
        }

        public override int CanDelete(
            object fileNode,
            object fileDesc,
            string fileName)
        {
            if (ReadOnly)
                return STATUS_MEDIA_WRITE_PROTECTED;

            try
            {
                var handle = (RemoteFileHandle)fileDesc;
                if (handle.Entry.IsDirectory)
                {
                    var children = _remote.ListDirectory(handle.Entry.FullPath);
                    if (children.Any(c => c.Name != "." && c.Name != ".."))
                        return STATUS_DIRECTORY_NOT_EMPTY;
                }

                return STATUS_SUCCESS;
            }
            catch (Exception exception)
            {
                return ExceptionHandler(exception);
            }
        }

        public override int SetDelete(
            object fileNode,
            object fileDesc,
            string fileName,
            bool deleteFlag)
        {
            if (ReadOnly && deleteFlag)
                return STATUS_MEDIA_WRITE_PROTECTED;

            var handle = (RemoteFileHandle)fileDesc;
            handle.DeleteOnClose = deleteFlag;
            return STATUS_SUCCESS;
        }

        public override int Rename(
            object fileNode,
            object fileDesc,
            string fileName,
            string newFileName,
            bool replaceIfExists)
        {
            if (ReadOnly)
                return STATUS_MEDIA_WRITE_PROTECTED;

            try
            {
                var oldMapped = MapPath(fileName);
                var newMapped = MapPath(newFileName);
                _remote.Rename(oldMapped, newMapped, replaceIfExists);
                InvalidateCache(oldMapped);
                InvalidateCache(newMapped);
                return STATUS_SUCCESS;
            }
            catch (Exception exception)
            {
                return ExceptionHandler(exception);
            }
        }

        public override int GetFileInfo(
            object fileNode,
            object fileDesc,
            out FileInfo fileInfo)
        {
            var handle = (RemoteFileHandle)fileDesc;
            fileInfo = WinFspFileInfoMapper.Map(handle.Entry);
            return STATUS_SUCCESS;
        }

        public override bool ReadDirectoryEntry(
            object fileNode,
            object fileDesc,
            string pattern,
            string marker,
            ref object context,
            out string fileName,
            out FileInfo fileInfo)
        {
            var handle = (RemoteFileHandle)fileDesc;
            if (handle.DirectoryEntries == null)
                handle.DirectoryEntries = LoadDirectoryEntries(handle.Entry);

            var index = context == null ? 0 : (int)context;
            if (context == null && !string.IsNullOrEmpty(marker))
            {
                index = FindEntryAfterMarker(handle.DirectoryEntries, marker);
            }

            if (index >= handle.DirectoryEntries.Count)
            {
                fileName = null;
                fileInfo = default(FileInfo);
                return false;
            }

            var entry = handle.DirectoryEntries[index];
            context = index + 1;
            fileName = entry.Name;
            fileInfo = WinFspFileInfoMapper.Map(entry);
            return true;
        }

        private IReadOnlyList<RemoteEntry> LoadDirectoryEntries(RemoteEntry directory)
        {
            if (_directoryEntriesCache.TryGetValue(directory.FullPath, out var cached) && !cached.IsExpired)
                return cached.Value;

            var rawEntries = _remote.ListDirectory(directory.FullPath);

            var childNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entries = new List<RemoteEntry>(rawEntries.Count);

            foreach (var item in rawEntries)
            {
                if (item.Name == "." || item.Name == "..")
                    continue;

                _entryCache[item.FullPath] = new CacheItem<RemoteEntry>(item, CacheTtl);
                childNames.Add(item.Name);
                entries.Add(item);
            }

            _dirChildrenCache[directory.FullPath] = new CacheItem<HashSet<string>>(childNames, CacheTtl);
            _entryCache[directory.FullPath] = new CacheItem<RemoteEntry>(directory, CacheTtl);

            entries.Sort((a, b) =>
            {
                var comp = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                return comp != 0 ? comp : string.Compare(a.Name, b.Name, StringComparison.Ordinal);
            });

            entries.Insert(0, new RemoteEntry(
                ".",
                directory.FullPath,
                true,
                0,
                directory.LastAccessTimeUtc,
                directory.LastWriteTimeUtc));

            if (!string.Equals(directory.FullPath, _remoteRoot, StringComparison.Ordinal))
            {
                entries.Insert(1, new RemoteEntry(
                    "..",
                    directory.FullPath,
                    true,
                    0,
                    directory.LastAccessTimeUtc,
                    directory.LastWriteTimeUtc));
            }

            var cachedEntries = entries.AsReadOnly();
            _directoryEntriesCache[directory.FullPath] = new CacheItem<IReadOnlyList<RemoteEntry>>(
                cachedEntries,
                DirectoryEntriesCacheTtl);
            return cachedEntries;
        }

        private static int FindEntryAfterMarker(
            IReadOnlyList<RemoteEntry> entries,
            string marker)
        {
            for (var index = 0; index < entries.Count; index++)
            {
                if (string.Compare(
                        entries[index].Name,
                        marker,
                        StringComparison.OrdinalIgnoreCase) > 0)
                    return index;
            }

            return entries.Count;
        }

        private string MapPath(string fileName)
        {
            return RemotePathMapper.Map(_remoteRoot, fileName);
        }

        private static string GetName(string path)
        {
            var normalized = (path ?? string.Empty).TrimEnd('/');
            var lastIndex = normalized.LastIndexOf('/');
            return lastIndex < 0 ? normalized : normalized.Substring(lastIndex + 1);
        }
    }
}

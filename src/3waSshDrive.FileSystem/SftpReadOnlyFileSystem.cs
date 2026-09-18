using System;
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

        public override int Init(object hostObject)
        {
            var host = (FileSystemHost)hostObject;
            host.SectorSize = 4096;
            host.SectorsPerAllocationUnit = 1;
            host.MaxComponentLength = 255;
            host.FileInfoTimeout = 1000;
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
            volumeInfo.TotalSize = 1UL << 50;
            volumeInfo.FreeSize = 1UL << 49;
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
                var entry = _remote.GetEntry(MapPath(fileName));
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
                var entry = _remote.GetEntry(mappedPath);
                Stream stream = null;

                if (!entry.IsDirectory)
                {
                    bool wantsWrite = !ReadOnly && (grantedAccess & (FILE_WRITE_DATA | FILE_APPEND_DATA | GENERIC_WRITE)) != 0;
                    stream = wantsWrite
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

                lock (handle.SyncRoot)
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
            (fileDesc as RemoteFileHandle)?.Dispose();
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

            if (!ReadOnly && ((flags & CleanupDelete) != 0 || handle.DeleteOnClose))
            {
                try
                {
                    handle.Dispose();
                    var mappedPath = MapPath(fileName);
                    if (handle.Entry.IsDirectory)
                        _remote.DeleteDirectory(mappedPath);
                    else
                        _remote.DeleteFile(mappedPath);
                }
                catch
                {
                    // WinFsp ignores exceptions during cleanup
                }
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

                lock (handle.SyncRoot)
                {
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

                if (constrainedIo)
                {
                    var currentLength = (ulong)handle.Stream.Length;
                    if (offset >= currentLength)
                        return STATUS_SUCCESS;
                    if (offset + length > currentLength)
                        length = (uint)(currentLength - offset);
                }

                var bytes = new byte[length];
                Marshal.Copy(buffer, bytes, 0, (int)length);

                lock (handle.SyncRoot)
                {
                    if (writeToEndOfFile)
                        handle.Stream.Seek(0, SeekOrigin.End);
                    else
                        handle.Stream.Seek((long)offset, SeekOrigin.Begin);

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
                    lock (handle.SyncRoot)
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

                lock (handle.SyncRoot)
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
            var entries = _remote.ListDirectory(directory.FullPath)
                .Where(entry => entry.Name != "." && entry.Name != "..")
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Name, StringComparer.Ordinal)
                .ToList();

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

            return entries;
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

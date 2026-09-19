using System;
using ThreeWa.SshDrive.Core.Remote;
using FileInfo = Fsp.Interop.FileInfo;

namespace ThreeWa.SshDrive.FileSystem
{
    internal static class WinFspFileInfoMapper
    {
        private const ulong AllocationUnit = 4096;

        public static uint GetAttributes(RemoteEntry entry)
        {
            if (entry.IsDirectory)
                return (uint)System.IO.FileAttributes.Directory;

            var attributes = System.IO.FileAttributes.Archive;
            if (entry.Name.StartsWith(".", StringComparison.Ordinal))
                attributes |= System.IO.FileAttributes.Hidden;

            return (uint)attributes;
        }

        public static FileInfo Map(RemoteEntry entry)
        {
            var size = entry.IsDirectory || entry.Length < 0
                ? 0UL
                : (ulong)entry.Length;
            var lastAccessTime = (ulong)entry.LastAccessTimeUtc.ToFileTimeUtc();
            var lastWriteTime = (ulong)entry.LastWriteTimeUtc.ToFileTimeUtc();

            return new FileInfo
            {
                FileAttributes = GetAttributes(entry),
                ReparseTag = 0,
                FileSize = size,
                AllocationSize = (size + AllocationUnit - 1) / AllocationUnit * AllocationUnit,
                CreationTime = lastWriteTime,
                LastAccessTime = lastAccessTime,
                LastWriteTime = lastWriteTime,
                ChangeTime = lastWriteTime,
                IndexNumber = 0,
                HardLinks = 0
            };
        }
    }
}

using System;

namespace ThreeWa.SshDrive.Core.Remote
{
    public sealed class RemoteEntry
    {
        public RemoteEntry(
            string name,
            string fullPath,
            bool isDirectory,
            long length,
            DateTime lastAccessTimeUtc,
            DateTime lastWriteTimeUtc)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            FullPath = fullPath ?? throw new ArgumentNullException(nameof(fullPath));
            IsDirectory = isDirectory;
            Length = length;
            LastAccessTimeUtc = EnsureUtc(lastAccessTimeUtc);
            LastWriteTimeUtc = EnsureUtc(lastWriteTimeUtc);
        }

        public string Name { get; }

        public string FullPath { get; }

        public bool IsDirectory { get; }

        public long Length { get; }

        public DateTime LastAccessTimeUtc { get; }

        public DateTime LastWriteTimeUtc { get; }

        private static DateTime EnsureUtc(DateTime value)
        {
            return value.Kind == DateTimeKind.Utc
                ? value
                : value.ToUniversalTime();
        }
    }
}

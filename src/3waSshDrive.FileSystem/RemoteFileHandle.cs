using System;
using System.Collections.Generic;
using System.IO;
using ThreeWa.SshDrive.Core.Remote;

namespace ThreeWa.SshDrive.FileSystem
{
    internal sealed class RemoteFileHandle : IDisposable
    {
        public RemoteFileHandle(RemoteEntry entry, Stream stream)
        {
            Entry = entry ?? throw new ArgumentNullException(nameof(entry));
            Stream = stream;
        }

        public RemoteEntry Entry { get; set; }

        public Stream Stream { get; }

        public object SyncRoot { get; } = new object();

        public bool DeleteOnClose { get; set; }

        public IReadOnlyList<RemoteEntry> DirectoryEntries { get; set; }

        public void Dispose()
        {
            Stream?.Dispose();
        }
    }
}

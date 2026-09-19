using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using ThreeWa.SshDrive.Core.Remote;

namespace ThreeWa.SshDrive.FileSystem
{
    internal sealed class RemoteFileHandle : IDisposable
    {
        private Stream _stream;

        public RemoteFileHandle(RemoteEntry entry, Stream stream)
        {
            Entry = entry ?? throw new ArgumentNullException(nameof(entry));
            _stream = stream;
        }

        public RemoteEntry Entry { get; set; }

        public Stream Stream => _stream;

        public object SyncRoot { get; } = new object();

        public bool DeleteOnClose { get; set; }

        public IReadOnlyList<RemoteEntry> DirectoryEntries { get; set; }

        public void Dispose()
        {
            // WinFsp always follows Cleanup with Close. Cleanup may need to close an
            // SFTP stream early to avoid exhausting server handles, but Close must
            // not issue a second SFTP close for that same stream.
            var stream = Interlocked.Exchange(ref _stream, null);
            stream?.Dispose();
        }
    }
}

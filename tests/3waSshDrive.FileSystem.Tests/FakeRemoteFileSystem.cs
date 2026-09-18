using System;
using System.Collections.Generic;
using System.IO;
using ThreeWa.SshDrive.Core.Remote;

namespace ThreeWa.SshDrive.FileSystem.Tests
{
    internal sealed class FakeRemoteFileSystem : IRemoteFileSystem
    {
        private readonly Dictionary<string, RemoteEntry> _entries =
            new Dictionary<string, RemoteEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, byte[]> _contents =
            new Dictionary<string, byte[]>(StringComparer.Ordinal);
        private readonly Dictionary<string, IReadOnlyList<RemoteEntry>> _directories =
            new Dictionary<string, IReadOnlyList<RemoteEntry>>(StringComparer.Ordinal);

        public bool IsConnected { get; private set; }
        public bool IsDisposed { get; private set; }

        public void AddEntry(RemoteEntry entry, byte[] content = null)
        {
            _entries[entry.FullPath] = entry;
            if (content != null)
                _contents[entry.FullPath] = content;
        }

        public void SetDirectory(string path, params RemoteEntry[] entries)
        {
            _directories[path] = entries;
        }

        public void Connect()
        {
            IsConnected = true;
        }

        public RemoteEntry GetEntry(string path)
        {
            if (!_entries.TryGetValue(path, out var entry))
                throw new RemotePathNotFoundException(path);
            return entry;
        }

        public IReadOnlyList<RemoteEntry> ListDirectory(string path)
        {
            if (!_directories.TryGetValue(path, out var entries))
                throw new RemotePathNotFoundException(path);
            return entries;
        }

        public Stream OpenRead(string path)
        {
            if (!_contents.TryGetValue(path, out var content))
                throw new RemotePathNotFoundException(path);
            return new MemoryStream(content, false);
        }

        public void Dispose()
        {
            IsDisposed = true;
            IsConnected = false;
        }
    }
}

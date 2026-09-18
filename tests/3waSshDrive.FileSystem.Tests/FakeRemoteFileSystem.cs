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
            {
                if (_entries.ContainsKey(path) && _entries[path].IsDirectory)
                    return new List<RemoteEntry>();
                throw new RemotePathNotFoundException(path);
            }
            return entries;
        }

        public Stream OpenRead(string path)
        {
            return OpenFile(path, FileMode.Open, FileAccess.Read);
        }

        public Stream OpenFile(string path, FileMode mode, FileAccess access)
        {
            if (mode == FileMode.CreateNew)
            {
                if (_entries.ContainsKey(path))
                    throw new RemoteAccessDeniedException(path, new IOException("File already exists"));

                _contents[path] = new byte[0];
                _entries[path] = new RemoteEntry(GetName(path), path, false, 0, DateTime.UtcNow, DateTime.UtcNow);
                return new TrackingMemoryStream(this, path, _contents[path]);
            }

            if (mode == FileMode.Create || mode == FileMode.Truncate)
            {
                _contents[path] = new byte[0];
                _entries[path] = new RemoteEntry(GetName(path), path, false, 0, DateTime.UtcNow, DateTime.UtcNow);
                return new TrackingMemoryStream(this, path, _contents[path]);
            }

            if (!_contents.TryGetValue(path, out var content))
                throw new RemotePathNotFoundException(path);

            return new TrackingMemoryStream(this, path, content);
        }

        public void CreateDirectory(string path)
        {
            _entries[path] = new RemoteEntry(GetName(path), path, true, 0, DateTime.UtcNow, DateTime.UtcNow);
            _directories[path] = new List<RemoteEntry>();
        }

        public void DeleteFile(string path)
        {
            _entries.Remove(path);
            _contents.Remove(path);
        }

        public void DeleteDirectory(string path)
        {
            _entries.Remove(path);
            _directories.Remove(path);
        }

        public void Rename(string oldPath, string newPath, bool replaceIfExists)
        {
            if (!_entries.TryGetValue(oldPath, out var entry))
                throw new RemotePathNotFoundException(oldPath);

            _entries.Remove(oldPath);
            _entries[newPath] = new RemoteEntry(
                GetName(newPath),
                newPath,
                entry.IsDirectory,
                entry.Length,
                entry.LastAccessTimeUtc,
                DateTime.UtcNow);

            if (_contents.TryGetValue(oldPath, out var content))
            {
                _contents.Remove(oldPath);
                _contents[newPath] = content;
            }

            if (_directories.TryGetValue(oldPath, out var dirEntries))
            {
                _directories.Remove(oldPath);
                _directories[newPath] = dirEntries;
            }
        }

        public byte[] GetContent(string path)
        {
            return _contents.TryGetValue(path, out var content) ? content : null;
        }

        public bool Exists(string path)
        {
            return _entries.ContainsKey(path);
        }

        public void Dispose()
        {
            IsDisposed = true;
            IsConnected = false;
        }

        internal void UpdateFileContent(string path, byte[] bytes)
        {
            _contents[path] = bytes;
            if (_entries.TryGetValue(path, out var entry))
            {
                _entries[path] = new RemoteEntry(
                    entry.Name,
                    entry.FullPath,
                    false,
                    bytes.Length,
                    entry.LastAccessTimeUtc,
                    DateTime.UtcNow);
            }
        }

        private static string GetName(string path)
        {
            var normalized = (path ?? string.Empty).TrimEnd('/');
            var lastIndex = normalized.LastIndexOf('/');
            return lastIndex < 0 ? normalized : normalized.Substring(lastIndex + 1);
        }

        private sealed class TrackingMemoryStream : MemoryStream
        {
            private readonly FakeRemoteFileSystem _owner;
            private readonly string _path;

            public TrackingMemoryStream(FakeRemoteFileSystem owner, string path, byte[] initial)
            {
                _owner = owner;
                _path = path;
                if (initial != null && initial.Length > 0)
                {
                    Write(initial, 0, initial.Length);
                    Position = 0;
                }
            }

            public override void Flush()
            {
                base.Flush();
                Sync();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    Sync();
                base.Dispose(disposing);
            }

            private void Sync()
            {
                _owner.UpdateFileContent(_path, ToArray());
            }
        }
    }
}

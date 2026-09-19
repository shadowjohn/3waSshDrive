using System;
using System.Collections.Generic;
using System.IO;

namespace ThreeWa.SshDrive.Core.Remote
{
    public interface IRemoteFileSystem : IDisposable
    {
        bool IsConnected { get; }

        object SyncRoot { get; }

        void Connect();

        RemoteEntry GetEntry(string path);

        IReadOnlyList<RemoteEntry> ListDirectory(string path);

        Stream OpenRead(string path);

        Stream OpenFile(string path, FileMode mode, FileAccess access);

        void CreateDirectory(string path);

        void DeleteFile(string path);

        void DeleteDirectory(string path);

        void Rename(string oldPath, string newPath, bool replaceIfExists);
    }
}

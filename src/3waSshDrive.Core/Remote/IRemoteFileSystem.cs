using System;
using System.Collections.Generic;
using System.IO;

namespace ThreeWa.SshDrive.Core.Remote
{
    public interface IRemoteFileSystem : IDisposable
    {
        bool IsConnected { get; }

        void Connect();

        RemoteEntry GetEntry(string path);

        IReadOnlyList<RemoteEntry> ListDirectory(string path);

        Stream OpenRead(string path);
    }
}

using System;

namespace ThreeWa.SshDrive.FileSystem.Mounting
{
    public interface IFileSystemHost : IDisposable
    {
        int Mount(string mountPoint);

        void Unmount();
    }
}

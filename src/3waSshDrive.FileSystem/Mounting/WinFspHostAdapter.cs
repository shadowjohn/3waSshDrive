using System;
using Fsp;

namespace ThreeWa.SshDrive.FileSystem.Mounting
{
    public sealed class WinFspHostAdapter : IFileSystemHost
    {
        private readonly FileSystemHost _host;

        public WinFspHostAdapter(SftpReadOnlyFileSystem fileSystem)
        {
            if (fileSystem == null)
                throw new ArgumentNullException(nameof(fileSystem));

            _host = new FileSystemHost(fileSystem);
        }

        public int Mount(string mountPoint)
        {
            return _host.Mount(mountPoint, null, true, 0);
        }

        public void Unmount()
        {
            _host.Unmount();
        }

        public void Dispose()
        {
            _host.Dispose();
        }
    }
}

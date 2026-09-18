using System;
using ThreeWa.SshDrive.Core.Remote;

namespace ThreeWa.SshDrive.FileSystem.Mounting
{
    public sealed class MountedDrive : IDisposable
    {
        private readonly IFileSystemHost _host;
        private readonly IRemoteFileSystem _remote;
        private bool _disposed;

        internal MountedDrive(
            string profileName,
            string driveLetter,
            IFileSystemHost host,
            IRemoteFileSystem remote)
        {
            ProfileName = profileName;
            DriveLetter = driveLetter;
            _host = host;
            _remote = remote;
        }

        public string ProfileName { get; }

        public string DriveLetter { get; }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            try
            {
                _host.Unmount();
            }
            finally
            {
                try
                {
                    _host.Dispose();
                }
                finally
                {
                    _remote.Dispose();
                }
            }
        }
    }
}

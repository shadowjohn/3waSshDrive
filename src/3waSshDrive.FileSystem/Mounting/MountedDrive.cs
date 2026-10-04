using System;
using System.Runtime.ExceptionServices;
using System.Threading;
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

        public bool IsConnected => !_disposed && _remote.IsConnected;
        public bool IsConnecting => !_disposed && (_remote as IRemoteConnectionControl)?.IsConnecting == true;

        public void EnsureConnected()
            => EnsureConnected(CancellationToken.None);

        public void EnsureConnected(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_disposed && !_remote.IsConnected)
            {
                if (_remote is IRemoteConnectionControl control)
                    control.Connect(cancellationToken);
                else
                    _remote.Connect();
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            ExceptionDispatchInfo failure = null;
            try
            {
                try
                {
                    _host.Unmount();
                }
                catch (Exception exception)
                {
                    failure = ExceptionDispatchInfo.Capture(exception);
                }
            }
            finally
            {
                try
                {
                    try
                    {
                        _host.Dispose();
                    }
                    catch (Exception exception)
                    {
                        if (failure == null)
                            failure = ExceptionDispatchInfo.Capture(exception);
                    }
                }
                finally
                {
                    try
                    {
                        _remote.Dispose();
                    }
                    catch (Exception exception)
                    {
                        if (failure == null)
                            failure = ExceptionDispatchInfo.Capture(exception);
                    }
                }
            }

            failure?.Throw();
        }
    }
}

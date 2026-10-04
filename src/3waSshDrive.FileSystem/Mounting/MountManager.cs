using System;
using System.Linq;
using System.Threading;
using ThreeWa.SshDrive.Core.Models;
using ThreeWa.SshDrive.Core.Profiles;
using ThreeWa.SshDrive.Core.Remote;
using ThreeWa.SshDrive.Sftp;
using ThreeWa.SshDrive.Sftp.Security;

namespace ThreeWa.SshDrive.FileSystem.Mounting
{
    public sealed class MountManager
    {
        private readonly Func<DriveProfile, IRemoteFileSystem> _remoteFactory;
        private readonly Func<SftpReadOnlyFileSystem, IFileSystemHost> _hostFactory;

        public MountManager()
            : this(
                profile => new SshNetRemoteFileSystem(
                    profile,
                    HostKeyPolicy.ForMount(profile.HostKeyFingerprintSha256)),
                fileSystem => new WinFspHostAdapter(fileSystem))
        {
        }

        public MountManager(
            Func<DriveProfile, IRemoteFileSystem> remoteFactory,
            Func<SftpReadOnlyFileSystem, IFileSystemHost> hostFactory)
        {
            _remoteFactory = remoteFactory ??
                throw new ArgumentNullException(nameof(remoteFactory));
            _hostFactory = hostFactory ??
                throw new ArgumentNullException(nameof(hostFactory));
        }

        public MountedDrive Mount(DriveProfile profile)
            => Mount(profile, CancellationToken.None);

        public MountedDrive Mount(DriveProfile profile, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var errors = DriveProfileValidator.Validate(profile);
            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    "Drive profile is invalid: " +
                    string.Join(" ", errors.ToArray()));
            }

            IRemoteFileSystem remote = null;
            IFileSystemHost host = null;
            try
            {
                remote = _remoteFactory(profile);
                if (remote is IRemoteConnectionControl control)
                    control.Connect(cancellationToken);
                else
                    remote.Connect();
                cancellationToken.ThrowIfCancellationRequested();

                var fileSystem = new SftpReadOnlyFileSystem(
                    remote,
                    profile.RemoteRoot,
                    profile.ReadOnly);
                host = _hostFactory(fileSystem);
                var status = host.Mount(profile.DriveLetter.ToUpperInvariant());
                if (status < 0)
                    throw new MountException(profile.DriveLetter, status);

                if (cancellationToken.IsCancellationRequested)
                {
                    // Keep ownership until a late successful mount has actually been removed.
                    host.Unmount();
                    cancellationToken.ThrowIfCancellationRequested();
                }

                return new MountedDrive(
                    profile.Name,
                    profile.DriveLetter.ToUpperInvariant(),
                    host,
                    remote);
            }
            catch
            {
                try
                {
                    host?.Dispose();
                }
                catch
                {
                    // Preserve the original connection or mount failure.
                }
                finally
                {
                    try
                    {
                        remote?.Dispose();
                    }
                    catch
                    {
                        // Preserve the original connection or mount failure.
                    }
                }
                throw;
            }
        }
    }
}

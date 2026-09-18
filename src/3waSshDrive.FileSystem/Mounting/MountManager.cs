using System;
using System.Linq;
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
        {
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
                remote.Connect();

                var fileSystem = new SftpReadOnlyFileSystem(
                    remote,
                    profile.RemoteRoot,
                    profile.ReadOnly);
                host = _hostFactory(fileSystem);
                var status = host.Mount(profile.DriveLetter.ToUpperInvariant());
                if (status < 0)
                    throw new MountException(profile.DriveLetter, status);

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

using System;
using System.Threading;
using ThreeWa.SshDrive.Core.Models;
using ThreeWa.SshDrive.Core.Remote;
using ThreeWa.SshDrive.Sftp.Security;

namespace ThreeWa.SshDrive.Sftp
{
    public sealed class SshConnectionProbe : ISshConnectionProbe
    {
        public ConnectionProbeResult Probe(DriveProfile profile)
            => Probe(profile, CancellationToken.None);

        public ConnectionProbeResult Probe(DriveProfile profile, CancellationToken cancellationToken)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            var policy = HostKeyPolicy.ForCapture();
            using (var remote = new SshNetRemoteFileSystem(profile, policy))
            {
                remote.Connect(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                var root = remote.GetEntry(profile.RemoteRoot);
                if (!root.IsDirectory)
                {
                    throw new RemoteFileSystemException(
                        "Configured remote root is not a directory: " + profile.RemoteRoot);
                }

                cancellationToken.ThrowIfCancellationRequested();
                remote.ListDirectory(profile.RemoteRoot);
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(policy.CapturedFingerprint))
                {
                    throw new RemoteConnectionException(
                        "The SSH server did not provide a host-key fingerprint.");
                }

                return new ConnectionProbeResult(
                    policy.CapturedFingerprint,
                    root.FullPath);
            }
        }
    }

    public sealed class ConnectionProbeResult
    {
        public ConnectionProbeResult(
            string hostKeyFingerprintSha256,
            string remoteRoot)
        {
            HostKeyFingerprintSha256 = hostKeyFingerprintSha256;
            RemoteRoot = remoteRoot;
        }

        public string HostKeyFingerprintSha256 { get; }

        public string RemoteRoot { get; }
    }
}

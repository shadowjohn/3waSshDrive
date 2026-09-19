using System;
using ThreeWa.SshDrive.Core.Models;
using ThreeWa.SshDrive.Core.Remote;
using ThreeWa.SshDrive.Sftp.Security;

namespace ThreeWa.SshDrive.Sftp
{
    public sealed class SshConnectionProbe
    {
        public ConnectionProbeResult Probe(DriveProfile profile)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            var policy = HostKeyPolicy.ForCapture();
            using (var remote = new SshNetRemoteFileSystem(profile, policy))
            {
                remote.Connect();
                var root = remote.GetEntry(profile.RemoteRoot);
                if (!root.IsDirectory)
                {
                    throw new RemoteFileSystemException(
                        "Configured remote root is not a directory: " + profile.RemoteRoot);
                }

                remote.ListDirectory(profile.RemoteRoot);
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

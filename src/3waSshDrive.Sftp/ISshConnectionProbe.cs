using System.Threading;
using ThreeWa.SshDrive.Core.Models;

namespace ThreeWa.SshDrive.Sftp
{
    public interface ISshConnectionProbe
    {
        ConnectionProbeResult Probe(DriveProfile profile, CancellationToken cancellationToken);
    }
}

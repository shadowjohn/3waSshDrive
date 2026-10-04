using System.Threading;

namespace ThreeWa.SshDrive.Core.Remote
{
    // Optional control surface: existing filesystem implementations stay compatible.
    public interface IRemoteConnectionControl
    {
        bool IsConnecting { get; }
        void Connect(CancellationToken cancellationToken);
    }
}

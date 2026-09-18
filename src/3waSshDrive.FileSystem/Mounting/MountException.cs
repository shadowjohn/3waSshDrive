using System.IO;

namespace ThreeWa.SshDrive.FileSystem.Mounting
{
    public sealed class MountException : IOException
    {
        public MountException(string mountPoint, int status)
            : base(string.Format(
                "WinFsp could not mount {0}. NTSTATUS: 0x{1:X8}",
                mountPoint,
                unchecked((uint)status)))
        {
            MountPoint = mountPoint;
            Status = status;
        }

        public string MountPoint { get; }

        public int Status { get; }
    }
}

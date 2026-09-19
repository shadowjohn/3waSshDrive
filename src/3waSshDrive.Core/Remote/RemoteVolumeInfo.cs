namespace ThreeWa.SshDrive.Core.Remote
{
    public sealed class RemoteVolumeInfo
    {
        public RemoteVolumeInfo(ulong totalSize, ulong freeSize)
        {
            TotalSize = totalSize;
            FreeSize = freeSize;
        }

        public ulong TotalSize { get; }

        public ulong FreeSize { get; }
    }
}

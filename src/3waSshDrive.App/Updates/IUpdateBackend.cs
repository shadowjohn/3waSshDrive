using System;
using System.Threading;
using System.Threading.Tasks;

namespace ThreeWa.SshDrive.App.Updates
{
    internal interface IUpdateBackend
    {
        bool IsInstalled { get; }

        bool IsPortable { get; }

        string CurrentPackageVersion { get; }

        Task<UpdatePackage> CheckAsync(CancellationToken token);

        Task DownloadAsync(
            UpdatePackage package,
            Action<int> progress,
            CancellationToken token);

        void ApplyAndRestart(UpdatePackage package);
    }
}

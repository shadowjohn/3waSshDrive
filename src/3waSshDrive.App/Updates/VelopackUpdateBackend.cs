using System;
using System.Threading;
using System.Threading.Tasks;
using ThreeWa.SshDrive.Core.Versioning;
using Velopack;
using Velopack.Sources;

namespace ThreeWa.SshDrive.App.Updates
{
    internal interface IVelopackClient
    {
        bool IsInstalled { get; }

        bool IsPortable { get; }

        string CurrentVersion { get; }

        Task<VelopackUpdateData> CheckAsync();

        Task DownloadAsync(
            object nativeToken,
            Action<int> progress,
            CancellationToken token);

        void ApplyAndRestart(object nativeToken);
    }

    internal sealed class VelopackUpdateData
    {
        public VelopackUpdateData(
            string targetVersion,
            string releaseNotesMarkdown,
            object nativeToken)
        {
            TargetVersion = targetVersion;
            ReleaseNotesMarkdown = releaseNotesMarkdown ?? string.Empty;
            NativeToken = nativeToken ?? throw new ArgumentNullException(nameof(nativeToken));
        }

        public string TargetVersion { get; }

        public string ReleaseNotesMarkdown { get; }

        public object NativeToken { get; }
    }

    internal sealed class VelopackClient : IVelopackClient
    {
        private const string RepositoryUrl =
            "https://github.com/shadowjohn/3waSshDrive";

        private readonly UpdateManager _manager;

        public VelopackClient()
        {
            var source = new GithubSource(
                RepositoryUrl,
                accessToken: null,
                prerelease: false);
            _manager = new UpdateManager(source);
        }

        public bool IsInstalled => _manager.IsInstalled;

        public bool IsPortable => _manager.IsPortable;

        public string CurrentVersion => _manager.CurrentVersion?.ToString() ?? string.Empty;

        public async Task<VelopackUpdateData> CheckAsync()
        {
            var update = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update == null)
            {
                return null;
            }

            var target = update.TargetFullRelease;
            return new VelopackUpdateData(
                target.Version.ToString(),
                target.NotesMarkdown,
                update);
        }

        public Task DownloadAsync(
            object nativeToken,
            Action<int> progress,
            CancellationToken token)
        {
            return _manager.DownloadUpdatesAsync(
                (UpdateInfo)nativeToken,
                progress,
                token);
        }

        public void ApplyAndRestart(object nativeToken)
        {
            var update = (UpdateInfo)nativeToken;
            _manager.ApplyUpdatesAndRestart(update.TargetFullRelease);
        }
    }

    internal sealed class VelopackUpdateBackend : IUpdateBackend
    {
        private readonly IVelopackClient _client;

        public VelopackUpdateBackend()
            : this(new VelopackClient())
        {
        }

        internal VelopackUpdateBackend(IVelopackClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public bool IsInstalled => _client.IsInstalled;

        public bool IsPortable => _client.IsPortable;

        public string CurrentPackageVersion => _client.CurrentVersion;

        public async Task<UpdatePackage> CheckAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var update = await _client.CheckAsync().ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (update == null)
            {
                return null;
            }

            return new UpdatePackage(
                ReleaseVersion.TagFromPackageVersion(_client.CurrentVersion),
                ReleaseVersion.TagFromPackageVersion(update.TargetVersion),
                update.TargetVersion,
                update.ReleaseNotesMarkdown,
                update.NativeToken);
        }

        public Task DownloadAsync(
            UpdatePackage package,
            Action<int> progress,
            CancellationToken token)
        {
            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            return _client.DownloadAsync(package.NativeToken, progress, token);
        }

        public void ApplyAndRestart(UpdatePackage package)
        {
            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            _client.ApplyAndRestart(package.NativeToken);
        }
    }
}

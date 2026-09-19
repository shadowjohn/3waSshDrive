using System;
using System.Threading;
using System.Threading.Tasks;

namespace ThreeWa.SshDrive.App.Updates
{
    internal sealed class UpdateCoordinator
    {
        private static readonly TimeSpan PreparationTimeout =
            TimeSpan.FromSeconds(30);

        private readonly IUpdatePreparation _preparation;

        internal UpdateCoordinator(IUpdatePreparation preparation)
        {
            _preparation = preparation ??
                throw new ArgumentNullException(nameof(preparation));
        }

        internal async Task<UpdateApplyResult> DownloadAndApplyAsync(
            UpdateSession session,
            UpdatePackage package,
            Action<int> progress,
            CancellationToken token)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            var download = await session.DownloadAsync(package, progress, token);
            if (!download.Success)
            {
                return UpdateApplyResult.Failed(download.Message);
            }

            var preparation = await _preparation
                .PrepareAsync(PreparationTimeout)
                .ConfigureAwait(true);
            if (!preparation.Success)
            {
                if (preparation.CanResumeImmediately)
                {
                    _preparation.ResumeAfterFailure();
                }

                return UpdateApplyResult.Blocked(preparation.Message);
            }

            var apply = session.ApplyAndRestart(package);
            if (!apply.Success)
            {
                _preparation.ResumeAfterFailure();
                return UpdateApplyResult.Failed(apply.Message);
            }

            return UpdateApplyResult.RestartRequested();
        }
    }
}

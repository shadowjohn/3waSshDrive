using System;
using System.Threading;
using System.Threading.Tasks;

namespace ThreeWa.SshDrive.App.Updates
{
    internal sealed class UpdateSession : IDisposable
    {
        private readonly IUpdateBackend _backend;
        private readonly IUpdateLog _log;
        private Action _release;

        internal UpdateSession(
            IUpdateBackend backend,
            IUpdateLog log,
            Action release)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _release = release ?? throw new ArgumentNullException(nameof(release));
        }

        public async Task<UpdateCheckResult> CheckAsync(CancellationToken token)
        {
            ThrowIfDisposed();
            if (!_backend.IsInstalled || _backend.IsPortable)
            {
                return UpdateCheckResult.Disabled();
            }

            try
            {
                var package = await _backend.CheckAsync(token).ConfigureAwait(false);
                return package == null
                    ? UpdateCheckResult.UpToDate()
                    : UpdateCheckResult.Available(package);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return UpdateCheckResult.Failed("更新檢查已取消。");
            }
            catch (Exception exception)
            {
                LogFailure("UpdateCheck", exception);
                return UpdateCheckResult.Failed("無法檢查更新，請稍後再試。");
            }
        }

        public async Task<UpdateOperationResult> DownloadAsync(
            UpdatePackage package,
            Action<int> progress,
            CancellationToken token)
        {
            ThrowIfDisposed();
            try
            {
                await _backend.DownloadAsync(package, progress, token)
                    .ConfigureAwait(false);
                return UpdateOperationResult.Succeeded();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return UpdateOperationResult.Failed(
                    "更新下載已取消，已保留目前版本。");
            }
            catch (Exception exception)
            {
                LogFailure("UpdateDownload", exception);
                return UpdateOperationResult.Failed(
                    "更新下載或驗證失敗，已保留目前版本。");
            }
        }

        public UpdateOperationResult ApplyAndRestart(UpdatePackage package)
        {
            ThrowIfDisposed();
            try
            {
                _backend.ApplyAndRestart(package);
                return UpdateOperationResult.Succeeded();
            }
            catch (Exception exception)
            {
                LogFailure("UpdateApply", exception);
                return UpdateOperationResult.Failed(
                    "無法套用更新，已保留目前版本。");
            }
        }

        public void Dispose()
        {
            var release = Interlocked.Exchange(ref _release, null);
            release?.Invoke();
        }

        private void LogFailure(string operation, Exception exception)
        {
            _log.Failure(
                operation,
                exception.GetType().Name,
                exception.HResult);
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _release) == null)
            {
                throw new ObjectDisposedException(nameof(UpdateSession));
            }
        }
    }
}

using System;
using System.Threading;

namespace ThreeWa.SshDrive.App.Updates
{
    internal sealed class UpdateService
    {
        private readonly IUpdateBackend _backend;
        private readonly IUpdateLog _log;
        private int _activeSession;

        internal UpdateService(IUpdateBackend backend, IUpdateLog log)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        internal static UpdateService CreateDefault()
        {
            return new UpdateService(
                new VelopackUpdateBackend(),
                UpdateLog.Default);
        }

        internal bool TryBeginSession(out UpdateSession session)
        {
            session = null;
            if (Interlocked.CompareExchange(ref _activeSession, 1, 0) != 0)
            {
                return false;
            }

            session = new UpdateSession(
                _backend,
                _log,
                () => Interlocked.Exchange(ref _activeSession, 0));
            return true;
        }
    }
}

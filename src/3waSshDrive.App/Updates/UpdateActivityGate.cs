using System;
using System.Threading;
using System.Threading.Tasks;

namespace ThreeWa.SshDrive.App.Updates
{
    internal sealed class UpdateActivityGate
    {
        private readonly object _syncRoot = new object();
        private int _activeActivities;
        private bool _isPreparing;
        private TaskCompletionSource<bool> _idleSignal;

        internal bool IsPreparing
        {
            get
            {
                lock (_syncRoot)
                    return _isPreparing;
            }
        }

        internal bool TryBeginActivity(out IDisposable activity)
        {
            lock (_syncRoot)
            {
                activity = null;
                if (_isPreparing)
                    return false;

                if (_activeActivities == 0)
                {
                    _idleSignal = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                }

                _activeActivities++;
                activity = new Lease(EndActivity);
                return true;
            }
        }

        internal bool TryBeginPreparation(out IDisposable preparation)
        {
            lock (_syncRoot)
            {
                preparation = null;
                if (_isPreparing)
                    return false;

                _isPreparing = true;
                preparation = new Lease(EndPreparation);
                return true;
            }
        }

        internal Task WaitForIdleAsync()
        {
            lock (_syncRoot)
            {
                return _activeActivities == 0
                    ? Task.CompletedTask
                    : _idleSignal.Task;
            }
        }

        private void EndActivity()
        {
            TaskCompletionSource<bool> completed = null;
            lock (_syncRoot)
            {
                if (_activeActivities <= 0)
                    return;

                _activeActivities--;
                if (_activeActivities == 0)
                    completed = _idleSignal;
            }

            completed?.TrySetResult(true);
        }

        private void EndPreparation()
        {
            lock (_syncRoot)
                _isPreparing = false;
        }

        private sealed class Lease : IDisposable
        {
            private Action _release;

            internal Lease(Action release)
            {
                _release = release;
            }

            public void Dispose()
            {
                Interlocked.Exchange(ref _release, null)?.Invoke();
            }
        }
    }
}

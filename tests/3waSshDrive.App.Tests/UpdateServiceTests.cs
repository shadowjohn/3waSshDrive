using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ThreeWa.SshDrive.App.Updates;

namespace ThreeWa.SshDrive.App.Tests
{
    [TestClass]
    public sealed class UpdateServiceTests
    {
        [TestMethod]
        public void NotificationPolicy_BackgroundNonAvailableResultsStaySilent()
        {
            Assert.IsFalse(UpdateNotificationPolicy.ShouldShowMessage(
                manual: false,
                UpdateCheckKind.Failed));
            Assert.IsFalse(UpdateNotificationPolicy.ShouldShowMessage(
                manual: false,
                UpdateCheckKind.UpToDate));
            Assert.IsFalse(UpdateNotificationPolicy.ShouldShowMessage(
                manual: false,
                UpdateCheckKind.Disabled));
        }

        [TestMethod]
        public void NotificationPolicy_ManualNonBusyResultsShowMessage()
        {
            Assert.IsTrue(UpdateNotificationPolicy.ShouldShowMessage(
                manual: true,
                UpdateCheckKind.Failed));
            Assert.IsTrue(UpdateNotificationPolicy.ShouldShowMessage(
                manual: true,
                UpdateCheckKind.UpToDate));
            Assert.IsTrue(UpdateNotificationPolicy.ShouldShowMessage(
                manual: true,
                UpdateCheckKind.Disabled));
        }

        [TestMethod]
        public void NotificationPolicy_AvailableAlwaysShowsAndBusyNeverDoes()
        {
            Assert.IsTrue(UpdateNotificationPolicy.ShouldShowMessage(
                manual: false,
                UpdateCheckKind.Available));
            Assert.IsTrue(UpdateNotificationPolicy.ShouldShowMessage(
                manual: true,
                UpdateCheckKind.Available));
            Assert.IsFalse(UpdateNotificationPolicy.ShouldShowMessage(
                manual: false,
                UpdateCheckKind.Busy));
            Assert.IsFalse(UpdateNotificationPolicy.ShouldShowMessage(
                manual: true,
                UpdateCheckKind.Busy));
        }

        [TestMethod]
        public void TryBeginSession_RejectsConcurrentSession()
        {
            var service = new UpdateService(
                FakeBackend.Installed(),
                new RecordingUpdateLog());

            Assert.IsTrue(service.TryBeginSession(out var first));
            Assert.IsFalse(service.TryBeginSession(out var second));
            Assert.IsNull(second);
            first.Dispose();
            Assert.IsTrue(service.TryBeginSession(out var third));
            third.Dispose();
        }

        [DataTestMethod]
        [DataRow(false, false)]
        [DataRow(true, true)]
        public async Task CheckAsync_UnmanagedOrPortable_ReturnsDisabled(
            bool installed,
            bool portable)
        {
            var backend = FakeBackend.WithMode(installed, portable);
            var service = new UpdateService(backend, new RecordingUpdateLog());
            service.TryBeginSession(out var session);

            using (session)
            {
                var result = await session.CheckAsync(CancellationToken.None);

                Assert.AreEqual(UpdateCheckKind.Disabled, result.Kind);
                Assert.AreEqual(0, backend.CheckCount);
            }
        }

        [TestMethod]
        public async Task CheckAsync_NoUpdate_ReturnsUpToDate()
        {
            var backend = FakeBackend.Installed();
            var service = new UpdateService(backend, new RecordingUpdateLog());
            service.TryBeginSession(out var session);

            using (session)
            {
                var result = await session.CheckAsync(CancellationToken.None);

                Assert.AreEqual(UpdateCheckKind.UpToDate, result.Kind);
                Assert.AreEqual(1, backend.CheckCount);
            }
        }

        [TestMethod]
        public async Task CheckAsync_UpdateAvailable_ReturnsPackage()
        {
            var package = Package();
            var backend = FakeBackend.Available(package);
            var service = new UpdateService(backend, new RecordingUpdateLog());
            service.TryBeginSession(out var session);

            using (session)
            {
                var result = await session.CheckAsync(CancellationToken.None);

                Assert.AreEqual(UpdateCheckKind.Available, result.Kind);
                Assert.AreSame(package, result.Package);
            }
        }

        [TestMethod]
        public async Task CheckAsync_BackendFailure_ReturnsSafeFailureAndLogsOnce()
        {
            var exception = new InvalidOperationException(
                @"host.internal C:\keys\john.ppk token=abc");
            var log = new RecordingUpdateLog();
            var service = new UpdateService(
                FakeBackend.CheckThrowing(exception),
                log);
            service.TryBeginSession(out var session);

            using (session)
            {
                var result = await session.CheckAsync(CancellationToken.None);

                Assert.AreEqual(UpdateCheckKind.Failed, result.Kind);
                Assert.AreEqual("無法檢查更新，請稍後再試。", result.Message);
            }

            AssertFailure(
                log,
                "UpdateCheck",
                "InvalidOperationException",
                exception.HResult);
        }

        [TestMethod]
        public async Task CheckAsync_Cancellation_ReturnsSafeMessageWithoutLogging()
        {
            var source = new CancellationTokenSource();
            source.Cancel();
            var log = new RecordingUpdateLog();
            var service = new UpdateService(
                FakeBackend.CheckThrowing(new OperationCanceledException(source.Token)),
                log);
            service.TryBeginSession(out var session);

            using (session)
            {
                var result = await session.CheckAsync(source.Token);

                Assert.AreEqual(UpdateCheckKind.Failed, result.Kind);
                Assert.AreEqual("更新檢查已取消。", result.Message);
            }

            Assert.AreEqual(0, log.Failures.Count);
        }

        [TestMethod]
        public async Task DownloadAsync_BackendFailure_ReturnsSafeFailureAndNeverApplies()
        {
            var exception = new InvalidOperationException(
                @"host.internal C:\keys\john.ppk token=abc");
            var log = new RecordingUpdateLog();
            var backend = FakeBackend.DownloadThrowing(exception);
            var service = new UpdateService(backend, log);
            service.TryBeginSession(out var session);

            using (session)
            {
                var result = await session.DownloadAsync(
                    Package(),
                    _ => { },
                    CancellationToken.None);

                Assert.IsFalse(result.Success);
                Assert.AreEqual(
                    "更新下載或驗證失敗，已保留目前版本。",
                    result.Message);
            }

            Assert.AreEqual(1, backend.DownloadCount);
            Assert.AreEqual(0, backend.ApplyCount);
            AssertFailure(
                log,
                "UpdateDownload",
                "InvalidOperationException",
                exception.HResult);
        }

        [TestMethod]
        public async Task DownloadAsync_Success_ReturnsSuccess()
        {
            var backend = FakeBackend.Installed();
            var service = new UpdateService(backend, new RecordingUpdateLog());
            service.TryBeginSession(out var session);

            using (session)
            {
                var result = await session.DownloadAsync(
                    Package(),
                    _ => { },
                    CancellationToken.None);

                Assert.IsTrue(result.Success);
                Assert.AreEqual(1, backend.DownloadCount);
            }
        }

        [TestMethod]
        public void ApplyAndRestart_BackendFailure_ReturnsSafeFailure()
        {
            var exception = new InvalidOperationException(
                @"host.internal C:\keys\john.ppk token=abc");
            var log = new RecordingUpdateLog();
            var backend = FakeBackend.ApplyThrowing(exception);
            var service = new UpdateService(backend, log);
            service.TryBeginSession(out var session);

            using (session)
            {
                var result = session.ApplyAndRestart(Package());

                Assert.IsFalse(result.Success);
                Assert.AreEqual("無法套用更新，已保留目前版本。", result.Message);
            }

            AssertFailure(
                log,
                "UpdateApply",
                "InvalidOperationException",
                exception.HResult);
        }

        [TestMethod]
        public void ApplyAndRestart_WhenBackendReturns_ReturnsSuccess()
        {
            var backend = FakeBackend.Installed();
            var service = new UpdateService(backend, new RecordingUpdateLog());
            service.TryBeginSession(out var session);

            using (session)
            {
                var result = session.ApplyAndRestart(Package());

                Assert.IsTrue(result.Success);
                Assert.AreEqual(1, backend.ApplyCount);
            }
        }

        private static UpdatePackage Package()
        {
            return new UpdatePackage(
                "v2026.09.19.01",
                "v2026.09.19.02",
                "2026.919.2",
                "release notes",
                new object());
        }

        private static void AssertFailure(
            RecordingUpdateLog log,
            string operation,
            string exceptionType,
            int hresult)
        {
            Assert.AreEqual(1, log.Failures.Count);
            Assert.AreEqual(operation, log.Failures[0].Operation);
            Assert.AreEqual(exceptionType, log.Failures[0].ExceptionType);
            Assert.AreEqual(hresult, log.Failures[0].HResult);
        }

        private sealed class FakeBackend : IUpdateBackend
        {
            private UpdatePackage _package;
            private Exception _checkException;
            private Exception _downloadException;
            private Exception _applyException;

            private FakeBackend(bool installed, bool portable)
            {
                IsInstalled = installed;
                IsPortable = portable;
                CurrentPackageVersion = "2026.919.1";
            }

            public bool IsInstalled { get; }

            public bool IsPortable { get; }

            public string CurrentPackageVersion { get; }

            public int CheckCount { get; private set; }

            public int DownloadCount { get; private set; }

            public int ApplyCount { get; private set; }

            public static FakeBackend Installed()
            {
                return new FakeBackend(installed: true, portable: false);
            }

            public static FakeBackend WithMode(bool installed, bool portable)
            {
                return new FakeBackend(installed, portable);
            }

            public static FakeBackend Available(UpdatePackage package)
            {
                var backend = Installed();
                backend._package = package;
                return backend;
            }

            public static FakeBackend CheckThrowing(Exception exception)
            {
                var backend = Installed();
                backend._checkException = exception;
                return backend;
            }

            public static FakeBackend DownloadThrowing(Exception exception)
            {
                var backend = Installed();
                backend._downloadException = exception;
                return backend;
            }

            public static FakeBackend ApplyThrowing(Exception exception)
            {
                var backend = Installed();
                backend._applyException = exception;
                return backend;
            }

            public Task<UpdatePackage> CheckAsync(CancellationToken token)
            {
                CheckCount++;
                if (_checkException != null)
                {
                    throw _checkException;
                }

                return Task.FromResult(_package);
            }

            public Task DownloadAsync(
                UpdatePackage package,
                Action<int> progress,
                CancellationToken token)
            {
                DownloadCount++;
                if (_downloadException != null)
                {
                    throw _downloadException;
                }

                return Task.CompletedTask;
            }

            public void ApplyAndRestart(UpdatePackage package)
            {
                ApplyCount++;
                if (_applyException != null)
                {
                    throw _applyException;
                }
            }
        }

        private sealed class RecordingUpdateLog : IUpdateLog
        {
            public List<RecordedFailure> Failures { get; } =
                new List<RecordedFailure>();

            public void Failure(
                string operation,
                string exceptionType,
                int hresult)
            {
                Failures.Add(new RecordedFailure(
                    operation,
                    exceptionType,
                    hresult));
            }
        }

        private sealed class RecordedFailure
        {
            public RecordedFailure(
                string operation,
                string exceptionType,
                int hresult)
            {
                Operation = operation;
                ExceptionType = exceptionType;
                HResult = hresult;
            }

            public string Operation { get; }

            public string ExceptionType { get; }

            public int HResult { get; }
        }
    }
}

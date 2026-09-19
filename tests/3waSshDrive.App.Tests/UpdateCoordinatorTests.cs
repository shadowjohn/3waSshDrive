using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ThreeWa.SshDrive.App.Updates;

namespace ThreeWa.SshDrive.App.Tests
{
    [TestClass]
    public sealed class UpdateCoordinatorTests
    {
        [TestMethod]
        public void MainForm_ImplementsUpdatePreparation()
        {
            Assert.IsTrue(
                typeof(IUpdatePreparation).IsAssignableFrom(typeof(MainForm)));
        }

        [TestMethod]
        public async Task DownloadAndApply_Success_DownloadsThenPreparesThenApplies()
        {
            var events = new List<string>();
            var fixture = CoordinatorFixture.Success(events);

            var result = await fixture.Coordinator.DownloadAndApplyAsync(
                fixture.Session,
                fixture.Package,
                _ => { },
                CancellationToken.None);

            CollectionAssert.AreEqual(
                new[] { "download", "prepare", "apply" },
                events);
            Assert.AreEqual(UpdateApplyKind.RestartRequested, result.Kind);
        }

        [TestMethod]
        public async Task DownloadAndApply_ChecksumFailure_DoesNotPrepareOrApply()
        {
            var events = new List<string>();
            var fixture = CoordinatorFixture.DownloadFailure(
                events,
                new InvalidDataException("checksum mismatch"));

            var result = await fixture.RunAsync();

            CollectionAssert.AreEqual(new[] { "download" }, events);
            Assert.AreEqual(UpdateApplyKind.Failed, result.Kind);
        }

        [TestMethod]
        public async Task DownloadAndApply_UnmountFailure_BlocksApplyAndResumes()
        {
            var events = new List<string>();
            var fixture = CoordinatorFixture.PreparationFailure(
                events,
                canResumeImmediately: true);

            var result = await fixture.RunAsync();

            CollectionAssert.AreEqual(
                new[] { "download", "prepare", "resume" },
                events);
            Assert.AreEqual(UpdateApplyKind.Blocked, result.Kind);
        }

        [TestMethod]
        public async Task DownloadAndApply_Timeout_DoesNotResumeWhileCleanupStillRuns()
        {
            var events = new List<string>();
            var fixture = CoordinatorFixture.PreparationFailure(
                events,
                canResumeImmediately: false);

            var result = await fixture.RunAsync();

            CollectionAssert.AreEqual(new[] { "download", "prepare" }, events);
            Assert.AreEqual(UpdateApplyKind.Blocked, result.Kind);
        }

        [TestMethod]
        public async Task DownloadAndApply_ApplyFailure_ResumesAndReturnsFailure()
        {
            var events = new List<string>();
            var fixture = CoordinatorFixture.ApplyFailure(events);

            var result = await fixture.RunAsync();

            CollectionAssert.AreEqual(
                new[] { "download", "prepare", "apply", "resume" },
                events);
            Assert.AreEqual(UpdateApplyKind.Failed, result.Kind);
        }

        private sealed class CoordinatorFixture
        {
            private CoordinatorFixture(
                UpdateCoordinator coordinator,
                UpdateSession session,
                UpdatePackage package)
            {
                Coordinator = coordinator;
                Session = session;
                Package = package;
            }

            public UpdateCoordinator Coordinator { get; }

            public UpdateSession Session { get; }

            public UpdatePackage Package { get; }

            public static CoordinatorFixture Success(List<string> events)
            {
                return Create(
                    events,
                    UpdatePreparationResult.Ready(),
                    downloadException: null,
                    applyException: null);
            }

            public static CoordinatorFixture DownloadFailure(
                List<string> events,
                Exception exception)
            {
                return Create(
                    events,
                    UpdatePreparationResult.Ready(),
                    downloadException: exception,
                    applyException: null);
            }

            public static CoordinatorFixture PreparationFailure(
                List<string> events,
                bool canResumeImmediately)
            {
                return Create(
                    events,
                    UpdatePreparationResult.Blocked(
                        "preparation blocked",
                        canResumeImmediately),
                    downloadException: null,
                    applyException: null);
            }

            public static CoordinatorFixture ApplyFailure(List<string> events)
            {
                return Create(
                    events,
                    UpdatePreparationResult.Ready(),
                    downloadException: null,
                    applyException: new InvalidOperationException("apply failed"));
            }

            public Task<UpdateApplyResult> RunAsync()
            {
                return Coordinator.DownloadAndApplyAsync(
                    Session,
                    Package,
                    _ => { },
                    CancellationToken.None);
            }

            private static CoordinatorFixture Create(
                List<string> events,
                UpdatePreparationResult preparationResult,
                Exception downloadException,
                Exception applyException)
            {
                var backend = new RecordingBackend(
                    events,
                    downloadException,
                    applyException);
                var service = new UpdateService(backend, new NoOpUpdateLog());
                Assert.IsTrue(service.TryBeginSession(out var session));
                var preparation = new RecordingPreparation(
                    events,
                    preparationResult);
                return new CoordinatorFixture(
                    new UpdateCoordinator(preparation),
                    session,
                    new UpdatePackage(
                        "v2026.09.19.01",
                        "v2026.09.19.02",
                        "2026.919.2",
                        "release notes",
                        new object()));
            }
        }

        private sealed class RecordingPreparation : IUpdatePreparation
        {
            private readonly List<string> _events;
            private readonly UpdatePreparationResult _result;

            public RecordingPreparation(
                List<string> events,
                UpdatePreparationResult result)
            {
                _events = events;
                _result = result;
            }

            public Task<UpdatePreparationResult> PrepareAsync(TimeSpan timeout)
            {
                Assert.AreEqual(TimeSpan.FromSeconds(30), timeout);
                _events.Add("prepare");
                return Task.FromResult(_result);
            }

            public void ResumeAfterFailure()
            {
                _events.Add("resume");
            }
        }

        private sealed class RecordingBackend : IUpdateBackend
        {
            private readonly List<string> _events;
            private readonly Exception _downloadException;
            private readonly Exception _applyException;

            public RecordingBackend(
                List<string> events,
                Exception downloadException,
                Exception applyException)
            {
                _events = events;
                _downloadException = downloadException;
                _applyException = applyException;
            }

            public bool IsInstalled => true;

            public bool IsPortable => false;

            public string CurrentPackageVersion => "2026.919.1";

            public Task<UpdatePackage> CheckAsync(CancellationToken token)
            {
                throw new NotSupportedException();
            }

            public Task DownloadAsync(
                UpdatePackage package,
                Action<int> progress,
                CancellationToken token)
            {
                _events.Add("download");
                if (_downloadException != null)
                {
                    throw _downloadException;
                }

                return Task.CompletedTask;
            }

            public void ApplyAndRestart(UpdatePackage package)
            {
                _events.Add("apply");
                if (_applyException != null)
                {
                    throw _applyException;
                }
            }
        }

        private sealed class NoOpUpdateLog : IUpdateLog
        {
            public void Failure(
                string operation,
                string exceptionType,
                int hresult)
            {
            }
        }
    }
}

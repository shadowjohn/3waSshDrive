using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ThreeWa.SshDrive.App.Updates;

namespace ThreeWa.SshDrive.App.Tests
{
    [TestClass]
    public sealed class VelopackUpdateBackendTests
    {
        [TestMethod]
        public async Task CheckAsync_MapsPackageVersionToDisplayTagAndNotes()
        {
            var fake = FakeVelopackClient.Installed(
                current: "2026.919.1",
                target: "2026.919.2",
                notes: "修正更新流程");
            var backend = new VelopackUpdateBackend(fake);

            var package = await backend.CheckAsync(CancellationToken.None);

            Assert.AreEqual("v2026.09.19.01", package.CurrentDisplayVersion);
            Assert.AreEqual("v2026.09.19.02", package.TargetDisplayVersion);
            Assert.AreEqual("2026.919.2", package.PackageVersion);
            Assert.AreEqual("修正更新流程", package.ReleaseNotesMarkdown);
            Assert.AreEqual(1, fake.CheckCount);
        }

        [TestMethod]
        public async Task CheckAsync_NoRemoteUpdate_ReturnsNull()
        {
            var backend = new VelopackUpdateBackend(FakeVelopackClient.UpToDate());

            Assert.IsNull(await backend.CheckAsync(CancellationToken.None));
        }

        [TestMethod]
        public void ModeProperties_DelegateToClient()
        {
            var fake = FakeVelopackClient.WithMode(
                installed: false,
                portable: true,
                current: "2026.919.1");
            var backend = new VelopackUpdateBackend(fake);

            Assert.IsFalse(backend.IsInstalled);
            Assert.IsTrue(backend.IsPortable);
            Assert.AreEqual("2026.919.1", backend.CurrentPackageVersion);
        }

        [TestMethod]
        public async Task DownloadAndApply_KeepNativeTokenInsideAdapterBoundary()
        {
            var fake = FakeVelopackClient.Installed(
                current: "2026.919.1",
                target: "2026.919.2",
                notes: null);
            var backend = new VelopackUpdateBackend(fake);
            var package = await backend.CheckAsync(CancellationToken.None);
            var progressValue = -1;

            await backend.DownloadAsync(
                package,
                value => progressValue = value,
                CancellationToken.None);
            backend.ApplyAndRestart(package);

            Assert.AreSame(fake.NativeToken, fake.DownloadedToken);
            Assert.AreSame(fake.NativeToken, fake.AppliedToken);
            Assert.AreEqual(42, progressValue);
        }

        private sealed class FakeVelopackClient : IVelopackClient
        {
            private VelopackUpdateData _update;

            private FakeVelopackClient(
                bool installed,
                bool portable,
                string currentVersion,
                VelopackUpdateData update)
            {
                IsInstalled = installed;
                IsPortable = portable;
                CurrentVersion = currentVersion;
                _update = update;
            }

            public bool IsInstalled { get; }

            public bool IsPortable { get; }

            public string CurrentVersion { get; }

            public int CheckCount { get; private set; }

            public object NativeToken { get; private set; }

            public object DownloadedToken { get; private set; }

            public object AppliedToken { get; private set; }

            public static FakeVelopackClient Installed(
                string current,
                string target,
                string notes)
            {
                var token = new object();
                var client = new FakeVelopackClient(
                    installed: true,
                    portable: false,
                    currentVersion: current,
                    update: new VelopackUpdateData(target, notes, token));
                client.NativeToken = token;
                return client;
            }

            public static FakeVelopackClient UpToDate()
            {
                return new FakeVelopackClient(
                    installed: true,
                    portable: false,
                    currentVersion: "2026.919.1",
                    update: null);
            }

            public static FakeVelopackClient WithMode(
                bool installed,
                bool portable,
                string current)
            {
                return new FakeVelopackClient(
                    installed,
                    portable,
                    current,
                    update: null);
            }

            public Task<VelopackUpdateData> CheckAsync()
            {
                CheckCount++;
                return Task.FromResult(_update);
            }

            public Task DownloadAsync(
                object nativeToken,
                Action<int> progress,
                CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                DownloadedToken = nativeToken;
                progress(42);
                return Task.CompletedTask;
            }

            public void ApplyAndRestart(object nativeToken)
            {
                AppliedToken = nativeToken;
            }
        }
    }
}

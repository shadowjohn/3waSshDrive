using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ThreeWa.SshDrive.App.Updates;
using ThreeWa.SshDrive.Core.Models;
using ThreeWa.SshDrive.Core.Profiles;
using ThreeWa.SshDrive.Core.Remote;
using ThreeWa.SshDrive.FileSystem.Mounting;
using ThreeWa.SshDrive.Sftp;

namespace ThreeWa.SshDrive.App.Tests
{
    [TestClass]
    public sealed class MountConcurrencyTests
    {
        [TestMethod]
        public void SlowMount_KeepsProfileSelectionAndOtherMountAvailable()
        {
            RunSta(() =>
            {
                using (var release = new ManualResetEventSlim())
                using (var entered = new ManualResetEventSlim())
                {
                    var remote = new TestRemote(() => { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); });
                    using (var form = CreateForm(remote))
                    {
                        var profile = Profile("slow", "T:");
                        Call(form, "WriteForm", profile);
                        Click(Field<Button>(form, "_mountButton"));
                        try
                        {
                            PumpUntil(() => entered.IsSet);
                            Assert.IsTrue(Field<ComboBox>(form, "_profiles").Enabled, "A pending mount disabled every profile.");
                            Call(form, "WriteForm", Profile("healthy", "W:"));
                            Assert.IsTrue(Field<Button>(form, "_mountButton").Enabled, "A pending T: mount disabled W:.");
                        }
                        finally { release.Set(); PumpUntil(() => remote.Connected); Application.DoEvents(); }
                    }
                }
            });
        }

        [DataTestMethod]
        [DataRow("MountAllProfilesAsync")]
        [DataRow("AutoMountProfilesOnStartupAsync")]
        public void FleetMount_RunsHealthyProfileWhileSlowProfileWaits_AndLimitsConcurrency(string entryPoint)
        {
            RunSta(() =>
            {
                using (var release = new ManualResetEventSlim())
                using (var aEntered = new ManualResetEventSlim())
                using (var bEntered = new ManualResetEventSlim())
                using (var cEntered = new ManualResetEventSlim())
                {
                    var a = new TestRemote(() => { aEntered.Set(); release.Wait(TimeSpan.FromSeconds(10)); });
                    var b = new TestRemote(() => { bEntered.Set(); release.Wait(TimeSpan.FromSeconds(10)); });
                    var c = new TestRemote(() => cEntered.Set());
                    using (var form = CreateForm(p => p.Name == "A" ? a : p.Name == "B" ? b : c))
                    {
                        var profiles = new[] { Profile("A", "T:"), Profile("B", "W:"), Profile("C", "V:") };
                        foreach (var p in profiles) { p.AutoMountOnStartup = true; p.AuthenticationMode = AuthenticationMode.PrivateKey; p.PrivateKeyPath = "isolated-unused-key"; }
                        Field<List<DriveProfile>>(form, "_profileItems").AddRange(profiles);
                        var batch = (Task)Call(form, entryPoint);
                        try
                        {
                            PumpUntil(() => aEntered.IsSet);
                            PumpUntil(() => bEntered.IsSet);
                            Assert.IsFalse(cEntered.IsSet, "More than two mount workers ran concurrently.");
                            Assert.IsTrue(Field<ComboBox>(form, "_profiles").Enabled);
                        }
                        finally { release.Set(); PumpUntil(() => batch.IsCompleted); }
                        Assert.IsTrue(cEntered.IsSet);
                        Assert.AreEqual(3, Field<Dictionary<string, MountedDrive>>(form, "_mountedDrives").Count);
                    }
                }
            });
        }

        [TestMethod]
        public void SlowMount_UsesCapturedProfile_AndPreservesOtherDraftWithLiveUiHeartbeat()
        {
            RunSta(() =>
            {
                using (var release = new ManualResetEventSlim())
                using (var entered = new ManualResetEventSlim())
                using (var timer = new System.Windows.Forms.Timer { Interval = 15 })
                {
                    DriveProfile captured = null;
                    var remote = new TestRemote(() => { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); });
                    using (var form = CreateForm(p => { captured = p; return remote; }))
                    {
                        Call(form, "WriteForm", Profile("A", "T:"));
                        var operation = (Task)Call(form, "MountAsync");
                        var heartbeats = 0; timer.Tick += (s, e) => heartbeats++; timer.Start();
                        try
                        {
                            PumpUntil(() => entered.IsSet);
                            Call(form, "WriteForm", Profile("B draft", "W:"));
                            PumpUntil(() => heartbeats >= 3);
                        }
                        finally { release.Set(); PumpUntil(() => operation.IsCompleted); timer.Stop(); }
                        Assert.AreEqual("A", captured.Name);
                        Assert.AreEqual("T:", captured.DriveLetter);
                        Assert.AreEqual("B draft", Field<TextBox>(form, "_name").Text);
                        Assert.AreEqual("W:", ((DriveProfile)Call(form, "ReadForm")).DriveLetter);
                        Assert.AreEqual("A", Field<Dictionary<string, MountedDrive>>(form, "_mountedDrives")["T:"].ProfileName);
                    }
                }
            });
        }

        [TestMethod]
        public void CancelAndDuplicateClick_KeepReservationUntilLateConnectionCleansUp()
        {
            RunSta(() =>
            {
                using (var release = new ManualResetEventSlim())
                using (var entered = new ManualResetEventSlim())
                {
                    var creations = 0;
                    var remote = new TestRemote(() => { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); });
                    using (var form = CreateForm(p => { creations++; return remote; }))
                    {
                        Call(form, "WriteForm", Profile("A", "T:"));
                        var operation = (Task)Call(form, "MountAsync");
                        try
                        {
                            PumpUntil(() => entered.IsSet);
                            Call(form, "CancelSelectedOperation");
                            var duplicate = (Task)Call(form, "MountAsync");
                            Assert.IsTrue(duplicate.IsCompleted);
                            Assert.AreEqual(1, creations);
                            Assert.IsFalse(operation.IsCompleted, "Cancellation abandoned a live worker.");
                            Assert.IsFalse(Field<Button>(form, "_mountButton").Enabled);
                        }
                        finally { release.Set(); PumpUntil(() => operation.IsCompleted); }
                        Assert.IsTrue(remote.Disposed);
                        Assert.AreEqual(0, Field<Dictionary<string, MountedDrive>>(form, "_mountedDrives").Count);
                        Assert.IsTrue(Field<Button>(form, "_mountButton").Enabled);
                    }
                }
            });
        }

        [TestMethod]
        public void UpdatePreparation_DoesNotReleaseCancelledWorkersOrPermitDuplicateMount()
        {
            RunSta(() =>
            {
                using (var release = new ManualResetEventSlim())
                using (var entered = new ManualResetEventSlim())
                {
                    var remote = new TestRemote(() => { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); });
                    using (var form = CreateForm(remote))
                    {
                        Call(form, "WriteForm", Profile("A", "T:"));
                        var operation = (Task)Call(form, "MountAsync");
                        try
                        {
                            PumpUntil(() => entered.IsSet);
                            var preparation = ((IUpdatePreparation)form).PrepareAsync(TimeSpan.FromMilliseconds(30));
                            PumpUntil(() => preparation.IsCompleted);
                            Assert.IsFalse(preparation.Result.Success);
                            Assert.IsFalse(preparation.Result.CanResumeImmediately);
                            Assert.IsTrue(Field<bool>(form, "_isApplyingUpdate"));
                            Assert.IsFalse(operation.IsCompleted);
                        }
                        finally { release.Set(); PumpUntil(() => operation.IsCompleted); }
                        PumpUntil(() => !Field<bool>(form, "_isApplyingUpdate"));
                        Assert.IsTrue(remote.Disposed);
                        Assert.AreEqual(0, Field<Dictionary<string, MountedDrive>>(form, "_mountedDrives").Count);
                    }
                }
            });
        }

        [TestMethod]
        public void SlowConnections_DoNotDelayHealthyUnmount()
        {
            RunSta(() =>
            {
                using (var release = new ManualResetEventSlim())
                using (var aEntered = new ManualResetEventSlim())
                using (var cEntered = new ManualResetEventSlim())
                {
                    var healthy = new TestRemote(() => { });
                    var a = new TestRemote(() => { aEntered.Set(); release.Wait(TimeSpan.FromSeconds(10)); });
                    var c = new TestRemote(() => { cEntered.Set(); release.Wait(TimeSpan.FromSeconds(10)); });
                    using (var form = CreateForm(p => p.Name == "B" ? healthy : p.Name == "A" ? a : c))
                    {
                        Call(form, "WriteForm", Profile("B", "W:"));
                        var bTask = (Task)Call(form, "MountAsync"); PumpUntil(() => bTask.IsCompleted);
                        Call(form, "NewProfile"); Call(form, "WriteForm", Profile("A", "T:"));
                        var aTask = (Task)Call(form, "MountAsync");
                        Call(form, "NewProfile"); Call(form, "WriteForm", Profile("C", "V:"));
                        var cTask = (Task)Call(form, "MountAsync");
                        Task unmount = null;
                        try
                        {
                            PumpUntil(() => aEntered.IsSet && cEntered.IsSet);
                            Call(form, "NewProfile"); Call(form, "WriteForm", Profile("B", "W:"));
                            unmount = (Task)Call(form, "UnmountAsync");
                            var until = DateTime.UtcNow.AddMilliseconds(400);
                            while (!unmount.IsCompleted && DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(5); }
                            Assert.IsTrue(unmount.IsCompleted, "Healthy unmount queued behind two unrelated connections.");
                            Assert.IsTrue(healthy.Disposed);
                        }
                        finally { release.Set(); PumpUntil(() => aTask.IsCompleted && cTask.IsCompleted && (unmount == null || unmount.IsCompleted)); }
                    }
                }
            });
        }

        [TestMethod]
        public void Reconnect_SlowFailureDoesNotDelayHealthyDrive_AndTimerDoesNotDuplicate()
        {
            RunSta(() =>
            {
                using (var release = new ManualResetEventSlim())
                using (var entered = new ManualResetEventSlim())
                {
                    var aCalls = 0; var bCalls = 0;
                    var a = new TestRemote(() => { if (Interlocked.Increment(ref aCalls) > 1) { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); throw new IOException("isolated unreachable"); } });
                    var b = new TestRemote(() => Interlocked.Increment(ref bCalls));
                    using (var form = CreateForm(p => p.Name == "A" ? a : b))
                    {
                        Field<List<DriveProfile>>(form, "_profileItems").AddRange(new[] { Profile("A", "T:"), Profile("B", "W:") });
                        var mounts = (Task)Call(form, "MountAllProfilesAsync"); PumpUntil(() => mounts.IsCompleted);
                        a.Connected = false; b.Connected = false;
                        var reconnect = (Task)Call(form, "CheckAndReconnectDrivesAsync");
                        try
                        {
                            PumpUntil(() => entered.IsSet && b.Connected);
                            var duplicate = (Task)Call(form, "CheckAndReconnectDrivesAsync");
                            PumpUntil(() => duplicate.IsCompleted);
                            Assert.AreEqual(2, aCalls);
                            Assert.AreEqual(2, bCalls);
                            Assert.IsFalse(reconnect.IsCompleted);
                            Assert.IsTrue(Field<ComboBox>(form, "_profiles").Enabled);
                        }
                        finally { release.Set(); PumpUntil(() => reconnect.IsCompleted); }
                        Assert.IsTrue(b.IsConnected);
                        Assert.IsFalse(a.IsConnected);
                        var grid = Field<DataGridView>(form, "_profileGrid");
                        Assert.IsTrue(grid.Rows[0].Cells["Status"].Value.ToString().Contains("isolated unreachable"), "Background failure disappeared after the worker ended.");
                        var cooldown = (Task)Call(form, "CheckAndReconnectDrivesAsync"); PumpUntil(() => cooldown.IsCompleted);
                        Assert.AreEqual(2, aCalls, "Failed reconnect immediately retried without backoff.");
                    }
                }
            });
        }

        internal static MainForm CreateForm(TestRemote remote)
            => CreateForm(p => remote);

        internal static MainForm CreateForm(Func<DriveProfile, IRemoteFileSystem> factory, ISshConnectionProbe probe = null)
        {
            var store = new ProfileStore(Path.Combine(Path.GetTempPath(), "sshdrive-tests-" + Guid.NewGuid(), "profiles.json"));
            var form = new MainForm(store, probe ?? new SshConnectionProbe(), new MountManager(factory, fs => new TestHost()), new UpdateService(new TestUpdater(), new TestLog()), () => new HashSet<string>(), (operation, error) => { });
            var handle = form.Handle;
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            return form;
        }

        [TestMethod]
        public void TestAndMount_RenameAndSelectionChange_StillMountsTestedSnapshot()
        {
            RunSta(() =>
            {
                using (var release = new ManualResetEventSlim())
                using (var entered = new ManualResetEventSlim())
                {
                    DriveProfile mounted = null;
                    var probe = new TestProbe(p => { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); });
                    using (var form = CreateForm(p => { mounted = p; return new TestRemote(() => { }); }, probe))
                    {
                        Field<List<DriveProfile>>(form, "_profileItems").AddRange(new[] { Profile("A", "T:"), Profile("B", "W:") });
                        Call(form, "RefreshProfileSelector", "A");
                        Field<TextBox>(form, "_name").Text = "renamed A";
                        var operation = (Task)Call(form, "TestAndMountAsync");
                        try
                        {
                            PumpUntil(() => entered.IsSet);
                            Call(form, "RefreshProfileSelector", "B");
                            Field<TextBox>(form, "_host").Text = "unsaved-b.invalid";
                        }
                        finally { release.Set(); PumpUntil(() => operation.IsCompleted); }
                        Assert.IsNotNull(mounted, "Renamed profile was tested but never mounted.");
                        Assert.AreEqual("renamed A", probe.ProfileName);
                        Assert.AreEqual("renamed A", mounted.Name);
                        Assert.AreEqual("SHA256:test-captured", mounted.HostKeyFingerprintSha256);
                        Assert.AreEqual("B", Field<TextBox>(form, "_name").Text);
                        Assert.AreEqual("unsaved-b.invalid", Field<TextBox>(form, "_host").Text);
                        Assert.AreEqual("B", Field<string>(form, "_selectedProfileName"));
                    }
                }
            });
        }

        [TestMethod]
        public void ExitAfterBusyUpdateGate_CanBeRetriedAfterGateResumes()
        {
            RunSta(() =>
            {
                using (var form = CreateForm(new TestRemote(() => { })))
                {
                    var gate = Field<UpdateActivityGate>(form, "_updateActivityGate");
                    Assert.IsTrue(gate.TryBeginPreparation(out var preparation));
                    var blocked = (Task)Call(form, "ExitApplicationAsync");
                    Assert.IsTrue(blocked.IsCompleted);
                    Assert.IsFalse(form.IsDisposed);
                    preparation.Dispose();
                    var retry = (Task)Call(form, "ExitApplicationAsync");
                    PumpUntil(() => retry.IsCompleted);
                    Assert.IsTrue(form.IsDisposed, "QUIT was stuck on the earlier completed task.");
                }
            });
        }

        [TestMethod]
        public void Exit_CancelsAndWaitsForLateConnectionCleanupBeforeClosingUi()
        {
            RunSta(() =>
            {
                using (var release = new ManualResetEventSlim())
                using (var entered = new ManualResetEventSlim())
                {
                    var remote = new TestRemote(() => { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); });
                    using (var form = CreateForm(remote))
                    {
                        Call(form, "WriteForm", Profile("A", "T:"));
                        var mount = (Task)Call(form, "MountAsync");
                        Task exit = null;
                        try
                        {
                            PumpUntil(() => entered.IsSet);
                            exit = (Task)Call(form, "ExitApplicationAsync");
                            Assert.IsFalse(exit.IsCompleted);
                            Assert.IsFalse(form.IsDisposed);
                        }
                        finally { release.Set(); PumpUntil(() => mount.IsCompleted && (exit == null || exit.IsCompleted)); }
                        Assert.IsTrue(remote.Disposed);
                        Assert.IsTrue(form.IsDisposed);
                    }
                }
            });
        }

        private sealed class TestProbe : ISshConnectionProbe
        {
            private readonly Action<DriveProfile> _probe;
            public string ProfileName;
            internal TestProbe(Action<DriveProfile> probe) { _probe = probe; }
            public ConnectionProbeResult Probe(DriveProfile p, CancellationToken token)
            {
                ProfileName = p.Name; _probe(p); token.ThrowIfCancellationRequested();
                return new ConnectionProbeResult("SHA256:test-captured", p.RemoteRoot);
            }
        }

        internal static DriveProfile Profile(string name, string drive) => new DriveProfile { Name = name, Host = "isolated.invalid", Username = "fake", DriveLetter = drive, AuthenticationMode = AuthenticationMode.Password, Password = "fake", HostKeyFingerprintSha256 = "SHA256:fake" };
        internal static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        internal static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
        internal static void Click(Button button) => typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(button, new object[] { EventArgs.Empty });
        internal static void PumpUntil(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(12);
            while (!condition() && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(5); }
            Assert.IsTrue(condition(), "Isolated operation did not finish in time.");
            Application.DoEvents();
        }
        internal static void RunSta(Action action)
        {
            Exception failure = null;
            var thread = new Thread(() => { try { action(); } catch (Exception e) { failure = e; } });
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(30)), "UI test did not finish.");
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
        internal sealed class TestHost : IFileSystemHost
        {
            public int Mount(string point) => 0;
            public void Unmount() { }
            public void Dispose() { }
        }
        private sealed class TestUpdater : IUpdateBackend
        {
            public bool IsInstalled => false;
            public bool IsPortable => true;
            public string CurrentPackageVersion => "2026.922.1";
            public Task<UpdatePackage> CheckAsync(CancellationToken token) => Task.FromResult<UpdatePackage>(null);
            public Task DownloadAsync(UpdatePackage p, Action<int> progress, CancellationToken token) => throw new NotSupportedException();
            public void ApplyAndRestart(UpdatePackage p) => throw new NotSupportedException();
        }
        private sealed class TestLog : IUpdateLog
        {
            public void Failure(string operation, string exceptionType, int hresult) { }
        }
        internal sealed class TestRemote : IRemoteFileSystem
        {
            private readonly Action _connect;
            public volatile bool Connected;
            public volatile bool Disposed;
            public TestRemote(Action connect) { _connect = connect; }
            public bool IsConnected => Connected;
            public object SyncRoot { get; } = new object();
            public void Connect() { _connect(); Connected = true; }
            public void Dispose() { Connected = false; Disposed = true; }
            public RemoteEntry GetEntry(string path) => new RemoteEntry("root", path, true, 0, DateTime.UtcNow, DateTime.UtcNow);
            public IReadOnlyList<RemoteEntry> ListDirectory(string p) => Array.Empty<RemoteEntry>();
            public Stream OpenRead(string p) => throw new NotSupportedException();
            public Stream OpenFile(string p, FileMode m, FileAccess a) => throw new NotSupportedException();
            public void CreateDirectory(string p) => throw new NotSupportedException();
            public void DeleteFile(string p) => throw new NotSupportedException();
            public void DeleteDirectory(string p) => throw new NotSupportedException();
            public void Rename(string a, string b, bool replace) => throw new NotSupportedException();
            public RemoteVolumeInfo GetVolumeInfo(string p = null) => throw new NotSupportedException();
        }
    }
}

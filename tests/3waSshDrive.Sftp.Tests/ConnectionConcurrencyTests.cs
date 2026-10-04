using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ThreeWa.SshDrive.Core.Models;
using ThreeWa.SshDrive.Sftp.Security;

namespace ThreeWa.SshDrive.Sftp.Tests
{
    [TestClass]
    public sealed class ConnectionConcurrencyTests
    {
        [TestMethod]
        public void StatusRead_DoesNotWaitForSlowConnectionLifecycle()
        {
            using (var remote = new SshNetRemoteFileSystem(new DriveProfile(), HostKeyPolicy.ForCapture()))
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                var gate = typeof(SshNetRemoteFileSystem).GetField("_lifecycleLock", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(remote);
                var worker = Task.Run(() => { lock (gate) { entered.Set(); release.Wait(TimeSpan.FromSeconds(3)); } });
                Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(2)));
                var status = Task.Run(() => remote.IsConnected);
                try { Assert.IsTrue(status.Wait(TimeSpan.FromMilliseconds(300)), "Status polling waited on a network lifecycle lock."); }
                finally { release.Set(); worker.Wait(); status.Wait(); }
                Assert.IsFalse(status.Result);
            }
        }

        [TestMethod]
        public async Task SlowLoopbackHandshake_CancelsUnderlyingConnectAndCleansUp()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var profile = new DriveProfile { Host = "127.0.0.1", Port = ((IPEndPoint)listener.LocalEndpoint).Port, Username = "fake", AuthenticationMode = AuthenticationMode.Password, Password = "fake" };
                using (var remote = new SshNetRemoteFileSystem(profile, HostKeyPolicy.ForCapture()))
                using (var cancellation = new CancellationTokenSource())
                {
                    var accept = listener.AcceptTcpClientAsync();
                    var connection = Task.Run(() => remote.Connect(cancellation.Token));
                    Assert.AreEqual(accept, await Task.WhenAny(accept, Task.Delay(3000)), "Local connection did not reach the isolated listener.");
                    using (var socket = await accept)
                    {
                        // Accepted socket deliberately sends no SSH banner. No authentication takes place.
                        cancellation.Cancel();
                        Assert.AreEqual(connection, await Task.WhenAny(connection, Task.Delay(3000)), "Cancellation did not terminate underlying SSH connect.");
                        var cancelled = false;
                        try { await connection; }
                        catch (OperationCanceledException) { cancelled = true; }
                        Assert.IsTrue(cancelled, "Connect must preserve cancellation rather than translate it into a generic error.");
                        Assert.IsFalse(remote.IsConnected);
                        Assert.IsFalse(remote.IsConnecting);
                    }
                }
            }
            finally { listener.Stop(); }
        }

        [TestMethod]
        public void CancelWhileAnotherConnectionOwnsLock_DoesNotWaitOrStartAnotherAttempt()
        {
            using (var remote = new SshNetRemoteFileSystem(new DriveProfile(), HostKeyPolicy.ForCapture()))
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            using (var cancellation = new CancellationTokenSource())
            {
                var gate = typeof(SshNetRemoteFileSystem).GetField("_lifecycleLock", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(remote);
                var worker = Task.Run(() => { lock (gate) { entered.Set(); release.Wait(TimeSpan.FromSeconds(3)); } });
                Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(2)));
                var connection = Task.Run(() => Assert.ThrowsException<OperationCanceledException>(() => remote.Connect(cancellation.Token)));
                cancellation.Cancel();
                try { Assert.IsTrue(connection.Wait(TimeSpan.FromMilliseconds(500))); }
                finally { release.Set(); worker.Wait(); }
            }
        }
    }
}

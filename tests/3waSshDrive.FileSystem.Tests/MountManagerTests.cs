using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ThreeWa.SshDrive.Core.Models;
using ThreeWa.SshDrive.Core.Remote;
using ThreeWa.SshDrive.FileSystem.Mounting;

namespace ThreeWa.SshDrive.FileSystem.Tests
{
    [TestClass]
    public sealed class MountManagerTests
    {
        [TestMethod]
        public void Mount_InvalidProfileDoesNotCreateRemoteConnection()
        {
            var factoryCalls = 0;
            var manager = new MountManager(
                profile =>
                {
                    factoryCalls++;
                    return new TrackingRemoteFileSystem(new List<string>());
                },
                fileSystem => new TrackingFileSystemHost(new List<string>(), 0));
            var profile = ValidProfile();
            profile.HostKeyFingerprintSha256 = "";

            Assert.ThrowsException<InvalidOperationException>(
                () => manager.Mount(profile));
            Assert.AreEqual(0, factoryCalls);
        }

        [TestMethod]
        public void Mount_FailedWinFspMountDisposesBothOwnedResources()
        {
            var events = new List<string>();
            var remote = new TrackingRemoteFileSystem(events);
            var host = new TrackingFileSystemHost(
                events,
                unchecked((int)0xc0000001));
            var manager = new MountManager(
                profile => remote,
                fileSystem => host);

            var exception = Assert.ThrowsException<MountException>(
                () => manager.Mount(ValidProfile()));

            Assert.AreEqual(unchecked((int)0xc0000001), exception.Status);
            CollectionAssert.Contains(events, "host.dispose");
            CollectionAssert.Contains(events, "remote.dispose");
        }

        [TestMethod]
        public void Dispose_UnmountsHostBeforeClosingRemoteConnection()
        {
            var events = new List<string>();
            var remote = new TrackingRemoteFileSystem(events);
            var host = new TrackingFileSystemHost(events, 0);
            var manager = new MountManager(
                profile => remote,
                fileSystem => host);

            var mounted = manager.Mount(ValidProfile());
            mounted.Dispose();
            mounted.Dispose();

            CollectionAssert.AreEqual(new[]
            {
                "remote.connect",
                "host.mount:Z:",
                "host.unmount",
                "host.dispose",
                "remote.dispose"
            }, events);
        }

        private static DriveProfile ValidProfile()
        {
            return new DriveProfile
            {
                Name = "AIHub-5090",
                Host = "192.168.1.100",
                Port = 22,
                Username = "feather",
                RemoteRoot = "/home/feather",
                DriveLetter = "Z:",
                PrivateKeyPath = @"C:\keys\id_ed25519",
                HostKeyFingerprintSha256 = "SHA256:abc123"
            };
        }

        private sealed class TrackingRemoteFileSystem : IRemoteFileSystem
        {
            private readonly IList<string> _events;

            public TrackingRemoteFileSystem(IList<string> events)
            {
                _events = events;
            }

            public bool IsConnected { get; private set; }

            public void Connect()
            {
                IsConnected = true;
                _events.Add("remote.connect");
            }

            public RemoteEntry GetEntry(string path)
            {
                throw new NotSupportedException();
            }

            public IReadOnlyList<RemoteEntry> ListDirectory(string path)
            {
                throw new NotSupportedException();
            }

            public Stream OpenRead(string path)
            {
                throw new NotSupportedException();
            }

            public void Dispose()
            {
                if (!IsConnected)
                    return;
                IsConnected = false;
                _events.Add("remote.dispose");
            }
        }

        private sealed class TrackingFileSystemHost : IFileSystemHost
        {
            private readonly IList<string> _events;
            private readonly int _mountStatus;
            private bool _disposed;

            public TrackingFileSystemHost(IList<string> events, int mountStatus)
            {
                _events = events;
                _mountStatus = mountStatus;
            }

            public int Mount(string mountPoint)
            {
                _events.Add("host.mount:" + mountPoint);
                return _mountStatus;
            }

            public void Unmount()
            {
                _events.Add("host.unmount");
            }

            public void Dispose()
            {
                if (_disposed)
                    return;
                _disposed = true;
                _events.Add("host.dispose");
            }
        }
    }
}

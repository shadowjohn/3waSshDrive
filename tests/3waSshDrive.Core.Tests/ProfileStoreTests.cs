using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ThreeWa.SshDrive.Core.Models;
using ThreeWa.SshDrive.Core.Profiles;

namespace ThreeWa.SshDrive.Core.Tests
{
    [TestClass]
    public sealed class ProfileStoreTests
    {
        [TestMethod]
        public void Load_ReturnsEmptyListWhenStoreDoesNotExist()
        {
            var directory = CreateTemporaryDirectory();
            try
            {
                var store = new ProfileStore(Path.Combine(directory, "profiles.json"));

                Assert.AreEqual(0, store.Load().Count);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void SaveAndLoad_RoundTripsProfilesAndRemovesTemporaryFile()
        {
            var directory = CreateTemporaryDirectory();
            var filePath = Path.Combine(directory, "config", "profiles.json");
            try
            {
                var store = new ProfileStore(filePath);
                store.Save(new[]
                {
                    new DriveProfile
                    {
                        Name = "AIHub-5090",
                        Host = "192.168.1.100",
                        Port = 2222,
                        Username = "feather",
                        RemoteRoot = "/home/feather/3waAIHub",
                        DriveLetter = "Z:",
                        PrivateKeyPath = @"C:\keys\id_ed25519",
                        HostKeyFingerprintSha256 = "SHA256:abc123"
                    }
                });

                var loaded = store.Load().Single();

                Assert.AreEqual("AIHub-5090", loaded.Name);
                Assert.AreEqual("192.168.1.100", loaded.Host);
                Assert.AreEqual(2222, loaded.Port);
                Assert.AreEqual("feather", loaded.Username);
                Assert.AreEqual("/home/feather/3waAIHub", loaded.RemoteRoot);
                Assert.AreEqual("Z:", loaded.DriveLetter);
                Assert.AreEqual(@"C:\keys\id_ed25519", loaded.PrivateKeyPath);
                Assert.AreEqual("SHA256:abc123", loaded.HostKeyFingerprintSha256);
                Assert.IsFalse(File.Exists(filePath + ".tmp"));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static string CreateTemporaryDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "3waSshDrive.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}

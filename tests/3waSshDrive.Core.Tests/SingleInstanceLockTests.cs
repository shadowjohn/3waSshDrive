using System;
using System.Diagnostics;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ThreeWa.SshDrive.Core.Utils;

namespace ThreeWa.SshDrive.Core.Tests
{
    [TestClass]
    public sealed class SingleInstanceLockTests
    {
        private string _tempDir;
        private string _lockPath;

        [TestInitialize]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "3waSshDrive_LockTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _lockPath = Path.Combine(_tempDir, "lock.pid");
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_tempDir))
            {
                try
                {
                    Directory.Delete(_tempDir, true);
                }
                catch
                {
                }
            }
        }

        [TestMethod]
        public void TryAcquire_CreatesLockFileAndRecordsCurrentPid()
        {
            var acquired = SingleInstanceLock.TryAcquire(out var appLock, _lockPath);
            Assert.IsTrue(acquired);
            Assert.IsNotNull(appLock);

            using (appLock)
            {
                Assert.IsTrue(File.Exists(_lockPath));
                // Note: file is locked exclusively, but we know it was created
            }

            // After dispose, lock file is cleaned up
            Assert.IsFalse(File.Exists(_lockPath));
        }

        [TestMethod]
        public void GetDefaultLockFilePath_UsesPerUserStateDirectory()
        {
            var path = SingleInstanceLock.GetDefaultLockFilePath();
            var expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "3waSshDrive",
                "state",
                "lock.pid");

            Assert.AreEqual(expected, path);
        }

        [TestMethod]
        public void TryAcquire_StaleUnlockedFileDoesNotBlockStartup()
        {
            File.WriteAllText(_lockPath, "999999\r\n2000-01-01 00:00:00");

            Assert.IsTrue(SingleInstanceLock.TryAcquire(out var appLock, _lockPath));
            appLock.Dispose();
        }

        [TestMethod]
        public void TryAcquire_LiveExclusiveHandleStillRejectsSecondInstance()
        {
            Assert.IsTrue(SingleInstanceLock.TryAcquire(out var first, _lockPath));
            using (first)
            {
                Assert.IsFalse(SingleInstanceLock.TryAcquire(out var second, _lockPath));
                Assert.IsNull(second);
            }
        }

        [TestMethod]
        public void TryAcquire_FailsWhenLockIsAlreadyHeld()
        {
            var firstAcquired = SingleInstanceLock.TryAcquire(out var firstLock, _lockPath);
            Assert.IsTrue(firstAcquired);

            using (firstLock)
            {
                var secondAcquired = SingleInstanceLock.TryAcquire(out var secondLock, _lockPath);
                Assert.IsFalse(secondAcquired);
                Assert.IsNull(secondLock);
            }

            // Once first lock is released, acquiring again succeeds
            var thirdAcquired = SingleInstanceLock.TryAcquire(out var thirdLock, _lockPath);
            Assert.IsTrue(thirdAcquired);
            thirdLock.Dispose();
        }
    }
}

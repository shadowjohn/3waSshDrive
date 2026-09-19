using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ThreeWa.SshDrive.App.Diagnostics;
using ThreeWa.SshDrive.Core.Utils;

namespace ThreeWa.SshDrive.App.Tests
{
    [TestClass]
    public sealed class SelfCheckRunnerTests
    {
        private string _tempDir;

        [TestInitialize]
        public void Setup()
        {
            _tempDir = Path.Combine(
                Path.GetTempPath(),
                "3waSshDrive_SelfCheckTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }

        [TestMethod]
        public void Evaluate_WhenAllInputsMatch_ReturnsSuccess()
        {
            WriteRequiredFiles(_tempDir);
            var context = ValidContext(_tempDir);

            var result = SelfCheckRunner.Evaluate(context);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(
                "Version=v2026.09.19.01 Package=2026.919.1 " +
                "UpdateMode=Installed",
                result.Message);
        }

        [DataTestMethod]
        [DataRow("Installed")]
        [DataRow("Portable")]
        [DataRow("Unmanaged")]
        public void Evaluate_UpdateModeIsDiagnosticOnly(string updateMode)
        {
            WriteRequiredFiles(_tempDir);
            var context = ValidContext(_tempDir);
            context.UpdateMode = updateMode;

            Assert.IsTrue(SelfCheckRunner.Evaluate(context).Success);
        }

        [TestMethod]
        public void Evaluate_WhenRequiredDllIsMissing_ReturnsFailure()
        {
            WriteRequiredFiles(_tempDir);
            File.Delete(Path.Combine(_tempDir, "Velopack.dll"));

            var result = SelfCheckRunner.Evaluate(ValidContext(_tempDir));

            Assert.IsFalse(result.Success);
            StringAssert.Contains(result.Message, "Velopack.dll");
        }

        [TestMethod]
        public void Evaluate_WhenPackageVersionDoesNotMatchTag_ReturnsFailure()
        {
            WriteRequiredFiles(_tempDir);
            var context = ValidContext(_tempDir);
            context.PackageVersion = "2026.919.2";

            Assert.IsFalse(SelfCheckRunner.Evaluate(context).Success);
        }

        [TestMethod]
        public void Evaluate_WhenProcessIsNotX64_ReturnsFailure()
        {
            WriteRequiredFiles(_tempDir);
            var context = ValidContext(_tempDir);
            context.Is64BitProcess = false;

            Assert.IsFalse(SelfCheckRunner.Evaluate(context).Success);
        }

        [TestMethod]
        public void Evaluate_WhenDisplayVersionIsInvalid_ReturnsFailure()
        {
            WriteRequiredFiles(_tempDir);
            var context = ValidContext(_tempDir);
            context.DisplayVersion = "2026.09.19.01";

            var result = SelfCheckRunner.Evaluate(context);

            Assert.IsFalse(result.Success);
            StringAssert.Contains(result.Message, "Display version");
        }

        [TestMethod]
        public void Evaluate_WhenAssemblyVersionDoesNotMatchTag_ReturnsFailure()
        {
            WriteRequiredFiles(_tempDir);
            var context = ValidContext(_tempDir);
            context.AssemblyVersion = new Version(2026, 9, 19, 2);

            var result = SelfCheckRunner.Evaluate(context);

            Assert.IsFalse(result.Success);
            StringAssert.Contains(result.Message, "Assembly version");
        }

        [TestMethod]
        public void Evaluate_WhenFileVersionDoesNotMatchTag_ReturnsFailure()
        {
            WriteRequiredFiles(_tempDir);
            var context = ValidContext(_tempDir);
            context.FileVersion = new Version(2026, 9, 19, 2);

            var result = SelfCheckRunner.Evaluate(context);

            Assert.IsFalse(result.Success);
            StringAssert.Contains(result.Message, "File version");
        }

        [TestMethod]
        public void Evaluate_WhenVelopackBootstrapFailed_ReturnsFailureFirst()
        {
            var context = ValidContext(_tempDir);
            context.VelopackBootstrapSucceeded = false;

            var result = SelfCheckRunner.Evaluate(context);

            Assert.IsFalse(result.Success);
            StringAssert.Contains(result.Message, "Velopack bootstrap");
        }

        [TestMethod]
        public void Evaluate_WhenInstanceLockIsHeld_DoesNotDependOnTheLock()
        {
            var lockPath = Path.Combine(_tempDir, "state", "lock.pid");
            Assert.IsTrue(SingleInstanceLock.TryAcquire(out var appLock, lockPath));

            using (appLock)
            {
                WriteRequiredFiles(_tempDir);

                Assert.IsTrue(SelfCheckRunner.Evaluate(ValidContext(_tempDir)).Success);
            }
        }

        private static void WriteRequiredFiles(string directory)
        {
            foreach (var relativePath in SelfCheckRunner.RequiredFiles)
            {
                var path = Path.Combine(directory, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, "self-check fixture");
            }
        }

        private static SelfCheckContext ValidContext(string directory)
        {
            return new SelfCheckContext
            {
                BaseDirectory = directory,
                DisplayVersion = "v2026.09.19.01",
                PackageVersion = "2026.919.1",
                AssemblyVersion = new Version(2026, 9, 19, 1),
                FileVersion = new Version(2026, 9, 19, 1),
                Is64BitProcess = true,
                VelopackBootstrapSucceeded = true,
                UpdateMode = "Installed"
            };
        }
    }
}

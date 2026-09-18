using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ThreeWa.SshDrive.FileSystem.Mounting;

namespace ThreeWa.SshDrive.FileSystem.Tests
{
    [TestClass]
    public sealed class WinFspInstallerTests
    {
        [TestMethod]
        public void CreateProcessStartInfo_ContainsExpectedPackageAndArguments()
        {
            var psi = WinFspInstaller.CreateProcessStartInfo();

            Assert.IsFalse(string.IsNullOrWhiteSpace(psi.FileName));
            StringAssert.Contains(psi.Arguments, "install WinFsp.WinFsp");
            StringAssert.Contains(psi.Arguments, "--accept-source-agreements");
            StringAssert.Contains(psi.Arguments, "--accept-package-agreements");
            Assert.IsFalse(psi.UseShellExecute);
            Assert.IsTrue(psi.CreateNoWindow);
            Assert.IsTrue(psi.RedirectStandardOutput);
            Assert.IsTrue(psi.RedirectStandardError);
        }

        [TestMethod]
        public async Task InstallAsync_WhenWingetFails_ThrowsInvalidOperationException()
        {
            // Launch a dummy process that exits with code 1
            Process Launcher(ProcessStartInfo _) =>
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c exit 1",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });

            var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => WinFspInstaller.InstallAsync(Launcher, () => WinFspRuntimeVerification.Success("a", "b", "c", "d")));

            StringAssert.Contains(exception.Message, "結束代碼 1");
        }

        [TestMethod]
        public async Task InstallAsync_WhenVerificationFails_ThrowsInvalidOperationException()
        {
            // Launch a dummy process that exits with code 0
            Process Launcher(ProcessStartInfo _) =>
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c exit 0",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });

            var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => WinFspInstaller.InstallAsync(Launcher, () => WinFspRuntimeVerification.Failure("Simulated verification failure")));

            StringAssert.Contains(exception.Message, "Simulated verification failure");
        }

        [TestMethod]
        public async Task InstallAsync_WhenWingetSucceedsAndVerificationPasses_ReturnsSuccess()
        {
            Process Launcher(ProcessStartInfo _) =>
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c exit 0",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });

            var expected = WinFspRuntimeVerification.Success("native.dll", "driver.sys", "hash1", "hash2");
            var result = await WinFspInstaller.InstallAsync(Launcher, () => expected);

            Assert.IsTrue(result.IsValid);
            Assert.AreEqual("native.dll", result.NativeDllPath);
            Assert.AreEqual("driver.sys", result.DriverPath);
        }

        [TestMethod]
        public async Task InstallAsync_WhenWingetNotFound_ThrowsDescriptiveException()
        {
            Process Launcher(ProcessStartInfo _) =>
                throw new Win32Exception(2, "The system cannot find the file specified");

            var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => WinFspInstaller.InstallAsync(Launcher, () => WinFspRuntimeVerification.Success("a", "b", "c", "d")));

            StringAssert.Contains(exception.Message, "找不到 winget 指令");
        }
    }
}

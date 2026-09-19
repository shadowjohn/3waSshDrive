using System;
using System.IO;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ThreeWa.SshDrive.FileSystem.Mounting;

namespace ThreeWa.SshDrive.FileSystem.Tests
{
    [TestClass]
    public sealed class WinFspRuntimeVerifierTests
    {
        [TestMethod]
        public void Verify_AcceptsExactPinnedFiles()
        {
            var directory = CreateTemporaryDirectory();
            try
            {
                var nativeDllPath = Path.Combine(directory, "winfsp-x64.dll");
                var driverPath = Path.Combine(directory, "winfsp-x64.sys");
                CopyVersionedRuntimeFixture(nativeDllPath);
                CopyVersionedRuntimeFixture(driverPath);
                var verifier = new WinFspRuntimeVerifier(
                    GetVersion(nativeDllPath),
                    ComputeSha256(nativeDllPath),
                    ComputeSha256(driverPath));

                var result = verifier.Verify(nativeDllPath, driverPath);

                Assert.IsTrue(result.IsValid, result.Error);
                Assert.AreEqual(ComputeSha256(nativeDllPath), result.NativeDllSha256);
                Assert.AreEqual(ComputeSha256(driverPath), result.DriverSha256);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void Verify_RejectsHashMismatch()
        {
            var directory = CreateTemporaryDirectory();
            try
            {
                var nativeDllPath = Path.Combine(directory, "winfsp-x64.dll");
                var driverPath = Path.Combine(directory, "winfsp-x64.sys");
                CopyVersionedRuntimeFixture(nativeDllPath);
                CopyVersionedRuntimeFixture(driverPath);
                var verifier = new WinFspRuntimeVerifier(
                    GetVersion(nativeDllPath),
                    new string('0', 64),
                    ComputeSha256(driverPath));

                var result = verifier.Verify(nativeDllPath, driverPath);

                Assert.IsFalse(result.IsValid);
                Assert.AreEqual(
                    "WinFsp native DLL SHA-256 does not match the pinned runtime.",
                    result.Error);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static string GetVersion(string path)
        {
            return System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileVersion;
        }

        private static void CopyVersionedRuntimeFixture(string destinationPath)
        {
            File.Copy(typeof(string).Assembly.Location, destinationPath);
        }

        private static string ComputeSha256(string path)
        {
            using (var sha256 = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                return BitConverter.ToString(sha256.ComputeHash(stream))
                    .Replace("-", string.Empty);
            }
        }

        private static string CreateTemporaryDirectory()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "3waSshDrive.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}

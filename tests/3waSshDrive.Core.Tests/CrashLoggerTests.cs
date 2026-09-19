using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ThreeWa.SshDrive.Core.Logging;

namespace ThreeWa.SshDrive.Core.Tests
{
    [TestClass]
    public sealed class CrashLoggerTests
    {
        private string _tempDir;

        [TestInitialize]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "3waSshDrive_CrashLogTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            CrashLogger.LogDirectory = _tempDir;
        }

        [TestCleanup]
        public void Cleanup()
        {
            CrashLogger.LogDirectory = null;
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
        public void GetLogFilePath_MatchesPhpYmdFormat()
        {
            var date = new DateTime(2026, 9, 19);
            var path = CrashLogger.GetLogFilePath(date);

            Assert.IsTrue(path.EndsWith(@"20260919.txt", StringComparison.OrdinalIgnoreCase),
                $"Expected file path to end with 20260919.txt, but was {path}");
        }

        [TestMethod]
        public void Log_WritesAndAppendsExceptionsOnSameDay()
        {
            var ex1 = new InvalidOperationException("Test crash 1: simulate unhandled crash");
            CrashLogger.Log("UnitTest.Crash1", ex1);

            var todayFile = CrashLogger.GetLogFilePath();
            Assert.IsTrue(File.Exists(todayFile), "Log file should exist after first log call.");

            var content1 = File.ReadAllText(todayFile);
            StringAssert.Contains(content1, "UnitTest.Crash1");
            StringAssert.Contains(content1, "Test crash 1: simulate unhandled crash");

            var ex2 = new ArgumentNullException("paramName", "Test crash 2: simulate second crash");
            CrashLogger.Log("UnitTest.Crash2", ex2);

            var content2 = File.ReadAllText(todayFile);
            StringAssert.Contains(content2, "Test crash 1: simulate unhandled crash");
            StringAssert.Contains(content2, "UnitTest.Crash2");
            StringAssert.Contains(content2, "Test crash 2: simulate second crash");
            Assert.IsTrue(content2.Length > content1.Length, "Second log should be appended to the same file.");
        }
    }
}

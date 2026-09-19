using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ThreeWa.SshDrive.App.Diagnostics;

namespace ThreeWa.SshDrive.App.Tests
{
    [TestClass]
    public sealed class CrashLoggerTests
    {
        [TestMethod]
        public void CreateSafeLine_DoesNotIncludeOriginalExceptionDetails()
        {
            var exception = new InvalidOperationException(
                @"host.internal C:\keys\john.ppk token=abc");

            var line = CrashLogger.CreateSafeLine("UpdateCheck", exception);

            StringAssert.Contains(line, "UpdateCheck");
            StringAssert.Contains(line, "InvalidOperationException");
            Assert.IsFalse(line.Contains("host.internal"));
            Assert.IsFalse(line.Contains("john.ppk"));
            Assert.IsFalse(line.Contains("token=abc"));
        }
    }
}

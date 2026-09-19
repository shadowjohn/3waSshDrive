using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ThreeWa.SshDrive.App.Tests
{
    [TestClass]
    public sealed class StartupOptionsTests
    {
        [TestMethod]
        public void Parse_SelfCheck_IsCaseInsensitive()
        {
            Assert.IsTrue(StartupOptions.Parse(new[] { "--SELF-CHECK" }).SelfCheck);
        }

        [TestMethod]
        public void Parse_NormalLaunch_DoesNotSelectSelfCheck()
        {
            Assert.IsFalse(StartupOptions.Parse(Array.Empty<string>()).SelfCheck);
        }

        [TestMethod]
        public void Parse_UnrelatedArguments_DoNotSelectSelfCheck()
        {
            Assert.IsFalse(StartupOptions.Parse(new[] { "--check" }).SelfCheck);
        }
    }
}

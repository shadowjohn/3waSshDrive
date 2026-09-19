using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ThreeWa.SshDrive.Core.Versioning;

namespace ThreeWa.SshDrive.Core.Tests
{
    [TestClass]
    public sealed class ReleaseVersionTests
    {
        [TestMethod]
        public void ParseTag_MapsExternalTagToVelopackAndAssemblyVersions()
        {
            var value = ReleaseVersion.ParseTag("v2026.09.19.01");

            Assert.AreEqual("v2026.09.19.01", value.Tag);
            Assert.AreEqual("2026.919.1", value.PackageVersion);
            Assert.AreEqual(new Version(2026, 9, 19, 1), value.AssemblyVersion);
        }

        [TestMethod]
        public void TagFromPackageVersion_RestoresDisplayTag()
        {
            Assert.AreEqual(
                "v2026.09.19.02",
                ReleaseVersion.TagFromPackageVersion("2026.919.2"));
        }

        [DataTestMethod]
        [DataRow("v2026.02.30.01")]
        [DataRow("v2026.09.19.00")]
        [DataRow("2026.09.19.01")]
        [DataRow("v2026.9.19.01")]
        public void ParseTag_RejectsInvalidInput(string tag)
        {
            Assert.ThrowsException<FormatException>(() => ReleaseVersion.ParseTag(tag));
        }
    }
}

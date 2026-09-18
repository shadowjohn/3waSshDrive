using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ThreeWa.SshDrive.Core.Paths;

namespace ThreeWa.SshDrive.Core.Tests
{
    [TestClass]
    public sealed class RemotePathMapperTests
    {
        [TestMethod]
        public void Map_JoinsWindowsPathBelowNormalizedRemoteRoot()
        {
            var result = RemotePathMapper.Map(
                "/home/feather/",
                @"\packs\yolo\api.php");

            Assert.AreEqual("/home/feather/packs/yolo/api.php", result);
        }

        [TestMethod]
        public void Map_PreservesPosixRoot()
        {
            Assert.AreEqual("/", RemotePathMapper.Map("/", @"\"));
            Assert.AreEqual("/etc/hosts", RemotePathMapper.Map("/", @"\etc\hosts"));
        }

        [TestMethod]
        public void Map_RejectsParentTraversal()
        {
            var exception = Assert.ThrowsException<ArgumentException>(
                () => RemotePathMapper.Map("/home/feather", @"\..\etc\passwd"));

            Assert.AreEqual("Parent path segments are not allowed. (Parameter 'windowsPath')", exception.Message);
        }

        [TestMethod]
        public void Map_RejectsRelativeRemoteRoot()
        {
            Assert.ThrowsException<ArgumentException>(
                () => RemotePathMapper.Map("home/feather", @"\project"));
        }
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Renci.SshNet.Common;
using ThreeWa.SshDrive.Core.Remote;
using ThreeWa.SshDrive.Sftp;

namespace ThreeWa.SshDrive.Sftp.Tests
{
    [TestClass]
    public sealed class SshNetExceptionMapperTests
    {
        [TestMethod]
        public void Translate_MapsMissingPathAndPreservesServerPath()
        {
            var source = new SftpPathNotFoundException(
                "missing",
                "/server/missing.txt");

            var mapped = SshNetExceptionMapper.Translate(
                source,
                "/requested/missing.txt");

            Assert.IsInstanceOfType(mapped, typeof(RemotePathNotFoundException));
            Assert.AreEqual(
                "/server/missing.txt",
                ((RemotePathNotFoundException)mapped).Path);
        }

        [TestMethod]
        public void Translate_MapsPermissionDeniedToRequestedPath()
        {
            var mapped = SshNetExceptionMapper.Translate(
                new SftpPermissionDeniedException("denied"),
                "/private/file.txt");

            Assert.IsInstanceOfType(mapped, typeof(RemoteAccessDeniedException));
            Assert.AreEqual(
                "/private/file.txt",
                ((RemoteAccessDeniedException)mapped).Path);
        }

        [TestMethod]
        public void Translate_MapsBrokenConnection()
        {
            var mapped = SshNetExceptionMapper.Translate(
                new SshConnectionException("connection lost"),
                "/home/feather");

            Assert.IsInstanceOfType(mapped, typeof(RemoteConnectionException));
            StringAssert.Contains(mapped.Message, "connection lost");
        }
    }
}

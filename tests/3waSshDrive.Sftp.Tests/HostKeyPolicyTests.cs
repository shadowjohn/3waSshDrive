using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ThreeWa.SshDrive.Sftp.Security;

namespace ThreeWa.SshDrive.Sftp.Tests
{
    [TestClass]
    public sealed class HostKeyPolicyTests
    {
        [TestMethod]
        public void ForMount_AcceptsEquivalentSha256Fingerprint()
        {
            var policy = HostKeyPolicy.ForMount(" SHA256:abc123== ");

            Assert.IsTrue(policy.Evaluate("abc123"));
            Assert.AreEqual("SHA256:abc123", policy.CapturedFingerprint);
        }

        [TestMethod]
        public void ForMount_RejectsDifferentFingerprint()
        {
            var policy = HostKeyPolicy.ForMount("SHA256:expected");

            Assert.IsFalse(policy.Evaluate("actual"));
            Assert.AreEqual("SHA256:actual", policy.CapturedFingerprint);
        }

        [TestMethod]
        public void ForMount_RejectsMissingPinnedFingerprint()
        {
            Assert.ThrowsException<ArgumentException>(
                () => HostKeyPolicy.ForMount(" "));
        }

        [TestMethod]
        public void ForCapture_AcceptsAndRecordsServerFingerprint()
        {
            var policy = HostKeyPolicy.ForCapture();

            Assert.IsTrue(policy.Evaluate("serverKey=="));
            Assert.AreEqual("SHA256:serverKey", policy.CapturedFingerprint);
        }
    }
}

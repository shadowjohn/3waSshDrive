using Microsoft.VisualStudio.TestTools.UnitTesting;
using ThreeWa.SshDrive.Core.Models;
using ThreeWa.SshDrive.Core.Profiles;

namespace ThreeWa.SshDrive.Core.Tests
{
    [TestClass]
    public sealed class DriveProfileValidatorTests
    {
        [TestMethod]
        public void Validate_AcceptsCompleteReadOnlyProfile()
        {
            var profile = new DriveProfile
            {
                Name = "AIHub-5090",
                Host = "192.168.1.100",
                Port = 22,
                Username = "feather",
                RemoteRoot = "/home/feather/3waAIHub",
                DriveLetter = "Z:",
                PrivateKeyPath = @"C:\Users\feather\.ssh\id_ed25519",
                HostKeyFingerprintSha256 = "SHA256:abc123"
            };

            var errors = DriveProfileValidator.Validate(profile);

            Assert.AreEqual(0, errors.Count);
        }

        [TestMethod]
        public void Validate_ReportsEveryUnsafeOrMissingField()
        {
            var profile = new DriveProfile
            {
                Name = " ",
                Host = "",
                Port = 70000,
                Username = null,
                RemoteRoot = "home/feather",
                DriveLetter = "ZZ:",
                PrivateKeyPath = "",
                HostKeyFingerprintSha256 = ""
            };

            var errors = DriveProfileValidator.Validate(profile);

            CollectionAssert.AreEquivalent(new[]
            {
                "Name is required.",
                "Host is required.",
                "Port must be between 1 and 65535.",
                "Username is required.",
                "Remote root must be an absolute POSIX path.",
                "Drive letter must use the form Z:.",
                "Private key path is required.",
                "A SHA-256 host-key fingerprint is required before mounting."
            }, new System.Collections.Generic.List<string>(errors));
        }
    }
}

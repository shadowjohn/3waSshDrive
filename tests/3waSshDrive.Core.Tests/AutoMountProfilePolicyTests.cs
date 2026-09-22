using Microsoft.VisualStudio.TestTools.UnitTesting;
using ThreeWa.SshDrive.Core.Models;
using ThreeWa.SshDrive.Core.Profiles;

namespace ThreeWa.SshDrive.Core.Tests
{
    [TestClass]
    public sealed class AutoMountProfilePolicyTests
    {
        [TestMethod]
        public void ShouldAttemptOnStartup_AcceptsEnabledPrivateKeyProfile()
        {
            var profile = new DriveProfile
            {
                AutoMountOnStartup = true,
                AuthenticationMode = AuthenticationMode.PrivateKey
            };

            Assert.IsTrue(AutoMountProfilePolicy.ShouldAttemptOnStartup(profile));
        }

        [TestMethod]
        public void ShouldAttemptOnStartup_RejectsPasswordProfile()
        {
            var profile = new DriveProfile
            {
                AutoMountOnStartup = true,
                AuthenticationMode = AuthenticationMode.Password
            };

            Assert.IsFalse(AutoMountProfilePolicy.ShouldAttemptOnStartup(profile));
        }
    }
}

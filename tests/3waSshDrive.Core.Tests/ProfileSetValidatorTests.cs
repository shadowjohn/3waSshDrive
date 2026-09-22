using Microsoft.VisualStudio.TestTools.UnitTesting;
using ThreeWa.SshDrive.Core.Models;
using ThreeWa.SshDrive.Core.Profiles;

namespace ThreeWa.SshDrive.Core.Tests
{
    [TestClass]
    public sealed class ProfileSetValidatorTests
    {
        [TestMethod]
        public void ValidateUniqueNamesAndDriveLetters_AcceptsDistinctProfiles()
        {
            var errors = ProfileSetValidator.ValidateUniqueNamesAndDriveLetters(
                new[]
                {
                    new DriveProfile { Name = "Build", DriveLetter = "T:" },
                    new DriveProfile { Name = "Deploy", DriveLetter = "U:" }
                });

            Assert.AreEqual(0, errors.Count);
        }

        [TestMethod]
        public void ValidateUniqueNamesAndDriveLetters_RejectsCaseInsensitiveConflicts()
        {
            var errors = ProfileSetValidator.ValidateUniqueNamesAndDriveLetters(
                new[]
                {
                    new DriveProfile { Name = "Build", DriveLetter = "T:" },
                    new DriveProfile { Name = "build", DriveLetter = "t:" }
                });

            CollectionAssert.AreEquivalent(new[]
            {
                "Profile names must be unique: build",
                "Drive letters must be unique: T:"
            }, new System.Collections.Generic.List<string>(errors));
        }
    }
}

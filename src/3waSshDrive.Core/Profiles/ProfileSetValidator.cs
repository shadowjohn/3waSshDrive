using System;
using System.Collections.Generic;
using System.Linq;
using ThreeWa.SshDrive.Core.Models;

namespace ThreeWa.SshDrive.Core.Profiles
{
    public static class ProfileSetValidator
    {
        public static IReadOnlyList<string> ValidateUniqueNamesAndDriveLetters(
            IEnumerable<DriveProfile> profiles)
        {
            if (profiles == null)
                throw new ArgumentNullException(nameof(profiles));

            var errors = new List<string>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var driveLetters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var profile in profiles.Where(profile => profile != null))
            {
                if (!string.IsNullOrWhiteSpace(profile.Name) &&
                    !names.Add(profile.Name.Trim()))
                {
                    errors.Add("Profile names must be unique: " + profile.Name.Trim());
                }

                if (!string.IsNullOrWhiteSpace(profile.DriveLetter) &&
                    !driveLetters.Add(profile.DriveLetter.Trim()))
                {
                    errors.Add("Drive letters must be unique: " +
                        profile.DriveLetter.Trim().ToUpperInvariant());
                }
            }

            return errors;
        }
    }
}

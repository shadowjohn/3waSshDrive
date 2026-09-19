using System.Collections.Generic;
using System.Text.RegularExpressions;
using ThreeWa.SshDrive.Core.Models;

namespace ThreeWa.SshDrive.Core.Profiles
{
    public static class DriveProfileValidator
    {
        private static readonly Regex DriveLetterPattern =
            new Regex("^[A-Za-z]:$", RegexOptions.CultureInvariant);

        public static IReadOnlyList<string> Validate(DriveProfile profile)
        {
            var errors = new List<string>();

            if (profile == null)
            {
                errors.Add("Profile is required.");
                return errors;
            }

            if (string.IsNullOrWhiteSpace(profile.Name))
                errors.Add("Name is required.");
            if (string.IsNullOrWhiteSpace(profile.Host))
                errors.Add("Host is required.");
            if (profile.Port < 1 || profile.Port > 65535)
                errors.Add("Port must be between 1 and 65535.");
            if (string.IsNullOrWhiteSpace(profile.Username))
                errors.Add("Username is required.");
            if (string.IsNullOrWhiteSpace(profile.RemoteRoot) ||
                !profile.RemoteRoot.StartsWith("/"))
                errors.Add("Remote root must be an absolute POSIX path.");
            if (string.IsNullOrWhiteSpace(profile.DriveLetter) ||
                !DriveLetterPattern.IsMatch(profile.DriveLetter))
                errors.Add("Drive letter must use the form Z:.");
            if (!System.Enum.IsDefined(
                typeof(AuthenticationMode), profile.AuthenticationMode))
                errors.Add("Authentication mode is invalid.");
            else if (profile.AuthenticationMode == AuthenticationMode.PrivateKey &&
                     string.IsNullOrWhiteSpace(profile.PrivateKeyPath))
                errors.Add("Private key path is required.");
            else if (profile.AuthenticationMode == AuthenticationMode.Password &&
                     string.IsNullOrEmpty(profile.Password))
                errors.Add("Password is required.");
            if (string.IsNullOrWhiteSpace(profile.HostKeyFingerprintSha256))
                errors.Add("A SHA-256 host-key fingerprint is required before mounting.");

            return errors;
        }
    }
}

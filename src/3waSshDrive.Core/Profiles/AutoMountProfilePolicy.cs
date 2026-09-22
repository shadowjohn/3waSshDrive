using ThreeWa.SshDrive.Core.Models;

namespace ThreeWa.SshDrive.Core.Profiles
{
    public static class AutoMountProfilePolicy
    {
        public static bool ShouldAttemptOnStartup(DriveProfile profile)
        {
            return profile != null &&
                   profile.AutoMountOnStartup &&
                   profile.AuthenticationMode == AuthenticationMode.PrivateKey;
        }
    }
}

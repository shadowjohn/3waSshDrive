using System;
using System.Linq;

namespace ThreeWa.SshDrive.App
{
    internal sealed class StartupOptions
    {
        private StartupOptions(bool selfCheck)
        {
            SelfCheck = selfCheck;
        }

        public bool SelfCheck { get; }

        public static StartupOptions Parse(string[] args)
        {
            var selfCheck = (args ?? Array.Empty<string>()).Any(
                argument => string.Equals(
                    argument,
                    "--self-check",
                    StringComparison.OrdinalIgnoreCase));

            return new StartupOptions(selfCheck);
        }
    }
}

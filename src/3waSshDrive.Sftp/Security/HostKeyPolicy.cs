using System;

namespace ThreeWa.SshDrive.Sftp.Security
{
    public sealed class HostKeyPolicy
    {
        private readonly string _expectedFingerprint;
        private readonly bool _captureOnly;

        private HostKeyPolicy(string expectedFingerprint, bool captureOnly)
        {
            _expectedFingerprint = expectedFingerprint;
            _captureOnly = captureOnly;
        }

        public string CapturedFingerprint { get; private set; }

        public static HostKeyPolicy ForMount(string expectedFingerprint)
        {
            var normalized = Normalize(expectedFingerprint);
            if (normalized == null)
                throw new ArgumentException(
                    "A SHA-256 host-key fingerprint is required before mounting.",
                    nameof(expectedFingerprint));

            return new HostKeyPolicy(normalized, false);
        }

        public static HostKeyPolicy ForCapture()
        {
            return new HostKeyPolicy(null, true);
        }

        public bool Evaluate(string serverFingerprint)
        {
            var normalized = Normalize(serverFingerprint);
            CapturedFingerprint = normalized == null ? null : "SHA256:" + normalized;

            if (normalized == null)
                return false;
            if (_captureOnly)
                return true;

            return string.Equals(
                _expectedFingerprint,
                normalized,
                StringComparison.Ordinal);
        }

        private static string Normalize(string fingerprint)
        {
            if (string.IsNullOrWhiteSpace(fingerprint))
                return null;

            var normalized = fingerprint.Trim();
            if (normalized.StartsWith("SHA256:", StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring("SHA256:".Length);

            normalized = normalized.TrimEnd('=');
            return normalized.Length == 0 ? null : normalized;
        }
    }
}

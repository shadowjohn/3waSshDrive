using System;

namespace ThreeWa.SshDrive.App.Updates
{
    internal sealed class UpdatePackage
    {
        public UpdatePackage(
            string currentDisplayVersion,
            string targetDisplayVersion,
            string packageVersion,
            string releaseNotesMarkdown,
            object nativeToken)
        {
            CurrentDisplayVersion = currentDisplayVersion;
            TargetDisplayVersion = targetDisplayVersion;
            PackageVersion = packageVersion;
            ReleaseNotesMarkdown = releaseNotesMarkdown ?? string.Empty;
            NativeToken = nativeToken ?? throw new ArgumentNullException(nameof(nativeToken));
        }

        public string CurrentDisplayVersion { get; }

        public string TargetDisplayVersion { get; }

        public string PackageVersion { get; }

        public string ReleaseNotesMarkdown { get; }

        internal object NativeToken { get; }
    }

    internal enum UpdateCheckKind
    {
        Disabled,
        Busy,
        UpToDate,
        Available,
        Failed
    }

    internal static class UpdateNotificationPolicy
    {
        internal static bool ShouldShowMessage(
            bool manual,
            UpdateCheckKind kind)
        {
            if (kind == UpdateCheckKind.Available)
                return true;

            return manual && kind != UpdateCheckKind.Busy;
        }
    }

    internal sealed class UpdateCheckResult
    {
        private UpdateCheckResult(
            UpdateCheckKind kind,
            UpdatePackage package,
            string message)
        {
            Kind = kind;
            Package = package;
            Message = message ?? string.Empty;
        }

        public UpdateCheckKind Kind { get; }

        public UpdatePackage Package { get; }

        public string Message { get; }

        public static UpdateCheckResult Disabled()
        {
            return new UpdateCheckResult(UpdateCheckKind.Disabled, null, string.Empty);
        }

        public static UpdateCheckResult Busy()
        {
            return new UpdateCheckResult(UpdateCheckKind.Busy, null, string.Empty);
        }

        public static UpdateCheckResult UpToDate()
        {
            return new UpdateCheckResult(UpdateCheckKind.UpToDate, null, string.Empty);
        }

        public static UpdateCheckResult Available(UpdatePackage package)
        {
            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            return new UpdateCheckResult(UpdateCheckKind.Available, package, string.Empty);
        }

        public static UpdateCheckResult Failed(string message)
        {
            return new UpdateCheckResult(UpdateCheckKind.Failed, null, message);
        }
    }

    internal sealed class UpdateOperationResult
    {
        private UpdateOperationResult(bool success, string message)
        {
            Success = success;
            Message = message ?? string.Empty;
        }

        public bool Success { get; }

        public string Message { get; }

        public static UpdateOperationResult Succeeded()
        {
            return new UpdateOperationResult(true, string.Empty);
        }

        public static UpdateOperationResult Failed(string message)
        {
            return new UpdateOperationResult(false, message);
        }
    }

    internal enum UpdateApplyKind
    {
        RestartRequested,
        Blocked,
        Failed
    }

    internal sealed class UpdateApplyResult
    {
        private UpdateApplyResult(UpdateApplyKind kind, string message)
        {
            Kind = kind;
            Message = message ?? string.Empty;
        }

        public UpdateApplyKind Kind { get; }

        public string Message { get; }

        public static UpdateApplyResult RestartRequested()
        {
            return new UpdateApplyResult(
                UpdateApplyKind.RestartRequested,
                string.Empty);
        }

        public static UpdateApplyResult Blocked(string message)
        {
            return new UpdateApplyResult(UpdateApplyKind.Blocked, message);
        }

        public static UpdateApplyResult Failed(string message)
        {
            return new UpdateApplyResult(UpdateApplyKind.Failed, message);
        }
    }
}

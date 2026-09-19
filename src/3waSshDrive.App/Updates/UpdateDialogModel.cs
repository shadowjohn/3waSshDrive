using System;

namespace ThreeWa.SshDrive.App.Updates
{
    internal sealed class UpdateDialogModel
    {
        private const string MissingReleaseNotes =
            "此版本未提供 release notes。";

        private UpdateDialogModel(
            string currentVersion,
            string targetVersion,
            string releaseNotes)
        {
            CurrentVersion = currentVersion ?? string.Empty;
            TargetVersion = targetVersion ?? string.Empty;
            ReleaseNotes = releaseNotes ?? MissingReleaseNotes;
        }

        public string CurrentVersion { get; }

        public string TargetVersion { get; }

        public string ReleaseNotes { get; }

        public static UpdateDialogModel From(UpdatePackage package)
        {
            if (package == null)
                throw new ArgumentNullException(nameof(package));

            return new UpdateDialogModel(
                package.CurrentDisplayVersion,
                package.TargetDisplayVersion,
                string.IsNullOrWhiteSpace(package.ReleaseNotesMarkdown)
                    ? MissingReleaseNotes
                    : package.ReleaseNotesMarkdown);
        }
    }
}

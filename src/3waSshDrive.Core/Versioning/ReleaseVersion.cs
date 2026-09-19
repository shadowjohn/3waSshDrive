using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ThreeWa.SshDrive.Core.Versioning
{
    public sealed class ReleaseVersion
    {
        private static readonly Regex TagPattern = new Regex(
            @"^v(?<year>\d{4})\.(?<month>\d{2})\.(?<day>\d{2})\.(?<revision>\d{2})$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex PackagePattern = new Regex(
            @"^(?<year>\d{4})\.(?<monthDay>\d{3,4})\.(?<revision>\d{1,2})$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private ReleaseVersion(string tag, int year, int month, int day, int revision)
        {
            Tag = tag;
            PackageVersion = string.Format(
                CultureInfo.InvariantCulture,
                "{0}.{1}.{2}",
                year,
                month * 100 + day,
                revision);
            AssemblyVersion = new Version(year, month, day, revision);
        }

        public string Tag { get; }

        public string PackageVersion { get; }

        public Version AssemblyVersion { get; }

        public static ReleaseVersion ParseTag(string tag)
        {
            var match = TagPattern.Match(tag ?? string.Empty);
            if (!match.Success)
            {
                throw new FormatException("Release tag must use vYYYY.MM.DD.RR format.");
            }

            var year = ParseComponent(match, "year");
            var month = ParseComponent(match, "month");
            var day = ParseComponent(match, "day");
            var revision = ParseComponent(match, "revision");
            Validate(year, month, day, revision);

            return new ReleaseVersion(tag, year, month, day, revision);
        }

        public static string TagFromPackageVersion(string packageVersion)
        {
            var match = PackagePattern.Match(packageVersion ?? string.Empty);
            if (!match.Success)
            {
                throw new FormatException("Package version must contain three numeric components.");
            }

            var year = ParseComponent(match, "year");
            var monthDay = ParseComponent(match, "monthDay");
            var revision = ParseComponent(match, "revision");
            var month = monthDay / 100;
            var day = monthDay % 100;
            Validate(year, month, day, revision);

            return string.Format(
                CultureInfo.InvariantCulture,
                "v{0:D4}.{1:D2}.{2:D2}.{3:D2}",
                year,
                month,
                day,
                revision);
        }

        private static int ParseComponent(Match match, string groupName)
        {
            if (!int.TryParse(
                match.Groups[groupName].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var value))
            {
                throw new FormatException("Release version contains an invalid numeric component.");
            }

            return value;
        }

        private static void Validate(int year, int month, int day, int revision)
        {
            if (revision < 1 || revision > 99)
            {
                throw new FormatException("Release revision must be between 01 and 99.");
            }

            try
            {
                _ = new DateTime(year, month, day);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                throw new FormatException("Release version contains an invalid calendar date.", exception);
            }
        }
    }
}

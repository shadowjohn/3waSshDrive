using System;
using System.Collections.Generic;

namespace ThreeWa.SshDrive.Core.Paths
{
    public static class RemotePathMapper
    {
        public static string Map(string remoteRoot, string windowsPath)
        {
            var normalizedRoot = NormalizeRoot(remoteRoot);
            var segments = new List<string>();

            foreach (var segment in (windowsPath ?? string.Empty)
                .Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (segment == ".")
                    continue;
                if (segment == "..")
                    throw new ArgumentException(
                        "Parent path segments are not allowed.",
                        nameof(windowsPath));

                segments.Add(segment);
            }

            if (segments.Count == 0)
                return normalizedRoot;

            var suffix = string.Join("/", segments);
            return normalizedRoot == "/"
                ? "/" + suffix
                : normalizedRoot + "/" + suffix;
        }

        public static string NormalizeRoot(string remoteRoot)
        {
            if (string.IsNullOrWhiteSpace(remoteRoot) || !remoteRoot.StartsWith("/"))
                throw new ArgumentException(
                    "Remote root must be an absolute POSIX path.",
                    nameof(remoteRoot));

            var normalized = remoteRoot.TrimEnd('/');
            return normalized.Length == 0 ? "/" : normalized;
        }
    }
}

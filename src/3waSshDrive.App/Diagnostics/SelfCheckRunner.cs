using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using ThreeWa.SshDrive.Core.Versioning;
using Velopack;
using Velopack.Sources;

namespace ThreeWa.SshDrive.App.Diagnostics
{
    internal sealed class SelfCheckContext
    {
        public string BaseDirectory { get; set; }

        public string DisplayVersion { get; set; }

        public string PackageVersion { get; set; }

        public Version AssemblyVersion { get; set; }

        public Version FileVersion { get; set; }

        public bool Is64BitProcess { get; set; }

        public bool VelopackBootstrapSucceeded { get; set; }

        public string UpdateMode { get; set; }
    }

    internal sealed class SelfCheckResult
    {
        public SelfCheckResult(bool success, string message)
        {
            Success = success;
            Message = message;
        }

        public bool Success { get; }

        public string Message { get; }
    }

    internal static class SelfCheckRunner
    {
        internal static readonly IReadOnlyList<string> RequiredFiles = new[]
        {
            "3waSshDrive.exe",
            "3waSshDrive.Core.dll",
            "3waSshDrive.FileSystem.dll",
            "3waSshDrive.Sftp.dll",
            "3waSshDrive.WinFsp.dll",
            "Renci.SshNet.dll",
            "Velopack.dll",
            "Newtonsoft.Json.dll",
            @"runtime\winfsp-2.1.25156-manifest.json",
            @"Assets\background.png",
            @"Assets\header_logo.png"
        };

        internal static SelfCheckResult Evaluate(SelfCheckContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (!context.VelopackBootstrapSucceeded)
            {
                return Failure("Velopack bootstrap did not complete.");
            }

            if (!context.Is64BitProcess)
            {
                return Failure("Process architecture is not x64.");
            }

            if (string.IsNullOrWhiteSpace(context.BaseDirectory))
            {
                return Failure("Application base directory is unavailable.");
            }

            foreach (var relativePath in RequiredFiles)
            {
                if (!File.Exists(Path.Combine(context.BaseDirectory, relativePath)))
                {
                    return Failure("Required file is missing: " + relativePath);
                }
            }

            ReleaseVersion releaseVersion;
            try
            {
                releaseVersion = ReleaseVersion.ParseTag(context.DisplayVersion);
            }
            catch (FormatException)
            {
                return Failure("Display version is invalid.");
            }

            if (!string.Equals(
                releaseVersion.PackageVersion,
                context.PackageVersion,
                StringComparison.Ordinal))
            {
                return Failure("Package version does not match the display version.");
            }

            if (context.AssemblyVersion != releaseVersion.AssemblyVersion)
            {
                return Failure("Assembly version does not match the display version.");
            }

            if (context.FileVersion != releaseVersion.AssemblyVersion)
            {
                return Failure("File version does not match the display version.");
            }

            return new SelfCheckResult(
                true,
                "Version=" + context.DisplayVersion +
                " Package=" + context.PackageVersion +
                " UpdateMode=" + context.UpdateMode);
        }

        internal static int RunCurrentProcess(
            bool velopackBootstrapSucceeded,
            TextWriter output)
        {
            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            try
            {
                var assembly = typeof(SelfCheckRunner).Assembly;
                var informationalVersion = assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion;
                var packageVersion = assembly
                    .GetCustomAttributes<AssemblyMetadataAttribute>()
                    .FirstOrDefault(attribute => string.Equals(
                        attribute.Key,
                        "VelopackPackageVersion",
                        StringComparison.Ordinal))
                    ?.Value;
                var fileVersionText = FileVersionInfo
                    .GetVersionInfo(assembly.Location)
                    .FileVersion;

                Version.TryParse(fileVersionText, out var fileVersion);

                var source = new GithubSource(
                    "https://github.com/shadowjohn/3waSshDrive",
                    accessToken: null,
                    prerelease: false);
                var manager = new UpdateManager(source);
                var updateMode = manager.IsPortable
                    ? "Portable"
                    : manager.IsInstalled
                        ? "Installed"
                        : "Unmanaged";

                var result = Evaluate(new SelfCheckContext
                {
                    BaseDirectory = AppDomain.CurrentDomain.BaseDirectory,
                    DisplayVersion = informationalVersion,
                    PackageVersion = packageVersion,
                    AssemblyVersion = assembly.GetName().Version,
                    FileVersion = fileVersion,
                    Is64BitProcess = Environment.Is64BitProcess,
                    VelopackBootstrapSucceeded = velopackBootstrapSucceeded,
                    UpdateMode = updateMode
                });

                output.WriteLine(
                    result.Success
                        ? "SELF-CHECK OK " + result.Message
                        : "SELF-CHECK FAILED: " + result.Message);
                return result.Success ? 0 : 1;
            }
            catch (Exception exception)
            {
                output.WriteLine(
                    "SELF-CHECK FAILED: diagnostics (" +
                    exception.GetType().Name +
                    ")");
                return 1;
            }
        }

        private static SelfCheckResult Failure(string message)
        {
            return new SelfCheckResult(false, message);
        }
    }
}

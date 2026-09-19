using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace ThreeWa.SshDrive.FileSystem.Mounting
{
    public sealed class WinFspRuntimeVerifier
    {
        public WinFspRuntimeVerifier(
            string expectedFileVersion,
            string expectedNativeDllSha256,
            string expectedDriverSha256)
        {
            ExpectedFileVersion = RequireValue(
                expectedFileVersion,
                nameof(expectedFileVersion));
            ExpectedNativeDllSha256 = NormalizeHash(
                expectedNativeDllSha256,
                nameof(expectedNativeDllSha256));
            ExpectedDriverSha256 = NormalizeHash(
                expectedDriverSha256,
                nameof(expectedDriverSha256));
        }

        public string ExpectedFileVersion { get; }

        public string ExpectedNativeDllSha256 { get; }

        public string ExpectedDriverSha256 { get; }

        public WinFspRuntimeVerification Verify(
            string nativeDllPath,
            string driverPath)
        {
            if (!File.Exists(nativeDllPath))
                return WinFspRuntimeVerification.Failure(
                    "WinFsp native DLL was not found: " + nativeDllPath);
            if (!File.Exists(driverPath))
                return WinFspRuntimeVerification.Failure(
                    "WinFsp driver was not found: " + driverPath);

            var nativeDllHash = ComputeSha256(nativeDllPath);
            if (!string.Equals(
                ExpectedNativeDllSha256,
                nativeDllHash,
                StringComparison.OrdinalIgnoreCase))
            {
                return WinFspRuntimeVerification.Failure(
                    "WinFsp native DLL SHA-256 does not match the pinned runtime.");
            }

            var driverHash = ComputeSha256(driverPath);
            if (!string.Equals(
                ExpectedDriverSha256,
                driverHash,
                StringComparison.OrdinalIgnoreCase))
            {
                return WinFspRuntimeVerification.Failure(
                    "WinFsp driver SHA-256 does not match the pinned runtime.");
            }

            var nativeDllVersion = FileVersionInfo.GetVersionInfo(nativeDllPath).FileVersion;
            var driverVersion = FileVersionInfo.GetVersionInfo(driverPath).FileVersion;
            if (!string.Equals(
                ExpectedFileVersion,
                nativeDllVersion,
                StringComparison.Ordinal) ||
                !string.Equals(
                    ExpectedFileVersion,
                    driverVersion,
                    StringComparison.Ordinal))
            {
                return WinFspRuntimeVerification.Failure(
                    "WinFsp native DLL and driver must both use version " +
                    ExpectedFileVersion + ".");
            }

            return WinFspRuntimeVerification.Success(
                nativeDllPath,
                driverPath,
                nativeDllHash,
                driverHash);
        }

        private static string ComputeSha256(string path)
        {
            using (var sha256 = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                return BitConverter.ToString(sha256.ComputeHash(stream))
                    .Replace("-", string.Empty);
            }
        }

        private static string RequireValue(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A value is required.", parameterName);
            return value;
        }

        private static string NormalizeHash(string value, string parameterName)
        {
            var normalized = RequireValue(value, parameterName)
                .Replace("-", string.Empty)
                .Trim();
            if (normalized.Length != 64)
            {
                throw new ArgumentException(
                    "A SHA-256 hash must contain 64 hexadecimal characters.",
                    parameterName);
            }

            return normalized;
        }
    }

    public sealed class WinFspRuntimeVerification
    {
        private WinFspRuntimeVerification(
            bool isValid,
            string error,
            string nativeDllPath,
            string driverPath,
            string nativeDllSha256,
            string driverSha256)
        {
            IsValid = isValid;
            Error = error;
            NativeDllPath = nativeDllPath;
            DriverPath = driverPath;
            NativeDllSha256 = nativeDllSha256;
            DriverSha256 = driverSha256;
        }

        public bool IsValid { get; }

        public string Error { get; }

        public string NativeDllPath { get; }

        public string DriverPath { get; }

        public string NativeDllSha256 { get; }

        public string DriverSha256 { get; }

        public static WinFspRuntimeVerification Success(
            string nativeDllPath,
            string driverPath,
            string nativeDllSha256,
            string driverSha256)
        {
            return new WinFspRuntimeVerification(
                true,
                null,
                nativeDllPath,
                driverPath,
                nativeDllSha256,
                driverSha256);
        }

        public static WinFspRuntimeVerification Failure(string error)
        {
            return new WinFspRuntimeVerification(
                false,
                error,
                null,
                null,
                null,
                null);
        }
    }
}

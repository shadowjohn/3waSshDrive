using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Microsoft.Win32;

namespace ThreeWa.SshDrive.FileSystem.Mounting
{
    public static class WinFspRuntimePreflight
    {
        private const string RegistryPath =
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\WinFsp";
        private const string ManifestResourceSuffix =
            "runtime.winfsp-2.1.25156-manifest.json";

        private static readonly PinnedRuntimeManifest Manifest = LoadManifest();

        public static WinFspRuntimeVerification CheckX64()
        {
            var installDirectory = Registry.GetValue(
                RegistryPath,
                "InstallDir",
                null) as string;
            var sxsDirectory = Registry.GetValue(
                RegistryPath,
                "SxsDir",
                null) as string;
            if (string.IsNullOrWhiteSpace(installDirectory) ||
                string.IsNullOrWhiteSpace(sxsDirectory))
            {
                return WinFspRuntimeVerification.Failure(
                    "WinFsp v2.1 Core runtime is not installed.");
            }

            var nativeDllPath = Path.Combine(
                installDirectory,
                "bin",
                "winfsp-x64.dll");
            var driverPath = Path.Combine(
                sxsDirectory,
                "bin",
                "winfsp-x64.sys");

            var verifier = new WinFspRuntimeVerifier(
                Manifest.X64.FileVersion,
                Manifest.X64.NativeDll.Sha256,
                Manifest.X64.Driver.Sha256);
            return verifier.Verify(nativeDllPath, driverPath);
        }

        private static PinnedRuntimeManifest LoadManifest()
        {
            var assembly = typeof(WinFspRuntimePreflight).Assembly;
            var resourceName = Array.Find(
                assembly.GetManifestResourceNames(),
                name => name.EndsWith(
                    ManifestResourceSuffix,
                    StringComparison.OrdinalIgnoreCase));
            if (resourceName == null)
            {
                throw new InvalidOperationException(
                    "The pinned WinFsp runtime manifest is missing from the application.");
            }

            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                var serializer = new DataContractJsonSerializer(
                    typeof(PinnedRuntimeManifest));
                var manifest = (PinnedRuntimeManifest)serializer.ReadObject(stream);
                if (manifest?.X64?.NativeDll == null || manifest.X64.Driver == null ||
                    string.IsNullOrWhiteSpace(manifest.X64.FileVersion))
                {
                    throw new InvalidOperationException(
                        "The pinned WinFsp runtime manifest is incomplete.");
                }

                return manifest;
            }
        }

        [DataContract]
        private sealed class PinnedRuntimeManifest
        {
            [DataMember(Name = "x64")]
            public X64RuntimeManifest X64 { get; set; }
        }

        [DataContract]
        private sealed class X64RuntimeManifest
        {
            [DataMember(Name = "fileVersion")]
            public string FileVersion { get; set; }

            [DataMember(Name = "nativeDll")]
            public RuntimeFileManifest NativeDll { get; set; }

            [DataMember(Name = "driver")]
            public RuntimeFileManifest Driver { get; set; }
        }

        [DataContract]
        private sealed class RuntimeFileManifest
        {
            [DataMember(Name = "sha256")]
            public string Sha256 { get; set; }
        }
    }
}

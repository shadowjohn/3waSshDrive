using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace ThreeWa.SshDrive.FileSystem.Mounting
{
    public static class WinFspInstaller
    {
        public const string PackageId = "WinFsp.WinFsp";
        public const string DefaultArguments =
            "install WinFsp.WinFsp --accept-source-agreements --accept-package-agreements";

        public static ProcessStartInfo CreateProcessStartInfo(string arguments = DefaultArguments)
        {
            var wingetExe = ResolveWingetPath();
            return new ProcessStartInfo
            {
                FileName = wingetExe,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
        }

        public static string ResolveWingetPath()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                var aliasPath = Path.Combine(localAppData, @"Microsoft\WindowsApps\winget.exe");
                if (File.Exists(aliasPath))
                    return aliasPath;
            }

            return "winget";
        }

        public static async Task<WinFspRuntimeVerification> InstallAsync(
            Func<ProcessStartInfo, Process> processLauncher = null,
            Func<WinFspRuntimeVerification> preflightChecker = null)
        {
            processLauncher = processLauncher ?? Process.Start;
            preflightChecker = preflightChecker ?? WinFspRuntimePreflight.CheckX64;

            var startInfo = CreateProcessStartInfo();
            Process process;
            try
            {
                process = processLauncher(startInfo);
            }
            catch (Win32Exception ex)
            {
                throw new InvalidOperationException(
                    "找不到 winget 指令。請確認系統支援 winget 或手動安裝 WinFsp 驅動。",
                    ex);
            }

            if (process == null)
            {
                throw new InvalidOperationException("無法啟動 winget 安裝程序。");
            }

            using (process)
            {
                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();

                await Task.Run(() => process.WaitForExit());
                var stdout = await stdoutTask;
                var stderr = await stderrTask;

                if (process.ExitCode != 0)
                {
                    var detail = !string.IsNullOrWhiteSpace(stderr) ? stderr : stdout;
                    throw new InvalidOperationException(
                        $"winget 安裝 WinFsp 失敗 (結束代碼 {process.ExitCode}): {detail?.Trim()}");
                }
            }

            var verification = preflightChecker();
            if (!verification.IsValid)
            {
                throw new InvalidOperationException(
                    "WinFsp 安裝已結束，但驅動校驗未通過: " + verification.Error);
            }

            return verification;
        }
    }
}

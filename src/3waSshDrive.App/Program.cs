using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using ThreeWa.SshDrive.Core.Logging;
using ThreeWa.SshDrive.Core.Utils;

namespace ThreeWa.SshDrive.App
{
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        [STAThread]
        private static void Main()
        {
            // ponytail: Single-instance lock via lock.pid to prevent duplicate runs.
            if (!SingleInstanceLock.TryAcquire(out var appLock))
            {
                TryActivateExistingWindow();
                MessageBox.Show(
                    "3waSshDrive 已經在執行中，請勿重複啟動。",
                    "3waSshDrive",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            using (appLock)
            {
                // ponytail: Hook all unhandled exception sinks to CrashLogger for automatic crash recording.
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            // 1. UI Thread unhandled exceptions
            Application.ThreadException += (sender, e) =>
            {
                CrashLogger.Log("Application.ThreadException", e.Exception);
                try
                {
                    MessageBox.Show(
                        $"程式發生未預期的異常 (已記錄至 log 目錄)：\n\n{e.Exception.Message}",
                        "3waSshDrive - 異常錯誤",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
                catch
                {
                }
            };

            // 2. Non-UI / background thread unhandled exceptions
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                var ex = e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString());
                CrashLogger.Log($"AppDomain.UnhandledException (IsTerminating={e.IsTerminating})", ex);
            };

            // 3. Unobserved task exceptions
            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                CrashLogger.Log("TaskScheduler.UnobservedTaskException", e.Exception);
                e.SetObserved();
            };

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                CrashLogger.Log("Program.Main", ex);
                throw;
            }
            }
        }

        private static void TryActivateExistingWindow()
        {
            try
            {
                var current = Process.GetCurrentProcess();
                var processes = Process.GetProcessesByName(current.ProcessName);
                foreach (var p in processes)
                {
                    if (p.Id != current.Id && p.MainWindowHandle != IntPtr.Zero)
                    {
                        ShowWindow(p.MainWindowHandle, SW_RESTORE);
                        SetForegroundWindow(p.MainWindowHandle);
                        break;
                    }
                }
            }
            catch
            {
            }
        }
    }
}

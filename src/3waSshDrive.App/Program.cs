using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using ThreeWa.SshDrive.App.Diagnostics;
using ThreeWa.SshDrive.Core.Utils;
using Velopack;

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
        private static int Main(string[] args)
        {
            var options = StartupOptions.Parse(args);
            if (options.SelfCheck)
            {
                SelfCheckConsole.AttachParent();
            }

            try
            {
                VelopackApp.Build()
                    .SetAutoApplyOnStartup(false)
                    .Run();
            }
            catch (Exception exception)
            {
                if (options.SelfCheck)
                {
                    Console.Error.WriteLine(
                        "SELF-CHECK FAILED: Velopack bootstrap (" +
                        exception.GetType().Name +
                        ")");
                    return 1;
                }

                CrashLogger.Log("Velopack.Bootstrap", exception);
                return 1;
            }

            if (options.SelfCheck)
            {
                return SelfCheckRunner.RunCurrentProcess(true, Console.Out);
            }

            if (!SingleInstanceLock.TryAcquire(out var appLock))
            {
                TryActivateExistingWindow();
                MessageBox.Show(
                    "3waSshDrive 已經在執行中，請勿重複啟動。",
                    "3waSshDrive",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return 0;
            }

            using (appLock)
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

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

                AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
                {
                    var exception = e.ExceptionObject as Exception ?? new Exception();
                    CrashLogger.Log("AppDomain.UnhandledException", exception);
                };

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
                    return 0;
                }
                catch (Exception exception)
                {
                    CrashLogger.Log("Program.Main", exception);
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

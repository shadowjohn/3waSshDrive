using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using ThreeWa.SshDrive.Core.Logging;

namespace ThreeWa.SshDrive.App
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
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
}

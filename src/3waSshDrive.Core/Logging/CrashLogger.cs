using System;
using System.IO;
using System.Text;

namespace ThreeWa.SshDrive.Core.Logging
{
    public static class CrashLogger
    {
        private static readonly object SyncLock = new object();
        private static string _customLogDirectory;

        /// <summary>
        /// Gets or sets a custom log directory path. If null, defaults to [AppBaseDirectory]\log.
        /// </summary>
        public static string LogDirectory
        {
            get => _customLogDirectory ?? GetDefaultLogDirectory();
            set => _customLogDirectory = value;
        }

        /// <summary>
        /// Gets the full log file path using PHP 'Ymd.txt' format (e.g., 20260919.txt).
        /// </summary>
        public static string GetLogFilePath(DateTime? date = null)
        {
            var targetDate = date ?? DateTime.Now;
            var fileName = targetDate.ToString("yyyyMMdd") + ".txt";
            return Path.Combine(LogDirectory, fileName);
        }

        /// <summary>
        /// Logs a crash or unhandled exception to the day's log file using append mode.
        /// </summary>
        public static void Log(string source, Exception exception)
        {
            if (exception == null) return;

            lock (SyncLock)
            {
                try
                {
                    var filePath = GetLogFilePath();
                    var dir = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    var sb = new StringBuilder();
                    sb.AppendLine(new string('=', 80));
                    sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{source}]");
                    sb.AppendLine($"Exception Type: {exception.GetType().FullName}");
                    sb.AppendLine($"Message: {exception.Message}");
                    sb.AppendLine("StackTrace:");
                    sb.AppendLine(exception.ToString());
                    sb.AppendLine();

                    File.AppendAllText(filePath, sb.ToString(), Encoding.UTF8);
                }
                catch
                {
                    // ponytail: Crash logger must not throw unhandled exceptions if logging fails.
                }
            }
        }

        public static string GetDefaultLogDirectory()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;

                // If running inside source tree or git repo, locate repo root so logs appear in /log
                var current = new DirectoryInfo(baseDir);
                while (current != null)
                {
                    if (Directory.Exists(Path.Combine(current.FullName, ".git")) ||
                        File.Exists(Path.Combine(current.FullName, "3waSshDrive.sln")))
                    {
                        return Path.Combine(current.FullName, "log");
                    }

                    current = current.Parent;
                }

                return Path.Combine(baseDir, "log");
            }
            catch
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appData, "3waSshDrive", "log");
            }
        }
    }
}

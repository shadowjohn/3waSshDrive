using System;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text;

namespace ThreeWa.SshDrive.App.Diagnostics
{
    internal static class CrashLogger
    {
        private static readonly object SyncLock = new object();

        internal static string CreateSafeLine(string operation, Exception exception)
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            return CreateSafeLine(
                operation,
                exception.GetType().Name,
                exception.HResult);
        }

        internal static string CreateSafeLine(
            string operation,
            string exceptionType,
            int hresult)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:O} {1} failed ({2}, HRESULT 0x{3:X8})",
                DateTime.UtcNow,
                NormalizeIdentifier(operation, "UnknownOperation"),
                NormalizeIdentifier(exceptionType, "Exception"),
                hresult);
        }

        internal static void Log(string operation, Exception exception)
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            Log(operation, exception.GetType().Name, exception.HResult);
        }

        internal static void Log(
            string operation,
            string exceptionType,
            int hresult)
        {
            var line = CreateSafeLine(operation, exceptionType, hresult);

            lock (SyncLock)
            {
                try
                {
                    var logPath = GetLogPath();
                    Directory.CreateDirectory(Path.GetDirectoryName(logPath));
                    File.AppendAllText(logPath, line + Environment.NewLine, Encoding.UTF8);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
                catch (SecurityException)
                {
                }
            }
        }

        private static string GetLogPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "3waSshDrive",
                "logs",
                "application.log");
        }

        private static string NormalizeIdentifier(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            var builder = new StringBuilder(Math.Min(value.Length, 96));
            foreach (var character in value)
            {
                if (builder.Length == 96)
                {
                    break;
                }

                builder.Append(
                    char.IsLetterOrDigit(character) ||
                    character == '.' ||
                    character == '_' ||
                    character == '-'
                        ? character
                        : '_');
            }

            return builder.Length == 0 ? fallback : builder.ToString();
        }
    }
}

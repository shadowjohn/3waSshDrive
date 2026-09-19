using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace ThreeWa.SshDrive.Core.Utils
{
    public sealed class SingleInstanceLock : IDisposable
    {
        private readonly string _lockPath;
        private FileStream _fileStream;
        private bool _disposed;

        private SingleInstanceLock(string lockPath, FileStream fileStream)
        {
            _lockPath = lockPath;
            _fileStream = fileStream;
        }

        public string LockPath => _lockPath;

        public static string GetDefaultLockFilePath()
        {
            var localAppData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
            {
                throw new InvalidOperationException(
                    "LocalApplicationData is unavailable.");
            }

            return Path.Combine(
                localAppData,
                "3waSshDrive",
                "state",
                "lock.pid");
        }

        public static bool TryAcquire(out SingleInstanceLock instanceLock, string lockPath = null)
        {
            instanceLock = null;
            var path = lockPath ?? GetDefaultLockFilePath();

            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                // ponytail: FileShare.None provides OS-level exclusive locking that automatically releases if the process crashes.
                var stream = new FileStream(
                    path,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);

                stream.SetLength(0);
                using (var writer = new StreamWriter(stream, Encoding.UTF8, 1024, leaveOpen: true))
                {
                    writer.WriteLine(Process.GetCurrentProcess().Id);
                    writer.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                }
                stream.Flush();

                instanceLock = new SingleInstanceLock(path, stream);
                return true;
            }
            catch
            {
                // File is currently locked by another running instance, or path is inaccessible
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            try
            {
                _fileStream?.Dispose();
                _fileStream = null;
            }
            catch
            {
            }

            try
            {
                if (File.Exists(_lockPath))
                {
                    File.Delete(_lockPath);
                }
            }
            catch
            {
            }
        }
    }
}

using System;
using Fsp;

namespace ThreeWa.SshDrive.FileSystem.Mounting
{
    public sealed class WinFspHostAdapter : IFileSystemHost
    {
        private const string DebugLogPathEnvironmentVariable = "3WASSHDRIVE_WINFSP_DEBUG_LOG_PATH";
        private readonly FileSystemHost _host;

        public WinFspHostAdapter(SftpReadOnlyFileSystem fileSystem)
        {
            if (fileSystem == null)
                throw new ArgumentNullException(nameof(fileSystem));

            _host = new FileSystemHost(fileSystem);
        }

        public int Mount(string mountPoint)
        {
            var debugLogPath = Environment.GetEnvironmentVariable(DebugLogPathEnvironmentVariable);
            var debugLog = 0U;
            if (!string.IsNullOrWhiteSpace(debugLogPath))
            {
                var configureResult = FileSystemHost.SetDebugLogFile(debugLogPath);
                if (configureResult < 0)
                    return configureResult;

                debugLog = uint.MaxValue;
            }

            return _host.Mount(mountPoint, null, false, debugLog);
        }

        public void Unmount()
        {
            _host.Unmount();
        }

        public void Dispose()
        {
            _host.Dispose();
        }
    }
}

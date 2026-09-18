using System;
using System.IO;

namespace ThreeWa.SshDrive.Core.Remote
{
    public class RemoteFileSystemException : IOException
    {
        public RemoteFileSystemException(string message, Exception innerException = null)
            : base(message, innerException)
        {
        }
    }

    public sealed class RemotePathNotFoundException : RemoteFileSystemException
    {
        public RemotePathNotFoundException(string path, Exception innerException = null)
            : base("Remote path was not found: " + path, innerException)
        {
            Path = path;
        }

        public string Path { get; }
    }

    public sealed class RemoteAccessDeniedException : RemoteFileSystemException
    {
        public RemoteAccessDeniedException(string path, Exception innerException = null)
            : base("Access was denied for remote path: " + path, innerException)
        {
            Path = path;
        }

        public string Path { get; }
    }

    public sealed class RemoteConnectionException : RemoteFileSystemException
    {
        public RemoteConnectionException(string message, Exception innerException = null)
            : base(message, innerException)
        {
        }
    }
}

using System;
using System.IO;
using System.Net.Sockets;
using Renci.SshNet.Common;
using ThreeWa.SshDrive.Core.Remote;

namespace ThreeWa.SshDrive.Sftp
{
    public static class SshNetExceptionMapper
    {
        public static RemoteFileSystemException Translate(
            Exception exception,
            string requestedPath)
        {
            if (exception is RemoteFileSystemException remoteException)
                return remoteException;

            if (exception is SftpPathNotFoundException notFound)
            {
                var path = string.IsNullOrWhiteSpace(notFound.Path)
                    ? requestedPath
                    : notFound.Path;
                return new RemotePathNotFoundException(path, exception);
            }

            if (exception is SftpPermissionDeniedException)
                return new RemoteAccessDeniedException(requestedPath, exception);

            if (exception is SshConnectionException ||
                exception is SshAuthenticationException ||
                exception is SshOperationTimeoutException ||
                exception is SocketException)
            {
                return new RemoteConnectionException(
                    "SSH/SFTP connection failed: " + exception.Message,
                    exception);
            }

            if (exception is FileNotFoundException)
            {
                return new RemoteConnectionException(
                    exception.Message,
                    exception);
            }

            return new RemoteFileSystemException(
                "SFTP operation failed for " + requestedPath + ": " + exception.Message,
                exception);
        }
    }
}

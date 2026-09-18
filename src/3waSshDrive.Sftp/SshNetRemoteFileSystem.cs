using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;
using ThreeWa.SshDrive.Core.Models;
using ThreeWa.SshDrive.Core.Remote;
using ThreeWa.SshDrive.Sftp.Security;

namespace ThreeWa.SshDrive.Sftp
{
    public sealed class SshNetRemoteFileSystem : IRemoteFileSystem
    {
        private readonly object _lifecycleLock = new object();
        private readonly DriveProfile _profile;
        private readonly HostKeyPolicy _hostKeyPolicy;
        private SftpClient _client;
        private PrivateKeyFile _privateKey;
        private bool _disposed;

        public SshNetRemoteFileSystem(
            DriveProfile profile,
            HostKeyPolicy hostKeyPolicy)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _hostKeyPolicy = hostKeyPolicy ?? throw new ArgumentNullException(nameof(hostKeyPolicy));
        }

        public bool IsConnected
        {
            get
            {
                lock (_lifecycleLock)
                    return !_disposed && _client?.IsConnected == true;
            }
        }

        public void Connect()
        {
            lock (_lifecycleLock)
            {
                ThrowIfDisposed();
                if (_client?.IsConnected == true)
                    return;

                try
                {
                    var authentication = CreateAuthenticationMethod();
                    var connectionInfo = new ConnectionInfo(
                        _profile.Host,
                        _profile.Port,
                        _profile.Username,
                        authentication)
                    {
                        Timeout = TimeSpan.FromSeconds(15)
                    };

                    _client = new SftpClient(connectionInfo)
                    {
                        KeepAliveInterval = TimeSpan.FromSeconds(30),
                        OperationTimeout = TimeSpan.FromSeconds(30)
                    };
                    _client.HostKeyReceived += OnHostKeyReceived;
                    _client.Connect();
                }
                catch (Exception exception)
                {
                    CleanupClient();
                    throw SshNetExceptionMapper.Translate(
                        exception,
                        _profile.RemoteRoot);
                }
            }
        }

        public RemoteEntry GetEntry(string path)
        {
            return Execute(path, () =>
            {
                var attributes = _client.GetAttributes(path);
                return MapEntry(GetName(path), path, attributes);
            });
        }

        public IReadOnlyList<RemoteEntry> ListDirectory(string path)
        {
            return Execute(path, () =>
                (IReadOnlyList<RemoteEntry>)_client.ListDirectory(path)
                    .Where(entry => entry.Name != "." && entry.Name != "..")
                    .Select(MapEntry)
                    .ToList());
        }

        public Stream OpenRead(string path)
        {
            return Execute<Stream>(path, () => _client.OpenRead(path));
        }

        public Stream OpenFile(string path, FileMode mode, FileAccess access)
        {
            return Execute<Stream>(path, () => _client.Open(path, mode, access));
        }

        public void CreateDirectory(string path)
        {
            Execute(path, () => _client.CreateDirectory(path));
        }

        public void DeleteFile(string path)
        {
            Execute(path, () => _client.DeleteFile(path));
        }

        public void DeleteDirectory(string path)
        {
            Execute(path, () => _client.DeleteDirectory(path));
        }

        public void Rename(string oldPath, string newPath, bool replaceIfExists)
        {
            Execute(oldPath, () =>
            {
                if (replaceIfExists)
                {
                    try
                    {
                        _client.RenameFile(oldPath, newPath, isPosix: true);
                        return;
                    }
                    catch
                    {
                        try
                        {
                            if (_client.Exists(newPath))
                            {
                                var attrs = _client.GetAttributes(newPath);
                                if (attrs.IsDirectory)
                                    _client.DeleteDirectory(newPath);
                                else
                                    _client.DeleteFile(newPath);
                            }
                        }
                        catch
                        {
                            // If explicit removal fails, continue to standard rename
                        }
                    }
                }

                _client.RenameFile(oldPath, newPath, isPosix: false);
            });
        }

        private void Execute(string path, Action operation)
        {
            Execute(path, () =>
            {
                operation();
                return true;
            });
        }

        public void Dispose()
        {
            lock (_lifecycleLock)
            {
                if (_disposed)
                    return;

                _disposed = true;
                CleanupClient();
            }
        }

        private T Execute<T>(string path, Func<T> operation)
        {
            if (!IsConnected)
            {
                throw new RemoteConnectionException(
                    "The SSH/SFTP connection is not active.");
            }

            try
            {
                return operation();
            }
            catch (Exception exception)
            {
                throw SshNetExceptionMapper.Translate(exception, path);
            }
        }

        private void OnHostKeyReceived(object sender, HostKeyEventArgs eventArgs)
        {
            eventArgs.CanTrust = _hostKeyPolicy.Evaluate(
                eventArgs.FingerPrintSHA256);
        }

        private AuthenticationMethod CreateAuthenticationMethod()
        {
            if (_profile.AuthenticationMode == AuthenticationMode.Password)
            {
                if (string.IsNullOrEmpty(_profile.Password))
                {
                    throw new RemoteConnectionException(
                        "A password is required for password authentication.");
                }

                return new PasswordAuthenticationMethod(
                    _profile.Username,
                    _profile.Password);
            }

            var keyPath = Environment.ExpandEnvironmentVariables(
                _profile.PrivateKeyPath ?? string.Empty);
            if (!File.Exists(keyPath))
            {
                throw new RemoteConnectionException(
                    "Private key file was not found: " + keyPath);
            }

            _privateKey = new PrivateKeyFile(keyPath);
            return new PrivateKeyAuthenticationMethod(
                _profile.Username,
                _privateKey);
        }

        private static RemoteEntry MapEntry(ISftpFile entry)
        {
            return new RemoteEntry(
                entry.Name,
                entry.FullName,
                entry.IsDirectory,
                entry.Length,
                entry.LastAccessTimeUtc,
                entry.LastWriteTimeUtc);
        }

        private static RemoteEntry MapEntry(
            string name,
            string fullPath,
            SftpFileAttributes attributes)
        {
            return new RemoteEntry(
                name,
                fullPath,
                attributes.IsDirectory,
                attributes.Size,
                attributes.LastAccessTimeUtc,
                attributes.LastWriteTimeUtc);
        }

        private static string GetName(string path)
        {
            var normalized = (path ?? string.Empty).TrimEnd('/');
            if (normalized.Length == 0)
                return string.Empty;

            var separator = normalized.LastIndexOf('/');
            return separator < 0
                ? normalized
                : normalized.Substring(separator + 1);
        }

        private void CleanupClient()
        {
            if (_client != null)
            {
                _client.HostKeyReceived -= OnHostKeyReceived;
                if (_client.IsConnected)
                    _client.Disconnect();
                _client.Dispose();
                _client = null;
            }

            _privateKey?.Dispose();
            _privateKey = null;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SshNetRemoteFileSystem));
        }
    }
}

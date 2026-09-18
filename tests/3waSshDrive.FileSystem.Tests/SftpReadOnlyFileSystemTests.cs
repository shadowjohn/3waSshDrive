using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Fsp;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ThreeWa.SshDrive.Core.Remote;
using FileInfo = Fsp.Interop.FileInfo;

namespace ThreeWa.SshDrive.FileSystem.Tests
{
    [TestClass]
    public sealed class SftpReadOnlyFileSystemTests
    {
        private static readonly DateTime Timestamp =
            new DateTime(2026, 9, 18, 1, 2, 3, DateTimeKind.Utc);

        [TestMethod]
        public void GetSecurityByName_MapsDirectoryMetadata()
        {
            var remote = new FakeRemoteFileSystem();
            remote.AddEntry(Directory("project", "/home/feather/project"));
            var fileSystem = new SftpReadOnlyFileSystem(remote, "/home/feather");
            byte[] securityDescriptor = null;

            var status = fileSystem.GetSecurityByName(
                @"\project",
                out var attributes,
                ref securityDescriptor);

            Assert.AreEqual(FileSystemBase.STATUS_SUCCESS, status);
            Assert.AreEqual((uint)System.IO.FileAttributes.Directory, attributes);
            Assert.IsNull(securityDescriptor);
        }

        [TestMethod]
        public void OpenAndRead_ReadsRequestedOffsetIntoWinFspBuffer()
        {
            var remote = new FakeRemoteFileSystem();
            remote.AddEntry(
                File("README.md", "/home/feather/README.md", 6),
                Encoding.UTF8.GetBytes("abcdef"));
            var fileSystem = new SftpReadOnlyFileSystem(remote, "/home/feather");

            var openStatus = fileSystem.Open(
                @"\README.md",
                0,
                0,
                out var fileNode,
                out var fileDesc,
                out var fileInfo,
                out var normalizedName);

            Assert.AreEqual(FileSystemBase.STATUS_SUCCESS, openStatus);
            Assert.AreEqual((ulong)6, fileInfo.FileSize);
            Assert.IsNull(normalizedName);

            var buffer = Marshal.AllocHGlobal(3);
            try
            {
                var readStatus = fileSystem.Read(
                    fileNode,
                    fileDesc,
                    buffer,
                    2,
                    3,
                    out var bytesTransferred);
                var bytes = new byte[bytesTransferred];
                Marshal.Copy(buffer, bytes, 0, bytes.Length);

                Assert.AreEqual(FileSystemBase.STATUS_SUCCESS, readStatus);
                Assert.AreEqual((uint)3, bytesTransferred);
                Assert.AreEqual("cde", Encoding.UTF8.GetString(bytes));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
                fileSystem.Close(fileNode, fileDesc);
            }
        }

        [TestMethod]
        public void Read_ReturnsEndOfFileWhenOffsetEqualsFileLength()
        {
            var remote = new FakeRemoteFileSystem();
            remote.AddEntry(
                File("small.txt", "/home/feather/small.txt", 2),
                Encoding.UTF8.GetBytes("ok"));
            var fileSystem = new SftpReadOnlyFileSystem(remote, "/home/feather");
            fileSystem.Open(
                @"\small.txt", 0, 0,
                out var node, out var desc, out var info, out var name);
            var buffer = Marshal.AllocHGlobal(1);

            try
            {
                var status = fileSystem.Read(node, desc, buffer, 2, 1, out var transferred);

                Assert.AreEqual(FileSystemBase.STATUS_END_OF_FILE, status);
                Assert.AreEqual((uint)0, transferred);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
                fileSystem.Close(node, desc);
            }
        }

        [TestMethod]
        public void ReadDirectoryEntry_ReturnsStableCaseInsensitiveOrder()
        {
            var remote = new FakeRemoteFileSystem();
            var root = Directory("", "/home/feather");
            remote.AddEntry(root);
            remote.SetDirectory(
                "/home/feather",
                File("z.txt", "/home/feather/z.txt", 1),
                File("Alpha.txt", "/home/feather/Alpha.txt", 1));
            var fileSystem = new SftpReadOnlyFileSystem(remote, "/home/feather");
            fileSystem.Open(
                @"\", FileSystemBase.FILE_DIRECTORY_FILE, 0,
                out var node, out var desc, out var info, out var normalizedName);
            object context = null;
            var names = new List<string>();

            while (fileSystem.ReadDirectoryEntry(
                node, desc, null, null, ref context,
                out var fileName, out var entryInfo))
            {
                names.Add(fileName);
            }

            CollectionAssert.AreEqual(
                new[] { ".", "Alpha.txt", "z.txt" },
                names);
        }

        [TestMethod]
        public void Write_AlwaysRejectsReadOnlyVolume()
        {
            var fileSystem = new SftpReadOnlyFileSystem(
                new FakeRemoteFileSystem(),
                "/home/feather");

            var status = fileSystem.Write(
                null, null, IntPtr.Zero, 0, 0, false, false,
                out var bytesTransferred,
                out var fileInfo);

            Assert.AreEqual(FileSystemBase.STATUS_MEDIA_WRITE_PROTECTED, status);
            Assert.AreEqual((uint)0, bytesTransferred);
        }

        private static RemoteEntry Directory(string name, string path)
        {
            return new RemoteEntry(name, path, true, 0, Timestamp, Timestamp);
        }

        private static RemoteEntry File(string name, string path, long length)
        {
            return new RemoteEntry(name, path, false, length, Timestamp, Timestamp);
        }
    }
}

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
            remote.AddEntry(Directory("project", "/home/dev/project"));
            var fileSystem = new SftpReadOnlyFileSystem(remote, "/home/dev");
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
                File("README.md", "/home/dev/README.md", 6),
                Encoding.UTF8.GetBytes("abcdef"));
            var fileSystem = new SftpReadOnlyFileSystem(remote, "/home/dev");

            var openStatus = fileSystem.Open(
                @"\README.md",
                0,
                0x0001,
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
                File("small.txt", "/home/dev/small.txt", 2),
                Encoding.UTF8.GetBytes("ok"));
            var fileSystem = new SftpReadOnlyFileSystem(remote, "/home/dev");
            fileSystem.Open(
                @"\small.txt", 0, 0x0001,
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
            var root = Directory("", "/home/dev");
            remote.AddEntry(root);
            remote.SetDirectory(
                "/home/dev",
                File("z.txt", "/home/dev/z.txt", 1),
                File("Alpha.txt", "/home/dev/Alpha.txt", 1));
            var fileSystem = new SftpReadOnlyFileSystem(remote, "/home/dev");
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
        public void ReadDirectoryEntry_ReusesCachedListingAcrossDirectoryHandles()
        {
            var remote = new FakeRemoteFileSystem();
            var root = Directory("", "/home/dev");
            remote.AddEntry(root);
            remote.SetDirectory("/home/dev", File("entry.txt", "/home/dev/entry.txt", 1));
            var fileSystem = new SftpReadOnlyFileSystem(remote, "/home/dev");

            for (var iteration = 0; iteration < 2; iteration++)
            {
                fileSystem.Open(
                    @"\", FileSystemBase.FILE_DIRECTORY_FILE, 0,
                    out var node, out var desc, out _, out _);
                object context = null;
                while (fileSystem.ReadDirectoryEntry(node, desc, null, null, ref context, out _, out _))
                {
                }
                fileSystem.Close(node, desc);
            }

            Assert.AreEqual(1, remote.ListDirectoryCallCount);
        }

        [TestMethod]
        public void Write_AlwaysRejectsReadOnlyVolume()
        {
            var fileSystem = new SftpReadOnlyFileSystem(
                new FakeRemoteFileSystem(),
                "/home/dev",
                readOnly: true);

            var status = fileSystem.Write(
                null, null, IntPtr.Zero, 0, 0, false, false,
                out var bytesTransferred,
                out var fileInfo);

            Assert.AreEqual(FileSystemBase.STATUS_MEDIA_WRITE_PROTECTED, status);
            Assert.AreEqual((uint)0, bytesTransferred);
        }

        [TestMethod]
        public void CreateAndWrite_CreatesFileAndWritesContent()
        {
            var remote = new FakeRemoteFileSystem();
            var fileSystem = new SftpReadOnlyFileSystem(remote, "/home/dev");

            var createStatus = fileSystem.Create(
                @"\hello.txt",
                0,
                0x40000000,
                0,
                null,
                0,
                out var fileNode,
                out var fileDesc,
                out var fileInfo,
                out var normalizedName);

            Assert.AreEqual(FileSystemBase.STATUS_SUCCESS, createStatus);

            var textBytes = Encoding.UTF8.GetBytes("hello world");
            var buffer = Marshal.AllocHGlobal(textBytes.Length);
            try
            {
                Marshal.Copy(textBytes, 0, buffer, textBytes.Length);
                var writeStatus = fileSystem.Write(
                    fileNode,
                    fileDesc,
                    buffer,
                    0,
                    (uint)textBytes.Length,
                    false,
                    false,
                    out var bytesTransferred,
                    out fileInfo);

                Assert.AreEqual(FileSystemBase.STATUS_SUCCESS, writeStatus);
                Assert.AreEqual((uint)textBytes.Length, bytesTransferred);
                Assert.AreEqual((ulong)textBytes.Length, fileInfo.FileSize);
                fileSystem.Flush(fileNode, fileDesc, out _);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
                fileSystem.Close(fileNode, fileDesc);
            }

            var savedBytes = remote.GetContent("/home/dev/hello.txt");
            Assert.IsNotNull(savedBytes);
            Assert.AreEqual("hello world", Encoding.UTF8.GetString(savedBytes));
        }

        [TestMethod]
        public void Create_CreatesDirectory()
        {
            var remote = new FakeRemoteFileSystem();
            var fileSystem = new SftpReadOnlyFileSystem(remote, "/home/dev");

            var status = fileSystem.Create(
                @"\subfolder",
                FileSystemBase.FILE_DIRECTORY_FILE,
                0,
                0,
                null,
                0,
                out var fileNode,
                out var fileDesc,
                out var fileInfo,
                out _);

            Assert.AreEqual(FileSystemBase.STATUS_SUCCESS, status);
            Assert.IsTrue(remote.Exists("/home/dev/subfolder"));
            Assert.IsTrue(remote.GetEntry("/home/dev/subfolder").IsDirectory);
            fileSystem.Close(fileNode, fileDesc);
        }

        [TestMethod]
        public void Open_MetadataOnly_DoesNotCreateSftpFileHandle()
        {
            var remote = new FakeRemoteFileSystem();
            remote.AddEntry(File("metadata.txt", "/home/dev/metadata.txt", 4), Encoding.UTF8.GetBytes("test"));
            var fileSystem = new SftpReadOnlyFileSystem(remote, "/home/dev");

            var status = fileSystem.Open(
                @"\metadata.txt",
                0,
                0x0080,
                out var fileNode,
                out var fileDesc,
                out _,
                out _);

            Assert.AreEqual(FileSystemBase.STATUS_SUCCESS, status);
            Assert.AreEqual(0, remote.ActiveStreamCount);
            fileSystem.Close(fileNode, fileDesc);
            Assert.AreEqual(0, remote.StreamDisposeAttemptCount);
        }

        [TestMethod]
        public void Overwrite_TruncatesFileLengthToZero()
        {
            var remote = new FakeRemoteFileSystem();
            remote.AddEntry(File("data.txt", "/home/dev/data.txt", 5), Encoding.UTF8.GetBytes("12345"));
            var fileSystem = new SftpReadOnlyFileSystem(remote, "/home/dev");

            fileSystem.Open(
                @"\data.txt", 0, 0x40000000,
                out var node, out var desc, out var info, out _);

            try
            {
                var overwriteStatus = fileSystem.Overwrite(node, desc, 0, false, 0, out var newInfo);
                Assert.AreEqual(FileSystemBase.STATUS_SUCCESS, overwriteStatus);
                Assert.AreEqual((ulong)0, newInfo.FileSize);
            }
            finally
            {
                fileSystem.Close(node, desc);
            }
        }

        [TestMethod]
        public void Rename_RenamesRemoteFile()
        {
            var remote = new FakeRemoteFileSystem();
            remote.AddEntry(File("old.txt", "/home/dev/old.txt", 4), Encoding.UTF8.GetBytes("test"));
            var fileSystem = new SftpReadOnlyFileSystem(remote, "/home/dev");

            var status = fileSystem.Rename(null, null, @"\old.txt", @"\renamed.txt", true);

            Assert.AreEqual(FileSystemBase.STATUS_SUCCESS, status);
            Assert.IsFalse(remote.Exists("/home/dev/old.txt"));
            Assert.IsTrue(remote.Exists("/home/dev/renamed.txt"));
            Assert.AreEqual("test", Encoding.UTF8.GetString(remote.GetContent("/home/dev/renamed.txt")));
        }

        [TestMethod]
        public void Cleanup_WithDeleteFlag_DeletesFile()
        {
            var remote = new FakeRemoteFileSystem();
            remote.AddEntry(File("del.txt", "/home/dev/del.txt", 4), Encoding.UTF8.GetBytes("test"));
            var fileSystem = new SftpReadOnlyFileSystem(remote, "/home/dev");

            fileSystem.Open(
                @"\del.txt", 0, 0,
                out var node, out var desc, out var info, out _);

            fileSystem.SetDelete(node, desc, @"\del.txt", true);
            fileSystem.Cleanup(node, desc, @"\del.txt", FileSystemBase.CleanupDelete);

            Assert.IsFalse(remote.Exists("/home/dev/del.txt"));
        }

        [TestMethod]
        public void Cleanup_WithoutDeleteFlag_ReleasesSftpHandle()
        {
            var remote = new FakeRemoteFileSystem();
            var fileSystem = new SftpReadOnlyFileSystem(remote, "/home/dev");

            var status = fileSystem.Create(
                @"\release-handle.txt",
                0,
                0x40000000,
                0,
                null,
                0,
                out var fileNode,
                out var fileDesc,
                out _,
                out _);

            Assert.AreEqual(FileSystemBase.STATUS_SUCCESS, status);
            Assert.AreEqual(1, remote.ActiveStreamCount);

            fileSystem.Cleanup(fileNode, fileDesc, @"\release-handle.txt", 0);

            Assert.AreEqual(0, remote.ActiveStreamCount);
            fileSystem.Close(fileNode, fileDesc);
            Assert.AreEqual(0, remote.ActiveStreamCount);
            Assert.AreEqual(1, remote.StreamDisposeAttemptCount);
        }

        [TestMethod]
        public void GetSecurityByName_UsesCache_WhenListingDirectoryFirst()
        {
            var remote = new FakeRemoteFileSystem();
            var root = Directory("", "/home/dev");
            remote.AddEntry(root);
            remote.SetDirectory("/home/dev", File("file1.txt", "/home/dev/file1.txt", 10));

            var fileSystem = new SftpReadOnlyFileSystem(remote, "/home/dev");

            // Open directory and read entries (populates _entryCache & _dirChildrenCache)
            fileSystem.Open(
                @"\", FileSystemBase.FILE_DIRECTORY_FILE, 0,
                out var node, out var desc, out _, out _);
            object context = null;
            while (fileSystem.ReadDirectoryEntry(node, desc, null, null, ref context, out _, out _))
            {
            }

            // Remove file1 from remote directly (simulating it was cached)
            remote.DeleteFile("/home/dev/file1.txt");

            // GetSecurityByName for file1 hits memory cache
            byte[] sd = null;
            var status = fileSystem.GetSecurityByName(@"\file1.txt", out var attrs, ref sd);
            Assert.AreEqual(FileSystemBase.STATUS_SUCCESS, status);

            // Probing a non-existent file like desktop.ini is rejected immediately via parent dir cache
            var missingStatus = fileSystem.GetSecurityByName(@"\desktop.ini", out _, ref sd);
            Assert.AreEqual(FileSystemBase.STATUS_OBJECT_NAME_NOT_FOUND, missingStatus);
        }

        [TestMethod]
        public void GetVolumeInfo_ReturnsRemoteVolumeInformation()
        {
            var remote = new FakeRemoteFileSystem
            {
                VolumeInfo = new RemoteVolumeInfo(500UL * 1024 * 1024 * 1024, 250UL * 1024 * 1024 * 1024)
            };
            var fileSystem = new SftpReadOnlyFileSystem(remote, "/home/dev");

            var status = fileSystem.GetVolumeInfo(out var volumeInfo);

            Assert.AreEqual(FileSystemBase.STATUS_SUCCESS, status);
            Assert.AreEqual(500UL * 1024 * 1024 * 1024, volumeInfo.TotalSize);
            Assert.AreEqual(250UL * 1024 * 1024 * 1024, volumeInfo.FreeSize);
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

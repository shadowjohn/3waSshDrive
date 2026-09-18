using System.Diagnostics;
using System.Reflection;
using Fsp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ThreeWa.SshDrive.FileSystem.Tests
{
    [TestClass]
    public sealed class WinFspAssemblyMetadataTests
    {
        [TestMethod]
        public void CompiledWinFspAssembly_MatchesNativeLoaderContract()
        {
            var assembly = typeof(FileSystemBase).Assembly;
            var product = assembly.GetCustomAttribute<AssemblyProductAttribute>();
            var fileVersion = FileVersionInfo.GetVersionInfo(assembly.Location);

            Assert.IsNotNull(product);
            Assert.AreEqual("WinFsp", product.Product);
            Assert.AreEqual(2, fileVersion.FileMajorPart);
            Assert.AreEqual(1, fileVersion.FileMinorPart);
        }
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;
using ThreeWa.SshDrive.App.Updates;

namespace ThreeWa.SshDrive.App.Tests
{
    [TestClass]
    public sealed class UpdateDialogModelTests
    {
        [TestMethod]
        public void From_UsesExternalVersionsAndReleaseNotes()
        {
            var model = UpdateDialogModel.From(Package(
                current: "v2026.09.19.01",
                target: "v2026.09.19.02",
                notes: "修正重啟"));

            Assert.AreEqual("v2026.09.19.01", model.CurrentVersion);
            Assert.AreEqual("v2026.09.19.02", model.TargetVersion);
            Assert.AreEqual("修正重啟", model.ReleaseNotes);
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void From_EmptyNotes_UsesSafeFallback(string notes)
        {
            Assert.AreEqual(
                "此版本未提供 release notes。",
                UpdateDialogModel.From(Package(notes: notes)).ReleaseNotes);
        }

        private static UpdatePackage Package(
            string current = "v2026.09.19.01",
            string target = "v2026.09.19.02",
            string notes = "release notes")
        {
            return new UpdatePackage(
                current,
                target,
                "2026.919.2",
                notes,
                new object());
        }
    }
}

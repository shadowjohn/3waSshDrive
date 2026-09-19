using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ThreeWa.SshDrive.App.Updates;

namespace ThreeWa.SshDrive.App.Tests
{
    [TestClass]
    public sealed class UpdateActivityGateTests
    {
        [TestMethod]
        public async Task Preparation_WaitsForExistingActivityAndRejectsNewActivity()
        {
            var gate = new UpdateActivityGate();
            Assert.IsTrue(gate.TryBeginActivity(out var activity));
            Assert.IsTrue(gate.TryBeginPreparation(out var preparation));

            var idle = gate.WaitForIdleAsync();

            Assert.IsTrue(gate.IsPreparing);
            Assert.IsFalse(idle.IsCompleted);
            Assert.IsFalse(gate.TryBeginActivity(out var rejected));
            Assert.IsNull(rejected);

            activity.Dispose();
            await idle;

            preparation.Dispose();
            Assert.IsFalse(gate.IsPreparing);
            Assert.IsTrue(gate.TryBeginActivity(out var nextActivity));
            nextActivity.Dispose();
        }

        [TestMethod]
        public void Preparation_RemainsExclusiveUntilItsLeaseIsReleased()
        {
            var gate = new UpdateActivityGate();
            Assert.IsTrue(gate.TryBeginPreparation(out var first));

            Assert.IsFalse(gate.TryBeginPreparation(out var second));
            Assert.IsNull(second);
            Assert.IsTrue(gate.IsPreparing);

            first.Dispose();
            first.Dispose();

            Assert.IsFalse(gate.IsPreparing);
            Assert.IsTrue(gate.TryBeginPreparation(out var afterRelease));
            afterRelease.Dispose();
        }
    }
}

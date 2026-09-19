using ThreeWa.SshDrive.App.Diagnostics;

namespace ThreeWa.SshDrive.App.Updates
{
    internal interface IUpdateLog
    {
        void Failure(string operation, string exceptionType, int hresult);
    }

    internal sealed class UpdateLog : IUpdateLog
    {
        internal static IUpdateLog Default { get; } = new UpdateLog();

        private UpdateLog()
        {
        }

        public void Failure(
            string operation,
            string exceptionType,
            int hresult)
        {
            CrashLogger.Log(operation, exceptionType, hresult);
        }
    }
}

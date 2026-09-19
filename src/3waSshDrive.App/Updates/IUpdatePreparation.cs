using System;
using System.Threading.Tasks;

namespace ThreeWa.SshDrive.App.Updates
{
    internal interface IUpdatePreparation
    {
        Task<UpdatePreparationResult> PrepareAsync(TimeSpan timeout);

        void ResumeAfterFailure();
    }

    internal sealed class UpdatePreparationResult
    {
        private UpdatePreparationResult(
            bool success,
            bool canResumeImmediately,
            string message)
        {
            Success = success;
            CanResumeImmediately = canResumeImmediately;
            Message = message ?? string.Empty;
        }

        public bool Success { get; }

        public bool CanResumeImmediately { get; }

        public string Message { get; }

        public static UpdatePreparationResult Ready()
        {
            return new UpdatePreparationResult(
                success: true,
                canResumeImmediately: false,
                message: string.Empty);
        }

        public static UpdatePreparationResult Blocked(
            string message,
            bool canResumeImmediately)
        {
            return new UpdatePreparationResult(
                success: false,
                canResumeImmediately,
                message);
        }
    }
}

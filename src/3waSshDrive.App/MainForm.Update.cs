using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ThreeWa.SshDrive.App.Updates;

namespace ThreeWa.SshDrive.App
{
    internal sealed partial class MainForm
    {
        private async Task CheckForUpdatesAsync(bool manual)
        {
            if (_updateActivityGate.IsPreparing || _isApplyingUpdate)
            {
                if (manual)
                    SetStatus("更新準備尚未完成，請稍後再試。");
                return;
            }

            if (!_updateService.TryBeginSession(out var session))
            {
                if (manual)
                    SetStatus("更新檢查已在進行中。");
                return;
            }

            _isCheckingUpdates = true;
            _checkUpdatesButton.Enabled = false;

            CheckingUpdateDialog loadingDialog = null;
            var cts = new CancellationTokenSource();

            try
            {
                UpdateCheckResult result;
                if (manual)
                {
                    SetStatus("正在檢查更新…");
                    SetMascotSpeech("正在檢查有沒有新版本喔～芳寶搜尋中…🔍✨");

                    loadingDialog = new CheckingUpdateDialog(cts);
                    loadingDialog.Show(this);
                    loadingDialog.Refresh();

                    var checkTask = session.CheckAsync(cts.Token);
                    var minDelayTask = Task.Delay(800, cts.Token);
                    await Task.WhenAll(checkTask, minDelayTask);
                    result = await checkTask;
                }
                else
                {
                    result = await session.CheckAsync(CancellationToken.None);
                }

                if (loadingDialog != null && !loadingDialog.IsDisposed)
                {
                    loadingDialog.Close();
                    loadingDialog.Dispose();
                    loadingDialog = null;
                }

                if (result.Kind == UpdateCheckKind.Available)
                {
                    if (manual)
                        SetMascotSpeech($"發現新版本 {result.Package.TargetDisplayVersion}！快來看看有什麼新功能吧～🎉");
                    ShowUpdateDialog(session, result.Package);
                    return;
                }

                if (manual)
                {
                    if (result.Kind == UpdateCheckKind.UpToDate)
                        SetMascotSpeech("太棒了！目前已經是最新版本囉～芳寶隨時為你待命！💪✨");
                    else if (result.Kind == UpdateCheckKind.Disabled)
                        SetMascotSpeech("這是免安裝 (Portable) 版本喔～如需自動更新可下載安裝版！📦");
                    else if (result.Kind == UpdateCheckKind.Failed)
                        SetMascotSpeech("檢查更新時遇到一點狀況…請稍後再試一次看看喔！🐾");
                }

                if (!UpdateNotificationPolicy.ShouldShowMessage(
                    manual,
                    result.Kind))
                {
                    return;
                }

                ShowUpdateCheckResult(result);
            }
            catch (OperationCanceledException)
            {
                if (manual)
                {
                    SetStatus("已取消檢查更新。");
                    SetMascotSpeech("好喔，已經幫你取消檢查更新囉～☕");
                }
            }
            catch (Exception exception)
            {
                UpdateLog.Default.Failure(
                    "UpdateFlow",
                    exception.GetType().Name,
                    exception.HResult);
                if (manual)
                {
                    SetMascotSpeech("檢查更新失敗，請稍後再試一次喔！🐾");
                    ShowUpdateMessage(
                        "無法檢查更新，請稍後再試。",
                        MessageBoxIcon.Warning);
                }
            }
            finally
            {
                if (loadingDialog != null && !loadingDialog.IsDisposed)
                {
                    loadingDialog.Close();
                    loadingDialog.Dispose();
                }
                cts.Dispose();
                session.Dispose();
                _isCheckingUpdates = false;
                if (!IsDisposed && !Disposing)
                {
                    _checkUpdatesButton.Enabled =
                        !_busy && !_isApplyingUpdate;
                }
            }
        }

        private void ShowUpdateCheckResult(UpdateCheckResult result)
        {
            switch (result.Kind)
            {
                case UpdateCheckKind.Disabled:
                    ShowUpdateMessage(
                        "此版本為 portable／未安裝版本，請下載 Setup 啟用自動更新。",
                        MessageBoxIcon.Information);
                    break;
                case UpdateCheckKind.UpToDate:
                    ShowUpdateMessage(
                        "目前已是最新版本。",
                        MessageBoxIcon.Information);
                    break;
                case UpdateCheckKind.Failed:
                    ShowUpdateMessage(
                        result.Message,
                        MessageBoxIcon.Warning);
                    break;
            }
        }

        private void ShowUpdateMessage(string message, MessageBoxIcon icon)
        {
            SetStatus(message, icon != MessageBoxIcon.Warning);
            MessageBox.Show(
                this,
                message,
                "3waSshDrive 更新",
                MessageBoxButtons.OK,
                icon);
        }

        private void ShowUpdateDialog(
            UpdateSession session,
            UpdatePackage package)
        {
            using (var dialog = new UpdateDialog(
                UpdateDialogModel.From(package)))
            {
                dialog.UpdateRequested += async (sender, args) =>
                {
                    try
                    {
                        var result = await _updateCoordinator
                            .DownloadAndApplyAsync(
                                session,
                                package,
                                dialog.SetProgress,
                                CancellationToken.None);
                        if (result.Kind != UpdateApplyKind.RestartRequested)
                            dialog.SetFailure(result.Message);
                    }
                    catch (Exception exception)
                    {
                        UpdateLog.Default.Failure(
                            "UpdateFlow",
                            exception.GetType().Name,
                            exception.HResult);
                        if (_isApplyingUpdate)
                            ((IUpdatePreparation)this).ResumeAfterFailure();
                        dialog.SetFailure(
                            "更新失敗，已保留目前版本。");
                    }
                };

                dialog.ShowDialog(this);
            }
        }

        async Task<UpdatePreparationResult> IUpdatePreparation.PrepareAsync(
            TimeSpan timeout)
        {
            if (!_updateActivityGate.TryBeginPreparation(
                out var preparationLease))
            {
                return UpdatePreparationResult.Blocked(
                    "另一個更新準備仍在進行中，已取消這次更新。",
                    canResumeImmediately: false);
            }

            _updatePreparationLease = preparationLease;
            _isApplyingUpdate = true;
            RefreshActionState();
            _reconnectTimer.Stop();

            var deadline = DateTime.UtcNow + timeout;
            var pendingActivity = _updateActivityGate.WaitForIdleAsync();
            if (!pendingActivity.IsCompleted)
            {
                var activityRemaining = deadline - DateTime.UtcNow;
                if (activityRemaining <= TimeSpan.Zero ||
                    await Task.WhenAny(
                        pendingActivity,
                        Task.Delay(activityRemaining)) != pendingActivity)
                {
                    ResumeWhenActivityCompletes(pendingActivity);
                    return UpdatePreparationResult.Blocked(
                        "目前操作尚未完成，已取消更新；目前版本不會被替換。",
                        canResumeImmediately: false);
                }

                await pendingActivity;
            }

            foreach (var pair in _mountedDrives.ToList())
            {
                var disposeTask = Task.Run(() => pair.Value.Dispose());
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero ||
                    await Task.WhenAny(disposeTask, Task.Delay(remaining)) !=
                        disposeTask)
                {
                    ResumeWhenCleanupCompletes(pair.Key, disposeTask);
                    return UpdatePreparationResult.Blocked(
                        "磁碟卸載逾時，已取消更新；目前版本不會被替換。",
                        canResumeImmediately: false);
                }

                try
                {
                    await disposeTask;
                    _mountedDrives.Remove(pair.Key);
                }
                catch (Exception exception)
                {
                    UpdateLog.Default.Failure(
                        "UpdateUnmount",
                        exception.GetType().Name,
                        exception.HResult);
                    _mountedDrives.Remove(pair.Key);
                    RefreshDriveLetters();
                    return UpdatePreparationResult.Blocked(
                        "無法安全卸載所有磁碟，已取消更新。",
                        canResumeImmediately: true);
                }
            }

            RefreshDriveLetters();
            _isExplicitExit = true;
            return UpdatePreparationResult.Ready();
        }

        void IUpdatePreparation.ResumeAfterFailure()
        {
            ResumeAfterFailure();
        }

        private async void ResumeWhenCleanupCompletes(
            string driveLetter,
            Task pendingDispose)
        {
            try
            {
                await pendingDispose.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                UpdateLog.Default.Failure(
                    "UpdateUnmountLate",
                    exception.GetType().Name,
                    exception.HResult);
            }

            if (IsDisposed || Disposing || !IsHandleCreated)
                return;

            try
            {
                BeginInvoke(new Action(() =>
                {
                    _mountedDrives.Remove(driveLetter);
                    ResumeAfterFailure();
                }));
            }
            catch (InvalidOperationException)
            {
                // The form completed shutdown while cleanup was finishing.
            }
        }

        private async void ResumeWhenActivityCompletes(Task pendingActivity)
        {
            await pendingActivity;
            if (IsDisposed || Disposing)
                return;

            ResumeAfterFailure();
        }

        private void ResumeAfterFailure()
        {
            _isExplicitExit = false;
            _isApplyingUpdate = false;
            var preparationLease = _updatePreparationLease;
            _updatePreparationLease = null;
            preparationLease?.Dispose();
            RefreshActionState();
            RefreshDriveLetters();
            if (!IsDisposed && !Disposing)
                _reconnectTimer.Start();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ThreeWa.SshDrive.App.Diagnostics;
using ThreeWa.SshDrive.Core.Models;
using ThreeWa.SshDrive.Core.Profiles;
using ThreeWa.SshDrive.FileSystem.Mounting;

namespace ThreeWa.SshDrive.App
{
    internal sealed partial class MainForm
    {
        private readonly List<ProfileOperation> _profileOperations = new List<ProfileOperation>();
        private readonly SemaphoreSlim _connectionSlots = new SemaphoreSlim(2, 2);
        private readonly Dictionary<string, DateTime> _reconnectAfter = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly Button _cancelOperationButton = new Button();
        private long _nextOperationId;
        private Task _exitTask;
        private readonly Dictionary<string, ProfileResult> _profileResults = new Dictionary<string, ProfileResult>(StringComparer.OrdinalIgnoreCase);

        private sealed class ProfileResult
        {
            internal string Name, OriginalName, Status;
        }

        private sealed class ProfileOperation : IDisposable
        {
            internal readonly DriveProfile Profile;
            internal readonly string OriginalName;
            internal string PersistedName;
            internal readonly long Id;
            internal readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
            internal string Status;
            internal ProfileOperation(DriveProfile profile, string originalName, long id)
            {
                Profile = profile; OriginalName = originalName; PersistedName = originalName; Id = id;
                Status = "等待連線";
                Cancellation.CancelAfter(TimeSpan.FromSeconds(60));
            }
            public void Dispose() => Cancellation.Dispose();
        }

        private static DriveProfile Snapshot(DriveProfile p) => new DriveProfile
        {
            Name = p.Name, Host = p.Host, Port = p.Port, Username = p.Username,
            RemoteRoot = p.RemoteRoot, DriveLetter = p.DriveLetter,
            AuthenticationMode = p.AuthenticationMode, PrivateKeyPath = p.PrivateKeyPath,
            Password = p.Password, HostKeyFingerprintSha256 = p.HostKeyFingerprintSha256,
            ReadOnly = p.ReadOnly, AutoMountOnStartup = p.AutoMountOnStartup
        };

        private ProfileOperation FindOperation(string name, string drive, string originalName = null)
        {
            return _profileOperations.FirstOrDefault(op =>
                string.Equals(op.Profile.DriveLetter, drive, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(op.Profile.Name, name, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(originalName) && (string.Equals(op.Profile.Name, originalName, StringComparison.OrdinalIgnoreCase) || string.Equals(op.OriginalName, originalName, StringComparison.OrdinalIgnoreCase))) ||
                (!string.IsNullOrEmpty(op.OriginalName) && string.Equals(op.OriginalName, name, StringComparison.OrdinalIgnoreCase)));
        }

        private bool SelectedProfileIsBusy() => FindOperation(_name.Text.Trim(), SelectedDriveLetter(), _selectedProfileName) != null;

        private bool IsSelectedOperation(ProfileOperation op) =>
            _profileOperations.Any(active => active.Id == op.Id) &&
            (string.Equals(_name.Text.Trim(), op.Profile.Name, StringComparison.OrdinalIgnoreCase) ||
             (!string.IsNullOrEmpty(op.OriginalName) && string.Equals(_selectedProfileName, op.OriginalName, StringComparison.OrdinalIgnoreCase))) &&
            string.Equals(SelectedDriveLetter(), op.Profile.DriveLetter, StringComparison.OrdinalIgnoreCase);

        private string ProfileResultStatus(string name, string drive)
        {
            if (drive == null || !_profileResults.TryGetValue(drive, out var result)) return null;
            return string.Equals(name, result.Name, StringComparison.OrdinalIgnoreCase) || string.Equals(name, result.OriginalName, StringComparison.OrdinalIgnoreCase) ? result.Status : null;
        }

        private void ApplySelectedOperationStatus()
        {
            var pending = FindOperation(_name.Text.Trim(), SelectedDriveLetter(), _selectedProfileName);
            var status = pending?.Status ?? ProfileResultStatus(_name.Text.Trim(), SelectedDriveLetter());
            if (status != null) SetStatus(status);
        }

        private void SetOperationStatus(ProfileOperation op, string text, bool success = false)
        {
            op.Status = text;
            if (IsSelectedOperation(op)) SetStatus(text, success);
            RefreshProfileGrid();
        }

        private async Task<bool> RunProfileOperationAsync(DriveProfile source, string originalName,
            Func<ProfileOperation, Task> action, bool showError = true, bool usesConnectionSlot = true)
        {
            var profile = Snapshot(source);
            if (_busy || _isApplyingUpdate || FindOperation(profile.Name, profile.DriveLetter, originalName) != null)
                return false;
            if (!_updateActivityGate.TryBeginActivity(out var activity)) return false;
            using (activity)
            using (var op = new ProfileOperation(profile, originalName, ++_nextOperationId))
            {
                _profileOperations.Add(op);
                _profileResults.Remove(profile.DriveLetter);
                RefreshActionState(); RefreshProfileGrid();
                var acquired = false;
                var succeeded = false;
                try
                {
                    if (usesConnectionSlot)
                    {
                        await _connectionSlots.WaitAsync(op.Cancellation.Token);
                        acquired = true;
                    }
                    op.Cancellation.Token.ThrowIfCancellationRequested();
                    await action(op);
                    succeeded = true;
                    return true;
                }
                catch (OperationCanceledException)
                {
                    SetOperationStatus(op, "已取消或逾時；清理完成");
                    return false;
                }
                catch (Exception ex)
                {
                    _logOperationFailure("ProfileOperation", ex);
                    SetOperationStatus(op, "操作失敗: " + ex.Message, false);
                    if (showError && IsSelectedOperation(op)) ShowError(ex);
                    return false;
                }
                finally
                {
                    if (acquired) _connectionSlots.Release();
                    if (!succeeded)
                        _profileResults[profile.DriveLetter] = new ProfileResult { Name = profile.Name, OriginalName = originalName, Status = op.Status };
                    else _profileResults.Remove(profile.DriveLetter);
                    _profileOperations.Remove(op);
                    if (!IsDisposed && !Disposing) { RefreshActionState(); RefreshProfileGrid(); }
                }
            }
        }

        private void CancelSelectedOperation()
        {
            var op = FindOperation(_name.Text.Trim(), SelectedDriveLetter(), _selectedProfileName);
            if (op == null) return;
            op.Cancellation.Cancel();
            SetOperationStatus(op, "正在取消，等待連線及清理結束");
        }

        private void CancelProfileOperations()
        {
            foreach (var op in _profileOperations.ToArray()) op.Cancellation.Cancel();
        }

        private Task TestAndTrustAsync() => RunSelectedOperationAsync(test: true, mount: false);
        private Task TestAndMountAsync() => RunSelectedOperationAsync(test: true, mount: true);
        private Task MountAsync() => RunSelectedOperationAsync(test: false, mount: true);

        private Task RunSelectedOperationAsync(bool test, bool mount)
        {
            // Capture both profile and rename origin before any await or selection change.
            var profile = ReadForm();
            var originalName = _selectedProfileName;
            return RunProfileOperationAsync(profile, originalName, async op =>
            {
                if (test)
                {
                    ValidateForProbe(op.Profile);
                    SetOperationStatus(op, "正在測試連線 " + op.Profile.Name);
                    var result = await Task.Run(() => _connectionProbe.Probe(op.Profile, op.Cancellation.Token));
                    op.Cancellation.Token.ThrowIfCancellationRequested();
                    op.Profile.HostKeyFingerprintSha256 = result.HostKeyFingerprintSha256;
                    if (IsSelectedOperation(op)) _hostFingerprint.Text = result.HostKeyFingerprintSha256;
                    PersistOperationProfile(op);
                    SetOperationStatus(op, "連線信任已儲存 " + op.Profile.Name, true);
                }
                if (mount) await MountProfileAsync(op, saveProfile: true);
            });
        }

        private void PersistOperationProfile(ProfileOperation op)
        {
            var selected = _selectedProfileName;
            var preserveSelection = !IsSelectedOperation(op);
            var draft = preserveSelection ? ReadForm() : null;
            UpsertProfile(op.Profile, op.PersistedName, selectProfile: !preserveSelection);
            op.PersistedName = op.Profile.Name;
            if (preserveSelection)
            {
                _selectedProfileName = selected;
                WriteForm(draft);
                SelectProfileGrid(selected);
            }
        }

        private async Task MountProfileAsync(ProfileOperation op, bool saveProfile)
        {
            var profile = op.Profile;
            var errors = DriveProfileValidator.Validate(profile);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
            if (saveProfile) ValidateProfileCanBeSaved(profile, op.PersistedName);
            var runtime = _checkRuntime();
            if (!runtime.IsValid) throw new InvalidOperationException(runtime.Error);
            var drive = profile.DriveLetter.ToUpperInvariant();
            if (_mountedDrives.ContainsKey(drive) || _getLogicalDrives().Contains(drive))
                throw new InvalidOperationException(drive + " is already mounted.");
            SetOperationStatus(op, "Mounting " + profile.Name + " at " + drive + "…");
            var mounted = await Task.Run(() => _mountManager.Mount(profile, op.Cancellation.Token));
            if (op.Cancellation.IsCancellationRequested || IsDisposed || Disposing)
            {
                await Task.Run(() => mounted.Dispose());
                op.Cancellation.Token.ThrowIfCancellationRequested();
                return;
            }
            _mountedDrives.Add(drive, mounted);
            RefreshDriveLetters();
            if (saveProfile) PersistOperationProfile(op);
            SetOperationStatus(op, "Mounted " + profile.Name + " at " + drive, true);
        }

        private async Task MountAllProfilesAsync()
        {
            var profiles = _profileItems.Select(Snapshot).ToArray();
            await Task.WhenAll(profiles.Where(p => !IsMounted(p.DriveLetter)).Select(p =>
                RunProfileOperationAsync(p, p.Name, op => MountProfileAsync(op, false), showError: false)));
        }

        private async Task AutoMountProfilesOnStartupAsync()
        {
            var profiles = _profileItems.Where(AutoMountProfilePolicy.ShouldAttemptOnStartup).Select(Snapshot).ToArray();
            await Task.WhenAll(profiles.Select(p =>
                RunProfileOperationAsync(p, p.Name, op => MountProfileAsync(op, false), showError: false)));
        }

        private Task UnmountAsync()
        {
            var profile = ReadForm();
            return RunProfileOperationAsync(profile, _selectedProfileName, UnmountProfileAsync, usesConnectionSlot: false);
        }

        private async Task UnmountProfileAsync(ProfileOperation op)
        {
            var drive = op.Profile.DriveLetter;
            if (!_mountedDrives.TryGetValue(drive, out var mounted)) throw new InvalidOperationException(drive + " is not mounted.");
            SetOperationStatus(op, "Unmounting " + drive);
            // Unmount is not abandonable: keep reservation/update lease until real cleanup finishes.
            await Task.Run(() => mounted.Dispose());
            _mountedDrives.Remove(drive); _reconnectAfter.Remove(drive);
            RefreshDriveLetters();
            SetOperationStatus(op, "Unmounted " + drive, true);
        }

        private Task UnmountAllProfilesAsync()
        {
            var profiles = _mountedDrives.Values.Select(d => new DriveProfile { Name = d.ProfileName, DriveLetter = d.DriveLetter }).ToArray();
            return Task.WhenAll(profiles.Select(p => RunProfileOperationAsync(p, p.Name, UnmountProfileAsync, false, usesConnectionSlot: false)));
        }

        private Task CheckAndReconnectDrivesAsync()
        {
            if (_busy || _isApplyingUpdate || IsDisposed || Disposing) return Task.CompletedTask;
            var disconnected = _mountedDrives.Values.Where(d => !d.IsConnected && !d.IsConnecting).ToArray();
            return Task.WhenAll(disconnected.Select(d => ReconnectDriveAsync(d)));
        }

        private async Task ReconnectDriveAsync(MountedDrive drive)
        {
            if (_reconnectAfter.TryGetValue(drive.DriveLetter, out var next) && DateTime.UtcNow < next) return;
            var profile = new DriveProfile { Name = drive.ProfileName, DriveLetter = drive.DriveLetter };
            var succeeded = await RunProfileOperationAsync(profile, profile.Name, async op =>
            {
                SetOperationStatus(op, "正在重新連線 " + drive.DriveLetter);
                await Task.Run(() => drive.EnsureConnected(op.Cancellation.Token));
                SetOperationStatus(op, "Mounted at " + drive.DriveLetter, true);
            }, showError: false);
            _reconnectAfter[drive.DriveLetter] = DateTime.UtcNow.AddSeconds(succeeded ? 0 : 10);
        }

        private Task ExitApplicationAsync()
        {
            if (IsDisposed) return Task.CompletedTask;
            if (_exitTask == null || _exitTask.IsCompleted) _exitTask = ExitAfterOperationsAsync();
            return _exitTask;
        }

        private async Task ExitAfterOperationsAsync()
        {
            if (!_updateActivityGate.TryBeginPreparation(out var preparation)) return;
            using (preparation)
            {
                _isApplyingUpdate = true;
                _reconnectTimer.Stop();
                CancelProfileOperations();
                RefreshActionState();
                SetStatus("正在結束連線及清理掛載…");
                try
                {
                    await _updateActivityGate.WaitForIdleAsync();
                    var drives = _mountedDrives.Values.ToArray();
                    await Task.WhenAll(drives.Select(d => Task.Run(() => d.Dispose())));
                    _mountedDrives.Clear();
                    _isExplicitExit = true;
                    Close();
                }
                catch (Exception ex)
                {
                    _logOperationFailure("ExitCleanup", ex);
                    _isApplyingUpdate = false;
                    _exitTask = null;
                    RefreshActionState();
                    SetStatus("清理尚未完成，請再試一次退出", false);
                    _reconnectTimer.Start();
                }
            }
        }
    }
}

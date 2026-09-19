# 3waSshDrive Phase C In-App Updater Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 讓 Velopack Setup 安裝版能安全檢查 GitHub Release、提示使用者、下載驗證、卸載磁碟、套用新版並重新啟動，同時讓 portable ZIP 明確停用原地更新。

**Architecture:** App 專案以 `IUpdateBackend` 隔離 Velopack，`UpdateService` 用 single-session gate 保證一次只有一個更新工作，`UpdateCoordinator` 負責下載後的安全靜止與 apply。`MainForm` 改為 partial class，將更新 UI 與既有 1,300 行表單分開；既有 mount、reconnect 與 UI 邏輯只做必要接點，不重寫檔案系統架構。

**Tech Stack:** C#；.NET Framework 4.7.2；WinForms；MSTest；Velopack 1.2.0；GitHub Releases stable channel；既有 Phase B Windows build/release workflows。

**Spec:** `docs/superpowers/specs/2026-09-19-phase-b-c-release-updater-design.md`

## Global Constraints

- Phase B 必須先完成且能發布可安裝的 Velopack 1.2.0 release。
- Velopack bootstrap 固定在正常 UI 與 `SingleInstanceLock` 之前，並使用 `SetAutoApplyOnStartup(false)`。
- Update source 固定為 `https://github.com/shadowjohn/3waSshDrive`，只採 stable、published Releases；不接受 draft 或 prerelease。
- 只有 Setup 安裝版可更新；portable／unmanaged build 不得原地替換。
- 不做 silent forced update；發現新版後一定由使用者選擇「更新」或「稍後」。
- 套用前停止 reconnect timer、阻止新 mount、卸載全部 3waSshDrive drive 並關閉 SFTP 資源。
- 任一卸載失敗或逾時都不得呼叫 Velopack apply；目前版本保持可啟動。
- 更新應用程式，不更新 WinFsp；WinFsp 仍固定由既有 helper／preflight 管理。
- 更新 log 不記錄 exception 原始訊息、密碼、token、主機、私鑰內容或私鑰路徑。
- `lock.pid` 固定於 `%LocalAppData%\3waSshDrive\state\lock.pid`，檔案存在本身不構成 lock。
- Phase D 效能量測與調校完全排除。

## Review Focus

- 啟動時離線／GitHub 429：背景檢查只安全記錄，不顯示 modal error、不影響 mount，Task 2 與 Task 6 測試固定此行為。
- 使用者連點主畫面與 tray 的「檢查更新」：只能存在一個 `UpdateSession`，Task 2 的 concurrency test 固定此行為。
- 下載完整性失敗：不得進入卸載或 apply，Task 3 的 checksum-failure test 固定此行為。
- 部分磁碟已卸載、下一顆卸載失敗或逾時：不得 apply；可安全恢復時重新開放 UI，Task 4 與 Task 3 測試固定此行為。
- apply 後新程序遇到 stale `lock.pid`：OS handle 已釋放即可啟動，不以刪檔為必要條件，Task 7 的 restart smoke 固定此行為。

---

## File Structure

### 新增檔案

- `src/3waSshDrive.App/Updates/UpdateModels.cs`：update result／package／preparation 等 immutable model。
- `src/3waSshDrive.App/Updates/IUpdateBackend.cs`：UpdateService 唯一依賴的 backend contract。
- `src/3waSshDrive.App/Updates/VelopackUpdateBackend.cs`：Velopack `GithubSource`、`UpdateManager` 與 native `UpdateInfo` adapter。
- `src/3waSshDrive.App/Updates/UpdateLog.cs`：只輸出 exception type 與 HRESULT 的去敏 log。
- `src/3waSshDrive.App/Updates/UpdateService.cs`：single-session gate。
- `src/3waSshDrive.App/Updates/UpdateSession.cs`：一次 check／download／apply 的生命週期。
- `src/3waSshDrive.App/Updates/IUpdatePreparation.cs`：更新前安全靜止 contract。
- `src/3waSshDrive.App/Updates/UpdateCoordinator.cs`：download → prepare → apply ordering。
- `src/3waSshDrive.App/Updates/UpdateDialogModel.cs`：可測的顯示資料與空 release-note fallback。
- `src/3waSshDrive.App/UpdateDialog.cs`：更新提示與下載進度 WinForms dialog。
- `src/3waSshDrive.App/MainForm.Update.cs`：MainForm 更新入口、startup check、tray／header button、mount quiescence。
- `tests/3waSshDrive.App.Tests/UpdateServiceTests.cs`：disabled／up-to-date／available／offline／single-session tests。
- `tests/3waSshDrive.App.Tests/UpdateCoordinatorTests.cs`：下載、checksum、卸載與 apply ordering tests。
- `tests/3waSshDrive.App.Tests/UpdateDialogModelTests.cs`：版本與 release-note view model tests。
- `scripts/Test-UpdateRestart.ps1`：安裝版 lock／relaunch smoke helper。

### 修改檔案

- `src/3waSshDrive.App/MainForm.cs`：class 改 partial、注入 UpdateService、停用／恢復既有操作。
- `src/3waSshDrive.App/Program.cs`：沿用 Phase B bootstrap ordering，不新增 silent apply。
- `src/3waSshDrive.App/Diagnostics/SelfCheckRunner.cs`：報告 installed／portable／unmanaged update mode，但三種模式都可 self-check。
- `tests/3waSshDrive.App.Tests/SelfCheckRunnerTests.cs`：更新模式不影響基礎 self-check。
- `.github/workflows/windows-release.yml`：安裝 smoke 加入 updater mode 與 restart-lock 檢查。
- `README.md`、`history.md`：使用方式、失敗邊界與更新 SOP。

---

### Task 1: 建立更新模型與 Velopack backend boundary

**Files:**
- Create: `src/3waSshDrive.App/Updates/UpdateModels.cs`
- Create: `src/3waSshDrive.App/Updates/IUpdateBackend.cs`
- Create: `src/3waSshDrive.App/Updates/VelopackUpdateBackend.cs`
- Create: `tests/3waSshDrive.App.Tests/VelopackUpdateBackendTests.cs`

**Interfaces:**
- Produces: `IUpdateBackend.IsInstalled`、`IsPortable`、`CurrentPackageVersion`。
- Produces: `Task<UpdatePackage> IUpdateBackend.CheckAsync(CancellationToken token)`; `null` means up-to-date。
- Produces: `Task IUpdateBackend.DownloadAsync(UpdatePackage package, Action<int> progress, CancellationToken token)`。
- Produces: `void IUpdateBackend.ApplyAndRestart(UpdatePackage package)`。
- Produces: `UpdatePackage` with `CurrentDisplayVersion`, `TargetDisplayVersion`, `PackageVersion`, `ReleaseNotesMarkdown`, and internal `NativeToken`。

- [ ] **Step 1: 寫 backend contract RED tests**

Expose one internal constructor accepting an `IVelopackClient` so tests do not call GitHub. Define the interface beside the backend:

```csharp
internal interface IVelopackClient
{
    bool IsInstalled { get; }
    bool IsPortable { get; }
    string CurrentVersion { get; }
    Task<VelopackUpdateData> CheckAsync();
    Task DownloadAsync(object nativeToken, Action<int> progress, CancellationToken token);
    void ApplyAndRestart(object nativeToken);
}

internal sealed class VelopackUpdateData
{
    public string TargetVersion { get; }
    public string ReleaseNotesMarkdown { get; }
    public object NativeToken { get; }

    public VelopackUpdateData(
        string targetVersion,
        string releaseNotesMarkdown,
        object nativeToken)
    {
        TargetVersion = targetVersion;
        ReleaseNotesMarkdown = releaseNotesMarkdown ?? string.Empty;
        NativeToken = nativeToken ?? throw new ArgumentNullException(nameof(nativeToken));
    }
}
```

`VelopackClient` is the production implementation of `IVelopackClient`: it owns the
`UpdateManager`, wraps `CheckForUpdatesAsync()` into `VelopackUpdateData`, and unwraps
`NativeToken` only inside `DownloadAsync` and `ApplyAndRestart`. In the test file,
define `FakeVelopackClient` as a nested sealed class with the same five members,
factory methods `Installed(current, target, notes)` and `UpToDate()`, and call counters
for check, download, and apply. No test double may reference Velopack native types.

Tests must assert:

```csharp
[TestMethod]
public async Task CheckAsync_MapsPackageVersionToDisplayTagAndNotes()
{
    var fake = FakeVelopackClient.Installed(
        current: "2026.919.1",
        target: "2026.919.2",
        notes: "修正更新流程");
    var backend = new VelopackUpdateBackend(fake);

    var package = await backend.CheckAsync(CancellationToken.None);

    Assert.AreEqual("v2026.09.19.01", package.CurrentDisplayVersion);
    Assert.AreEqual("v2026.09.19.02", package.TargetDisplayVersion);
    Assert.AreEqual("2026.919.2", package.PackageVersion);
    Assert.AreEqual("修正更新流程", package.ReleaseNotesMarkdown);
}

[TestMethod]
public async Task CheckAsync_NoRemoteUpdate_ReturnsNull()
{
    var backend = new VelopackUpdateBackend(FakeVelopackClient.UpToDate());
    Assert.IsNull(await backend.CheckAsync(CancellationToken.None));
}
```

- [ ] **Step 2: 執行 App tests，確認 RED**

```powershell
dotnet test .\tests\3waSshDrive.App.Tests\3waSshDrive.App.Tests.csproj -c Release --nologo
```

Expected: missing update types.

- [ ] **Step 3: 實作 immutable models**

Use these exact public-to-App signatures:

```csharp
internal sealed class UpdatePackage
{
    public string CurrentDisplayVersion { get; }
    public string TargetDisplayVersion { get; }
    public string PackageVersion { get; }
    public string ReleaseNotesMarkdown { get; }
    internal object NativeToken { get; }

    public UpdatePackage(
        string currentDisplayVersion,
        string targetDisplayVersion,
        string packageVersion,
        string releaseNotesMarkdown,
        object nativeToken)
    {
        CurrentDisplayVersion = currentDisplayVersion;
        TargetDisplayVersion = targetDisplayVersion;
        PackageVersion = packageVersion;
        ReleaseNotesMarkdown = releaseNotesMarkdown ?? string.Empty;
        NativeToken = nativeToken ?? throw new ArgumentNullException(nameof(nativeToken));
    }
}
```

Also define `UpdateCheckKind` values `Disabled`, `Busy`, `UpToDate`, `Available`, `Failed`; `UpdateCheckResult`; `UpdateOperationResult`; `UpdateApplyKind` values `RestartRequested`, `Blocked`, `Failed`; and `UpdateApplyResult`. Each result has a static named factory so later tasks never construct conflicting combinations. `UpdateOperationResult` has `bool Success`, `string Message`, `Succeeded()` and `Failed(string)`; `UpdateApplyResult` has `UpdateApplyKind Kind`, `string Message`, `RestartRequested()`, `Blocked(string)` and `Failed(string)`.

- [ ] **Step 4: 實作 official Velopack 1.2.0 adapter**

The production client must construct:

```csharp
var source = new GithubSource(
    "https://github.com/shadowjohn/3waSshDrive",
    accessToken: null,
    prerelease: false);
var manager = new UpdateManager(source);
```

Map `UpdateInfo.TargetFullRelease.Version`, `NotesMarkdown`, and the native `UpdateInfo`. Use `ReleaseVersion.TagFromPackageVersion` for current and target display versions. Download with:

```csharp
await manager.DownloadUpdatesAsync(
    (UpdateInfo)nativeToken,
    progress,
    token).ConfigureAwait(false);
```

Apply with:

```csharp
var update = (UpdateInfo)nativeToken;
manager.ApplyUpdatesAndRestart(update.TargetFullRelease);
```

Do not provide a GitHub token in the desktop client; the repository is public and asset downloads use public URLs.

- [ ] **Step 5: 執行 tests 與 commit**

```powershell
dotnet test .\tests\3waSshDrive.App.Tests\3waSshDrive.App.Tests.csproj -c Release --nologo
dotnet test .\3waSshDrive.sln -c Release --nologo
```

```bash
git add src/3waSshDrive.App/Updates tests/3waSshDrive.App.Tests/VelopackUpdateBackendTests.cs
git commit -m "feat: add Velopack update backend"
```

---

### Task 2: 建立 single-session UpdateService 與安全 log

**Files:**
- Create: `src/3waSshDrive.App/Updates/UpdateLog.cs`
- Create: `src/3waSshDrive.App/Updates/UpdateService.cs`
- Create: `src/3waSshDrive.App/Updates/UpdateSession.cs`
- Create: `tests/3waSshDrive.App.Tests/UpdateServiceTests.cs`

**Interfaces:**
- Produces: `UpdateService.CreateDefault()` using `VelopackUpdateBackend` and `UpdateLog.Default`。
- Produces: `bool UpdateService.TryBeginSession(out UpdateSession session)`。
- Produces: `Task<UpdateCheckResult> UpdateSession.CheckAsync(CancellationToken token)`。
- Produces: `Task<UpdateOperationResult> UpdateSession.DownloadAsync(UpdatePackage package, Action<int> progress, CancellationToken token)`。
- Produces: `UpdateOperationResult UpdateSession.ApplyAndRestart(UpdatePackage package)`。
- Session owns the gate until `Dispose()`; every code path must dispose it exactly once。

- [ ] **Step 1: 寫 service RED tests**

Use a fake backend with call counters and queued outcomes. Cover:

```csharp
[TestMethod]
public void TryBeginSession_RejectsConcurrentSession()
{
    var service = new UpdateService(FakeBackend.Installed(), new RecordingUpdateLog());
    Assert.IsTrue(service.TryBeginSession(out var first));
    Assert.IsFalse(service.TryBeginSession(out var second));
    Assert.IsNull(second);
    first.Dispose();
    Assert.IsTrue(service.TryBeginSession(out var third));
    third.Dispose();
}

[DataTestMethod]
[DataRow(false, false)]
[DataRow(true, true)]
public async Task CheckAsync_UnmanagedOrPortable_ReturnsDisabled(
    bool installed,
    bool portable)
{
    var service = new UpdateService(
        FakeBackend.WithMode(installed, portable),
        new RecordingUpdateLog());
    service.TryBeginSession(out var session);
    using (session)
    {
        var result = await session.CheckAsync(CancellationToken.None);
        Assert.AreEqual(UpdateCheckKind.Disabled, result.Kind);
    }
}

[TestMethod]
public async Task CheckAsync_BackendFailure_ReturnsSafeFailureAndLogsOnce()
{
    var exception = new InvalidOperationException(
        @"host.internal C:\keys\john.ppk token=abc");
    var log = new RecordingUpdateLog();
    var service = new UpdateService(
        FakeBackend.Throwing(exception),
        log);
    service.TryBeginSession(out var session);
    using (session)
    {
        var result = await session.CheckAsync(CancellationToken.None);
        Assert.AreEqual(UpdateCheckKind.Failed, result.Kind);
        Assert.AreEqual("無法檢查更新，請稍後再試。", result.Message);
    }
    Assert.AreEqual(1, log.Failures.Count);
    Assert.AreEqual("UpdateCheck", log.Failures[0].Operation);
    Assert.AreEqual("InvalidOperationException", log.Failures[0].ExceptionType);
    Assert.AreEqual(exception.HResult, log.Failures[0].HResult);
}
```

Also cover up-to-date and available results.

In `UpdateServiceTests`, define `FakeBackend` as a nested sealed `IUpdateBackend`
implementation with factories `Installed()`, `WithMode(bool installed, bool portable)`,
and `Throwing(Exception)`. It exposes check/download/apply call counters and returns a
single configured package or exception. Define `RecordingUpdateLog` as a nested sealed
`IUpdateLog` implementation. Its `RecordedFailure` contains only `Operation`,
`ExceptionType`, and `HResult`; its `Failures` list must never retain the original
exception object. Phase B `CrashLoggerTests` owns the on-disk redaction assertion.

- [ ] **Step 2: 執行 tests，確認 RED**

```powershell
dotnet test .\tests\3waSshDrive.App.Tests\3waSshDrive.App.Tests.csproj -c Release --nologo
```

- [ ] **Step 3: 實作 gate 與 safe logger**

`UpdateService` uses `Interlocked.CompareExchange(ref _activeSession, 1, 0)`. `UpdateSession.Dispose` calls one release delegate protected by its own `Interlocked.Exchange`.

Define the logging boundary exactly as:

```csharp
internal interface IUpdateLog
{
    void Failure(string operation, string exceptionType, int hresult);
}

internal sealed class UpdateLog : IUpdateLog
{
    internal static IUpdateLog Default { get; } = new UpdateLog();

    public void Failure(string operation, string exceptionType, int hresult)
    {
        CrashLogger.Log(operation, exceptionType, hresult);
    }
}
```

`UpdateSession.CheckAsync` ordering:

```csharp
if (!_backend.IsInstalled || _backend.IsPortable)
    return UpdateCheckResult.Disabled();

try
{
    var package = await _backend.CheckAsync(token).ConfigureAwait(false);
    return package == null
        ? UpdateCheckResult.UpToDate()
        : UpdateCheckResult.Available(package);
}
catch (OperationCanceledException) when (token.IsCancellationRequested)
{
    return UpdateCheckResult.Failed("更新檢查已取消。");
}
catch (Exception exception)
{
    _log.Failure(
        "UpdateCheck",
        exception.GetType().Name,
        exception.HResult);
    return UpdateCheckResult.Failed("無法檢查更新，請稍後再試。");
}
```

The Phase B crash logger formats only operation, exception type, and HRESULT:

```csharp
return string.Format(
    CultureInfo.InvariantCulture,
    "{0} failed ({1}, HRESULT 0x{2:X8})",
    operation,
    exceptionType,
    hresult);
```

`UpdateLog.Failure` delegates these already-safe fields to the Phase B crash logger.
Never pass, wrap, or persist the original exception across `IUpdateLog`, because any of
those paths can accidentally retain sensitive messages.

- [ ] **Step 4: 實作 download／apply result handling**

`DownloadAsync` catches backend exceptions, passes only type name and HRESULT to the
logger under operation `UpdateDownload`, and returns
`UpdateOperationResult.Failed("更新下載或驗證失敗，已保留目前版本。")`. It must never call apply.

`ApplyAndRestart` follows the same safe-field rule under `UpdateApply` and returns
failure. If the backend call returns in a test double, return success so the
coordinator can report `RestartRequested`; production Velopack exits the process.

- [ ] **Step 5: 執行 tests 與 commit**

```powershell
dotnet test .\tests\3waSshDrive.App.Tests\3waSshDrive.App.Tests.csproj -c Release --nologo
```

```bash
git add src/3waSshDrive.App/Updates/UpdateLog.cs src/3waSshDrive.App/Updates/UpdateService.cs src/3waSshDrive.App/Updates/UpdateSession.cs tests/3waSshDrive.App.Tests/UpdateServiceTests.cs
git commit -m "feat: serialize update sessions and redact logs"
```

---

### Task 3: 固定 download → quiesce → apply 順序

**Files:**
- Create: `src/3waSshDrive.App/Updates/IUpdatePreparation.cs`
- Create: `src/3waSshDrive.App/Updates/UpdateCoordinator.cs`
- Create: `tests/3waSshDrive.App.Tests/UpdateCoordinatorTests.cs`

**Interfaces:**
- Produces: `Task<UpdatePreparationResult> IUpdatePreparation.PrepareAsync(TimeSpan timeout)`。
- Produces: `void IUpdatePreparation.ResumeAfterFailure()`。
- Produces: `UpdateCoordinator(IUpdatePreparation preparation)`; the coordinator does not own `UpdateService`。
- Produces: `Task<UpdateApplyResult> UpdateCoordinator.DownloadAndApplyAsync(UpdateSession session, UpdatePackage package, Action<int> progress, CancellationToken token)`。

- [ ] **Step 1: 寫 coordinator RED tests**

Cover these exact orderings with event lists:

```csharp
[TestMethod]
public async Task DownloadAndApply_Success_DownloadsThenPreparesThenApplies()
{
    var events = new List<string>();
    var fixture = CoordinatorFixture.Success(events);

    var result = await fixture.Coordinator.DownloadAndApplyAsync(
        fixture.Session,
        fixture.Package,
        _ => { },
        CancellationToken.None);

    CollectionAssert.AreEqual(
        new[] { "download", "prepare", "apply" },
        events);
    Assert.AreEqual(UpdateApplyKind.RestartRequested, result.Kind);
}

[TestMethod]
public async Task DownloadAndApply_ChecksumFailure_DoesNotPrepareOrApply()
{
    var events = new List<string>();
    var fixture = CoordinatorFixture.DownloadFailure(
        events,
        new InvalidDataException("checksum mismatch"));

    var result = await fixture.RunAsync();

    CollectionAssert.AreEqual(new[] { "download" }, events);
    Assert.AreEqual(UpdateApplyKind.Failed, result.Kind);
}

[TestMethod]
public async Task DownloadAndApply_UnmountFailure_BlocksApplyAndResumes()
{
    var events = new List<string>();
    var fixture = CoordinatorFixture.PreparationFailure(
        events,
        canResumeImmediately: true);

    var result = await fixture.RunAsync();

    CollectionAssert.AreEqual(
        new[] { "download", "prepare", "resume" },
        events);
    Assert.AreEqual(UpdateApplyKind.Blocked, result.Kind);
}

[TestMethod]
public async Task DownloadAndApply_Timeout_DoesNotResumeWhileCleanupStillRuns()
{
    var events = new List<string>();
    var fixture = CoordinatorFixture.PreparationFailure(
        events,
        canResumeImmediately: false);

    await fixture.RunAsync();

    CollectionAssert.AreEqual(new[] { "download", "prepare" }, events);
}
```

`CoordinatorFixture` is a nested test helper containing `Coordinator`, `Session`, and
`Package`, plus `RunAsync()`. Its `Success`, `DownloadFailure`, and
`PreparationFailure` factories compose a `FakeBackend`, a real `UpdateService` /
`UpdateSession`, and a recording `IUpdatePreparation`; the fake appends exactly
`download`, `prepare`, `resume`, and `apply` at the corresponding boundary. The package
factory used in these tests must return a valid `UpdatePackage` with a non-null opaque
native token.

- [ ] **Step 2: 執行 tests，確認 RED**

```powershell
dotnet test .\tests\3waSshDrive.App.Tests\3waSshDrive.App.Tests.csproj -c Release --nologo
```

- [ ] **Step 3: 定義 preparation result**

```csharp
internal sealed class UpdatePreparationResult
{
    public bool Success { get; }
    public bool CanResumeImmediately { get; }
    public string Message { get; }

    public static UpdatePreparationResult Ready() =>
        new UpdatePreparationResult(true, false, string.Empty);

    public static UpdatePreparationResult Blocked(
        string message,
        bool canResumeImmediately) =>
        new UpdatePreparationResult(false, canResumeImmediately, message);
}
```

- [ ] **Step 4: 實作 coordinator**

Use the exact sequence:

```csharp
var download = await session.DownloadAsync(package, progress, token);
if (!download.Success)
    return UpdateApplyResult.Failed(download.Message);

var preparation = await _preparation
    .PrepareAsync(TimeSpan.FromSeconds(30))
    .ConfigureAwait(true);
if (!preparation.Success)
{
    if (preparation.CanResumeImmediately)
        _preparation.ResumeAfterFailure();
    return UpdateApplyResult.Blocked(preparation.Message);
}

var apply = session.ApplyAndRestart(package);
if (!apply.Success)
{
    _preparation.ResumeAfterFailure();
    return UpdateApplyResult.Failed(apply.Message);
}

return UpdateApplyResult.RestartRequested();
```

Do not put unmount logic inside `UpdateService`; the coordinator is the only place allowed to call preparation and then apply.

- [ ] **Step 5: 執行 tests 與 commit**

```powershell
dotnet test .\tests\3waSshDrive.App.Tests\3waSshDrive.App.Tests.csproj -c Release --nologo
```

```bash
git add src/3waSshDrive.App/Updates/IUpdatePreparation.cs src/3waSshDrive.App/Updates/UpdateCoordinator.cs tests/3waSshDrive.App.Tests/UpdateCoordinatorTests.cs
git commit -m "feat: coordinate safe update application"
```

---

### Task 4: 將既有 mount／reconnect 安全靜止

**Files:**
- Modify: `src/3waSshDrive.App/MainForm.cs:14-95, 703-729, 906-965, 1094-1125, 1240-1322`
- Create: `src/3waSshDrive.App/MainForm.Update.cs`
- Modify: `tests/3waSshDrive.FileSystem.Tests/MountManagerTests.cs`
- Modify: `tests/3waSshDrive.App.Tests/UpdateCoordinatorTests.cs`

**Interfaces:**
- `MainForm` explicitly implements `IUpdatePreparation`。
- `PrepareAsync` blocks new operations, stops `_reconnectTimer`, disposes each `MountedDrive`, and removes only successfully disposed entries。
- `ResumeAfterFailure` re-enables UI and reconnect only after no timed-out dispose task remains。

- [ ] **Step 1: 固定 MountedDrive cleanup 行為**

Add a FileSystem test where `IFileSystemHost.Unmount()` throws. Assert `remote.dispose` still occurs and the original unmount exception is preserved. The expected event prefix is:

```csharp
new[] { "remote.connect", "host.mount:Z:", "host.unmount", "host.dispose", "remote.dispose" }
```

If current `MountedDrive.Dispose` fails this test, change its nested `try/finally` so host and remote disposal still run without replacing the original unmount error.

- [ ] **Step 2: 執行 FileSystem test，確認 RED/GREEN boundary**

```powershell
dotnet test .\tests\3waSshDrive.FileSystem.Tests\3waSshDrive.FileSystem.Tests.csproj -c Release --nologo
```

- [ ] **Step 3: 將 MainForm 改為 partial 並注入 UpdateService**

Change the declaration to:

```csharp
internal sealed partial class MainForm : Form, IUpdatePreparation
```

Add fields `_updateService` and `_updateCoordinator`. The public constructor creates
`UpdateService.CreateDefault()`. The internal constructor receives
`UpdateService updateService` after `MountManager mountManager`, validates and stores it,
then creates `_updateCoordinator = new UpdateCoordinator(this)`. Do not change
ProfileStore, SshConnectionProbe or MountManager ownership.

Add `_isApplyingUpdate`; `RunBusyAsync` returns immediately when this flag is true. `SetBusy` also disables the update button during normal work and disables all mount/profile actions during update preparation.

- [ ] **Step 4: 實作 MainForm preparation**

In `MainForm.Update.cs`, implement:

```csharp
async Task<UpdatePreparationResult> IUpdatePreparation.PrepareAsync(TimeSpan timeout)
{
    _isApplyingUpdate = true;
    SetBusy(true);
    _reconnectTimer.Stop();

    var deadline = DateTime.UtcNow + timeout;
    foreach (var pair in _mountedDrives.ToList())
    {
        var disposeTask = Task.Run(() => pair.Value.Dispose());
        var remaining = deadline - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero ||
            await Task.WhenAny(disposeTask, Task.Delay(remaining)) != disposeTask)
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
```

`ResumeWhenCleanupCompletes(string driveLetter, Task pendingDispose)` awaits the pending
task. It logs a safe `UpdateUnmountLate` failure if the task faults, then uses
`BeginInvoke` to remove `driveLetter` from `_mountedDrives` and call
`ResumeAfterFailure`. Removal is required even after a dispose exception because
`MountedDrive.Dispose` is idempotently marked disposed and its nested `finally` blocks
have already attempted host and remote cleanup. `ResumeAfterFailure` sets
`_isExplicitExit=false` and `_isApplyingUpdate=false`, calls `SetBusy(false)`, refreshes drive letters, and restarts
`_reconnectTimer` only when the form is not disposed. Never resume immediately while a
timed-out dispose task is still running.

- [ ] **Step 5: 固定 form close 與 reconnect guards**

`CheckAndReconnectDrivesAsync` must return when `_isApplyingUpdate` is true.
`OnFormClosing` must not convert an update-driven close into tray minimization. Keep
`_isExplicitExit`; set it immediately before `PrepareAsync` returns `Ready`, and clear
it in `ResumeAfterFailure`, so the existing close path disposes resources if Velopack
does not exit immediately or apply fails in a test environment.

- [ ] **Step 6: 執行完整 tests 與 commit**

```powershell
dotnet test .\3waSshDrive.sln -c Release --nologo
```

```bash
git add src/3waSshDrive.App/MainForm.cs src/3waSshDrive.App/MainForm.Update.cs src/3waSshDrive.FileSystem/Mounting/MountedDrive.cs tests/3waSshDrive.FileSystem.Tests/MountManagerTests.cs tests/3waSshDrive.App.Tests/UpdateCoordinatorTests.cs
git commit -m "feat: quiesce mounted drives before update"
```

---

### Task 5: 建立更新 dialog 與可測 view model

**Files:**
- Create: `src/3waSshDrive.App/Updates/UpdateDialogModel.cs`
- Create: `src/3waSshDrive.App/UpdateDialog.cs`
- Create: `tests/3waSshDrive.App.Tests/UpdateDialogModelTests.cs`

**Interfaces:**
- Produces: `UpdateDialogModel.From(UpdatePackage package)`。
- Produces: `UpdateDialog.SetDownloading()`、`SetProgress(int)`、`SetFailure(string)`。
- Produces: `UpdateDialog.UpdateRequested` event; the Update button keeps the modal dialog open while work runs, and `Cancel` means later。

- [ ] **Step 1: 寫 view-model RED tests**

```csharp
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
```

- [ ] **Step 2: 執行 tests，確認 RED**

```powershell
dotnet test .\tests\3waSshDrive.App.Tests\3waSshDrive.App.Tests.csproj -c Release --nologo
```

- [ ] **Step 3: 實作 dialog layout**

Build the dialog in code with:

- fixed title `3waSshDrive 更新`;
- current/new version labels;
- read-only multiline release-notes TextBox with vertical scroll;
- progress bar initially hidden;
- primary button `更新` with no `DialogResult`; its click handler calls
  `SetDownloading()` first and then raises `UpdateRequested` exactly once;
- secondary button `稍後` with `DialogResult.Cancel`;
- no password/profile/host data。

`SetDownloading` disables both decision buttons, shows progress and changes status to
`正在下載並驗證新版…`. Because the Update button does not close the dialog, the nested
`ShowDialog` message loop continues to paint progress. `SetProgress` clamps `0..100`
and marshals to the UI thread with `BeginInvoke` when required. `SetFailure` restores a
close button and shows only the safe message supplied by `UpdateService`. The only
`async void` allowed in this flow is the WinForms `UpdateRequested` event handler.
Track `_operationInProgress`: set it before raising `UpdateRequested`, cancel user-driven
`FormClosing` while it is true, and clear it only in `SetFailure`. This prevents closing
the dialog from disposing the live `UpdateSession` during download or preparation.

- [ ] **Step 4: 執行 model tests 與 compile smoke**

```powershell
dotnet test .\tests\3waSshDrive.App.Tests\3waSshDrive.App.Tests.csproj -c Release --nologo
dotnet build .\src\3waSshDrive.App\3waSshDrive.App.csproj -c Release --nologo
```

- [ ] **Step 5: Commit Task 5**

```bash
git add src/3waSshDrive.App/Updates/UpdateDialogModel.cs src/3waSshDrive.App/UpdateDialog.cs tests/3waSshDrive.App.Tests/UpdateDialogModelTests.cs
git commit -m "feat: add update prompt and progress dialog"
```

---

### Task 6: 接上 startup、主畫面與 tray 更新入口

**Files:**
- Modify: `src/3waSshDrive.App/MainForm.cs:110-260, 680-730, 1264-1284`
- Modify: `src/3waSshDrive.App/MainForm.Update.cs`
- Modify: `tests/3waSshDrive.App.Tests/UpdateServiceTests.cs`

**Interfaces:**
- Startup: `Shown` event performs one non-blocking `CheckForUpdatesAsync(manual: false)`。
- Manual: header button and tray item both call `CheckForUpdatesAsync(manual: true)`。
- Background failure stays silent; manual failure shows one safe message。

- [ ] **Step 1: 固定 notification policy tests**

Add `UpdateNotificationPolicy` to `UpdateModels.cs` with one method:

```csharp
internal static bool ShouldShowMessage(bool manual, UpdateCheckKind kind)
{
    if (kind == UpdateCheckKind.Available)
        return true;
    return manual && kind != UpdateCheckKind.Busy;
}
```

Tests must assert background `Failed`, `UpToDate`, and `Disabled` return false; manual variants return true; `Available` always returns true; `Busy` only produces a short manual status update, not a second dialog.

- [ ] **Step 2: 加入 header 與 tray controls**

Create a field `_checkUpdatesButton`. Add it to `rightHeader` next to `關於` using the existing button style and text `↻ 檢查更新`. In `SetupTrayIcon`, insert `檢查更新` between `展開視窗` and the separator.

Wire both to:

```csharp
await CheckForUpdatesAsync(manual: true);
```

Add:

```csharp
Shown += async (sender, args) =>
    await CheckForUpdatesAsync(manual: false);
```

- [ ] **Step 3: 實作 check/prompt flow**

`CheckForUpdatesAsync` must:

1. call `TryBeginSession`; manual busy sets status `更新檢查已在進行中。` and returns;
2. await `session.CheckAsync`;
3. for `Disabled`, show manual text `此版本為 portable／未安裝版本，請下載 Setup 啟用自動更新。`;
4. for `UpToDate`, show manual text `目前已是最新版本。`;
5. for `Failed`, show only `result.Message` when manual;
6. for `Available`, show `UpdateDialog`;
7. attach one `async void` UI handler to `UpdateRequested`; the dialog has already
   called `SetDownloading`, so the handler invokes
   `UpdateCoordinator.DownloadAndApplyAsync` with `dialog.SetProgress`;
8. show a safe blocked/failure message inside the still-open dialog; never expose
   backend exception text;
9. call `ShowDialog(this)`; choosing Later closes it without download, while successful
   production apply exits/restarts the process;
10. dispose the session in `finally` when `ShowDialog` returns or a failed/blocked flow
    lets the user close the dialog。

- [ ] **Step 4: 保護 UI state**

Add `_isCheckingUpdates` only to disable the header update button and to report status;
the `UpdateSession` gate rejects duplicate header/tray checks. Do not call `SetBusy`
during check or download: normal mount/profile operations remain available until
`PrepareAsync` sets `_isApplyingUpdate`. On Later or download failure, clear
`_isCheckingUpdates` and leave every mount untouched. On preparation failure, delegate
restoration to `IUpdatePreparation.ResumeAfterFailure` exactly once. This preserves the
startup/offline guarantee that a slow or failed GitHub request never blocks mounting.

- [ ] **Step 5: 執行 tests 與 manual UI smoke**

```powershell
dotnet test .\3waSshDrive.sln -c Release --nologo
cmd /c build.bat --no-pause
cmd /c run.bat --check
```

Run the portable build normally and click both update entries. Expected: it reports portable mode, does not contact apply/update paths, and normal mount controls still work.

- [ ] **Step 6: Commit Task 6**

```bash
git add src/3waSshDrive.App/MainForm.cs src/3waSshDrive.App/MainForm.Update.cs src/3waSshDrive.App/Updates/UpdateModels.cs tests/3waSshDrive.App.Tests/UpdateServiceTests.cs
git commit -m "feat: expose manual and startup update checks"
```

---

### Task 7: 驗證 installed mode、stale lock 與更新重啟

**Files:**
- Modify: `src/3waSshDrive.App/Diagnostics/SelfCheckRunner.cs`
- Modify: `tests/3waSshDrive.App.Tests/SelfCheckRunnerTests.cs`
- Create: `scripts/Test-UpdateRestart.ps1`
- Modify: `scripts/Test-InstalledRelease.ps1`
- Modify: `.github/workflows/windows-release.yml`

**Interfaces:**
- Self-check output adds `UpdateMode=Installed|Portable|Unmanaged` without changing success criteria。
- `Test-UpdateRestart.ps1` verifies an installed process exits, stale lock remains harmless, and the installed EXE can reacquire on the next launch。

- [ ] **Step 1: 寫 update-mode self-check RED tests**

Extend `SelfCheckContext` with `UpdateMode`. Add one data-driven test for all three modes and assert `Evaluate` succeeds when every other input is valid. The update mode is diagnostic, not a pass/fail condition.

The success line must be:

```text
SELF-CHECK OK Version=<display> Package=<package> UpdateMode=<mode>
```

- [ ] **Step 2: 實作 mode detection**

After Phase B bootstrap, construct one `UpdateManager` with the production source and map:

```csharp
var mode = manager.IsPortable
    ? "Portable"
    : manager.IsInstalled
        ? "Installed"
        : "Unmanaged";
```

Do not call `CheckForUpdatesAsync` from self-check.

- [ ] **Step 3: 實作 restart-lock smoke script**

Parameters are `-InstalledExe` and `-LockPath`. The script writes a fake stale PID before each check, runs two sequential self-check processes and asserts both exit `0`:

```powershell
Set-Content -LiteralPath $LockPath -Value "999999`r`n2000-01-01 00:00:00"
1..2 | ForEach-Object {
    $process = Start-Process -FilePath $InstalledExe -ArgumentList '--self-check' -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        throw "Installed self-check #$_ failed: $($process.ExitCode)"
    }
}
```

Then open `$LockPath` from PowerShell with `FileMode.OpenOrCreate`,
`FileAccess.ReadWrite`, and `FileShare.None`. While that handle is held, run
`$InstalledExe --self-check` and require exit `0`; this proves self-check bypasses the
normal instance lock. Dispose the handle, leave the stale file in place, and run one
more self-check requiring exit `0`. The existing Core `SingleInstanceLockTests` remains
the authoritative test that a second normal instance cannot acquire the live OS lock;
do not add product-only test arguments or environment-variable backdoors.

Use `try/finally` so the handle is always released:

```powershell
$handle = [System.IO.File]::Open(
    $LockPath,
    [System.IO.FileMode]::OpenOrCreate,
    [System.IO.FileAccess]::ReadWrite,
    [System.IO.FileShare]::None)
try {
    $check = Start-Process -FilePath $InstalledExe `
        -ArgumentList '--self-check' -Wait -PassThru
    if ($check.ExitCode -ne 0) {
        throw "Self-check was blocked by live lock handle."
    }
}
finally {
    $handle.Dispose()
}
```

- [ ] **Step 4: 更新 release smoke**

Extend Phase B `Test-InstalledRelease.ps1` with optional parameter
`-AdditionalSmokeScript`. After install, normal self-check, and version assertions—but
before its existing `finally` uninstall—it invokes that script with:

```powershell
-InstalledExe "$env:LOCALAPPDATA\3waSshDrive\3waSshDrive.exe"
-LockPath "$env:LOCALAPPDATA\3waSshDrive\state\lock.pid"
```

The workflow passes `scripts/Test-UpdateRestart.ps1` through this parameter. This keeps
the installed EXE available for the restart test and still guarantees uninstall in the
existing `finally`. The workflow remains draft until this script and uninstall succeed.

- [ ] **Step 5: 執行完整 tests**

```powershell
dotnet test .\3waSshDrive.sln -c Release --nologo
cmd /c build.bat --no-pause
cmd /c run.bat --check
```

- [ ] **Step 6: Commit Task 7**

```bash
git add src/3waSshDrive.App/Diagnostics/SelfCheckRunner.cs tests/3waSshDrive.App.Tests/SelfCheckRunnerTests.cs scripts/Test-UpdateRestart.ps1 scripts/Test-InstalledRelease.ps1 .github/workflows/windows-release.yml
git commit -m "test: verify updater restart and instance lock"
```

---

### Task 8: Phase C 端到端驗收與文件

**Files:**
- Modify: `README.md`
- Modify: `history.md`

**Interfaces:**
- Produces: 使用者更新操作說明與維護者兩版本 acceptance procedure。

- [ ] **Step 1: 跑自動測試矩陣**

```powershell
dotnet test .\3waSshDrive.sln -c Release --nologo
cmd /c build.bat --no-pause
if ($LASTEXITCODE -ne 0) { throw "build failed" }
cmd /c run.bat --check
if ($LASTEXITCODE -ne 0) { throw "self-check failed" }
pwsh -NoProfile -File .\scripts\tests\Test-ReleaseVersion.ps1
pwsh -NoProfile -File .\scripts\tests\Test-NewPortablePackage.ps1
pwsh -NoProfile -File .\scripts\tests\Test-NewVelopackRelease.ps1
```

Required App.Tests names visible in output: unmanaged disabled, portable disabled, no update, update available, offline failure, checksum failure, single session, preparation success, preparation failure, preparation timeout, safe logging, and update-mode self-check.

- [ ] **Step 2: 驗收「稍後」與 offline 行為**

Install release N with Setup. Disconnect network, start the app and mount a test profile; expected: no modal update error and mount remains usable. Reconnect, publish N+1, start N, choose `稍後`; expected: N remains running and mounted with no downloaded apply.

- [ ] **Step 3: 驗收成功更新**

With N installed and one test drive mounted:

1. trigger manual check;
2. confirm current/target external versions and release notes;
3. choose Update;
4. verify progress reaches download completion;
5. verify drive disappears before process exit;
6. verify the new process opens automatically;
7. run installed `--self-check` and confirm target display/package versions plus `UpdateMode=Installed`;
8. confirm `%LocalAppData%\3waSshDrive\state\lock.pid` did not deadlock restart。

- [ ] **Step 4: 驗收 blocked update**

Use the App.Tests fake preparation failure for the deterministic acceptance gate. An
optional real-machine smoke may keep a file operation active long enough for WinFsp to
reject unmount, but do not add product test switches or environment backdoors.
Expected: apply is never called, old app/version remains, and the user sees
`無法安全卸載所有磁碟，已取消更新。` without host/key details.

- [ ] **Step 5: 更新 README 與 history**

README must state:

- startup check is non-blocking and failures do not affect mount;
- manual check exists in header and tray;
- Update/Later behavior;
- only Setup-installed builds update;
- portable ZIP requires downloading Setup;
- update never upgrades WinFsp;
- failure to unmount cancels apply;
- logs intentionally omit sensitive connection details。

History records the stable GitHub feed, single-session gate, safe unmount, per-user restart and portable boundary.

- [ ] **Step 6: Commit Task 8**

```bash
git add README.md history.md
git commit -m "docs: document Phase C updater behavior"
```

---

## Phase C Completion Gate

Phase C is complete only when all conditions are true:

- Setup-installed release N discovers published stable release N+1 and displays external versions plus release notes.
- `更新` completes download/checksum verification, blocks new mounts, safely disposes every mounted drive, applies and restarts N+1.
- `稍後` leaves N and all mounted drives untouched.
- Offline/background failure is silent except safe log; manual failure shows a short safe message.
- Checksum/download failure and unmount failure never call apply.
- Only one update session can exist across startup, header and tray triggers.
- Portable/unmanaged builds never attempt an in-place update.
- Restart succeeds with a stale but unlocked `%LocalAppData%\3waSshDrive\state\lock.pid`.
- Automated and two-version manual acceptance pass without leaking password, token, host or private-key data.
- Phase D remains a separate future plan with no CI performance gate.

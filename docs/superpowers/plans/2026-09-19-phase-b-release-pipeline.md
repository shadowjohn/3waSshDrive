# 3waSshDrive Phase B Release Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 讓每次 push／pull request 都產生可下載的 Windows x64 ZIP，並讓 `vYYYY.MM.DD.RR` tag 經過安裝 smoke test 後發布 Velopack per-user Setup 與更新資產。

**Architecture:** 保留 `build.bat` 與 `run.bat` 作為本機及 CI 的共同入口，將版本解析、portable 封裝與安裝 smoke test放在可單獨執行的 PowerShell 腳本。應用程式加入無網路、無 WinFsp 需求的 `--self-check`，正式 tag 以固定版號注入建置，再由 Velopack 1.2.0 建立 per-user Setup 與 release feed。

**Tech Stack:** C#；.NET Framework 4.7.2；WinForms；MSTest；PowerShell 7；GitHub Actions `windows-2022`；Velopack / `vpk` 1.2.0；GitHub CLI。

**Spec:** `docs/superpowers/specs/2026-09-19-phase-b-c-release-updater-design.md`

## Global Constraints

- 所有 3waSshDrive assembly 維持 `net472`，正式程式固定 x64。
- 對外版本固定為 `vYYYY.MM.DD.RR`；`RR` 僅接受 `01`–`99`。
- Velopack 版號固定映射為 `YYYY.(M * 100 + D).R`，例如 `v2026.09.19.01` → `2026.919.1`。
- Assembly/File version 使用 `YYYY.M.D.R`，例如 `2026.9.19.1`。
- Velopack NuGet 與 `vpk` CLI 同時固定為穩定版 `1.2.0`。
- Setup 固定 `--instLocation PerUser`，預設安裝根目錄為 `%LocalAppData%\3waSshDrive`。
- Setup 不打包 WinFsp；既有 helper 固定安裝 WinFsp `2.1.25156`，既有 manifest／SHA-256 驗證保留。
- Authenticode 為選配；未提供簽章參數時 unsigned release 仍是合法輸出。
- Microsoft Security Intelligence 送檢不在 workflow 內，也不阻擋 Release。
- `build.bat` 與 `run.bat` 必須保留；Phase D 效能量測不進入本計畫。

## Review Focus

- 無效日期與 `RR=00`：版號解析必須在建置前失敗，Task 1 的 C# 與 PowerShell 測試固定此行為。
- ZIP 少 DLL／靜態資源：封裝必須失敗而非上傳殘缺 artifact，Task 5 的腳本測試固定此行為。
- 第二次執行同一 tag workflow：只能合併至既有 draft，不可發布兩個互相衝突的 Release，Task 7 的 release 驗證固定此行為。
- unsigned 發行：未設定簽章 secret 時仍須成功產包，Task 6 的腳本測試及 Task 7 workflow 固定此行為。
- 安裝後殘留 `lock.pid`：解除安裝／重裝與 `--self-check` 不得被檔案存在阻擋，Task 3 與 Task 7 的測試固定此行為。

---

## File Structure

### 新增檔案

- `src/3waSshDrive.Core/Versioning/ReleaseVersion.cs`：唯一的 C# tag／Velopack／Assembly 版號轉換規則。
- `src/3waSshDrive.App/Diagnostics/SelfCheckRunner.cs`：檢查 x64、版本 metadata、必要輸出檔案與 Velopack bootstrap 結果。
- `src/3waSshDrive.App/Diagnostics/SelfCheckConsole.cs`：WinExe 在 `--self-check` 時附著父 console 並輸出診斷。
- `src/3waSshDrive.App/Diagnostics/CrashLogger.cs`：只寫 operation、exception type 與 HRESULT 的本機安全 log。
- `src/3waSshDrive.App/StartupOptions.cs`：只解析啟動參數，不觸碰 UI 或 lock。
- `src/3waSshDrive.App/Properties/AssemblyInfo.cs`：只開放 App internals 給測試 assembly。
- `tests/3waSshDrive.App.Tests/3waSshDrive.App.Tests.csproj`：App 啟動／self-check 測試專案。
- `tests/3waSshDrive.App.Tests/SelfCheckRunnerTests.cs`：self-check 成功與缺檔／版本／x64 失敗案例。
- `tests/3waSshDrive.App.Tests/CrashLoggerTests.cs`：確認例外訊息、主機、token 與私鑰路徑不落盤。
- `tests/3waSshDrive.App.Tests/StartupOptionsTests.cs`：`--self-check` 解析與 bypass lock 規則。
- `tests/3waSshDrive.Core.Tests/ReleaseVersionTests.cs`：版號 mapping 與反向顯示測試。
- `scripts/Resolve-ReleaseVersion.ps1`：供本機及 Actions 解析 tag 並寫入 `GITHUB_ENV`。
- `scripts/New-PortablePackage.ps1`：建立固定名稱 ZIP 與 SHA-256 檔。
- `scripts/New-VelopackRelease.ps1`：下載上一版 feed、呼叫固定版 `vpk pack`、建立最終資產。
- `scripts/Test-InstalledRelease.ps1`：靜默安裝、執行 self-check、核對路徑／版本、解除安裝。
- `scripts/Test-ReleaseAssets.ps1`：檢查 local／draft Release 必要資產。
- `scripts/tests/Test-ReleaseVersion.ps1`：PowerShell 版號解析測試。
- `scripts/tests/Test-NewPortablePackage.ps1`：portable 封裝與缺檔測試。
- `scripts/tests/Test-NewVelopackRelease.ps1`：unsigned 參數組合與 asset 規則測試。
- `.github/workflows/windows-release.yml`：tag-only draft → smoke → publish workflow。

### 修改檔案

- `Directory.Build.props`：集中 deterministic 與 CI build 屬性，不放人工 tag。
- `src/3waSshDrive.App/3waSshDrive.App.csproj`：x64、版本 metadata、Velopack 1.2.0 與測試可見性。
- `src/3waSshDrive.App/MainForm.cs`：移除硬編碼 `AppVersion`，改讀 assembly informational version。
- `src/3waSshDrive.App/Program.cs`：Velopack bootstrap → self-check → single-instance lock → WinForms。
- `src/3waSshDrive.Core/Utils/SingleInstanceLock.cs`：固定 lock 到 LocalAppData state 目錄。
- `src/3waSshDrive.FileSystem/Mounting/WinFspInstaller.cs`：winget 精確指定 `2.1.25156`。
- `tests/3waSshDrive.Core.Tests/SingleInstanceLockTests.cs`：stale lock 與預設路徑測試。
- `tests/3waSshDrive.FileSystem.Tests/WinFspInstallerTests.cs`：固定版本參數測試。
- `3waSshDrive.sln`：加入 App.Tests。
- `build.bat`：接受三個版號環境變數並傳入 MSBuild。
- `run.bat`：`--check` 真正等待 EXE `--self-check` 並傳回 exit code。
- `.github/workflows/windows-build.yml`：加入 script tests、ZIP、SHA-256 與 artifact upload。
- `README.md`、`history.md`：記錄本機命令、artifact／Setup 差異與 release SOP。

---

### Task 1: 建立唯一版號契約

**Files:**
- Create: `src/3waSshDrive.Core/Versioning/ReleaseVersion.cs`
- Create: `tests/3waSshDrive.Core.Tests/ReleaseVersionTests.cs`
- Create: `scripts/Resolve-ReleaseVersion.ps1`
- Create: `scripts/tests/Test-ReleaseVersion.ps1`
- Modify: `src/3waSshDrive.App/3waSshDrive.App.csproj:1-43`
- Modify: `src/3waSshDrive.App/MainForm.cs:17-98`

**Interfaces:**
- Produces: `ReleaseVersion ReleaseVersion.ParseTag(string tag)`。
- Produces: `string ReleaseVersion.TagFromPackageVersion(string packageVersion)`。
- Produces: environment keys `THREEWA_DISPLAY_VERSION`、`THREEWA_PACKAGE_VERSION`、`THREEWA_ASSEMBLY_VERSION`。
- Later tasks consume these exact names in `build.bat` and release workflow。

- [ ] **Step 1: 寫 C# RED tests**

Create `ReleaseVersionTests.cs` with the exact cases:

```csharp
[TestMethod]
public void ParseTag_MapsExternalTagToVelopackAndAssemblyVersions()
{
    var value = ReleaseVersion.ParseTag("v2026.09.19.01");
    Assert.AreEqual("v2026.09.19.01", value.Tag);
    Assert.AreEqual("2026.919.1", value.PackageVersion);
    Assert.AreEqual(new Version(2026, 9, 19, 1), value.AssemblyVersion);
}

[TestMethod]
public void TagFromPackageVersion_RestoresDisplayTag()
{
    Assert.AreEqual(
        "v2026.09.19.02",
        ReleaseVersion.TagFromPackageVersion("2026.919.2"));
}

[DataTestMethod]
[DataRow("v2026.02.30.01")]
[DataRow("v2026.09.19.00")]
[DataRow("2026.09.19.01")]
[DataRow("v2026.9.19.01")]
public void ParseTag_RejectsInvalidInput(string tag)
{
    Assert.ThrowsException<FormatException>(() => ReleaseVersion.ParseTag(tag));
}
```

- [ ] **Step 2: 執行 Core tests，確認 RED**

Run:

```powershell
dotnet test .\tests\3waSshDrive.Core.Tests\3waSshDrive.Core.Tests.csproj -c Release --nologo
```

Expected: compile failure because `ThreeWa.SshDrive.Core.Versioning.ReleaseVersion` does not exist.

- [ ] **Step 3: 實作 C# mapping**

Create `ReleaseVersion.cs` with a compiled regex `^v(?<year>\d{4})\.(?<month>\d{2})\.(?<day>\d{2})\.(?<revision>\d{2})$`. Parse numeric groups using invariant culture, reject revision outside `1..99`, and validate the date by constructing `new DateTime(year, month, day)`.

Expose immutable properties with these assignments:

```csharp
Tag = tag;
PackageVersion = string.Format(
    CultureInfo.InvariantCulture,
    "{0}.{1}.{2}",
    year,
    month * 100 + day,
    revision);
AssemblyVersion = new Version(year, month, day, revision);
```

For `TagFromPackageVersion`, accept only three numeric components, reconstruct `month = middle / 100` and `day = middle % 100`, validate the date and revision, then return:

```csharp
return string.Format(
    CultureInfo.InvariantCulture,
    "v{0:D4}.{1:D2}.{2:D2}.{3:D2}",
    year,
    month,
    day,
    revision);
```

- [ ] **Step 4: 寫並執行 PowerShell mapping tests**

`Resolve-ReleaseVersion.ps1` must return one object and optionally append environment values:

```powershell
param(
    [Parameter(Mandatory = $true)][string]$Tag,
    [string]$GitHubEnv
)

$pattern = '^v(?<year>\d{4})\.(?<month>\d{2})\.(?<day>\d{2})\.(?<revision>\d{2})$'
if ($Tag -notmatch $pattern) { throw "Invalid release tag: $Tag" }

$year = [int]$Matches.year
$month = [int]$Matches.month
$day = [int]$Matches.day
$revision = [int]$Matches.revision
if ($revision -lt 1 -or $revision -gt 99) { throw "Revision must be 01-99." }
[void][datetime]::new($year, $month, $day)

$result = [pscustomobject]@{
    DisplayVersion = $Tag
    PackageVersion = "$year.$($month * 100 + $day).$revision"
    AssemblyVersion = "$year.$month.$day.$revision"
}

if ($GitHubEnv) {
    Add-Content -LiteralPath $GitHubEnv -Value "THREEWA_DISPLAY_VERSION=$($result.DisplayVersion)"
    Add-Content -LiteralPath $GitHubEnv -Value "THREEWA_PACKAGE_VERSION=$($result.PackageVersion)"
    Add-Content -LiteralPath $GitHubEnv -Value "THREEWA_ASSEMBLY_VERSION=$($result.AssemblyVersion)"
}

$result | ConvertTo-Json -Compress
```

`Test-ReleaseVersion.ps1` must assert the two valid mappings above and assert that the four invalid tags throw. Run:

```powershell
pwsh -NoProfile -File .\scripts\tests\Test-ReleaseVersion.ps1
```

Expected: exit code `0` and `ReleaseVersion script tests passed.`

- [ ] **Step 5: 將版本值接到 project metadata 與視窗標題**

Replace the hard-coded app version properties with:

```xml
<ThreeWaDisplayVersion Condition="'$(ThreeWaDisplayVersion)' == ''">v2026.09.19.01</ThreeWaDisplayVersion>
<ThreeWaPackageVersion Condition="'$(ThreeWaPackageVersion)' == ''">2026.919.1</ThreeWaPackageVersion>
<ThreeWaAssemblyVersion Condition="'$(ThreeWaAssemblyVersion)' == ''">2026.9.19.1</ThreeWaAssemblyVersion>
<Version>$(ThreeWaPackageVersion)</Version>
<AssemblyVersion>$(ThreeWaAssemblyVersion)</AssemblyVersion>
<FileVersion>$(ThreeWaAssemblyVersion)</FileVersion>
<InformationalVersion>$(ThreeWaDisplayVersion)</InformationalVersion>
<IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>
<PlatformTarget>x64</PlatformTarget>
<Prefer32Bit>false</Prefer32Bit>
```

Add the package-version metadata:

```xml
<ItemGroup>
  <AssemblyMetadata Include="VelopackPackageVersion" Value="$(ThreeWaPackageVersion)" />
</ItemGroup>
```

Delete `MainForm.AppVersion` and set the title with `Application.ProductVersion`:

```csharp
Text = $"3waSshDrive - {Application.ProductVersion}";
```

Run the Core tests again; expected: all pass.

- [ ] **Step 6: Commit Task 1**

```bash
git add src/3waSshDrive.Core/Versioning/ReleaseVersion.cs tests/3waSshDrive.Core.Tests/ReleaseVersionTests.cs scripts/Resolve-ReleaseVersion.ps1 scripts/tests/Test-ReleaseVersion.ps1 src/3waSshDrive.App/3waSshDrive.App.csproj src/3waSshDrive.App/MainForm.cs
git commit -m "feat: add release version contract"
```

---

### Task 2: 加入 Velopack bootstrap 與可測 self-check

**Files:**
- Create: `src/3waSshDrive.App/Diagnostics/SelfCheckConsole.cs`
- Create: `src/3waSshDrive.App/Diagnostics/SelfCheckRunner.cs`
- Create: `src/3waSshDrive.App/Diagnostics/CrashLogger.cs`
- Create: `src/3waSshDrive.App/StartupOptions.cs`
- Create: `src/3waSshDrive.App/Properties/AssemblyInfo.cs`
- Create: `tests/3waSshDrive.App.Tests/3waSshDrive.App.Tests.csproj`
- Create: `tests/3waSshDrive.App.Tests/SelfCheckRunnerTests.cs`
- Create: `tests/3waSshDrive.App.Tests/CrashLoggerTests.cs`
- Create: `tests/3waSshDrive.App.Tests/StartupOptionsTests.cs`
- Modify: `src/3waSshDrive.App/3waSshDrive.App.csproj:1-50`
- Modify: `src/3waSshDrive.App/Program.cs:1-104`
- Modify: `3waSshDrive.sln`

**Interfaces:**
- Produces: `StartupOptions StartupOptions.Parse(string[] args)` with `bool SelfCheck`.
- Produces: `SelfCheckResult SelfCheckRunner.Evaluate(SelfCheckContext context)`.
- Produces: `int SelfCheckRunner.RunCurrentProcess(bool velopackBootstrapSucceeded, TextWriter output)`.
- Produces: `CrashLogger.Log(string operation, Exception exception)` and the safe-field overload `Log(string operation, string exceptionType, int hresult)`, with no raw exception message or stack trace.
- Produces: process contract `3waSshDrive.exe --self-check`, exit `0` on success and nonzero on failure.

- [ ] **Step 1: 建立 App.Tests 並寫 RED tests**

The new test project targets `net472`, sets `PlatformTarget` to `x64`, references MSTest `17.12.0 / 3.6.4 / 3.6.4`, and references both App and Core projects.

Add startup parsing tests:

```csharp
[TestMethod]
public void Parse_SelfCheck_IsCaseInsensitive()
{
    Assert.IsTrue(StartupOptions.Parse(new[] { "--SELF-CHECK" }).SelfCheck);
}

[TestMethod]
public void Parse_NormalLaunch_DoesNotSelectSelfCheck()
{
    Assert.IsFalse(StartupOptions.Parse(Array.Empty<string>()).SelfCheck);
}
```

Add self-check tests using a temporary directory and the exact required-file list exposed by `SelfCheckRunner.RequiredFiles`:

```csharp
[TestMethod]
public void Evaluate_WhenAllInputsMatch_ReturnsSuccess()
{
    WriteRequiredFiles(_tempDir);
    var context = ValidContext(_tempDir);
    Assert.IsTrue(SelfCheckRunner.Evaluate(context).Success);
}

[TestMethod]
public void Evaluate_WhenRequiredDllIsMissing_ReturnsFailure()
{
    WriteRequiredFiles(_tempDir);
    File.Delete(Path.Combine(_tempDir, "Velopack.dll"));
    var result = SelfCheckRunner.Evaluate(ValidContext(_tempDir));
    Assert.IsFalse(result.Success);
    StringAssert.Contains(result.Message, "Velopack.dll");
}

[TestMethod]
public void Evaluate_WhenPackageVersionDoesNotMatchTag_ReturnsFailure()
{
    WriteRequiredFiles(_tempDir);
    var context = ValidContext(_tempDir);
    context.PackageVersion = "2026.919.2";
    Assert.IsFalse(SelfCheckRunner.Evaluate(context).Success);
}

[TestMethod]
public void Evaluate_WhenProcessIsNotX64_ReturnsFailure()
{
    WriteRequiredFiles(_tempDir);
    var context = ValidContext(_tempDir);
    context.Is64BitProcess = false;
    Assert.IsFalse(SelfCheckRunner.Evaluate(context).Success);
}

private static void WriteRequiredFiles(string directory)
{
    foreach (var relativePath in SelfCheckRunner.RequiredFiles)
    {
        var path = Path.Combine(directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, "self-check fixture");
    }
}

private static SelfCheckContext ValidContext(string directory)
{
    return new SelfCheckContext
    {
        BaseDirectory = directory,
        DisplayVersion = "v2026.09.19.01",
        PackageVersion = "2026.919.1",
        AssemblyVersion = new Version(2026, 9, 19, 1),
        FileVersion = new Version(2026, 9, 19, 1),
        Is64BitProcess = true,
        VelopackBootstrapSucceeded = true
    };
}
```

Add a pure redaction test in `CrashLoggerTests.cs`:

```csharp
[TestMethod]
public void CreateSafeLine_DoesNotIncludeOriginalExceptionDetails()
{
    var exception = new InvalidOperationException(
        @"host.internal C:\keys\john.ppk token=abc");

    var line = CrashLogger.CreateSafeLine("UpdateCheck", exception);

    StringAssert.Contains(line, "UpdateCheck");
    StringAssert.Contains(line, "InvalidOperationException");
    Assert.IsFalse(line.Contains("host.internal"));
    Assert.IsFalse(line.Contains("john.ppk"));
    Assert.IsFalse(line.Contains("token=abc"));
}
```

- [ ] **Step 2: 執行 App tests，確認 RED**

Run:

```powershell
dotnet test .\tests\3waSshDrive.App.Tests\3waSshDrive.App.Tests.csproj -c Release --nologo
```

Expected: compile failure for missing `StartupOptions` and `SelfCheckRunner`.

- [ ] **Step 3: 實作 pure self-check evaluator**

Define these exact models in `SelfCheckRunner.cs`:

```csharp
internal sealed class SelfCheckContext
{
    public string BaseDirectory { get; set; }
    public string DisplayVersion { get; set; }
    public string PackageVersion { get; set; }
    public Version AssemblyVersion { get; set; }
    public Version FileVersion { get; set; }
    public bool Is64BitProcess { get; set; }
    public bool VelopackBootstrapSucceeded { get; set; }
}

internal sealed class SelfCheckResult
{
    public bool Success { get; }
    public string Message { get; }
    public SelfCheckResult(bool success, string message)
    {
        Success = success;
        Message = message;
    }
}
```

`RequiredFiles` must contain:

```csharp
"3waSshDrive.exe",
"3waSshDrive.Core.dll",
"3waSshDrive.FileSystem.dll",
"3waSshDrive.Sftp.dll",
"3waSshDrive.WinFsp.dll",
"Renci.SshNet.dll",
"Velopack.dll",
"Newtonsoft.Json.dll",
@"runtime\winfsp-2.1.25156-manifest.json",
@"Assets\background.png",
@"Assets\header_logo.png"
```

`Evaluate` checks in this order: bootstrap succeeded, x64, every required file exists, display tag parses, mapped package version equals metadata, and Assembly/File version equal the parsed assembly version. It returns the first safe diagnostic and never opens SSH or calls WinFsp.

`RunCurrentProcess` reads `AssemblyInformationalVersionAttribute`, `AssemblyMetadataAttribute` key `VelopackPackageVersion`, `AssemblyName.Version`, and `FileVersionInfo.FileVersion`, then prints exactly one success/failure line and returns `0` or `1`.

- [ ] **Step 4: 實作安全 crash logger**

`CrashLogger.CreateSafeLine` returns an invariant UTC timestamp followed only by the
caller-supplied operation identifier, exception type, and HRESULT in hex. The
exception overload extracts only `exception.GetType().Name` and `exception.HResult`,
then calls the safe-field overload. It must not use `exception.Message`, `StackTrace`,
`ToString()`, `InnerException`, or serialize the exception. Both `Log` overloads append
the same allow-listed line to
`%LocalAppData%\3waSshDrive\logs\application.log`, creates the directory when needed,
and swallows only file-system logging failures so diagnostics can never prevent startup.

```csharp
internal static string CreateSafeLine(
    string operation,
    string exceptionType,
    int hresult)
{
    return string.Format(
        CultureInfo.InvariantCulture,
        "{0:O} {1} failed ({2}, HRESULT 0x{3:X8})",
        DateTime.UtcNow,
        operation,
        exceptionType,
        hresult);
}
```

Keep a convenience `CreateSafeLine(string, Exception)` for the test above; it may only
forward the exception type and HRESULT to this safe-field overload.

- [ ] **Step 5: 加入 WinExe parent-console helper**

`SelfCheckConsole.AttachParent()` uses `AttachConsole(ATTACH_PARENT_PROCESS)` and resets stdout/stderr:

```csharp
[DllImport("kernel32.dll", SetLastError = true)]
private static extern bool AttachConsole(uint processId);

internal static void AttachParent()
{
    const uint AttachParentProcess = 0xffffffff;
    AttachConsole(AttachParentProcess);
    Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
    Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
}
```

- [ ] **Step 6: 加入 Velopack 1.2.0 並固定 startup ordering**

Add:

```xml
<PackageReference Include="Velopack" Version="1.2.0" />
```

Add `[assembly: InternalsVisibleTo("3waSshDrive.App.Tests")]` to `Properties/AssemblyInfo.cs`.

Change the entry point to `private static int Main(string[] args)`. The first executable operations must follow this exact order:

```csharp
var options = StartupOptions.Parse(args);
if (options.SelfCheck)
    SelfCheckConsole.AttachParent();

try
{
    VelopackApp.Build()
        .SetAutoApplyOnStartup(false)
        .Run();
}
catch (Exception exception)
{
    if (options.SelfCheck)
    {
        Console.Error.WriteLine("SELF-CHECK FAILED: Velopack bootstrap (" +
            exception.GetType().Name + ")");
        return 1;
    }
    CrashLogger.Log("Velopack.Bootstrap", exception);
    return 1;
}

if (options.SelfCheck)
    return SelfCheckRunner.RunCurrentProcess(true, Console.Out);
```

Only after this block may normal startup acquire `SingleInstanceLock` and call `Application.Run`.

- [ ] **Step 7: 執行 App tests 與完整 solution tests**

```powershell
dotnet test .\tests\3waSshDrive.App.Tests\3waSshDrive.App.Tests.csproj -c Release --nologo
dotnet test .\3waSshDrive.sln -c Release --nologo
```

Expected: all tests pass and the solution includes App.Tests.

- [ ] **Step 8: Commit Task 2**

```bash
git add src/3waSshDrive.App tests/3waSshDrive.App.Tests 3waSshDrive.sln
git commit -m "feat: add Velopack bootstrap and self-check"
```

---

### Task 3: 固定單例鎖的 per-user state path 與 handle semantics

**Files:**
- Modify: `src/3waSshDrive.Core/Utils/SingleInstanceLock.cs:19-78`
- Modify: `tests/3waSshDrive.Core.Tests/SingleInstanceLockTests.cs:1-78`
- Modify: `tests/3waSshDrive.App.Tests/SelfCheckRunnerTests.cs`

**Interfaces:**
- Produces: `string SingleInstanceLock.GetDefaultLockFilePath()` → `%LocalAppData%\3waSshDrive\state\lock.pid`。
- Preserves: `bool SingleInstanceLock.TryAcquire(out SingleInstanceLock instanceLock, string lockPath = null)`。

- [ ] **Step 1: 寫 lock RED tests**

Add:

```csharp
[TestMethod]
public void GetDefaultLockFilePath_UsesPerUserStateDirectory()
{
    var path = SingleInstanceLock.GetDefaultLockFilePath();
    var expected = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "3waSshDrive",
        "state",
        "lock.pid");
    Assert.AreEqual(expected, path);
}

[TestMethod]
public void TryAcquire_StaleUnlockedFileDoesNotBlockStartup()
{
    File.WriteAllText(_lockPath, "999999\r\n2000-01-01 00:00:00");
    Assert.IsTrue(SingleInstanceLock.TryAcquire(out var appLock, _lockPath));
    appLock.Dispose();
}

[TestMethod]
public void TryAcquire_LiveExclusiveHandleStillRejectsSecondInstance()
{
    Assert.IsTrue(SingleInstanceLock.TryAcquire(out var first, _lockPath));
    using (first)
    {
        Assert.IsFalse(SingleInstanceLock.TryAcquire(out var second, _lockPath));
        Assert.IsNull(second);
    }
}
```

In App.Tests, hold a temp `SingleInstanceLock`, call `SelfCheckRunner.Evaluate(ValidContext(...))`, and assert success to pin that self-check has no lock dependency.

- [ ] **Step 2: 執行 tests，確認 per-user-path test RED**

```powershell
dotnet test .\tests\3waSshDrive.Core.Tests\3waSshDrive.Core.Tests.csproj -c Release --nologo
dotnet test .\tests\3waSshDrive.App.Tests\3waSshDrive.App.Tests.csproj -c Release --nologo
```

- [ ] **Step 3: 簡化 default path**

Replace repository/base-directory discovery with:

```csharp
public static string GetDefaultLockFilePath()
{
    var localAppData = Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData);
    if (string.IsNullOrWhiteSpace(localAppData))
        throw new InvalidOperationException("LocalApplicationData is unavailable.");

    return Path.Combine(
        localAppData,
        "3waSshDrive",
        "state",
        "lock.pid");
}
```

Keep `FileShare.None`; do not use `File.Exists` as the ownership decision. Keep deletion in `Dispose` as best-effort cleanup only.

- [ ] **Step 4: 執行 tests 與實際 self-check**

```powershell
dotnet test .\3waSshDrive.sln -c Release --nologo
dotnet build .\src\3waSshDrive.App\3waSshDrive.App.csproj -c Release --nologo
& .\src\3waSshDrive.App\bin\Release\net472\3waSshDrive.exe --self-check
if ($LASTEXITCODE -ne 0) { throw "self-check failed" }
```

- [ ] **Step 5: Commit Task 3**

```bash
git add src/3waSshDrive.Core/Utils/SingleInstanceLock.cs tests/3waSshDrive.Core.Tests/SingleInstanceLockTests.cs tests/3waSshDrive.App.Tests/SelfCheckRunnerTests.cs
git commit -m "fix: stabilize per-user instance lock"
```

---

### Task 4: 固定 WinFsp helper 安裝版本

**Files:**
- Modify: `src/3waSshDrive.FileSystem/Mounting/WinFspInstaller.cs:8-14`
- Modify: `tests/3waSshDrive.FileSystem.Tests/WinFspInstallerTests.cs:10-29`

**Interfaces:**
- Preserves: `WinFspInstaller.CreateProcessStartInfo()` and `InstallAsync(...)`。
- Produces: winget command fixed to package `WinFsp.WinFsp`, version `2.1.25156`, exact match, winget source。

- [ ] **Step 1: 強化 RED test**

Add exact assertions:

```csharp
Assert.AreEqual("WinFsp.WinFsp", WinFspInstaller.PackageId);
StringAssert.Contains(psi.Arguments, "--id WinFsp.WinFsp");
StringAssert.Contains(psi.Arguments, "--version 2.1.25156");
StringAssert.Contains(psi.Arguments, "--exact");
StringAssert.Contains(psi.Arguments, "--source winget");
```

- [ ] **Step 2: 執行 FileSystem tests，確認 RED**

```powershell
dotnet test .\tests\3waSshDrive.FileSystem.Tests\3waSshDrive.FileSystem.Tests.csproj -c Release --nologo
```

- [ ] **Step 3: 固定 winget arguments**

Use:

```csharp
public const string WinFspVersion = "2.1.25156";
public const string DefaultArguments =
    "install --id WinFsp.WinFsp --version 2.1.25156 --exact --source winget " +
    "--accept-source-agreements --accept-package-agreements";
```

Do not change `WinFspRuntimePreflight` or the committed manifest/hash verifier.

- [ ] **Step 4: 執行完整 tests**

```powershell
dotnet test .\3waSshDrive.sln -c Release --nologo
```

- [ ] **Step 5: Commit Task 4**

```bash
git add src/3waSshDrive.FileSystem/Mounting/WinFspInstaller.cs tests/3waSshDrive.FileSystem.Tests/WinFspInstallerTests.cs
git commit -m "fix: pin WinFsp winget install version"
```

---

### Task 5: 統一本機 build、self-check 與 portable 封裝

**Files:**
- Modify: `build.bat:1-45`
- Modify: `run.bat:1-37`
- Create: `scripts/New-PortablePackage.ps1`
- Create: `scripts/tests/Test-NewPortablePackage.ps1`

**Interfaces:**
- Consumes: the three `THREEWA_*_VERSION` environment variables from Task 1。
- Produces: `artifacts/portable/3waSshDrive-win-x64-<label>.zip` and matching `.sha256`。
- Produces: `run.bat --check` returning the child process exit code。

- [ ] **Step 1: 寫 portable script RED tests**

`Test-NewPortablePackage.ps1` creates a temporary fake build folder containing every `SelfCheckRunner.RequiredFiles` entry, calls the packaging script with `-Label test123`, expands the ZIP, and asserts every file plus the `.sha256` file exists. A second case omits `Velopack.dll` and asserts that the script throws `Missing package file: Velopack.dll`.

Run:

```powershell
pwsh -NoProfile -File .\scripts\tests\Test-NewPortablePackage.ps1
```

Expected: RED because `New-PortablePackage.ps1` does not exist.

- [ ] **Step 2: 實作 portable packaging script**

Use parameters:

```powershell
param(
    [Parameter(Mandatory = $true)][string]$SourceDirectory,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][string]$Label
)
```

Use the same required list as self-check. Resolve all paths with `Resolve-Path -LiteralPath`, copy the complete build output to a temporary staging directory, exclude only `*.pdb`, `*.xml` documentation and existing package archives, then:

```powershell
$zip = Join-Path $OutputDirectory "3waSshDrive-win-x64-$Label.zip"
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
Set-Content -LiteralPath "$zip.sha256" -Value "$hash  $(Split-Path $zip -Leaf)" -Encoding ascii
```

Always remove the temporary staging directory in `finally`.

- [ ] **Step 3: 將版號注入 build.bat**

Build one `MSBUILD_VERSION_ARGS` value only when all three environment variables are present:

```bat
set "MSBUILD_VERSION_ARGS="
if defined THREEWA_DISPLAY_VERSION if defined THREEWA_PACKAGE_VERSION if defined THREEWA_ASSEMBLY_VERSION (
    set "MSBUILD_VERSION_ARGS=/p:ThreeWaDisplayVersion=%THREEWA_DISPLAY_VERSION% /p:ThreeWaPackageVersion=%THREEWA_PACKAGE_VERSION% /p:ThreeWaAssemblyVersion=%THREEWA_ASSEMBLY_VERSION%"
)
```

Append `%MSBUILD_VERSION_ARGS%` to both `dotnet test` and the App `dotnet build` command. Preserve `--no-pause`, submodule restore, Release tests and Release App build.

- [ ] **Step 4: 讓 run.bat 真正等待 self-check**

Replace the old file-existence-only branch with:

```bat
if /I "%~1"=="--check" (
    echo [3waSshDrive] Running self-check: %APP_PATH%
    start "" /wait "%APP_PATH%" --self-check
    if errorlevel 1 goto :failed
    echo [3waSshDrive] Self-check passed.
    exit /b 0
)
```

- [ ] **Step 5: 執行本機共同入口與封裝測試**

```powershell
pwsh -NoProfile -File .\scripts\tests\Test-NewPortablePackage.ps1
cmd /c build.bat --no-pause
if ($LASTEXITCODE -ne 0) { throw "build.bat failed" }
cmd /c run.bat --check
if ($LASTEXITCODE -ne 0) { throw "run.bat --check failed" }
pwsh -NoProfile -File .\scripts\New-PortablePackage.ps1 `
  -SourceDirectory .\src\3waSshDrive.App\bin\Release\net472 `
  -OutputDirectory .\artifacts\portable `
  -Label local
```

Expected: ZIP, `.sha256`, and exit code `0`.

- [ ] **Step 6: Commit Task 5**

```bash
git add build.bat run.bat scripts/New-PortablePackage.ps1 scripts/tests/Test-NewPortablePackage.ps1
git commit -m "build: add executable self-check and portable package"
```

---

### Task 6: 讓 push／PR workflow 上傳 ZIP artifact

**Files:**
- Modify: `.github/workflows/windows-build.yml:1-23`

**Interfaces:**
- Consumes: `build.bat`, `run.bat --check`, Task 1/5 script tests and `New-PortablePackage.ps1`。
- Produces: Actions artifact `3waSshDrive-win-x64-<short-sha>` containing ZIP and `.sha256`。

- [ ] **Step 1: 先在 workflow 加 script-test gate**

Add repository least privilege and script tests before the build:

```yaml
permissions:
  contents: read

steps:
  - name: Test release version script
    shell: pwsh
    run: .\scripts\tests\Test-ReleaseVersion.ps1

  - name: Test portable package script
    shell: pwsh
    run: .\scripts\tests\Test-NewPortablePackage.ps1
```

- [ ] **Step 2: 保留 build.bat 與 run.bat gates**

The workflow must still run:

```yaml
- name: Build and test through build.bat
  shell: cmd
  run: call build.bat --no-pause

- name: Run executable self-check through run.bat
  shell: cmd
  run: call run.bat --check
```

- [ ] **Step 3: 建立 short SHA、ZIP 與 upload-artifact**

Add:

```yaml
- name: Set artifact label
  id: artifact_meta
  shell: pwsh
  run: "Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value ('label=' + $env:GITHUB_SHA.Substring(0, 8))"

- name: Package portable build
  shell: pwsh
  run: >-
    .\scripts\New-PortablePackage.ps1
    -SourceDirectory .\src\3waSshDrive.App\bin\Release\net472
    -OutputDirectory .\artifacts\portable
    -Label '${{ steps.artifact_meta.outputs.label }}'

- name: Upload Windows x64 artifact
  uses: actions/upload-artifact@v4
  with:
    name: 3waSshDrive-win-x64-${{ steps.artifact_meta.outputs.label }}
    path: artifacts/portable/*
    if-no-files-found: error
    retention-days: 14
```

- [ ] **Step 4: 驗證 workflow syntax 與 push branch run**

Run locally:

```powershell
dotnet format .\3waSshDrive.sln --verify-no-changes --no-restore
```

Push the task branch and inspect the Actions run. Expected: build/test/self-check green and downloadable ZIP + `.sha256`; do not accept a run where only the source checkout artifact exists.

- [ ] **Step 5: Commit Task 6**

```bash
git add .github/workflows/windows-build.yml
git commit -m "ci: upload Windows portable artifact"
```

---

### Task 7: 建立 tag release、安裝 smoke 與 draft publish gate

**Files:**
- Create: `scripts/New-VelopackRelease.ps1`
- Create: `scripts/Test-InstalledRelease.ps1`
- Create: `scripts/Test-ReleaseAssets.ps1`
- Create: `scripts/tests/Test-NewVelopackRelease.ps1`
- Create: `.github/workflows/windows-release.yml`

**Interfaces:**
- Consumes: `THREEWA_*_VERSION`, build output, GitHub token and stable prior GitHub Release。
- Produces: `3waSshDrive-Setup.exe`, `3waSshDrive-<version>-full.nupkg`, optional delta, `releases.win.json`, `assets.win.json`, portable ZIP and `SHA256SUMS.txt`。
- Produces: draft Release first; only `Test-InstalledRelease.ps1` and `Test-ReleaseAssets.ps1` may unlock publish。

- [ ] **Step 1: 寫 Velopack argument RED tests**

Implement the packaging script with `-WhatIfArguments` returning its `vpk pack` argument array without running it. The test must assert these exact values are present:

```powershell
'pack'
'--packId'; '3waSshDrive'
'--packVersion'; '2026.919.1'
'--packDir'; $resolvedBuildDirectory
'--mainExe'; '3waSshDrive.exe'
'--packTitle'; '3waSshDrive'
'--runtime'; 'win-x64'
'--channel'; 'win'
'--instLocation'; 'PerUser'
'--noPortable'
```

The unsigned case must not contain `--signParams`. A second case sets `VELOPACK_SIGN_PARAMS=/fd SHA256` and asserts `--signParams` plus that exact value are appended.

Run:

```powershell
pwsh -NoProfile -File .\scripts\tests\Test-NewVelopackRelease.ps1
```

- [ ] **Step 2: 實作 release packaging script**

Parameters:

```powershell
param(
    [Parameter(Mandatory = $true)][string]$BuildDirectory,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][string]$PackageVersion,
    [Parameter(Mandatory = $true)][string]$ReleaseNotes,
    [string]$RepositoryUrl = 'https://github.com/shadowjohn/3waSshDrive',
    [string]$GitHubToken,
    [switch]$WhatIfArguments
)
```

Install/use `vpk` from a repository-local tool path:

```powershell
$vpkPath = '.\.tools\vpk.exe'
if (-not (Test-Path -LiteralPath $vpkPath)) {
    dotnet tool install --tool-path .\.tools vpk --version 1.2.0
    if ($LASTEXITCODE -ne 0) { throw "vpk install failed." }
}
$vpk = Resolve-Path -LiteralPath .\.tools\vpk.exe
if (-not ((& $vpk --version) -match '^1\.2\.0')) {
    throw "Unexpected vpk version."
}
```

If `gh release list --exclude-drafts --exclude-pre-releases --limit 1` returns a prior
stable release, run the equivalent of this exact command before packing so a delta can
be generated:

```powershell
& $vpk download github `
  --repoUrl $RepositoryUrl `
  --token $GitHubToken `
  --channel win `
  --outputDir $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw "Previous release download failed." }
```

Do not pass `--token` when `GitHubToken` is empty. Treat a download failure as fatal
when a prior release exists.

Build the argument array shown in Step 1. Add `--signParams $env:VELOPACK_SIGN_PARAMS` only when the value is non-empty. Run `vpk`, then require Setup, full package, `releases.win.json`, and `assets.win.json`. A delta is optional only when no prior stable full package was available.

- [ ] **Step 3: 實作 clean-runner install smoke**

`Test-InstalledRelease.ps1` accepts `-SetupPath`, `-ExpectedDisplayVersion` and `-ExpectedPackageVersion`. It must:

```powershell
$root = Join-Path $env:LOCALAPPDATA '3waSshDrive'
if (Test-Path -LiteralPath $root) { throw "Install root already exists: $root" }

$setup = Start-Process -FilePath $SetupPath -ArgumentList '--silent' -Wait -PassThru
if ($setup.ExitCode -ne 0) { throw "Setup failed: $($setup.ExitCode)" }

$exe = Join-Path $root '3waSshDrive.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "Installed executable missing: $exe" }

$check = Start-Process -FilePath $exe -ArgumentList '--self-check' -Wait -PassThru
if ($check.ExitCode -ne 0) { throw "Installed self-check failed: $($check.ExitCode)" }
```

Read the installed EXE with `System.Diagnostics.FileVersionInfo`: require
`ProductVersion == ExpectedDisplayVersion`, and parse `FileVersion` to require
`YYYY.M.D.R` derived from `ExpectedDisplayVersion`. Derive the package version from the
same display version and require `ExpectedPackageVersion`; the preceding installed
`--self-check` independently verifies that this derived value equals the embedded
`AssemblyMetadata("VelopackPackageVersion")`. Do not reflection-load the installed EXE
from PowerShell, because that can retain a file lock and break uninstall. In `finally`,
run:

```powershell
$updateExe = Join-Path $root 'Update.exe'
if (Test-Path -LiteralPath $updateExe) {
    $uninstall = Start-Process -FilePath $updateExe -ArgumentList '--silent uninstall' -Wait -PassThru
    if ($uninstall.ExitCode -ne 0) { throw "Uninstall failed: $($uninstall.ExitCode)" }
}
$deadline = [datetime]::UtcNow.AddSeconds(30)
while ((Test-Path -LiteralPath $root) -and [datetime]::UtcNow -lt $deadline) {
    Start-Sleep -Milliseconds 250
}
if (Test-Path -LiteralPath $root) { throw "Uninstall left install root: $root" }
```

- [ ] **Step 4: 實作 asset 與 checksum gate**

`Test-ReleaseAssets.ps1` takes `-Directory`, `-Tag`, and optional `-Remote`. It must require:

- `3waSshDrive-Setup.exe`
- exactly one `*-full.nupkg` for the target package version
- `releases.win.json`
- `assets.win.json`
- `3waSshDrive-win-x64-$Tag.zip`
- `SHA256SUMS.txt`

Generate `SHA256SUMS.txt` only after every final local asset exists, using uppercase SHA-256 and one relative filename per line; exclude `SHA256SUMS.txt` itself from its input list. When `-Remote` is set, compare required names against `gh release view $Tag --json isDraft,assets`; require `isDraft=true` until smoke completes.

- [ ] **Step 5: 建立 tag-only workflow**

The workflow starts with:

```yaml
name: Windows release

on:
  push:
    tags:
      - 'v[0-9][0-9][0-9][0-9].[0-9][0-9].[0-9][0-9].[0-9][0-9]'

concurrency:
  group: release-${{ github.ref }}
  cancel-in-progress: false

jobs:
  release:
    runs-on: windows-2022
    permissions:
      contents: write
```

Its ordered steps are:

1. checkout with submodules and full history;
2. setup .NET SDK `10.0.x`;
3. install repository-local `vpk` version `1.2.0`;
4. run all three PowerShell script-test files;
5. run `Resolve-ReleaseVersion.ps1 -Tag $env:GITHUB_REF_NAME -GitHubEnv $env:GITHUB_ENV`;
6. run `build.bat --no-pause` with injected environment values;
7. run `run.bat --check`;
8. create tag-named portable ZIP;
9. create release notes with the exact command below and save the returned body as UTF-8;
10. run `New-VelopackRelease.ps1`;
11. generate `SHA256SUMS.txt` and validate local assets;
12. query `gh release view $env:GITHUB_REF_NAME --json isDraft`; continue when no release exists or `isDraft=true`, but fail if the tag already has a published release;
13. run `vpk upload github --outputDir .\artifacts\release --repoUrl "https://github.com/$env:GITHUB_REPOSITORY" --token $env:GH_TOKEN --tag $env:GITHUB_REF_NAME --releaseName $env:GITHUB_REF_NAME --merge` without `--publish`, creating or updating a draft;
14. upload portable ZIP and `SHA256SUMS.txt` with `gh release upload --clobber`;
15. validate the remote draft assets;
16. run `Test-InstalledRelease.ps1`;
17. publish with `gh release edit $env:GITHUB_REF_NAME --draft=false --latest`;
18. upload the local release directory as an Actions diagnostic artifact under `if: always()`.

Set `GH_TOKEN: ${{ github.token }}` and `VELOPACK_SIGN_PARAMS: ${{ secrets.VELOPACK_SIGN_PARAMS }}` only on the steps that require them. Do not add Microsoft submission URLs or browser automation.

```powershell
$generated = gh api --method POST `
  "repos/$env:GITHUB_REPOSITORY/releases/generate-notes" `
  -f "tag_name=$env:GITHUB_REF_NAME" `
  -f "target_commitish=$env:GITHUB_SHA" | ConvertFrom-Json
Set-Content -LiteralPath .\artifacts\release-notes.md `
  -Value $generated.body `
  -Encoding utf8
```

- [ ] **Step 6: 驗證 dry run 與 YAML parse**

```powershell
pwsh -NoProfile -File .\scripts\tests\Test-NewVelopackRelease.ps1
pwsh -NoProfile -File .\scripts\New-VelopackRelease.ps1 `
  -BuildDirectory .\src\3waSshDrive.App\bin\Release\net472 `
  -OutputDirectory .\artifacts\release-dry-run `
  -PackageVersion 2026.919.1 `
  -ReleaseNotes .\README.md `
  -WhatIfArguments
```

Expected: the argument list contains `PerUser`, `win-x64`, package version `2026.919.1`, and no signing option in an unsigned environment.

- [ ] **Step 7: Commit Task 7**

```bash
git add scripts/New-VelopackRelease.ps1 scripts/Test-InstalledRelease.ps1 scripts/Test-ReleaseAssets.ps1 scripts/tests/Test-NewVelopackRelease.ps1 .github/workflows/windows-release.yml
git commit -m "ci: publish verified Velopack releases"
```

---

### Task 8: Phase B 整體驗證與文件

**Files:**
- Modify: `README.md`
- Modify: `history.md`

**Interfaces:**
- Produces: 維護者可直接照做的本機 build、artifact 與 tag release SOP。

- [ ] **Step 1: 跑完整本機驗證**

```powershell
pwsh -NoProfile -File .\scripts\tests\Test-ReleaseVersion.ps1
pwsh -NoProfile -File .\scripts\tests\Test-NewPortablePackage.ps1
pwsh -NoProfile -File .\scripts\tests\Test-NewVelopackRelease.ps1
cmd /c build.bat --no-pause
if ($LASTEXITCODE -ne 0) { throw "build failed" }
cmd /c run.bat --check
if ($LASTEXITCODE -ne 0) { throw "self-check failed" }
dotnet test .\3waSshDrive.sln -c Release --no-build --nologo
```

Expected: every command exits `0`; test count is at least the existing 44 plus the new version／lock／self-check cases.

- [ ] **Step 2: 更新 README**

Document these exact user-facing commands and distinctions:

```text
build.bat
run.bat
run.bat --check
git tag v2026.09.19.01
git push origin v2026.09.19.01
```

State that Actions ZIP is portable/test-only, `3waSshDrive-Setup.exe` is the formal install/update basis, WinFsp is a separately verified prerequisite, unsigned releases are expected when no certificate is configured, and Microsoft online submission is manual after download.

- [ ] **Step 3: 更新 history.md**

Add one dated entry recording: external/internal version mapping, executable self-check, x64 artifact workflow, Velopack 1.2.0 per-user Setup, `%LocalAppData%\3waSshDrive`, optional signing, and draft-before-publish smoke gate.

- [ ] **Step 4: Push branch and inspect both workflows**

Push without a tag first. Expected: only `Windows build` runs and publishes the portable artifact. Then create a disposable release-candidate tag in the approved format only when the branch is ready for formal Phase B acceptance; do not invent a nonconforming tag because the resolver must reject it.

- [ ] **Step 5: Commit Task 8**

```bash
git add README.md history.md
git commit -m "docs: document Phase B release workflow"
```

---

## Phase B Completion Gate

Phase B is complete only when all conditions are true:

- A normal push/PR run provides a downloadable `3waSshDrive-win-x64-<short-sha>.zip` and SHA-256 file.
- `run.bat --check` launches the actual EXE, performs no network or WinFsp operation, and returns the EXE exit code.
- A valid formal tag creates a draft containing Setup, full package, feed JSON, portable ZIP and `SHA256SUMS.txt`.
- Clean-runner per-user install lands at `%LocalAppData%\3waSshDrive`, installed self-check succeeds, and uninstall removes the install root.
- Only after smoke and remote-asset validation does the Release become published.
- No certificate is required, no WinFsp payload is bundled, and no Microsoft online submission step exists.
- Phase C may begin from this green, installable release baseline; Phase D remains excluded.

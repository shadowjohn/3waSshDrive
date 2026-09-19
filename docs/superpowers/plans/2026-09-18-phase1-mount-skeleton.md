# 3waSshDrive Phase 1 Mount Skeleton Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a source-compiled, read-only SFTP filesystem that mounts a selected Linux directory as a Windows drive letter without invoking external mount commands.

**Architecture:** A WinForms shell persists validated profiles and delegates to a mount manager. SSH.NET implements the `IRemoteFileSystem` boundary, while a separately testable WinFsp adapter converts Windows filesystem callbacks into read-only SFTP operations.

**Tech Stack:** C#; .NET Framework 4.7.2; WinForms; WinFsp v2.1 source; SSH.NET 2026.0.0 source; MSTest; GitHub Actions on Windows.

**Spec:** `docs/superpowers/specs/2026-09-18-phase1-mount-skeleton-design.md`

## Global Constraints

- Target .NET Framework 4.7.2 for every 3waSshDrive assembly.
- Do not execute `cmd.exe`, `net use`, `sshfs-win.exe`, or any external mounting process.
- Compile the pinned WinFsp and SSH.NET sources listed in the spec.
- Persist only private-key paths; never persist key contents or passphrases.
- Require an exact pinned SHA-256 host-key fingerprint for mounting.
- Expose a read-only filesystem in Phase 1.
- Keep WinFsp callbacks independent of SSH.NET concrete types.

---

### Task 1: Source-pinned solution and Windows CI

**Files:**
- Create: `3waSshDrive.sln`
- Create: `Directory.Build.props`
- Create: `.github/workflows/windows-build.yml`
- Create: `src/3waSshDrive.WinFsp/3waSshDrive.WinFsp.csproj`
- Create: remaining project files under `src/` and `tests/`
- Modify: `.gitmodules`

**Interfaces:**
- Consumes: WinFsp commit `ddca7bd5481857a65ba552f643b8776fd070836f`; SSH.NET commit `7b2fd3dbf2c86a80a7b06cea020aa5f821c9902e`.
- Produces: a restoreable solution whose WinFsp project links the four upstream .NET host files and whose SFTP project references upstream `Renci.SshNet.csproj`.

- [ ] **Step 1: Add project scaffolding and a compile-failing test reference**

Create the SDK-style projects targeting `net472`. Add a test that references `DriveProfileValidator.Validate`, which does not exist yet.

- [ ] **Step 2: Push the RED commit and verify CI fails for the missing behavior**

Run the Windows workflow and confirm the compiler reports the missing validator/type, rather than a checkout or toolchain error.

- [ ] **Step 3: Keep the source projects pinned**

Record the exact submodule commits as Git links and configure checkout with `submodules: recursive`.

### Task 2: Profile, path mapping, and persistence

**Files:**
- Create: `src/3waSshDrive.Core/Models/DriveProfile.cs`
- Create: `src/3waSshDrive.Core/Profiles/DriveProfileValidator.cs`
- Create: `src/3waSshDrive.Core/Profiles/ProfileStore.cs`
- Create: `src/3waSshDrive.Core/Paths/RemotePathMapper.cs`
- Test: `tests/3waSshDrive.Core.Tests/DriveProfileValidatorTests.cs`
- Test: `tests/3waSshDrive.Core.Tests/ProfileStoreTests.cs`
- Test: `tests/3waSshDrive.Core.Tests/RemotePathMapperTests.cs`

**Interfaces:**
- Produces: `IReadOnlyList<string> DriveProfileValidator.Validate(DriveProfile profile)`; `string RemotePathMapper.Map(string remoteRoot, string windowsPath)`; `IReadOnlyList<DriveProfile> ProfileStore.Load()`; `void ProfileStore.Save(IEnumerable<DriveProfile> profiles)`.

- [ ] **Step 1: Write tests for required fields and normalization**

The tests must assert literal validation messages for invalid port, non-absolute remote root, invalid drive letter, missing key path, and missing host fingerprint. Path tests must cover root, nested paths, trailing slash normalization, and rejection of `..`.

- [ ] **Step 2: Run tests and verify RED**

Run `dotnet test tests/3waSshDrive.Core.Tests/3waSshDrive.Core.Tests.csproj -c Release` and confirm the new behavior is missing.

- [ ] **Step 3: Implement the minimum profile and mapper behavior**

Use data-contract JSON for .NET Framework compatibility. Save through a same-directory temporary file and replace/move it so an interrupted write cannot leave truncated JSON.

- [ ] **Step 4: Run tests and verify GREEN**

Run the Core test project and confirm all cases pass.

### Task 3: SSH.NET read-only backend and host-key policy

**Files:**
- Create: `src/3waSshDrive.Core/Remote/IRemoteFileSystem.cs`
- Create: `src/3waSshDrive.Core/Remote/RemoteEntry.cs`
- Create: `src/3waSshDrive.Core/Remote/RemoteExceptions.cs`
- Create: `src/3waSshDrive.Sftp/Security/HostKeyPolicy.cs`
- Create: `src/3waSshDrive.Sftp/SshNetRemoteFileSystem.cs`
- Create: `src/3waSshDrive.Sftp/SshConnectionProbe.cs`
- Test: `tests/3waSshDrive.Sftp.Tests/HostKeyPolicyTests.cs`

**Interfaces:**
- Produces: `void IRemoteFileSystem.Connect()`; `RemoteEntry GetEntry(string path)`; `IReadOnlyList<RemoteEntry> ListDirectory(string path)`; `Stream OpenRead(string path)`; `ConnectionProbeResult Probe(DriveProfile profile)`.

- [ ] **Step 1: Write host-key policy tests**

Assert that `SHA256:abc123` and `abc123` normalize to the same value, an exact fingerprint is accepted, a mismatch is rejected, and a mount policy with no fingerprint is rejected.

- [ ] **Step 2: Run tests and verify RED**

Confirm the policy types do not yet exist.

- [ ] **Step 3: Implement host-key policy and SFTP boundary**

Create `PrivateKeyAuthenticationMethod`, subscribe to `HostKeyReceived`, set `CanTrust` only through the selected policy, and translate SSH.NET path, permission, and connection exceptions to Core exceptions. Filter `.` and `..` from SSH.NET listings because the WinFsp adapter adds them deliberately.

- [ ] **Step 4: Run tests and verify GREEN**

Run SFTP policy tests and the full solution test set.

### Task 4: Read-only WinFsp callback adapter

**Files:**
- Create: `src/3waSshDrive.FileSystem/SftpReadOnlyFileSystem.cs`
- Create: `src/3waSshDrive.FileSystem/RemoteFileHandle.cs`
- Create: `src/3waSshDrive.FileSystem/WinFspFileInfoMapper.cs`
- Test: `tests/3waSshDrive.FileSystem.Tests/FakeRemoteFileSystem.cs`
- Test: `tests/3waSshDrive.FileSystem.Tests/SftpReadOnlyFileSystemTests.cs`

**Interfaces:**
- Consumes: `IRemoteFileSystem`, `RemotePathMapper`, and WinFsp `FileSystemBase`.
- Produces: WinFsp overrides for `Init`, `GetVolumeInfo`, `GetSecurityByName`, `Open`, `Read`, `GetFileInfo`, `ReadDirectoryEntry`, `Close`, and write rejection.

- [ ] **Step 1: Write callback tests against a real adapter and fake remote**

Use literal fake entries and byte content. Assert stable directory order, file metadata, offset reads copied through unmanaged memory, EOF behavior, path-not-found status, and `STATUS_MEDIA_WRITE_PROTECTED` for write/create operations.

- [ ] **Step 2: Run tests and verify RED**

Confirm failure is caused by the absent adapter.

- [ ] **Step 3: Implement minimum read-only callbacks**

Cache an open stream per file handle and a sorted entry list per directory handle. Dispose streams in `Close`. Map Core exceptions to the NTSTATUS table in the design.

- [ ] **Step 4: Run tests and verify GREEN**

Run the FileSystem tests and all other test projects.

### Task 5: Mount lifecycle

**Files:**
- Create: `src/3waSshDrive.FileSystem/Mounting/IFileSystemHost.cs`
- Create: `src/3waSshDrive.FileSystem/Mounting/WinFspHostAdapter.cs`
- Create: `src/3waSshDrive.FileSystem/Mounting/MountedDrive.cs`
- Create: `src/3waSshDrive.FileSystem/Mounting/MountManager.cs`
- Test: `tests/3waSshDrive.FileSystem.Tests/MountManagerTests.cs`

**Interfaces:**
- Produces: `MountedDrive MountManager.Mount(DriveProfile profile)` and `void MountedDrive.Dispose()`.

- [ ] **Step 1: Write lifecycle tests**

Assert that mount validates before connecting, a failed WinFsp mount disposes the SFTP connection, and normal disposal unmounts before closing SFTP.

- [ ] **Step 2: Run tests and verify RED**

Confirm missing lifecycle types cause the failure.

- [ ] **Step 3: Implement mount ownership and cleanup**

Treat a negative WinFsp mount result as failure and include its hexadecimal NTSTATUS in the exception. Make disposal idempotent.

- [ ] **Step 4: Run tests and verify GREEN**

Run all test projects.

### Task 6: Minimal WinForms shell and handoff documentation

**Files:**
- Create: `src/3waSshDrive.App/Program.cs`
- Create: `src/3waSshDrive.App/MainForm.cs`
- Create: `src/3waSshDrive.App/PasswordlessProfileEditor.cs`
- Modify: `README.md`
- Modify: `history.md`
- Create: `THIRD_PARTY_NOTICES.md`

**Interfaces:**
- Consumes: `ProfileStore`, `SshConnectionProbe`, and `MountManager`.
- Produces: profile edit/save/delete, key-file browse, `Test & Trust`, mount, unmount, and Explorer-open actions.

- [ ] **Step 1: Build the code-only WinForms form**

Keep UI state on the UI thread, run network/mount actions asynchronously, disable conflicting actions while busy, and unmount all active mounts when the form closes.

- [ ] **Step 2: Update operator documentation**

Document source initialization, WinFsp v2.1 installation, key/fingerprint flow, Release build, known read-only limitations, and the manual smoke-test checklist.

- [ ] **Step 3: Run fresh full verification**

Run `dotnet test 3waSshDrive.sln -c Release` and `dotnet build src/3waSshDrive.App/3waSshDrive.App.csproj -c Release --no-restore` on Windows. Require zero failures before completion.

- [ ] **Step 4: Commit the verified Phase 1 implementation**

Commit the source pins, application, tests, CI workflow, and documentation together only after the final build and test evidence is available.


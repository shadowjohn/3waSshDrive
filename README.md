# 3waSshDrive

Project a Linux workspace into Windows as a drive letter through WinFsp and SFTP—without shelling out to SSHFS-Win, `net use`, or `cmd.exe`.

3waSshDrive is aimed at developer and agent workflows: mount a narrow Linux workspace such as `/home/dev/project` at `Z:` and let Explorer, Visual Studio Code, diff tools, or a Windows-side coding agent use normal file APIs.

## Phase 1

The first milestone is deliberately read-only:

- Multiple named SSH profiles.
- Per-profile authentication selection: private-key path or password.
- Private-key contents and passwords are never copied into the profile store.
- Explicit `Test & Trust` flow with SHA-256 host-key pinning.
- Source-built SSH.NET SFTP client.
- Source-built WinFsp .NET host layer.
- Directory listing, metadata, file open/read/close, mount, and unmount.
- Writes, create, rename, and delete are rejected by the filesystem.

The UI stores profiles at:

```text
%APPDATA%\3waSshDrive\profiles.json
```

## Prerequisites

- Windows 10 or Windows 11.
- [.NET Framework 4.7.2](https://dotnet.microsoft.com/en-us/download/dotnet-framework/net472) runtime and developer pack.
- Visual Studio 2022 with the .NET desktop build tools, or a compatible MSBuild/.NET SDK environment.
- The official [WinFsp v2.1 Core runtime](https://github.com/winfsp/winfsp/releases/tag/v2.1) installed on the machine. The native runtime/driver is required; SSHFS-Win, Cygwin, and WinFsp Developer files are not.
- A Linux host with SSH/SFTP enabled and either an accepted unencrypted private key or password for the selected user.

Phase 1 does not persist private-key passphrases or SSH passwords. Passwords stay only in the running application memory, so enter a password again after restarting the application. Use a dedicated unencrypted key with narrow server-side authorization and filesystem permissions where key authentication is available.

## Get the source

Both runtime dependencies are pinned source submodules, so clone recursively:

```powershell
git clone --recurse-submodules https://github.com/shadowjohn/3waSshDrive.git
cd 3waSshDrive
```

If the repository was cloned without submodules:

```powershell
git submodule update --init
```

Pinned upstream revisions:

| Component | Version | Commit |
| --- | --- | --- |
| WinFsp | v2.1 | `ddca7bd5481857a65ba552f643b8776fd070836f` |
| SSH.NET | 2026.0.0 | `7b2fd3dbf2c86a80a7b06cea020aa5f821c9902e` |

### WinFsp runtime verification

Before a drive can mount, 3waSshDrive verifies the installed x64 WinFsp DLL and driver against the pinned v2.1.25156 runtime manifest at [`runtime/winfsp-2.1.25156-manifest.json`](runtime/winfsp-2.1.25156-manifest.json). The official MSI SHA-256 is:

```text
073A70E00F77423E34BED98B86E600DEF93393BA5822204FAC57A29324DB9F7A
```

The manifest pins `winfsp-x64.dll`, `winfsp-x64.sys`, and their file version. This is intentionally strict: a different WinFsp release must be reviewed and its manifest updated before 3waSshDrive will mount. The WinFsp Launcher service is not required for 3waSshDrive's direct in-process drive mounts.

## Build and test

### Lazy Windows route

Double-click `build.bat` once to initialize the pinned submodules, restore,
test, and create a Release build. Then double-click `run.bat` to launch the
application. If the executable is missing, `run.bat` builds it automatically.

### Command line

```powershell
dotnet restore .\3waSshDrive.sln
dotnet test .\3waSshDrive.sln -c Release --no-restore
dotnet build .\src\3waSshDrive.App\3waSshDrive.App.csproj -c Release --no-restore
```

Run:

```text
src\3waSshDrive.App\bin\Release\net472\3waSshDrive.exe
```

## First connection

1. Create a profile and enter host, port, username, remote root, drive letter, and either a private-key path or password.
2. Choose `Test & Trust`. The application connects, verifies that the remote root is a directory, captures the server SHA-256 host-key fingerprint, and saves the profile.
3. Choose `Mount`.
4. Open the drive through `Open Explorer`, then read a known file.
5. Choose `Unmount` before changing or deleting the profile.

`Mount` requires the saved fingerprint. If the server host key changes, the connection is rejected; run `Test & Trust` only after independently confirming the change is legitimate.

## Manual smoke test

- WinFsp v2.1 is installed.
- The pinned runtime preflight succeeds before `Mount` continues.
- `Test & Trust` returns a fingerprint and the expected root using the selected authentication method.
- The selected drive letter appears in Explorer.
- Directories enumerate in stable order.
- A text file opens and content matches the Linux source.
- A write/create attempt fails as read-only.
- `Unmount` removes the drive letter.
- Closing 3waSshDrive releases every mount it created.

## Current compatibility boundary

- Read-only only.
- Linux symbolic links are followed by the SFTP server. A symlink inside the mounted root can point outside it.
- Linux filenames containing `\` cannot be represented by this Windows projection.
- No ACL, xattr, UID/GID, hard-link, sparse-file, or alternate-data-stream mapping.
- No reconnect, persistent cache, tray mode, Windows service, or installer yet.
- Large directory trees and many tiny files still pay SFTP round-trip cost.

See the [design](docs/superpowers/specs/2026-09-18-phase1-mount-skeleton-design.md), [implementation plan](docs/superpowers/plans/2026-09-18-phase1-mount-skeleton.md), and [compatibility notes](docs/compatibility.md).

## License

3waSshDrive is licensed under GPL-3.0-only. See [LICENSE](LICENSE) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

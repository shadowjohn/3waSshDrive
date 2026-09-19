# 3waSshDrive Phase 1 Mount Skeleton Design

## Purpose

3waSshDrive projects a selected Linux directory into Windows as a drive letter so Explorer, editors, diff tools, and coding agents can work against the remote files through normal Windows file APIs.

Phase 1 proves the full read path: configure an SSH host and private-key path, authenticate over SFTP, mount a remote root through WinFsp, enumerate directories, and read files. The application owns the mount lifecycle and does not execute `cmd.exe`, `net use`, `sshfs-win.exe`, or another mount process.

## Scope

Phase 1 includes:

- C# .NET Framework 4.7.2 and WinForms.
- Private-key authentication; only the key path is persisted.
- SHA-256 SSH host-key pinning before a mount is allowed.
- Multiple named profiles stored under `%APPDATA%\3waSshDrive\profiles.json`.
- A read-only WinFsp filesystem mounted at a selected drive letter.
- Directory enumeration, metadata lookup, file open/read/close, mount, unmount, and Explorer launch.
- A small connection UI for profile editing, host-key capture, connection testing, mounting, and unmounting.
- Windows CI that builds and runs unit tests.

Phase 1 explicitly excludes writes, rename/delete/create, ACL mapping, xattrs, hard links, sparse files, alternate data streams, UID/GID editing, persistent cache, automatic reconnect, tray behavior, Windows service mode, and an installer.

Linux symbolic links are followed by the SFTP server. This means a symlink below the selected remote root can expose a target outside that root; Phase 1 documents this behavior rather than claiming a security boundary it does not provide.

## Dependency strategy

Runtime filesystem and SSH code are compiled from source checked out at fixed commits:

| Component | Version | Commit | Integration |
| --- | --- | --- | --- |
| WinFsp | v2.1 | `ddca7bd5481857a65ba552f643b8776fd070836f` | Compile `FileSystemBase+Const.cs`, `FileSystemBase.cs`, `FileSystemHost.cs`, and `Interop.cs` into `3waSshDrive.WinFsp` |
| SSH.NET | 2026.0.0 | `7b2fd3dbf2c86a80a7b06cea020aa5f821c9902e` | Build the upstream `Renci.SshNet.csproj` through a project reference |

Both repositories are Git submodules so source provenance and upgrades remain reviewable. WinFsp's installed native runtime/driver is still required on the Windows machine; 3waSshDrive replaces SSHFS-Win/Cygwin, not the filesystem driver.

Because WinFsp source is compiled into the solution, 3waSshDrive is distributed under GPL-3.0-only. SSH.NET remains MIT-licensed and is listed in `THIRD_PARTY_NOTICES.md`.

## Architecture

```text
3waSshDrive.App (WinForms)
    |
    +-- ProfileStore / validation
    +-- ConnectionProbe
    +-- MountManager
            |
            +-- SshNetRemoteFileSystem
            |       |
            |       +-- SSH.NET source
            |
            +-- SftpReadOnlyFileSystem
                    |
                    +-- WinFsp .NET host source
                            |
                            +-- installed WinFsp native runtime/driver
```

`3waSshDrive.Core` owns profiles, validation, JSON persistence, remote-path mapping, remote entry models, and the `IRemoteFileSystem` boundary.

`3waSshDrive.Sftp` owns SSH.NET-specific authentication, host-key verification, exception translation, SFTP metadata, directory listing, and read streams.

`3waSshDrive.FileSystem` owns WinFsp callbacks and mount lifecycle. It depends only on `IRemoteFileSystem`, so callbacks can be tested with an in-memory fake without a live SSH server or installed driver.

`3waSshDrive.App` owns the WinForms interface and keeps mounted-drive objects alive until explicit unmount or application exit.

## Data flow

1. The operator selects or edits a profile.
2. `Test & Trust` opens an SFTP connection, captures the server SHA-256 host-key fingerprint, verifies the configured remote root can be listed, then stores that fingerprint in the profile.
3. `Mount` validates all fields and requires the saved fingerprint.
4. `SshNetRemoteFileSystem` connects and rejects a different host key.
5. `SftpReadOnlyFileSystem` maps a WinFsp path such as `\packs\yolo\api.php` under the configured remote root.
6. WinFsp callbacks call the SFTP boundary for metadata, listing, or stream reads.
7. `Unmount` disposes the WinFsp host first, then the SFTP connection.

## Path and metadata rules

- Remote roots are absolute POSIX paths and normalized without a trailing slash except `/`.
- WinFsp paths use backslashes and are converted segment by segment to `/`.
- `.` segments are ignored; `..` segments are rejected to prevent lexical escape.
- Linux names containing a backslash cannot be represented in Phase 1.
- SFTP directories map to `FileAttributes.Directory`.
- Regular files map to `FileAttributes.ReadOnly`; dotfiles additionally map to `FileAttributes.Hidden`.
- SFTP modification time is used as Windows creation/change time when no creation time exists.
- Directory entries are sorted case-insensitively for stable Explorer enumeration.

## Error handling

The SFTP layer translates upstream exceptions into filesystem-neutral errors. The WinFsp adapter maps them to NTSTATUS values:

| Remote condition | NTSTATUS |
| --- | --- |
| Missing path | `STATUS_OBJECT_NAME_NOT_FOUND` |
| Permission denied | `STATUS_ACCESS_DENIED` |
| Read past EOF | `STATUS_END_OF_FILE` |
| Any write operation | `STATUS_MEDIA_WRITE_PROTECTED` |
| Broken SSH/SFTP connection | `STATUS_DEVICE_NOT_READY` |
| Unexpected failure | `STATUS_UNEXPECTED_IO_ERROR` |

Mount failures are surfaced in the UI with the profile name and actionable cause. The UI never logs private-key contents.

## Security

- Profiles persist only the absolute private-key path, never key material or passphrases.
- Phase 1 supports unencrypted private keys. Passphrase prompting and DPAPI-backed session secrets are deferred.
- First-use trust is explicit through `Test & Trust`; normal mounts require an exact SHA-256 host-key match.
- Profiles default to a non-root Linux account and a narrow remote root.
- The filesystem is read-only even if the Linux account has write permission.

## Testing and verification

Unit tests cover profile validation, atomic profile persistence, path mapping, host-key matching, error translation, directory enumeration, metadata, offset reads, write rejection, and mount cleanup. WinFsp callback tests use the real adapter with a fake `IRemoteFileSystem`.

GitHub Actions runs on Windows, initializes both submodules, restores the source projects, executes tests, and builds the WinForms application in Release mode. A manual Windows smoke test with WinFsp installed remains required to prove a real drive-letter mount against a Linux host.

## Phase 1 acceptance criteria

- No runtime process launch is used for mounting or SSH.
- A pinned SSH host can be authenticated using a selected key path.
- A configured Linux directory mounts as the selected Windows drive letter.
- Explorer and a normal Windows file reader can enumerate and read remote files.
- Create, write, rename, and delete requests are rejected.
- Unmount releases the drive letter and SSH connection.
- CI passes on Windows with source-pinned WinFsp and SSH.NET checkouts.


# 3waSshDrive History

## 2026-09-19 — Phase C in-app updater

- Added a stable public GitHub Release feed through Velopack 1.2.0. Startup checks are non-blocking and silent on background failure; manual checks are available from both the header and system tray.
- Added a single-session gate shared by startup, header, and tray triggers so only one check/download/apply flow can run at a time. Portable and unmanaged builds are explicitly diagnostic-only and direct users to the per-user Setup.
- Added an update prompt with external current/target versions, release notes, Update/Later choices, progress reporting, checksum-aware failure handling, and safe messages that omit connection secrets.
- Added download → quiesce → apply ordering. New mount/profile actions and reconnect stop only at preparation time; every mounted drive is disposed before apply, while any unmount failure or timeout cancels replacement and keeps the current version.
- Added per-user restart verification for stale `lock.pid` contents and for a live exclusive lock-file handle during `--self-check`; the normal OS-backed single-instance lock remains authoritative for regular launches.
- Kept WinFsp outside application updates: Setup and in-app updates replace only 3waSshDrive, while the pinned WinFsp prerequisite remains a separate verified install.

## 2026-09-19 — Phase B build and release foundation

- Fixed the public version contract at `vYYYY.MM.DD.RR`; for example, `v2026.09.19.01` maps to Velopack `2026.919.1` and assembly/file version `2026.9.19.1`.
- Added an executable `--self-check` path that validates x64 and packaged metadata/files without requiring SSH, network access, or WinFsp.
- Added a Windows x64 push/PR workflow artifact with a portable ZIP and SHA-256 checksum while retaining `build.bat` and `run.bat` as the shared entry points.
- Added pinned Velopack 1.2.0 packaging for a per-user `3waSshDrive-Setup.exe` installed under `%LocalAppData%\3waSshDrive`; WinFsp remains a separately verified prerequisite and is not bundled.
- Made Authenticode signing optional so an unsigned release remains valid when no certificate is configured.
- Added a draft-before-publish gate: required assets and checksums are verified, the Setup is installed on a clean runner, the installed EXE and versions are checked, and uninstall must remove the install root before publication.

## 2026-09-18 — Project start

- Project name fixed as `3waSshDrive`.
- Chosen stack: C# .NET Framework 4.7.2 and WinForms.
- Runtime architecture: WinFsp .NET host callbacks directly backed by SSH.NET/SFTP.
- Explicitly rejected runtime wrappers around `cmd.exe`, `net use`, and `sshfs-win.exe`.
- Phase 1 target: source-compiled, key-authenticated, host-key-pinned, read-only drive mount.
- WinFsp native runtime/driver remains a required machine prerequisite.
- WinFsp v2.1 and SSH.NET 2026.0.0 are pinned as source submodules.

## 2026-09-18 — Phase 1 implementation

- Added a source-built WinFsp .NET host project and source-built SSH.NET project reference.
- Added profile validation, atomic JSON persistence, POSIX/Windows path mapping, and SHA-256 host-key policy.
- Added SSH.NET read-only SFTP backend and connection probe.
- Added WinFsp read-only metadata, directory enumeration, open/read/close, EOF handling, and write rejection.
- Added deterministic mount lifecycle cleanup for both successful and failed mounts.
- Added the first WinForms profile / trust / mount / unmount interface.
- Added Windows CI and 26 unit tests across Core, SFTP, and filesystem callbacks before the UI build gate.

## 2026-09-18 — Authentication choice and live SFTP probe

- Added a per-profile private-key/password selection. Passwords are intentionally excluded from `profiles.json` and stay only in process memory.
- Updated validation and SSH.NET connection setup to require the credential matching the selected method.
- Completed a read-only live SFTP probe against a user-supplied test host: PPK parsing, private-key authentication, root metadata/listing, and SHA-256 host-key capture succeeded. No mount was attempted because this workstation still lacks the required WinFsp native runtime.

## 2026-09-18 — Pinned WinFsp runtime and real drive mount

- Added a v2.1.25156 runtime manifest plus preflight that verifies the installed x64 native DLL and driver SHA-256/version before a mount is allowed.
- Downloaded the official v2.1.25156 MSI, verified its release SHA-256, installed only its Core runtime, and verified the installed DLL/driver hashes against the manifest.
- Mounted the user-supplied SFTP root read-only at `T:`, enumerated the root, read a byte through the Windows filesystem API, and unmounted it successfully. No remote writes were made.

## 2026-09-18 — .NET Framework SSH MAC compatibility repair

- A `Test & Trust` probe to an OpenSSH 9.6 host failed with `MAC error` after key exchange; OpenSSH itself negotiated normally, so this was not an authentication, key, or host-key-pinning failure.
- Advanced the source-built SSH.NET submodule to upstream `f099365c`, which resets the server MAC after `TransformFinalBlock` on .NET Framework before the next encrypted packet.
- The rebuilt SFTP probe authenticated with the supplied PPK, read root metadata and a directory listing, and captured the host key successfully. No remote writes were made.

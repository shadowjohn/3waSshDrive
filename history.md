# 3waSshDrive History

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

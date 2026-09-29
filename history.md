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

## 2026-09-29 - Local SSH server account direction

- Confirmed intended scope: local SFTP file transfer and a full SSH terminal. The user selected Windows native accounts following the Windows OpenSSH management-interface recommendation.
- Password authentication uses the corresponding Windows account password. Planned account controls should delegate credential creation/reset to Windows without persisting a separate password copy in application profiles.
- This is a design decision only. No server implementation, account creation, service startup, or firewall changes have been performed. Live SSH/SFTP login, account permissions, and mounting a Windows SFTP server remain unverified.

## 2026-09-29 - Local SSH server service and public-key design

- Confirmed service start/disable controls and per-account public-key management as first-version requirements, in addition to Windows native accounts, SFTP, and a full SSH terminal.
- Added docs/superpowers/specs/2026-09-29-local-ssh-server-design.md for review: independent Windows OpenSSH service, Windows-managed passwords, multiple public keys, publickey-only SSH mode, scoped elevation, and existing-config preservation.
- Distinguished service stop from Disabled startup type and from ending established sessions. Key removal and SSH access revocation must be verified with fresh connections; neither is presented as forced logout.
- Checked the design against Microsoft/OpenSSH documentation and one independent consistency review; addressed elevated read access for other users public-key lists. Documentation-only verification preserves existing history bytes and checks whitespace/encoding. No build, account creation, service/firewall changes, or live SSH/SFTP tests were performed; implementation and runtime acceptance remain pending.

## 2026-09-29 - SSH source IP allowlist requirement

- Added the user-requested source IP allowlist to the same server design: server-wide IPv4/IPv6 hosts or CIDRs for SSH and SFTP, with loopback-only initial access and explicit network exposure.
- Specified matching firewall source rules and OpenSSH login-source restrictions, handling additive AllowUsers rules, existing broad firewall exceptions, partial failures, and fresh-connection acceptance from allowed and denied sources.
- Verified the design against official Microsoft/OpenSSH rule documentation. This remains documentation only; no firewall, sshd configuration, service, or account changes have been applied.

## 2026-09-29 - Server tabs, bubble forms, and password failure bans

- Added the requested remote-mount/local-server tabs and bubble add/edit forms to the design. Inline field validation, retained non-secret drafts, UAC cancellation, duplicate-submit protection, and keyboard focus must preserve the existing mount lifecycle; the remote Test & Mount AcceptButton must not run from the server tab or a bubble.
- User clarified initial failure counters: source IP plus existing Windows account (SID) are independent; nonexistent account names share one counter per source IP. Any counter reaching five password failures in a rolling minute bans the entire source IP for five minutes.
- Defined the IP-level escalation: after expiry, the next password failure bans for one hour; further failures after expiry keep the one-hour stage until a new successful password/publickey login resets that IP. Active-ban delayed success events do not clear the ban.
- Added a narrowly scoped independent ServerGuard Windows service to monitor verified OpenSSH events and manage timed firewall bans while the GUI is closed. Recorded asynchronous enforcement, event deduplication, durable recovery, existing-session effects, and independent Windows account lockout limitations.
- Read existing UI code and official OpenSSH/Windows documentation, then completed one focused consistency review after the counter clarification. Only the spec/history changed; existing UTF-8 without BOM and CRLF are preserved, and whitespace checks are required. No app build, desktop UI run, account/service/firewall change, or live login/ban validation was performed.

## 2026-09-29 - Seven-day event grid

- Added the requested third Events tab with a read-only grid for network/connection loss, reconnects, inbound connections/authentication, bans, and unbans. Defined time/source/type/outcome/account/IP filters, safe details, paging, and non-disruptive refresh.
- Defined rolling 168-hour event retention with durable per-user client and protected machine-level server event stores; ServerGuard continues collecting server events with the UI closed. Grid pause does not pause collection or protection.
- Identified both UI heartbeat and SFTP operation-level reconnects as event sources; structured correlation/deduplication must prevent missed short reconnects and duplicate disconnect rows. Connection/authentication success is distinct from transferred-file success.
- Kept event retention separate from ban deadlines, escalation state, and event-processing cursors. Only application event history is cleaned; existing crash logs and Windows native event logs remain separate.
- Documentation-only checks preserve UTF-8 without BOM/CRLF and pass git diff whitespace validation. Desktop rendering, actual event collection, retention cleanup, authentication, and ban enforcement remain unimplemented and unverified.

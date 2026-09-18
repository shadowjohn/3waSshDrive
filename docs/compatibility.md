# Compatibility and semantics

## Supported in Phase 1

| Operation | Status | Notes |
| --- | --- | --- |
| Connect with private key | Supported | Unencrypted key; key path only is persisted |
| SSH host-key pinning | Supported | SHA-256 fingerprint required for mount |
| Mount drive letter | Supported | Installed WinFsp v2.1 runtime/driver required |
| Lookup metadata | Supported | SFTP size/access/modified fields |
| Enumerate directory | Supported | Stable case-insensitive order |
| Open/read/close file | Supported | Offset reads through an open SFTP stream |
| Create/write | Rejected | `STATUS_MEDIA_WRITE_PROTECTED` |
| Rename/delete and other mutations | Rejected | Unsupported by the read-only WinFsp adapter |

## Windows ↔ Linux mapping

| Linux/SFTP | Windows projection |
| --- | --- |
| Directory | `FileAttributes.Directory` |
| Regular file | `Archive | ReadOnly` |
| Name beginning with `.` | Adds `Hidden` |
| Last modified time | Last-write, change, and fallback creation time |
| Last access time | Last-access time |
| `/home/user/project/a.txt` below mounted root | `Z:\a.txt` |

Windows `..` segments are rejected before an SFTP call. This prevents lexical escape from the configured root, but server-followed symbolic links are not a containment boundary.

## Deliberately unsupported

- Full POSIX symlink/reparse-point semantics.
- ACL and security descriptor mapping.
- UID/GID or mode editing.
- Extended attributes and alternate data streams.
- Hard links and sparse files.
- Atomic write/rename behavior.
- Automatic reconnect or offline cache.
- Passphrase persistence.

## Manual environment matrix

The CI build proves source compatibility and callback behavior on Windows Server 2022. Before a release, manually smoke-test:

- Windows 10 22H2 + WinFsp v2.1.
- Windows 11 current stable + WinFsp v2.1.
- OpenSSH/SFTP servers on Ubuntu 22.04 and 24.04.
- Ed25519 and RSA private keys without passphrases.

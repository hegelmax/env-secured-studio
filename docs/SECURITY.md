# Security Model

EnvSecured Studio is designed to keep project secrets portable while avoiding plaintext storage when encryption is enabled.

## Encryption

Project passwords are converted into encryption keys with PBKDF2-HMAC-SHA256.

Current defaults:

```text
KDF: PBKDF2-HMAC-SHA256
Iterations: 300000
Payload encryption: AES-256-CBC + HMAC-SHA256
```

The HMAC is checked before decryption, so tampered encrypted payloads fail authentication.

## Storage Modes

Project settings support these storage modes:

```text
Open
SecretsOnly
AllValues
WholeJson
```

- Open: values are stored as plaintext.
- SecretsOnly: only variables marked as secret are encrypted.
- AllValues: all values are encrypted.
- WholeJson: the entire project JSON payload is encrypted.

## Local Password Cache

The application can cache a project key locally after the password is entered. The cache uses Windows DPAPI with `CurrentUser` scope, so cached data is bound to the current Windows user profile.

Moving the project vault to another machine or another Windows account does not move the usable local key cache.

## CLI Export Password Policy

By default, CLI export commands receive an explicit password through:

```text
--password
ENVSECURED_PASSWORD
```

Projects store a plaintext policy flag for whether CLI export requires a password. This policy is not secret; it exists so the CLI can decide whether to require a password before loading the project.

Missing policy data defaults to requiring a password. Whole-JSON encrypted vaults still require a password before export because all settings, including the policy flag, are inside the encrypted payload.

## Updates

The update checker reads public version metadata from GitHub over HTTPS and can download the matching versioned `bin/EnvSecured_vX.Y.Z.exe` file from the repository. Downloaded update binaries are saved next to the current executable and selected in Explorer; they are not executed automatically.

EnvSecured Studio does not currently verify a release signature or a published checksum. This means update integrity relies on HTTPS transport and trust in the GitHub release source. If stronger supply-chain guarantees are required, verify the release artifact independently before replacing the executable.

## Operational Notes

- Do not commit real vault files with secrets.
- Prefer `SecretsOnly`, `AllValues`, or `WholeJson` for shared repositories.
- Use `WholeJson` when project metadata itself should not be readable.
- Generated export files may contain plaintext secrets and should be treated as deployment artifacts.

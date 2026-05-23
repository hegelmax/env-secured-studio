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

Projects can require CLI export commands to receive an explicit password through:

```text
--password
ENVSECURED_PASSWORD
```

If the protected policy block is absent, EnvSecured Studio denies passwordless CLI export by default.

## Operational Notes

- Do not commit real vault files with secrets.
- Prefer `SecretsOnly`, `AllValues`, or `WholeJson` for shared repositories.
- Use `WholeJson` when project metadata itself should not be readable.
- Generated export files may contain plaintext secrets and should be treated as deployment artifacts.

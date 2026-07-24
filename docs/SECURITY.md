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

Version 2 binds encrypted values to their project, variable key, scope, service, environment, and value version through authenticated additional data. Per-variable, per-token-policy, and whole-vault HMAC tags detect manual changes to metadata such as `Key`, `Visible`, `AllowOverride`, `Export`, `Sync`, `Required`, and grants.

## Access tokens and envelope encryption

The project password-derived admin key can manage the complete vault. Access tokens grant only matching service/environment/variable operations. Token secrets are displayed once and are never stored in plaintext.

Token types enforce an outer authorization boundary in addition to grant matching:

- `Service` is permanently limited to its stored `ServiceId`.
- `Environment` is permanently limited to its stored `EnvironmentId`.
- `ServiceEnvironment` is limited to the stored service/environment pair.
- `Custom` permits arbitrary grant combinations.

Typed-token grants are validated when created, edited, rotated, saved, and used. A typed token cannot use `*` or alternatives to escape its declared scope. Existing typed tokens without explicit scope metadata infer the single safe scope from their grants when upgraded.

Newly issued secrets are opaque Base62 containers. Human-readable token IDs and scope names are not exposed in the token text; a random 128-bit lookup ID selects the stored token record. The container also carries its format version, current key version, key material, and a checksum for early corruption detection. Authentication still relies on the token verifier and encrypted key slots, not on the checksum. Legacy `envs_v2.*` strings remain readable until they are rotated or revoked.

Each value is encrypted once with a random DEK. The file stores that DEK wrapped separately in an admin key slot and in token key slots. Rotating or revoking a token changes key slots; it does not create another copy of the encrypted value.

`Read` permits an explicit value lookup from the UI or CLI. `BatchExport` permits inclusion in rendered output only when the variable's effective service scope also has `Export = true`. A token cannot be used with `WholeJson`, because token metadata and key slots are themselves hidden inside the admin-only outer envelope.

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

## Provider Settings

External synchronization provider settings are encrypted inside the vault, including when value storage is `Open`. GitHub provider setup is blocked in the UI unless the local GitHub CLI command `gh` is available on the computer.

GitHub authentication is delegated to `gh`. EnvSecured does not persist GitHub PATs in provider settings: desktop use may rely on `gh auth login`, while headless jobs inherit `GH_TOKEN` or `GITHUB_TOKEN`. Child commands set `GH_PROMPT_DISABLED=1` so missing CI credentials fail rather than opening an interactive prompt. Secret and variable values are supplied to `gh` through standard input instead of process arguments.

GitHub secret comparison uses companion `ENVSECURED_SECRET_FP_<24_HEX>` environment variables containing context-bound HMAC-SHA256 fingerprints. The internal name is derived from the normalized secret name and lives in a reserved namespace, leaving application names such as `DB_PASSWORD_HASH` available. The random fingerprint key is stored only in the provider's encrypted settings. Provider ID, remote environment, secret name, and exact secret value are authenticated, so equal secret values do not expose equality across different names or environments. Independent vaults use different keys by default. A plain secret digest or remotely stored HMAC key is not used because either would enable offline guessing of low-entropy secrets.

## Updates

The update checker reads public version metadata from GitHub over HTTPS and can download the matching versioned `bin/EnvSecured_vX.Y.Z.exe` file from the repository. Downloaded update binaries are saved next to the current executable and selected in Explorer; they are not executed automatically.

EnvSecured Studio does not currently verify a release signature or a published checksum. This means update integrity relies on HTTPS transport and trust in the GitHub release source. If stronger supply-chain guarantees are required, verify the release artifact independently before replacing the executable.

## Operational Notes

- Do not commit real vault files with secrets.
- Prefer `SecretsOnly`, `AllValues`, or `WholeJson` for shared repositories.
- Use `WholeJson` when project metadata itself should not be readable.
- Generated export files may contain plaintext secrets and should be treated as deployment artifacts.

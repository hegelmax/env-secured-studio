# CLI Reference

EnvSecured Studio has one executable. With no arguments it opens the WinForms UI. With a known command or `--help`, it runs in CLI mode.

```powershell
EnvSecured.exe --help                 # short product/CLI overview
EnvSecured.exe --help commands        # complete command list
EnvSecured.exe --help docs            # this full Markdown document
EnvSecured.exe sync-push --help       # command-specific usage, defaults, and exit codes
```

The Markdown document is embedded in the executable, so `--help docs` works with the standalone Release binary and in headless CI checkouts without the repository's `docs` directory.

Project vault files use the `.envs` extension.

```powershell
EnvSecured.exe --register-association
EnvSecured.exe --unregister-association
EnvSecured.exe --check-update
EnvSecured.exe --download-update
```

`--check-update` exit codes are `0` for no update, `10` for update available, and `2` for check failure.

## Project Commands

```powershell
EnvSecured.exe new --file C:\project\envsecured.envs --name MyProject
EnvSecured.exe migrate --file C:\project\envsecured.envs --password "admin password"
EnvSecured.exe save-as --file C:\project\envsecured.envs --to C:\project\renamed.envs
EnvSecured.exe save-as --file C:\project\envsecured.envs --to C:\project\renamed.envs --delete-source true
EnvSecured.exe split --file C:\project\envsecured.envs --to C:\project\backend.envs --owner-service backend --to-password "new password" --encryption whole-json
EnvSecured.exe merge --file C:\project\envsecured.envs --input "C:\project\backend.envs;C:\project\frontend.envs" --input-password "source password"
EnvSecured.exe project --file C:\project\envsecured.envs --name MyProject --description "Shared config vault"
EnvSecured.exe info --file C:\project\envsecured.envs
EnvSecured.exe validate --file C:\project\envsecured.envs
EnvSecured.exe list --file C:\project\envsecured.envs --what variables
EnvSecured.exe list --file C:\project\envsecured.envs --what values
EnvSecured.exe list --file C:\project\envsecured.envs --what values --show-secrets
EnvSecured.exe list --file C:\project\envsecured.envs --what services
EnvSecured.exe list --file C:\project\envsecured.envs --what envs
EnvSecured.exe get --file C:\project\envsecured.envs --key BACKEND_ALLOWED_ORIGINS --service backend --env prod
EnvSecured.exe variable-show --file C:\project\envsecured.envs --key BACKEND_ALLOWED_ORIGINS --service backend --env prod --format json
```

`migrate` converts a legacy flat vault to the nested v2 format. The first replacement preserves the untouched source as `<vault>.v1.bak`.

`variable-show` emits the complete variable metadata, including explicit owner `Visible`, `AllowOverride`, `Export`, `Sync`, and `Required` flags, all service scopes, direct values, and effective source metadata for an optional service/environment target.

`list --what values` masks secret variables by default. Use `--show-secrets` only when stdout is not captured into shared logs.

`get` prints one effective value for a service/environment. By default it returns the calculated value after interpolation:

```powershell
EnvSecured.exe get --file C:\project\envsecured.envs --key BACKEND_ALLOWED_ORIGINS --service backend --env prod
```

```text
http://localhost:5173
```

Use `--value raw` or `--calculated false` to return the selected effective value before interpolation. Secret variables are masked unless `--show-secrets` is passed.

Use `--format json` or `--json` when you need metadata:

```powershell
EnvSecured.exe get --file C:\project\envsecured.envs --key BACKEND_ALLOWED_ORIGINS --service backend --env prod --format json
```

```json
{"key":"BACKEND_ALLOWED_ORIGINS","variableId":"backend-allowed-origins","value":"http://localhost:5173","calculated":true,"service":"backend","environment":"prod","sourceScope":"ServiceEnvironment","sourceServiceId":"backend","sourceEnvironmentId":"prod","updatedAt":"2026-05-25T12:34:56.0000000Z","isSecret":false,"masked":false}
```

`save-as` copies the vault file as-is, including encrypted vaults. Add `--delete-source true` to move/rename the vault after the copy succeeds. Add `--overwrite true` only when the target file may already exist.

`split` creates a new vault file from part of an existing vault. Select variables with `--key KEY[;KEY2]`, `--owner-service service`, `--scope-service service`, or `--all true`. References used as `{{KEY}}` are included by default so interpolated values keep working; pass `--include-refs false` only when you intentionally want a hard split. Use `--to-password` to protect the new file with a different password and `--encryption open|secrets-only|all-values|whole-json` to choose the target storage mode.

`merge` loads one or more vault files and merges them into `--file`. Services/environments are matched by id or name, variables are matched by key, scope rows and values are copied into the target, and matching scoped values are replaced by default. Pass `--overwrite false` to keep existing target values. If input vaults use a different password than the target vault, pass `--input-password` for all input files or `--input-passwords pwd1;pwd2` for per-file passwords.

The same vault split/merge workflows are available in the WinForms UI as separate **Import / Export -> Split** and **Import / Export -> Merge** pages. The Merge page opens external vaults into a mapping wizard first: map environments, services, and variables with incoming-to-current dropdowns such as `jobs` -> `prefect-worker`, then review current and incoming values before applying selected rows.

## Access tokens

```powershell
EnvSecured.exe token-list --file C:\project\envsecured.envs --password "admin password"
EnvSecured.exe token-show --file C:\project\envsecured.envs --id backend-dev --format json --password "admin password"
EnvSecured.exe token-create --file C:\project\envsecured.envs --type service-environment --service backend --env dev --name "Backend Dev" --password "admin password"
EnvSecured.exe token-create-defaults --file C:\project\envsecured.envs --password "admin password"
EnvSecured.exe token-rotate --file C:\project\envsecured.envs --id backend-dev --password "admin password"
EnvSecured.exe token-revoke --file C:\project\envsecured.envs --id backend-dev --password "admin password"
EnvSecured.exe token-delete --file C:\project\envsecured.envs --id backend-dev --password "admin password"
```

`token-create` and `token-rotate` print the new token secret once. Prefer writing it immediately to a protected file or secret manager. Token secrets can be supplied through `--token-file <path>` or `ENVSECURED_TOKEN`; direct `--token` is supported but may appear in process history.

New secrets are emitted as one opaque Base62 string without token IDs, punctuation, or prefixes. Existing `envs_v2.*` secrets remain valid; rotation returns the new Base62 representation.

For `Service`, `Environment`, and `ServiceEnvironment`, CLI derives the standard ID, name, and grant when they are omitted. `Custom` requires `--id` and `--grants`. Typed grants are rejected if they include services or environments outside the selected type scope.

`token-create-defaults` creates one token per active service with `<service>.*.*.Read|BatchExport` and one token per active environment with `*.<environment>.*.Read|BatchExport`. Existing standard token IDs are left unchanged. The Access Tokens screen provides the same operation through **Generate Defaults**.

Grant syntax is `<services>.<environments>.<variables>.<permissions>`. `|` separates alternatives inside a segment, and `*` is a glob. Examples:

```text
database.*.*.Read|BatchExport
*.test.*.Read|BatchExport
database|backend|frontend.dev.*.Read
*.*.PREFECT_*|*_S3_*.Read
```

An explicit token read uses `Read` and is independent of the `Export` flag:

```powershell
$env:ENVSECURED_TOKEN = Get-Content C:\secrets\backend-dev.token -Raw
EnvSecured.exe get --file C:\project\envsecured.envs --key DATABASE_URL --service backend --env dev --show-secrets
```

A token batch export uses `BatchExport` and requires a concrete service and environment. It also enforces the variable scope's `Visible` and `Export` flags; `Export = false` omits the variable from the generated file.

```powershell
EnvSecured.exe export --file C:\project\envsecured.envs --service backend --env dev --output-root C:\project\out
```

Token access is not supported for `WholeJson` vaults because the token registry and key slots are inside the admin-encrypted outer envelope. Creating tokens switches value storage to `AllValues`.

## GitHub synchronization

Synchronization is fully configurable from either the UI or CLI. Provider connection settings, mappings, filters, mode, and fingerprint key are serialized only as `EncryptedSettings`.

### Headless provider management

```powershell
EnvSecured.exe sync-provider-add `
  --file C:\project\envsecured.envs `
  --id github-main `
  --name "Main GitHub" `
  --organization example `
  --repository application `
  --env-map "dev=development;test=test;prod=production" `
  --services "database;backend;frontend" `
  --environments "dev;test;prod" `
  --mode AddUpdate `
  --secrets true `
  --variables true `
  --required-only false

EnvSecured.exe sync-provider-edit --file C:\project\envsecured.envs --provider github-main --mode Mirror
EnvSecured.exe sync-provider-init --file C:\project\envsecured.envs --provider github-main
EnvSecured.exe sync-key-rotate --file C:\project\envsecured.envs --provider github-main
EnvSecured.exe sync-provider-delete --file C:\project\envsecured.envs --provider github-main
```

`sync-provider-add` generates the provider fingerprint key automatically. When `--env-map` is omitted, active vault environments receive identity mappings. Empty `--services` and `--environments` selections mean all active/configured entries. `sync-provider-init` initializes an older provider only when its key is missing. `sync-key-rotate` always creates a new key; the next `AddUpdate`, `Mirror`, or `ForceReplace` push refreshes every managed secret fingerprint.

Use `sync-list --format json` to inspect provider metadata without exposing the fingerprint key:

```powershell
EnvSecured.exe sync-list --file C:\project\envsecured.envs --format json
```

### GitHub authentication

Sync invokes the locally installed GitHub CLI, `gh`. EnvSecured does not store a GitHub PAT in `EncryptedSettings`. Interactive workstations can use credentials created by `gh auth login`. Headless runners can supply either environment variable supported by `gh`:

```powershell
$env:ENVSECURED_PASSWORD = "vault admin password"
$env:GH_TOKEN = "github token"
# GITHUB_TOKEN is also supported; GH_TOKEN has precedence when both are present.

EnvSecured.exe sync-preview --file C:\project\envsecured.envs --provider github-main --check true
```

Child `gh` processes run with `GH_PROMPT_DISABLED=1`, so missing or insufficient CI credentials fail instead of waiting for interactive input. The GitHub token must be able to read the selected repository environments for preview and modify their Actions variables/secrets for push. Vault access tokens are deliberately not accepted for provider administration or sync; use the vault administrator password through `--password` or `ENVSECURED_PASSWORD`.

### Preview, push, and modes

```powershell
EnvSecured.exe sync-preview --file C:\project\envsecured.envs --provider github-main
EnvSecured.exe sync-preview --file C:\project\envsecured.envs --provider github-main --service "backend;worker" --env dev --format json
EnvSecured.exe sync-push --file C:\project\envsecured.envs --provider github-main
EnvSecured.exe sync-push --file C:\project\envsecured.envs --provider github-main --apply true
EnvSecured.exe sync-push --file C:\project\envsecured.envs --provider github-main --apply true --allow-delete true
```

`sync-preview` reads GitHub Actions environment variables and secret names and prints the complete change plan. `sync-push` is a dry run unless `--apply true` is present. Values are sent to `gh variable set` and `gh secret set` over standard input, so they do not appear in process arguments.

- `AddMissing` creates missing variables/secrets and never changes an existing remote value. If an existing secret has no matching fingerprint, it remains unknown and unchanged.
- `AddUpdate` creates missing values and updates changed variables or secrets whose fingerprints differ.
- `Mirror` performs AddUpdate and plans deletion of remote-only values in the enabled provider categories.
- `ForceReplace` uploads every local value even when it currently matches and plans deletion of remote-only values.

`Mirror` and `ForceReplace` require `--allow-delete true` when an applied plan contains deletions. Remote deletion is suppressed when CLI `--service` narrows the provider service set, because a partial service view is not a complete environment mirror. An `--env` filter remains a complete plan for every selected environment mapping.

### Variable and service-scope resolution

The v2 top-level variable flags are the owner-service scope. Therefore top-level `Sync` controls publication through `OwnerServiceId`; it is not a project-wide master switch. Every `ServiceScopes[]` entry independently controls its own service.

For every provider-selected service, a variable participates only when it is visible to that service and that exact owner/service scope has effective `Sync = true`. A missing legacy `Sync` value inherits that scope's `Export` value. Provider membership is the union of qualifying selected-service rows, not an AND between the top-level and every service scope.

EnvSecured resolves the effective value separately for each service/environment. GitHub environments are flat, so rows with the same variable key are then merged:

- identical value and type: one GitHub value is emitted;
- different values or secret/variable types: the plan contains `Conflict`, nothing is applied, and the command exits `2`.

This prevents service iteration order from silently deciding which value wins.

### Secret fingerprints

GitHub does not expose secret values after creation. For every synchronized secret, EnvSecured therefore manages a companion Actions variable in the reserved namespace `ENVSECURED_SECRET_FP_<24_HEX>`. The suffix is the first 96 bits of SHA-256 over the normalized secret name, not its value, so application names such as `DB_PASSWORD_HASH` remain available.

The companion value is a context-bound HMAC-SHA256 over provider ID, remote environment, secret name, and exact value. Its random HMAC key stays inside encrypted provider settings. A plain secret digest is not used because it would permit offline guessing of weak passwords. An exact collision with a reserved internal variable name fails closed.

The same vault and `save-as` copies match on every machine. Independently created vaults use different provider keys and do not match by default.

### CI drift gate and exit codes

`--check true` turns a dry preview into a CI gate:

```powershell
EnvSecured.exe sync-preview --file C:\project\envsecured.envs --provider github-main --check true --format json
if ($LASTEXITCODE -eq 2) { throw "EnvSecured GitHub drift detected" }
```

- `0`: command completed; with `--check`, no drift exists. A successful applied push also returns `0`.
- `1`: vault, provider, authentication, GitHub execution, or safety error, including deletions lacking `--allow-delete true`.
- `2`: `Create`, `Update`, `Replace`, or `Delete` drift under `--check`, or a local flat-key conflict. Without `--check`, an ordinary preview containing non-conflicting drift still returns `0`.

## Editing

```powershell
EnvSecured.exe add-service --file C:\project\envsecured.envs --name backend --prefix BACKEND_ --folder backend --password "admin password"
EnvSecured.exe add-service --file C:\project\envsecured.envs --name worker --strict-contracts
EnvSecured.exe edit-service --file C:\project\envsecured.envs --service backend --display "Backend API" --active true --shared-without-contract false
EnvSecured.exe edit-service --file C:\project\envsecured.envs --service backend --config-name api --toml-name backendApi --yaml-name backend-api --xml-name backend --json-name backend
EnvSecured.exe delete-service --file C:\project\envsecured.envs --service worker
EnvSecured.exe add-env --file C:\project\envsecured.envs --name dev --password "admin password"
EnvSecured.exe edit-env --file C:\project\envsecured.envs --env dev --display Development --config-name dev
EnvSecured.exe edit-env --file C:\project\envsecured.envs --env prod --toml-name production --yaml-name production --xml-name production --json-name production
EnvSecured.exe delete-env --file C:\project\envsecured.envs --env old-dev
EnvSecured.exe add-var --file C:\project\envsecured.envs --key DATABASE_HOST --allow-blank
EnvSecured.exe add-var --file C:\project\envsecured.envs --key DATABASE_PASSWORD --secret
EnvSecured.exe add-var --file C:\project\envsecured.envs --key SHARED_TOKEN --secret --allow-shared-secret
EnvSecured.exe edit-var --file C:\project\envsecured.envs --key DATABASE_PASSWORD --secret true --allow-shared-secret false
EnvSecured.exe edit-var --file C:\project\envsecured.envs --key DATABASE_HOST --new-key DATABASE_SERVER --update-refs true --group Database --owner-service backend --move-owner-values true --allow-null false --allow-blank false --active true
EnvSecured.exe edit-var --file C:\project\envsecured.envs --key API_TOKEN --generated true --generator token-base62 --generator-length 32 --generator-scope owner-env --generator-mode manual
EnvSecured.exe generate --file C:\project\envsecured.envs --key API_TOKEN --env prod
EnvSecured.exe delete-var --file C:\project\envsecured.envs --key OLD_VARIABLE
EnvSecured.exe set --file C:\project\envsecured.envs --key DATABASE_HOST --value 127.0.0.1 --service backend --env dev
EnvSecured.exe delete-value --file C:\project\envsecured.envs --key DATABASE_HOST --service backend --env dev
EnvSecured.exe use --file C:\project\envsecured.envs --key DATABASE_HOST --service backend --visible true --override false
EnvSecured.exe unuse --file C:\project\envsecured.envs --key DATABASE_HOST --service backend --visible true --override false
EnvSecured.exe auto-assign --file C:\project\envsecured.envs
EnvSecured.exe compact-values --file C:\project\envsecured.envs
```

Use an explicit service owner for project-wide variables, for example `project`:

```powershell
EnvSecured.exe set --file C:\project\envsecured.envs --key PROJECT_NAME --value app --service project --env global
```

Use `--optional` to create a service scope entry where the variable is available but a value is not required:

```powershell
EnvSecured.exe use --file C:\project\envsecured.envs --key FEATURE_FLAG --service backend --optional
```

Variables are owned by one service. Use `edit-var --owner-service <service>` to bind a variable to its parent service. EnvSecured no longer creates a mandatory built-in global service; create a normal service such as `project` if you want one owner for project-wide variables.

When `edit-var --owner-service` changes the owner, owner values are moved to the new owner by default if the target layer has no direct value. Pass `--move-owner-values false` to change only ownership metadata.

When `edit-var --new-key` renames a variable, interpolation references such as `{{OLD_KEY}}` are updated to `{{NEW_KEY}}` by default. Pass `--update-refs false` to rename only the variable definition.

`use` controls file export for the service. `--sync true|false` independently controls publication to external integrations. `--visible true|false` controls whether the variable is in that service scope. `--override true|false` controls whether that service may override the owner value. This means `unuse --visible true --override false --sync true` can keep a variable out of rendered files while still publishing it through configured sync providers.

When removing a variable from a service scope with `--visible false`, CLI checks whether that service still references `{{KEY}}`. If references exist, pass `--allow-broken-scope true` to confirm.

`compact-values` removes duplicate value records for the same variable/scope and keeps the last record, matching runtime resolution.

Generated variables are owned by EnvSecured. `--generator` accepts `password`, `token-hex`, `token-base62`, or `guid`. `--generator-scope owner-global` stores one canonical value on the owner service for all environments; `owner-env` stores one canonical value per environment. `--generator-mode manual` only changes values when `generate` or the UI context menu is used; `rotate-on-sync` is reserved for external sync providers and does not rotate on `get` or file export.

Adding an active service or environment creates its standard token by default, switches storage to `AllValues`, and prints/shows the secret once. Pass `--default-token false` to the CLI add command only when token creation must be deferred; use `token-create-defaults` or **Generate Defaults** later.

## Import

```powershell
EnvSecured.exe import --file C:\project\envsecured.envs --input C:\input\.env.dev --service backend --env dev
```

Multiple input files are separated with semicolons:

```powershell
EnvSecured.exe import --file C:\project\envsecured.envs --input "C:\a.env;C:\b.env" --service backend --env dev
```

## Settings

```powershell
EnvSecured.exe settings --file C:\project\envsecured.envs --output-root C:\project\out --format CONFIG --ext .env
EnvSecured.exe settings --file C:\project\envsecured.envs --single-file true --single-file-mask "{project_name}{.ext}"
EnvSecured.exe settings --file C:\project\envsecured.envs --render-mode both --manifest-mask "apps\{service}\.env.example" --manifest-values demo
EnvSecured.exe settings --file C:\project\envsecured.envs --encryption secrets-only
EnvSecured.exe settings --file C:\project\envsecured.envs --encryption open --allow-security-downgrade true
EnvSecured.exe settings --file C:\project\envsecured.envs --cli-export-password true --password "secret"
EnvSecured.exe export-target --file C:\project\envsecured.envs --service backend --env dev --enabled true
EnvSecured.exe export-target --file C:\project\envsecured.envs --all --enabled false
```

Relative output roots such as `.\out`, `..\out`, and `..\..\out` are resolved from the vault file directory.

Supported formats:

```text
CONFIG
TOML
YAML
XML
JSON
```

Supported encryption modes:

```text
open
secrets-only
all-values
whole-json
```

CLI refuses encryption downgrades unless they are explicitly confirmed with `--allow-security-downgrade true`. Downgrades include `whole-json` to any lower mode, `all-values` to `secrets-only` or `open`, and `secrets-only` to `open`.

## Export

Use saved export settings:

```powershell
EnvSecured.exe export --file C:\project\envsecured.envs --all
```

Override output settings for one run:

```powershell
EnvSecured.exe export --file C:\project\envsecured.envs --all --output-root C:\project\out --format JSON --single-file true --single-file-mask "{project_name}{.ext}"
```

Export a specific service/environment:

```powershell
EnvSecured.exe export --file C:\project\envsecured.envs --service backend --env dev --output-root C:\project\out
```

Render service manifests with empty values, such as `.env.example`:

```powershell
EnvSecured.exe export --file C:\project\envsecured.envs --all --render-mode manifest --manifest-mask "apps\{service}\.env.example"
```

Service manifests contain active variables used by each rendered service. Manifest values can be `empty` or `demo`; demo writes `KEY={DemoValue} # {DemoComment}`.

`--render-mode` accepts `data`, `manifest`, or `both`. `--data true|false` and `--manifest true|false` are also available for explicit scripting.

## Export Password

By default, export commands require `--password` or `ENVSECURED_PASSWORD`.

```powershell
$env:ENVSECURED_PASSWORD = "secret"
EnvSecured.exe export --file C:\project\envsecured.envs --all
```

For plaintext or value-encrypted vaults, this can be disabled per project:

```powershell
EnvSecured.exe settings --file C:\project\envsecured.envs --cli-export-password false --password "secret"
EnvSecured.exe export --file C:\project\envsecured.envs --all
```

Whole-JSON encrypted vaults still need a password before export because the policy is inside the encrypted payload.

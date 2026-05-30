# CLI Reference

EnvSecured Studio has one executable. With no arguments it opens the WinForms UI. With a known command or `--help`, it runs in CLI mode.

```powershell
EnvSecured.exe --help
```

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
```

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

## Editing

```powershell
EnvSecured.exe add-service --file C:\project\envsecured.envs --name backend --prefix BACKEND_ --folder backend
EnvSecured.exe add-service --file C:\project\envsecured.envs --name worker --strict-contracts
EnvSecured.exe edit-service --file C:\project\envsecured.envs --service backend --display "Backend API" --active true --shared-without-contract false
EnvSecured.exe edit-service --file C:\project\envsecured.envs --service backend --config-name api --toml-name backendApi --yaml-name backend-api --xml-name backend --json-name backend
EnvSecured.exe delete-service --file C:\project\envsecured.envs --service worker
EnvSecured.exe add-env --file C:\project\envsecured.envs --name dev
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

`use` controls export for the service. `--visible true|false` controls whether the variable is in that service scope. `--override true|false` controls whether that service may override the owner value. This means `unuse --visible true --override false` can keep a variable visible for interpolation without exporting it and without allowing local overrides.

When removing a variable from a service scope with `--visible false`, CLI checks whether that service still references `{{KEY}}`. If references exist, pass `--allow-broken-scope true` to confirm.

`compact-values` removes duplicate value records for the same variable/scope and keeps the last record, matching runtime resolution.

Generated variables are owned by EnvSecured. `--generator` accepts `password`, `token-hex`, `token-base62`, or `guid`. `--generator-scope owner-global` stores one canonical value on the owner service for all environments; `owner-env` stores one canonical value per environment. `--generator-mode manual` only changes values when `generate` or the UI context menu is used; `rotate-on-sync` is reserved for external sync providers and does not rotate on `get` or file export.

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

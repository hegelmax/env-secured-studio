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
EnvSecured.exe project --file C:\project\envsecured.envs --name MyProject --description "Shared config vault"
EnvSecured.exe info --file C:\project\envsecured.envs
EnvSecured.exe validate --file C:\project\envsecured.envs
EnvSecured.exe list --file C:\project\envsecured.envs --what variables
EnvSecured.exe list --file C:\project\envsecured.envs --what values
EnvSecured.exe list --file C:\project\envsecured.envs --what values --show-secrets
EnvSecured.exe list --file C:\project\envsecured.envs --what services
EnvSecured.exe list --file C:\project\envsecured.envs --what envs
```

`list --what values` masks secret variables by default. Use `--show-secrets` only when stdout is not captured into shared logs.

`save-as` copies the vault file as-is, including encrypted vaults. Add `--delete-source true` to move/rename the vault after the copy succeeds. Add `--overwrite true` only when the target file may already exist.

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
EnvSecured.exe edit-var --file C:\project\envsecured.envs --key DATABASE_HOST --new-key DATABASE_SERVER --display "Database server" --group Database --allow-null false --allow-blank false --active true
EnvSecured.exe delete-var --file C:\project\envsecured.envs --key OLD_VARIABLE
EnvSecured.exe set --file C:\project\envsecured.envs --key DATABASE_HOST --value 127.0.0.1 --service backend --env dev
EnvSecured.exe delete-value --file C:\project\envsecured.envs --key DATABASE_HOST --service backend --env dev
EnvSecured.exe use --file C:\project\envsecured.envs --key DATABASE_HOST --service backend
EnvSecured.exe unuse --file C:\project\envsecured.envs --key DATABASE_HOST --service backend
EnvSecured.exe auto-assign --file C:\project\envsecured.envs
EnvSecured.exe compact-values --file C:\project\envsecured.envs
```

Use `global` for global service or environment scope:

```powershell
EnvSecured.exe set --file C:\project\envsecured.envs --key PROJECT_NAME --value app --service global --env global
```

Use `--optional` to create a service contract where the variable is used but a value is not required:

```powershell
EnvSecured.exe use --file C:\project\envsecured.envs --key FEATURE_FLAG --service backend --optional
```

`compact-values` removes duplicate value records for the same variable/scope and keeps the last record, matching runtime resolution.

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
EnvSecured.exe settings --file C:\project\envsecured.envs --encryption secrets-only
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

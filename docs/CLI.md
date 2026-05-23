# CLI Reference

EnvSecured Studio has one executable. With no arguments it opens the WinForms UI. With a known command or `--help`, it runs in CLI mode.

```powershell
EnvSecured.exe --help
```

## Project Commands

```powershell
EnvSecured.exe new --file C:\project\envsecured.vault.json --name MyProject
EnvSecured.exe info --file C:\project\envsecured.vault.json
EnvSecured.exe validate --file C:\project\envsecured.vault.json
EnvSecured.exe list --file C:\project\envsecured.vault.json --what variables
EnvSecured.exe list --file C:\project\envsecured.vault.json --what values
EnvSecured.exe list --file C:\project\envsecured.vault.json --what services
EnvSecured.exe list --file C:\project\envsecured.vault.json --what envs
```

## Editing

```powershell
EnvSecured.exe add-service --file C:\project\envsecured.vault.json --name backend --prefix BACKEND_ --folder backend
EnvSecured.exe add-env --file C:\project\envsecured.vault.json --name dev
EnvSecured.exe add-var --file C:\project\envsecured.vault.json --key DATABASE_HOST --allow-blank
EnvSecured.exe add-var --file C:\project\envsecured.vault.json --key DATABASE_PASSWORD --secret
EnvSecured.exe set --file C:\project\envsecured.vault.json --key DATABASE_HOST --value 127.0.0.1 --service backend --env dev
EnvSecured.exe delete-value --file C:\project\envsecured.vault.json --key DATABASE_HOST --service backend --env dev
EnvSecured.exe use --file C:\project\envsecured.vault.json --key DATABASE_HOST --service backend
EnvSecured.exe unuse --file C:\project\envsecured.vault.json --key DATABASE_HOST --service backend
```

Use `global` for global service or environment scope:

```powershell
EnvSecured.exe set --file C:\project\envsecured.vault.json --key PROJECT_NAME --value app --service global --env global
```

## Import

```powershell
EnvSecured.exe import --file C:\project\envsecured.vault.json --input C:\input\.env.dev --service backend --env dev
```

Multiple input files are separated with semicolons:

```powershell
EnvSecured.exe import --file C:\project\envsecured.vault.json --input "C:\a.env;C:\b.env" --service backend --env dev
```

## Settings

```powershell
EnvSecured.exe settings --file C:\project\envsecured.vault.json --output-root C:\project\out --format CONFIG --ext .env
EnvSecured.exe settings --file C:\project\envsecured.vault.json --single-file true --single-file-mask "{project_name}{.ext}"
EnvSecured.exe settings --file C:\project\envsecured.vault.json --encryption secrets-only
EnvSecured.exe settings --file C:\project\envsecured.vault.json --cli-export-password true --password "secret"
```

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
EnvSecured.exe export --file C:\project\envsecured.vault.json --all
```

Override output settings for one run:

```powershell
EnvSecured.exe export --file C:\project\envsecured.vault.json --all --output-root C:\project\out --format JSON --single-file true --single-file-mask "{project_name}{.ext}"
```

Export a specific service/environment:

```powershell
EnvSecured.exe export --file C:\project\envsecured.vault.json --service backend --env dev --output-root C:\project\out
```

## Encrypted Projects

For encrypted projects, pass `--password` or set `ENVSECURED_PASSWORD`.

```powershell
$env:ENVSECURED_PASSWORD = "secret"
EnvSecured.exe export --file C:\project\envsecured.vault.json --all
```

If CLI export password protection is enabled, export commands require `--password` or `ENVSECURED_PASSWORD`.

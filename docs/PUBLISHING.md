# GitHub Publishing Checklist

## Before First Push

- Confirm no real vaults or exported config files are present.
- Confirm `artifacts`, `bin`, and `obj` are absent or ignored.
- Confirm `EnvSecured.exe` is not committed unless intentionally publishing a binary release artifact.
- Review README and docs for machine-specific paths.
- Decide on a license before making the repository public.

## Recommended Repository Contents

```text
.editorconfig
.gitattributes
.gitignore
EnvSecured.sln
README.md
docs/
src/
```

## Release Artifact

Build:

```powershell
dotnet build src\EnvSecured.WinForms\EnvSecured.WinForms.csproj -c Release
```

Upload:

```text
src\EnvSecured.WinForms\bin\Release\net48\EnvSecured.exe
```

## Sensitive Files

Do not publish:

```text
*.autosave.json
*.vault.local.json
real project vaults with secrets
generated .env/config outputs
crash logs
```

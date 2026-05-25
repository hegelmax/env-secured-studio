# GitHub Publishing Checklist

## Before First Push

- Confirm no real vaults or exported config files are present.
- Confirm `artifacts`, `obj`, and project build output folders are absent or ignored.
- Confirm `bin` contains only intentionally published versioned update binaries such as `EnvSecured_v1.1.0.exe`.
- Confirm unversioned `EnvSecured.exe` is not committed.
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
bin/
src/
```

## Release Artifact

Build:

```powershell
dotnet build src\EnvSecured.WinForms\EnvSecured.WinForms.csproj -c Release
```

Update binary for the in-app updater:

```text
bin\EnvSecured_vX.Y.Z.exe
```

The binary file name uses the public release version only (`X.Y.Z`). The assembly/file metadata can include the fourth build component (`X.Y.Z.B`).

## Sensitive Files

Do not publish:

```text
*.autosave.json
*.vault.local.json
real project vaults with secrets
generated .env/config outputs
crash logs
```

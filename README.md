# EnvSecured Studio

EnvSecured Studio is a Windows desktop and CLI tool for managing service/environment configuration in one encrypted project vault.

It is built for teams that need to keep variables, secrets, service contracts, imports, validation, and exported runtime config files in one place without introducing a server or database.

## Features

- WinForms UI for Windows.
- CLI in the same executable: UI opens by default, commands run when arguments are provided.
- Single-file Release build using Costura.
- Project vault stored as JSON.
- Encryption modes: open values, secrets only, all values, or whole JSON file.
- Per-project password support with optional DPAPI local key cache.
- Service and environment scoped values with inheritance.
- `${VARIABLE_NAME}` interpolation and validation.
- Service contract matrix for variable usage.
- Import from one or more text config files.
- Export to CONFIG, TOML, YAML, XML, or JSON.
- Multi-file export by service/environment or structured single-file export.

## Requirements

- Windows
- Visual Studio 2022 or Build Tools for Visual Studio 2022
- .NET Framework 4.8 Developer Pack / targeting pack
- .NET SDK capable of building SDK-style projects

## Build

```powershell
dotnet restore EnvSecured.sln
dotnet build src\EnvSecured.WinForms\EnvSecured.WinForms.csproj -c Release
```

The Release output is:

```text
src\EnvSecured.WinForms\bin\Release\net48\EnvSecured.exe
```

The Release target embeds project assemblies into the main executable. The intended distributable artifact is `EnvSecured.exe`.

## CLI

```powershell
EnvSecured.exe --help
EnvSecured.exe validate --file C:\path\project.vault.json
EnvSecured.exe export --file C:\path\project.vault.json --all --output-root C:\project\out
```

For encrypted projects, pass a password explicitly:

```powershell
EnvSecured.exe export --file C:\path\project.vault.json --all --password "secret"
```

or via environment variable:

```powershell
$env:ENVSECURED_PASSWORD = "secret"
EnvSecured.exe export --file C:\path\project.vault.json --all
```

More CLI details are in [docs/CLI.md](docs/CLI.md).

## Documentation

- [Build and release](docs/BUILD.md)
- [CLI reference](docs/CLI.md)
- [Project model](docs/PROJECT_MODEL.md)
- [Security model](docs/SECURITY.md)
- [Framework decision](docs/FRAMEWORK.md)
- [GitHub publishing checklist](docs/PUBLISHING.md)

## Repository Layout

```text
EnvSecured.sln
src/
  EnvSecured.Core/
  EnvSecured.Crypto/
  EnvSecured.WinForms/
docs/
```

## Notes

EnvSecured Studio does not require Electron, Node.js, WebView2, a local HTTP server, or a database.

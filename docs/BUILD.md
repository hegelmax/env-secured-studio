# Build and Release

## Prerequisites

- Windows.
- Visual Studio 2022 or Build Tools for Visual Studio 2022.
- .NET Framework 4.8 Developer Pack / targeting pack.
- .NET SDK with SDK-style project support.

## Debug Build

```powershell
dotnet build src\EnvSecured.WinForms\EnvSecured.WinForms.csproj -c Debug
```

## Release Build

```powershell
dotnet build src\EnvSecured.WinForms\EnvSecured.WinForms.csproj -c Release
```

Release output:

```text
src\EnvSecured.WinForms\bin\Release\net48\EnvSecured.exe
```

The WinForms project uses Costura.Fody to embed project assemblies into the main executable. The Release target also removes copied project-reference DLLs from the output folder.

## Tests

```powershell
dotnet test EnvSecured.sln
```

## Clean

```powershell
dotnet clean EnvSecured.sln
Remove-Item -Recurse -Force .\artifacts, .\src\*\bin, .\src\*\obj
```

## Publishing

For a GitHub release, build Release and upload only:

```text
EnvSecured.exe
```

Update `app.version.cs` together with the WinForms project version before publishing a release. The update checker reads this file from the `main` branch and downloads the matching versioned executable from `bin/EnvSecured_vX.Y.Z.exe`.

Do not publish local vaults, autosaves, test exports, or crash logs.

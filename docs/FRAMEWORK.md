# Framework Decision

EnvSecured Studio is implemented as a classic Windows desktop application:

- UI: WinForms
- Runtime target: .NET Framework 4.8
- Main executable: `EnvSecured.exe`
- No Electron, Node.js, WebView2, browser runtime, local HTTP server, or database

Project layout:

```text
src/EnvSecured.Core
src/EnvSecured.Crypto
src/EnvSecured.WinForms
```

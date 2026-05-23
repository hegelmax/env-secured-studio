# Changelog

## 1.0.4

- Reworked the About dialog into a product-style window with the application icon, version, GitHub link, copyright, description, executable path, host name, OS version, and .NET runtime.
- Fixed update downloads so the configured timeout is actually applied during binary download.
- Made update downloads write to a temporary `.tmp` file first and only move to the final `.exe` after a successful download.
- Updated the downloader to fetch the matching versioned executable from `bin/EnvSecured_vX.Y.Z.exe`.
- Documented update integrity expectations in the security model: downloads rely on HTTPS/GitHub trust and are not signature or checksum verified.
- Changed `--check-update` exit codes to `0` for no update, `10` for update available, and `2` for check failure.
- Documented `--check-update` exit codes in CLI help and CLI documentation.
- Improved CLI config parser tests so reflection failures report clear assertions instead of `NullReferenceException`.

## 1.0.3

- Added `.envs` as the default vault file extension.
- Added Windows file association support so `.envs` files can open with `EnvSecured.exe` on double-click.
- Added interactive association checks when the UI starts, including a prompt to replace stale associations that point to another executable location.
- Added CLI commands for file association and updates: `--register-association`, `--unregister-association`, `--check-update`, and `--download-update`.
- Added startup update checks in the UI and `app.version.cs` as the public version metadata file used by update checks.
- Fixed CLI config import parsing for single-character quote values such as `KEY="` and `KEY='`.
- Removed bare `{KEY}` interpolation; only `${KEY}` is interpolated to avoid conflicts with template-like literals and paths.
- Expanded unit test coverage for crypto tampering, encrypted envelope detection, effective values, validation branches, CLI config parsing, and literal brace handling.
- Updated CLI, build, project model, and README documentation for `.envs`, update checks, and interpolation syntax.

## 1.0.2

- Fixed encrypted vault detection so plaintext and value-encrypted vaults are not mistaken for whole-JSON encrypted envelopes.
- Fixed saving project storage mode changes from the Project page: `Save` now applies pending `Storage` edits before writing the vault.
- Added CLI `save-as --file <path> --to <path>` for copying vault files and `--delete-source true` for move/rename workflows.
- Added unit tests for crypto round-trip and tamper detection, effective value precedence and interpolation, validation diagnostics, and encrypted-envelope detection.
- Hardened key cleanup in the CLI and WinForms UI by clearing key byte arrays before dropping references.
- Cleared temporary password character buffers in CLI password input where managed .NET allows it.
- Removed unreachable `SECRET_ENCRYPTED_VALUES_NOT_COMPARABLE` validation warning from normal validation output.
- Optimized interpolation cycle detection by replacing linear stack lookups with indexed stack tracking.
- Centralized encrypted envelope detection for UI and CLI.
- Updated build and CLI documentation for test execution and the new `save-as` command.

## 1.0.1

- Improved output path handling: relative `OutputRootFolder` values are now resolved from the vault file location.
- Added an output folder picker flow with absolute/relative path preview and same-drive validation.
- Fixed startup and card layout issues in the WinForms UI.
- Updated effective value inheritance to match the intended config loading order, including values from other services before current-service overrides.
- Improved validation for interpolation contracts, shared service variables, required values, blank values, and intentionally shared secrets.
- Added `Allow shared secret` for variables and `Allow without explicit contract` for services.
- Improved Variables UI ergonomics: stable matrix refresh after edits, editing inherited values, context-menu copy actions, `${KEY}` copy, and direct value deletion.
- Expanded CLI coverage so vaults can be managed without the UI: edit/delete services, environments, and variables; update project metadata; manage export targets; and run auto-assign.
- Fixed recovery backup cleanup when closing without saving.

## 1.0.0

- Initial EnvSecured Studio release.
- WinForms UI for managing service and environment configuration vaults.
- JSON vault storage with open, secrets-only, all-values, and whole-JSON encryption modes.
- Variable, service, environment, value, and contract management.
- Scoped effective values with interpolation and validation.
- Import from text config files.
- Export to CONFIG, TOML, YAML, XML, and JSON.
- CLI support for project creation, validation, listing, editing values/contracts, import, settings, and export.

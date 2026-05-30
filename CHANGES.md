# Changelog

## 1.1.2.7

- Added CLI vault `split` for exporting selected variables into a new `.envs` file, including referenced `{{KEY}}` variables by default and allowing a new target password/encryption mode.
- Added CLI vault `merge` for combining several `.envs` files into one target vault while matching variables by key and replacing duplicate scoped values.
- Added WinForms Import / Export pages for splitting the current vault into a new `.envs` file and previewing vault merge differences before applying selected rows; merge includes a mapping wizard for environments, services, and variables.
- Improved merge mapping with explicit incoming-to-current dropdowns, automatic matching by name/display name, conflict highlighting for create-new rows, inline rename for new items, and position preservation while editing.
- Added dedicated Split and Merge toolbar/navigation icons.
- Updated application versioning to `1.1.2.7`; update binary names use the public `1.1.2` release version.

## 1.1.1.6

- Added CLI `get` for retrieving one calculated or raw effective value by key, service, and environment; plain output returns only the value, while `--format json` includes source metadata and the source value update timestamp.
- Added value update timestamps to the Variables UI: scoped rows show a compact age, while selected-variable details show the exact UTC source timestamp.
- Added double-click editing for variables in the main Variables grid.
- Removed variable `DisplayName` from the UI and CLI editing flow; variable `Key` is now the single editable name used for export, interpolation, and lookup.
- Improved Variable Card key validation so duplicate or blank keys are shown inline and block save with a visible message.
- Fixed Variable Card owner selection so the selected owner service matches the edited variable.
- Fixed Variable Card scope grid initialization for owner rows.
- Changed validation so non-shared secrets defined in the Global environment are errors, including service-owned global-environment values.
- Added generated secret metadata with `Manual` and `RotateOnSync` modes, `Password`/`TokenHex`/`TokenBase62`/`Guid` generator types, owner-global and owner-environment canonical scopes, single-value and bulk UI regeneration, and CLI `generate`.
- Updated application versioning to `1.1.1.6`; update binary names use the public `1.1.1` release version.

## 1.1.0.5

- Added optional service manifest export for files such as `.env.example`, containing variables published to each service.
- Added explicit export render modes for data files only, manifest files only, or both.
- Added configurable service manifest values: empty or demo lines (`KEY=value # comment`), and renamed variable demo fields to `DemoValue` / `DemoComment`.
- Added scoped service ownership for variables with per-service scope, override permission, and export settings.
- Removed standalone global variable ownership from the UI/model flow; project-wide variables should use a normal project-defined owner service.
- Replaced the Contracts screen with a Scope Matrix using explicit `NONE`, `Read`, `Override`, `Export`, and `Full` modes per variable/service pair.
- Replaced the short variable rename prompt with a full Variable Card for metadata, owner, flags, and service scope modes.
- Added owner and scope filters plus an explicit Owner column to the main Variables grid.
- Added an Export checkbox column to the main Variables grid when a concrete Scope service filter is selected.
- Simplified the Variables toolbar and moved `Light matrix colors` next to the lower service values matrix.
- Expanded the selected-variable details panel with owner, group, demo value, demo comment, and description.
- Updated import so imported variables receive an owner service and imported service values create matching scope, override, and export permissions.
- Changed service output folder handling so an empty output folder exports `{service}` masks into the output root segment, and service output folders must be unique.
- Added warnings when removing a variable from a service scope while that service still references it through interpolation.
- Added optional owner-value migration when changing a variable owner.
- Added interpolation reference updates when renaming variables from `{{OLD_KEY}}` to `{{NEW_KEY}}` in UI and CLI.
- Added a CLI guard that blocks encryption downgrades unless `--allow-security-downgrade true` is passed explicitly.
- Fixed opening vault files that contain legacy JavaScript `/Date(...)\/` timestamps in value metadata; new saves write ISO-8601 timestamps instead.
- Fixed the variable details matrix so disabling service scope no longer hides existing or inherited values for that service.
- Fixed variable matrix direct/inherited styling for values inherited from other services.
- Reduced Export target matrix flicker when using Select All or Select None.
- Polished the WinForms shell with embedded toolbar/navigation icons, Quick Stats, a vault protection indicator, and a dedicated Variables page header.
- Updated application versioning to `1.1.0.5`; update binary names still use the public `X.Y.Z` release version such as `EnvSecured_v1.1.0.exe`.

## 1.0.4.4

- Reworked the About dialog into a product-style window with the application icon, version, GitHub link, copyright, description, executable path, host name, OS version, and .NET runtime.
- Fixed update downloads so the configured timeout is actually applied during binary download.
- Made update downloads write to a temporary `.tmp` file first and only move to the final `.exe` after a successful download.
- Updated the downloader to fetch the matching versioned executable from `bin/EnvSecured_vX.Y.Z.exe`.
- Documented update integrity expectations in the security model: downloads rely on HTTPS/GitHub trust and are not signature or checksum verified.
- Changed `--check-update` exit codes to `0` for no update, `10` for update available, and `2` for check failure.
- Documented `--check-update` exit codes in CLI help and CLI documentation.
- Improved CLI config parser tests so reflection failures report clear assertions instead of `NullReferenceException`.

## 1.0.3.3

- Added `.envs` as the default vault file extension.
- Added Windows file association support so `.envs` files can open with `EnvSecured.exe` on double-click.
- Added interactive association checks when the UI starts, including a prompt to replace stale associations that point to another executable location.
- Added CLI commands for file association and updates: `--register-association`, `--unregister-association`, `--check-update`, and `--download-update`.
- Added startup update checks in the UI and `app.version.cs` as the public version metadata file used by update checks.
- Fixed CLI config import parsing for single-character quote values such as `KEY="` and `KEY='`.
- Removed bare `{KEY}` interpolation; only `{{KEY}}` is interpolated to avoid conflicts with template-like literals and paths.
- Expanded unit test coverage for crypto tampering, encrypted envelope detection, effective values, validation branches, CLI config parsing, and literal brace handling.
- Updated CLI, build, project model, and README documentation for `.envs`, update checks, and interpolation syntax.

## 1.0.2.2

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

## 1.0.1.1

- Improved output path handling: relative `OutputRootFolder` values are now resolved from the vault file location.
- Added an output folder picker flow with absolute/relative path preview and same-drive validation.
- Fixed startup and card layout issues in the WinForms UI.
- Updated effective value inheritance to match the intended config loading order, including values from other services before current-service overrides.
- Improved validation for interpolation contracts, shared service variables, required values, blank values, and intentionally shared secrets.
- Added `Allow shared secret` for variables and `Allow without explicit contract` for services.
- Improved Variables UI ergonomics: stable matrix refresh after edits, editing inherited values, context-menu copy actions, `{{KEY}}` copy, and direct value deletion.
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

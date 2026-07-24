# Project Model

EnvSecured Studio stores the project vault as JSON.

New saves use the nested `EnvSecured.Vault` version 2 format documented in [VAULT_FORMAT_V2.md](VAULT_FORMAT_V2.md). Legacy flat `Contracts` and root `Values` are converted into each variable's explicit owner flags, `Values`, and `ServiceScopes`.

## Main Concepts

- Project: top-level vault with project metadata, settings, services, environments, variables, values, service scopes, and crypto metadata.
- Service: application/service that consumes a scoped subset of variables.
- Environment: deployment environment such as `dev`, `test`, or `prod`.
- Variable: key metadata such as owner service, secret flag, generated value settings, allow-shared-secret, allow-null, and allow-blank.
- Value: scoped value for a variable.
- Service scope entry: whether a service can see, override, export, and externally synchronize a variable.

## Value Scope Priority

Effective values are resolved by priority:

1. Global value.
2. Environment value.
3. Other service value, ordered by service sort order and name.
4. Other service + environment value, ordered by service sort order and name.
5. Current service value.
6. Current service + environment value.

The most specific available value wins.

## Interpolation

Values can reference other variables with `{{KEY}}`:

```text
DATABASE_URL=Server={{DATABASE_HOST}};User={{DATABASE_USER}}
```

Bare `{KEY}` text is treated as a literal and is not interpolated.

When a variable key is renamed in the UI, EnvSecured can update exact interpolation tokens from `{{OLD_KEY}}` to `{{NEW_KEY}}`. CLI `edit-var --new-key` does the same by default unless `--update-refs false` is provided.

Validation checks that referenced variables exist, have effective values, and do not create cycles.

For service-owned variables, interpolation requires the referenced variable to be in the current service scope. Missing scope is reported by validation.

## Required Values

`Required` is stored explicitly on both the owner scope and every dependent service scope.

- A scoped variable with `Required = true` must have an effective value for each active service/environment pair.
- If the value is missing and `AllowNull` is false, validation emits `REQUIRED_VALUE_MISSING`.
- If the value is an empty string and `AllowBlank` is false, validation emits `REQUIRED_VALUE_BLANK`.
- A scoped variable with `Required = false` is optional.

## Duplicate Values

A vault should contain at most one value for each `(variable, scope, service, environment)` tuple. If duplicates exist, runtime resolution keeps the last record. Validation emits `DUPLICATE_SCOPED_VALUE`, and `compact-values` can remove stale duplicates while preserving the winning record.

## Shared Secrets

Secrets defined in the Global environment or reused across environments normally produce validation errors or warnings. Set `AllowSharedSecret` on the variable when that reuse is intentional.

## Generated Values

Generated variables store canonical values on their owner service. `OwnerGlobal` creates one value shared by all environments. `OwnerEnvironment` creates one value per environment. Manual generation is available from the UI and CLI; `RotateOnSync` is reserved for future external sync providers and does not rotate values during `get` or file export.

## Synchronization Providers

External provider definitions are stored under `Sync.Providers`. The first provider type is GitHub Actions, which requires the local GitHub CLI command `gh` to be available on the computer before it can be configured in the UI.

External providers include only service scopes with `Sync = true`. This is independent from file export: a variable may be synchronized without appearing in rendered files, or exported to files without being sent to an external provider.

In the nested v2 file, the flags directly on a variable are the owner-service scope. Top-level `Sync` therefore controls only `OwnerServiceId`; it is not a project-wide master flag. Every `ServiceScopes[].Sync` controls that exact service independently. A provider takes the union of qualifying rows across its selected services after applying `Visible` and `Sync`. If multiple selected services resolve the same flat GitHub key to different values or types, synchronization reports a conflict and applies nothing.

Provider connection settings are stored in `EncryptedSettings`, even when project values use open storage. Decrypted provider settings are runtime-only and are not serialized as plaintext.

## Export Targets

Export targets are stored as a service/environment matrix. This allows the UI and CLI to reuse the same export selection.

## Output Root

`OutputRootFolder` can be absolute or relative. Relative values are resolved from the vault file directory, not from the current process directory.

## Output Masks

Default masks:

```text
global:       apps\.env{.ext}
global+env:   apps\.env.{env}{.ext}
service:      apps\{service}\.env{.ext}
service+env:  apps\{service}\.env.{env}{.ext}
single file:  {project_name}{.ext}
manifest:     apps\{service}\.env.example
```

Supported placeholders:

```text
{project_name}
{service}
{env}
{.ext}
```

## Service Manifests

Export can render data files, service manifest files, or both. When `OutputServiceManifest` is enabled, export writes one manifest per rendered service. `OutputDataFiles` controls whether real data export files are rendered. The default mask is:

```text
apps\{service}\.env.example
```

Manifest files contain the active variables used by the service. Manifest values can be configured as empty values or demo values:

```text
DATABASE_HOST=
DATABASE_PASSWORD=
```

Demo manifests use `KEY={DemoValue} # {DemoComment}`.

Variables are included when export is enabled for that service scope.

## Service Scope

A variable is owned by exactly one service (`OwnerServiceId = service id`). Project-wide variables should be owned by a normal service chosen by the project, for example `project`. Other services are dependents and must be explicitly scoped in.

A service scope has independent flags:

- `VisibleToService = true` means the variable is in that service scope.
- `AllowOverride = true` means the scoped dependent service may override the owner value.
- `Excluded = false` means the variable is exported for that service.
- `Sync = true` means the variable is published by external synchronization providers for that service.

In the v2 file these runtime compatibility fields are serialized as the direct flags `Visible`, `AllowOverride`, `Export`, `Sync`, and `Required`; `Contracts` is not written. Older files without `Sync` inherit it from `Export` during migration.

The owner service is always in scope and can always override its own variable. Export and external synchronization are configured independently for each scoped service.

The UI Scope Matrix exposes these combinations:

- `NONE`: not in scope, no override, no export.
- `Read`: in scope, can be referenced, cannot be overridden or exported directly.
- `Override`: in scope and can be overridden, not exported directly.
- `Export`: in scope and exported directly, cannot be overridden.
- `Full`: in scope, can be overridden, and exported directly.

When ownership changes, EnvSecured can move existing owner values to the new owner without overwriting direct values already present in the target owner scope.

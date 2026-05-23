# Project Model

EnvSecured Studio stores the project vault as JSON.

## Main Concepts

- Project: top-level vault with project metadata, settings, services, environments, variables, values, contracts, and crypto metadata.
- Service: application/service that consumes a subset of variables. Services can allow or forbid using shared interpolated variables without an explicit contract.
- Environment: deployment environment such as `dev`, `test`, or `prod`.
- Variable: key metadata such as display name, secret flag, allow-shared-secret, allow-null, and allow-blank.
- Value: scoped value for a variable.
- Contract: whether a service uses a variable, and whether it is required for that service.

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

Values can reference other variables with `${KEY}`:

```text
DATABASE_URL=Server=${DATABASE_HOST};User=${DATABASE_USER}
```

Bare `{KEY}` text is treated as a literal and is not interpolated.

Validation checks that referenced variables exist, have effective values, and do not create cycles.

For variables explicitly used by a service, interpolation can also require contracts for referenced service-scoped variables. Missing interpolation contracts are warnings when the service allows shared variables without contracts, or errors when it does not. Global and environment-scoped referenced values do not require service contracts.

## Required Values

`Required` is stored on a service contract, not on the variable itself.

- A used variable with `Required = true` must have an effective value for each active service/environment pair.
- If the value is missing and `AllowNull` is false, validation emits `REQUIRED_VALUE_MISSING`.
- If the value is an empty string and `AllowBlank` is false, validation emits `REQUIRED_VALUE_BLANK`.
- A used variable with `Required = false` is optional.

## Duplicate Values

A vault should contain at most one value for each `(variable, scope, service, environment)` tuple. If duplicates exist, runtime resolution keeps the last record. Validation emits `DUPLICATE_SCOPED_VALUE`, and `compact-values` can remove stale duplicates while preserving the winning record.

## Shared Secrets

Secrets defined globally or reused across environments normally produce validation warnings. Set `AllowSharedSecret` on the variable when that reuse is intentional.

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
```

Supported placeholders:

```text
{project_name}
{service}
{env}
{.ext}
```

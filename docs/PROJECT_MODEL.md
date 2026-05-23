# Project Model

EnvSecured Studio stores the project vault as JSON.

## Main Concepts

- Project: top-level vault with project metadata, settings, services, environments, variables, values, contracts, and crypto metadata.
- Service: application/service that consumes a subset of variables.
- Environment: deployment environment such as `dev`, `test`, or `prod`.
- Variable: key metadata such as display name, secret flag, required flag, allow-null, and allow-blank.
- Value: scoped value for a variable.
- Contract: whether a service uses a variable, and whether it is required for that service.

## Value Scope Priority

Effective values are resolved by priority:

1. Global value.
2. Environment value.
3. Service value.
4. Service + environment value.

The most specific available value wins.

## Interpolation

Values can reference other variables:

```text
DATABASE_URL=Server=${DATABASE_HOST};User=${DATABASE_USER}
```

Validation checks that referenced variables exist, have effective values, and do not create cycles.

## Export Targets

Export targets are stored as a service/environment matrix. This allows the UI and CLI to reuse the same export selection.

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

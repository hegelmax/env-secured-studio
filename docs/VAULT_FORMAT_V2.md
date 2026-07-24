# Vault file format v2

Version 2 stores each variable together with its owner values and service scopes. There are no root `Contracts` or root `Values` arrays.

```json
{
  "Format": "EnvSecured.Vault",
  "Version": 2,
  "Project": {
    "Id": "cpa",
    "Name": "CPA",
    "Description": "Application vault"
  },
  "Security": {
    "EncryptionMode": "AllValues",
    "TokenAccessEnabled": true,
    "DefaultTokenMode": "ServiceAndEnvironment"
  },
  "Crypto": {
    "Kdf": "PBKDF2-HMAC-SHA256",
    "Iterations": 300000,
    "Salt": "<<BASE64-SALT>>",
    "KeyCheck": {
      "Alg": "AES-256-CBC-HMAC-SHA256",
      "AadVersion": 0,
      "Nonce": "<<BASE64-NONCE>>",
      "Ciphertext": "<<BASE64-CIPHERTEXT>>",
      "Tag": "<<BASE64-TAG>>",
      "KeySlots": []
    }
  },
  "Settings": {
    "GeneratedFileHeader": true,
    "Output": {
      "RootFolder": ".\\out",
      "Format": "CONFIG",
      "Extension": ".env",
      "GlobalMask": "apps\\.env{.ext}",
      "EnvironmentMask": "apps\\.env.{env}{.ext}",
      "ServiceMask": "apps\\{service}\\.env{.ext}",
      "ServiceEnvironmentMask": "apps\\{service}\\.env.{env}{.ext}",
      "StructuredSingleFile": false,
      "StructuredSingleFileMask": "{project_name}{.ext}",
      "DataFiles": true,
      "ServiceManifest": false,
      "ServiceManifestMask": "apps\\{service}\\.env.example",
      "ServiceManifestValueMode": "Empty",
      "Targets": []
    },
    "Cli": {
      "ExportPasswordRequiredPolicy": true,
      "ExportPasswordRequiredEncrypted": null
    }
  },
  "Environments": [
    {
      "Id": "dev",
      "Name": "Development",
      "ExportNames": {
        "CONFIG": "dev",
        "TOML": "dev",
        "YAML": "dev",
        "XML": "dev",
        "JSON": "dev"
      },
      "SortOrder": 10,
      "IsActive": true
    }
  ],
  "Services": [
    {
      "Id": "database",
      "Name": "Database",
      "Description": "Database service",
      "OutputFolder": "database",
      "DefaultPrefix": "DATABASE_",
      "ExportNames": {
        "CONFIG": "database",
        "TOML": "database",
        "YAML": "database",
        "XML": "database",
        "JSON": "database"
      },
      "SortOrder": 10,
      "IsActive": true
    },
    {
      "Id": "backend",
      "Name": "Backend",
      "Description": "Backend service",
      "OutputFolder": "backend",
      "DefaultPrefix": "BACKEND_",
      "ExportNames": {
        "CONFIG": "backend",
        "TOML": "backend",
        "YAML": "backend",
        "XML": "backend",
        "JSON": "backend"
      },
      "SortOrder": 20,
      "IsActive": true
    }
  ],
  "AccessTokens": [
    {
      "Id": "backend-dev",
      "Name": "Backend Dev",
      "Type": "ServiceEnvironment",
      "ServiceId": "backend",
      "EnvironmentId": "dev",
      "LookupId": "<<BASE64-TOKEN-LOOKUP-ID>>",
      "Version": 1,
      "Status": "Active",
      "Grants": [
        "backend.dev.*.Read|BatchExport"
      ],
      "AdminWrappedTokenKey": {
        "Alg": "AES-256-CBC-HMAC-SHA256-AAD",
        "AadVersion": 1,
        "Nonce": "<<BASE64-NONCE>>",
        "Ciphertext": "<<BASE64-CIPHERTEXT>>",
        "Tag": "<<BASE64-TAG>>",
        "KeySlots": []
      },
      "Verifier": "<<BASE64-HMAC>>",
      "CreatedAt": "2026-07-24T12:00:00.0000000Z",
      "RotatedAt": null,
      "ExpiresAt": null,
      "Integrity": {
        "Tag": "<<BASE64-HMAC>>"
      }
    }
  ],
  "Variables": [
    {
      "Id": "database-url",
      "Key": "DATABASE_URL",
      "Description": "Database connection URL",
      "Type": "String",
      "IsSecret": true,
      "AllowSharedSecret": false,
      "AllowNull": false,
      "AllowBlank": false,
      "IsActive": true,
      "Demo": {
        "Value": "postgres://user:password@localhost/db",
        "Comment": "Local database URL"
      },
      "GroupName": "Database",
      "OwnerServiceId": "database",
      "Visible": true,
      "AllowOverride": true,
      "Export": false,
      "Sync": false,
      "Required": true,
      "Generator": null,
      "SortOrder": 10,
      "Values": [
        {
          "Id": "database-url-dev",
          "Version": 1,
          "EnvironmentId": "dev",
          "Value": null,
          "EncryptedValue": {
            "Alg": "AES-256-CBC-HMAC-SHA256-AAD",
            "AadVersion": 1,
            "Nonce": "<<BASE64-NONCE>>",
            "Ciphertext": "<<BASE64-CIPHERTEXT>>",
            "Tag": "<<BASE64-TAG>>",
            "KeySlots": [
              {
                "PrincipalType": "Admin",
                "PrincipalId": "vault-admin",
                "KeyVersion": 1,
                "WrappedDek": {
                  "Alg": "AES-256-CBC-HMAC-SHA256-AAD",
                  "AadVersion": 1,
                  "Nonce": "<<BASE64-NONCE>>",
                  "Ciphertext": "<<BASE64-WRAPPED-DEK>>",
                  "Tag": "<<BASE64-TAG>>",
                  "KeySlots": []
                }
              },
              {
                "PrincipalType": "Token",
                "PrincipalId": "backend-dev",
                "KeyVersion": 1,
                "WrappedDek": {
                  "Alg": "AES-256-CBC-HMAC-SHA256-AAD",
                  "AadVersion": 1,
                  "Nonce": "<<BASE64-NONCE>>",
                  "Ciphertext": "<<BASE64-WRAPPED-DEK>>",
                  "Tag": "<<BASE64-TAG>>",
                  "KeySlots": []
                }
              }
            ]
          },
          "Notes": null,
          "UpdatedAt": "2026-07-24T12:00:00.0000000Z"
        }
      ],
      "ServiceScopes": [
        {
          "ServiceId": "backend",
          "Visible": true,
          "AllowOverride": true,
          "Export": true,
          "Sync": true,
          "Required": true,
          "Values": []
        }
      ],
      "Integrity": {
        "Tag": "<<BASE64-HMAC>>"
      }
    }
  ],
  "Sync": {
    "Providers": []
  },
  "VaultIntegrity": {
    "Tag": "<<BASE64-HMAC>>"
  }
}
```

The value is encrypted once with a random data-encryption key (DEK). `KeySlots` contains the same DEK wrapped separately for the admin key and every authorized token. Rotation replaces a token's wrapped slots without duplicating the value ciphertext.

Grant syntax is `<services>.<environments>.<variables>.<permissions>`. Alternatives use `|`, and `*` is a glob within a segment. Supported permissions are `Read` and `BatchExport`. `Read` permits an explicit UI/CLI value read. `BatchExport` additionally requires the effective variable scope to have `Export = true`; variables with `Export = false` are omitted from rendered files.

`Export` and `Sync` are independent. `Export` controls rendered files and CLI batch export. `Sync` controls publication to external integrations such as GitHub Actions. When reading an older v2 file without `Sync`, the effective migration value is inherited from `Export`; the next authenticated save writes `Sync` explicitly.

Typed tokens persist their scope explicitly. `Service` requires one `ServiceId` and every grant must use only that service. `Environment` requires one `EnvironmentId`; `ServiceEnvironment` requires both. Only `Custom` tokens may grant arbitrary combinations. Scope fields, type, and grants are authenticated and cannot be widened by editing the JSON.

New token secrets are a single opaque Base62 string using only `0-9A-Za-z`. The decoded binary container contains a format marker, random lookup ID, key version, 64-byte token key, and checksum. It does not contain the human-readable token ID. Legacy `envs_v2.<id>.<version>.<key>` secrets remain accepted; rotating one emits the Base62 format.

Opening and saving a legacy v1 file writes v2 and preserves the original once as `<vault>.v1.bak`.

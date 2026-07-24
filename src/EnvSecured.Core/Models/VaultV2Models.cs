using System.Collections.Generic;

namespace EnvSecured.Core.Models
{
    public sealed class VaultV2Document
    {
        public string Format { get; set; } = "EnvSecured.Vault";
        public int Version { get; set; } = 2;
        public VaultV2Project Project { get; set; } = new VaultV2Project();
        public VaultV2Security Security { get; set; } = new VaultV2Security();
        public VaultCryptoMetadata Crypto { get; set; } = new VaultCryptoMetadata();
        public VaultV2Settings Settings { get; set; } = new VaultV2Settings();
        public List<VaultV2Environment> Environments { get; set; } = new List<VaultV2Environment>();
        public List<VaultV2Service> Services { get; set; } = new List<VaultV2Service>();
        public List<AccessTokenModel> AccessTokens { get; set; } = new List<AccessTokenModel>();
        public List<VaultV2Variable> Variables { get; set; } = new List<VaultV2Variable>();
        public ProjectSyncSettings Sync { get; set; } = new ProjectSyncSettings();
        public IntegrityTagModel VaultIntegrity { get; set; } = new IntegrityTagModel();
    }

    public sealed class VaultV2Project
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
    }

    public sealed class VaultV2Security
    {
        public string EncryptionMode { get; set; } = "Open";
        public bool TokenAccessEnabled { get; set; }
        public string DefaultTokenMode { get; set; } = "ServiceEnvironment";
    }

    public sealed class VaultV2Settings
    {
        public bool GeneratedFileHeader { get; set; } = true;
        public VaultV2OutputSettings Output { get; set; } = new VaultV2OutputSettings();
        public VaultV2CliSettings Cli { get; set; } = new VaultV2CliSettings();
    }

    public sealed class VaultV2OutputSettings
    {
        public string RootFolder { get; set; }
        public string Format { get; set; } = "CONFIG";
        public string Extension { get; set; } = ".env";
        public string GlobalMask { get; set; } = @"apps\.env{.ext}";
        public string EnvironmentMask { get; set; } = @"apps\.env.{env}{.ext}";
        public string ServiceMask { get; set; } = @"apps\{service}\.env{.ext}";
        public string ServiceEnvironmentMask { get; set; } = @"apps\{service}\.env.{env}{.ext}";
        public bool StructuredSingleFile { get; set; }
        public string StructuredSingleFileMask { get; set; } = @"{project_name}{.ext}";
        public bool DataFiles { get; set; } = true;
        public bool ServiceManifest { get; set; }
        public string ServiceManifestMask { get; set; } = @"apps\{service}\.env.example";
        public string ServiceManifestValueMode { get; set; } = "Empty";
        public List<OutputTargetSetting> Targets { get; set; } = new List<OutputTargetSetting>();
    }

    public sealed class VaultV2CliSettings
    {
        public bool ExportPasswordRequiredPolicy { get; set; } = true;
        public EncryptedPayload ExportPasswordRequiredEncrypted { get; set; }
    }

    public sealed class VaultV2ExportNames
    {
        public string CONFIG { get; set; }
        public string TOML { get; set; }
        public string YAML { get; set; }
        public string XML { get; set; }
        public string JSON { get; set; }
    }

    public sealed class VaultV2Environment
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public VaultV2ExportNames ExportNames { get; set; } = new VaultV2ExportNames();
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public sealed class VaultV2Service
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string OutputFolder { get; set; }
        public string DefaultPrefix { get; set; }
        public VaultV2ExportNames ExportNames { get; set; } = new VaultV2ExportNames();
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public sealed class VaultV2Demo
    {
        public string Value { get; set; }
        public string Comment { get; set; }
    }

    public sealed class VaultV2Generator
    {
        public string Type { get; set; }
        public int Length { get; set; } = 32;
        public string Scope { get; set; }
        public string Mode { get; set; }
    }

    public sealed class VaultV2Variable
    {
        public string Id { get; set; }
        public string Key { get; set; }
        public string Description { get; set; }
        public string Type { get; set; } = "String";
        public bool IsSecret { get; set; }
        public bool AllowSharedSecret { get; set; }
        public bool AllowNull { get; set; }
        public bool AllowBlank { get; set; }
        public bool IsActive { get; set; } = true;
        public VaultV2Demo Demo { get; set; } = new VaultV2Demo();
        public string GroupName { get; set; }
        public string OwnerServiceId { get; set; }
        public bool Visible { get; set; } = true;
        public bool AllowOverride { get; set; } = true;
        public bool Export { get; set; }
        public bool? Sync { get; set; }
        public bool Required { get; set; }
        public VaultV2Generator Generator { get; set; }
        public int SortOrder { get; set; }
        public List<VaultV2Value> Values { get; set; } = new List<VaultV2Value>();
        public List<VaultV2ServiceScope> ServiceScopes { get; set; } = new List<VaultV2ServiceScope>();
        public IntegrityTagModel Integrity { get; set; } = new IntegrityTagModel();
    }

    public sealed class VaultV2ServiceScope
    {
        public string ServiceId { get; set; }
        public bool Visible { get; set; }
        public bool AllowOverride { get; set; }
        public bool Export { get; set; }
        public bool? Sync { get; set; }
        public bool Required { get; set; }
        public List<VaultV2Value> Values { get; set; } = new List<VaultV2Value>();
    }

    public sealed class VaultV2Value
    {
        public string Id { get; set; }
        public int Version { get; set; } = 1;
        public string EnvironmentId { get; set; }
        public string Value { get; set; }
        public EncryptedPayload EncryptedValue { get; set; }
        public string Notes { get; set; }
        public string UpdatedAt { get; set; }
    }
}

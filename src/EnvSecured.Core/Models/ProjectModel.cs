using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace EnvSecured.Core.Models
{
    public sealed class ProjectModel
    {
        public string Version { get; set; } = "1.0";
        public string ProjectId { get; set; }
        public string ProjectName { get; set; }
        public string Description { get; set; }
        public ProjectSettings Settings { get; set; } = new ProjectSettings();
        public VaultCryptoMetadata Crypto { get; set; } = new VaultCryptoMetadata();
        public List<EnvironmentModel> Environments { get; set; } = new List<EnvironmentModel>();
        public List<ServiceModel> Services { get; set; } = new List<ServiceModel>();
        public List<VariableDefinitionModel> Variables { get; set; } = new List<VariableDefinitionModel>();
        public List<VariableContractModel> Contracts { get; set; } = new List<VariableContractModel>();
        public List<VariableValueModel> Values { get; set; } = new List<VariableValueModel>();
        public List<AccessTokenModel> AccessTokens { get; set; } = new List<AccessTokenModel>();
        public IntegrityTagModel VaultIntegrity { get; set; } = new IntegrityTagModel();
        public ProjectSyncSettings Sync { get; set; } = new ProjectSyncSettings();
    }

    public sealed class ProjectSettings
    {
        public string EncryptionMode { get; set; } = "Open";
        public bool EncryptAllValues { get; set; }
        public bool GeneratedFileHeader { get; set; } = true;
        public string OutputRootFolder { get; set; }
        public string OutputFormat { get; set; } = "CONFIG";
        public string OutputExtension { get; set; } = ".env";
        public string OutputGlobalMask { get; set; } = @"apps\.env{.ext}";
        public string OutputEnvironmentMask { get; set; } = @"apps\.env.{env}{.ext}";
        public string OutputServiceMask { get; set; } = @"apps\{service}\.env{.ext}";
        public string OutputServiceEnvironmentMask { get; set; } = @"apps\{service}\.env.{env}{.ext}";
        public bool OutputStructuredSingleFile { get; set; }
        public string OutputStructuredSingleFileMask { get; set; } = @"{project_name}{.ext}";
        public bool OutputServiceManifest { get; set; }
        public bool OutputDataFiles { get; set; } = true;
        public string OutputServiceManifestMask { get; set; } = @"apps\{service}\.env.example";
        public string OutputServiceManifestValueMode { get; set; } = "Empty";
        public List<OutputTargetSetting> OutputTargets { get; set; } = new List<OutputTargetSetting>();
        public bool CliExportPasswordRequiredPolicy { get; set; } = true;
        public EncryptedPayload CliExportPasswordRequiredEncrypted { get; set; }
        [ScriptIgnore]
        public bool CliExportPasswordRequired { get; set; } = true;
    }

    public sealed class OutputTargetSetting
    {
        public string ServiceId { get; set; }
        public string EnvironmentId { get; set; }
        public bool Enabled { get; set; } = true;
    }

    public sealed class ProjectSyncSettings
    {
        public List<SyncProviderModel> Providers { get; set; } = new List<SyncProviderModel>();
    }

    public sealed class SyncProviderModel
    {
        public string Id { get; set; }
        public string Type { get; set; }
        public string Name { get; set; }
        public bool IsEnabled { get; set; } = true;
        public EncryptedPayload EncryptedSettings { get; set; }
        public string LastCheckedAt { get; set; }
        public string LastStatus { get; set; }

        [ScriptIgnore]
        public Dictionary<string, string> Settings { get; set; } = new Dictionary<string, string>();
    }

    public sealed class IntegrityTagModel
    {
        public string Tag { get; set; }
    }

    public sealed class AccessTokenModel
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Type { get; set; } = "Custom";
        public string ServiceId { get; set; }
        public string EnvironmentId { get; set; }
        public string LookupId { get; set; }
        public int Version { get; set; } = 1;
        public string Status { get; set; } = "Active";
        public List<string> Grants { get; set; } = new List<string>();
        public EncryptedPayload AdminWrappedTokenKey { get; set; }
        public string Verifier { get; set; }
        public string CreatedAt { get; set; }
        public string RotatedAt { get; set; }
        public string ExpiresAt { get; set; }
        public IntegrityTagModel Integrity { get; set; } = new IntegrityTagModel();
    }
}

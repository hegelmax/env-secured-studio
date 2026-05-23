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
}

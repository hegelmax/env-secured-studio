using System;
using System.Collections.Generic;
using System.Linq;
using EnvSecured.Core.Models;

namespace EnvSecured.Core.Services
{
    public sealed class VaultFormatConverter
    {
        public VaultV2Document ToV2(ProjectModel source)
        {
            ProjectService.EnsureProjectCollections(source);
            source.Settings = source.Settings ?? new ProjectSettings();

            var document = new VaultV2Document
            {
                Project = new VaultV2Project
                {
                    Id = source.ProjectId,
                    Name = source.ProjectName,
                    Description = source.Description
                },
                Security = new VaultV2Security
                {
                    EncryptionMode = source.Settings.EncryptionMode ?? "Open",
                    TokenAccessEnabled = source.AccessTokens.Count > 0,
                    DefaultTokenMode = "ServiceAndEnvironment"
                },
                Crypto = source.Crypto ?? new VaultCryptoMetadata(),
                Settings = ToV2(source.Settings),
                AccessTokens = source.AccessTokens,
                Sync = source.Sync ?? new ProjectSyncSettings(),
                VaultIntegrity = new IntegrityTagModel { Tag = source.VaultIntegrity?.Tag }
            };

            document.Environments = source.Environments.Select(ToV2).ToList();
            document.Services = source.Services.Select(ToV2).ToList();
            document.Variables = source.Variables
                .OrderBy(v => v.SortOrder)
                .ThenBy(v => v.Key)
                .Select(v => ToV2(source, v))
                .ToList();
            return document;
        }

        public ProjectModel FromV2(VaultV2Document source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (!string.Equals(source.Format, "EnvSecured.Vault", StringComparison.Ordinal) || source.Version != 2)
                throw new InvalidOperationException("Unsupported vault format or version.");

            var project = new ProjectModel
            {
                Version = "2.0",
                ProjectId = source.Project?.Id,
                ProjectName = source.Project?.Name,
                Description = source.Project?.Description,
                Settings = FromV2(source.Settings),
                Crypto = source.Crypto ?? new VaultCryptoMetadata(),
                AccessTokens = source.AccessTokens ?? new List<AccessTokenModel>(),
                Sync = source.Sync ?? new ProjectSyncSettings(),
                VaultIntegrity = source.VaultIntegrity ?? new IntegrityTagModel(),
                Environments = (source.Environments ?? new List<VaultV2Environment>()).Select(FromV2).ToList(),
                Services = (source.Services ?? new List<VaultV2Service>()).Select(FromV2).ToList()
            };
            project.Settings.EncryptionMode = source.Security?.EncryptionMode ?? project.Settings.EncryptionMode ?? "Open";
            project.Settings.EncryptAllValues = string.Equals(project.Settings.EncryptionMode, "AllValues", StringComparison.OrdinalIgnoreCase);

            foreach (var storedVariable in source.Variables ?? new List<VaultV2Variable>())
            {
                var variable = FromV2(storedVariable);
                project.Variables.Add(variable);

                project.Contracts.Add(ToRuntimeContract(variable.Id, variable.OwnerServiceId,
                    storedVariable.Visible, storedVariable.AllowOverride, storedVariable.Export,
                    storedVariable.Sync, storedVariable.Required,
                    project.Contracts.Count, false));

                foreach (var value in storedVariable.Values ?? new List<VaultV2Value>())
                {
                    project.Values.Add(ToRuntimeValue(variable, variable.OwnerServiceId, value, false));
                }

                foreach (var scope in storedVariable.ServiceScopes ?? new List<VaultV2ServiceScope>())
                {
                    project.Contracts.Add(ToRuntimeContract(variable.Id, scope.ServiceId,
                        scope.Visible, scope.AllowOverride, scope.Export, scope.Sync, scope.Required,
                        project.Contracts.Count, true));
                    foreach (var value in scope.Values ?? new List<VaultV2Value>())
                    {
                        project.Values.Add(ToRuntimeValue(variable, scope.ServiceId, value, true));
                    }
                }
            }

            ProjectService.EnsureProjectCollections(project);
            return project;
        }

        private static VaultV2Settings ToV2(ProjectSettings source)
        {
            source = source ?? new ProjectSettings();
            return new VaultV2Settings
            {
                GeneratedFileHeader = source.GeneratedFileHeader,
                Output = new VaultV2OutputSettings
                {
                    RootFolder = source.OutputRootFolder,
                    Format = source.OutputFormat,
                    Extension = source.OutputExtension,
                    GlobalMask = source.OutputGlobalMask,
                    EnvironmentMask = source.OutputEnvironmentMask,
                    ServiceMask = source.OutputServiceMask,
                    ServiceEnvironmentMask = source.OutputServiceEnvironmentMask,
                    StructuredSingleFile = source.OutputStructuredSingleFile,
                    StructuredSingleFileMask = source.OutputStructuredSingleFileMask,
                    DataFiles = source.OutputDataFiles,
                    ServiceManifest = source.OutputServiceManifest,
                    ServiceManifestMask = source.OutputServiceManifestMask,
                    ServiceManifestValueMode = source.OutputServiceManifestValueMode,
                    Targets = source.OutputTargets ?? new List<OutputTargetSetting>()
                },
                Cli = new VaultV2CliSettings
                {
                    ExportPasswordRequiredPolicy = source.CliExportPasswordRequiredPolicy,
                    ExportPasswordRequiredEncrypted = source.CliExportPasswordRequiredEncrypted
                }
            };
        }

        private static ProjectSettings FromV2(VaultV2Settings source)
        {
            source = source ?? new VaultV2Settings();
            var output = source.Output ?? new VaultV2OutputSettings();
            var cli = source.Cli ?? new VaultV2CliSettings();
            return new ProjectSettings
            {
                GeneratedFileHeader = source.GeneratedFileHeader,
                OutputRootFolder = output.RootFolder,
                OutputFormat = output.Format,
                OutputExtension = output.Extension,
                OutputGlobalMask = output.GlobalMask,
                OutputEnvironmentMask = output.EnvironmentMask,
                OutputServiceMask = output.ServiceMask,
                OutputServiceEnvironmentMask = output.ServiceEnvironmentMask,
                OutputStructuredSingleFile = output.StructuredSingleFile,
                OutputStructuredSingleFileMask = output.StructuredSingleFileMask,
                OutputDataFiles = output.DataFiles,
                OutputServiceManifest = output.ServiceManifest,
                OutputServiceManifestMask = output.ServiceManifestMask,
                OutputServiceManifestValueMode = output.ServiceManifestValueMode,
                OutputTargets = output.Targets ?? new List<OutputTargetSetting>(),
                CliExportPasswordRequiredPolicy = cli.ExportPasswordRequiredPolicy,
                CliExportPasswordRequiredEncrypted = cli.ExportPasswordRequiredEncrypted,
                CliExportPasswordRequired = cli.ExportPasswordRequiredPolicy
            };
        }

        private static VaultV2Variable ToV2(ProjectModel project, VariableDefinitionModel variable)
        {
            var ownerContract = project.Contracts.FirstOrDefault(c =>
                c.VariableId == variable.Id && string.Equals(c.ServiceId, variable.OwnerServiceId, StringComparison.Ordinal) &&
                !c.StoredAsServiceScope);
            var stored = new VaultV2Variable
            {
                Id = variable.Id,
                Key = variable.Key,
                Description = variable.Description,
                Type = variable.Type.ToString(),
                IsSecret = variable.IsSecret,
                AllowSharedSecret = variable.AllowSharedSecret,
                AllowNull = variable.AllowNull,
                AllowBlank = variable.AllowBlank,
                IsActive = variable.IsActive,
                Demo = new VaultV2Demo { Value = variable.DemoValue, Comment = variable.DemoComment },
                GroupName = variable.GroupName,
                OwnerServiceId = variable.OwnerServiceId,
                Visible = ownerContract?.VisibleToService ?? true,
                AllowOverride = ownerContract?.AllowOverride ?? true,
                Export = ownerContract != null && !ownerContract.Excluded,
                Sync = ownerContract?.Sync,
                Required = ownerContract?.Required == true,
                Generator = variable.IsGenerated ? new VaultV2Generator
                {
                    Type = variable.GeneratorType,
                    Length = variable.GeneratorLength,
                    Scope = variable.GeneratorScope,
                    Mode = variable.GeneratorMode
                } : null,
                SortOrder = variable.SortOrder,
                Integrity = new IntegrityTagModel { Tag = variable.Integrity?.Tag }
            };

            stored.Values = project.Values
                .Where(v => v.VariableId == variable.Id && IsOwnerValue(variable, v) && !v.StoredInServiceScope)
                .OrderBy(v => v.EnvironmentId)
                .Select(ToV2)
                .ToList();

            var serviceIds = project.Contracts
                .Where(c => c.VariableId == variable.Id && !string.IsNullOrWhiteSpace(c.ServiceId) &&
                    (c.ServiceId != variable.OwnerServiceId || c.StoredAsServiceScope))
                .Select(c => c.ServiceId)
                .Concat(project.Values
                    .Where(v => v.VariableId == variable.Id && !string.IsNullOrWhiteSpace(v.ServiceId) &&
                        (!IsOwnerValue(variable, v) || v.StoredInServiceScope))
                    .Select(v => v.ServiceId))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            foreach (var serviceId in serviceIds)
            {
                var contract = project.Contracts.FirstOrDefault(c => c.VariableId == variable.Id && c.ServiceId == serviceId &&
                    (serviceId != variable.OwnerServiceId || c.StoredAsServiceScope));
                stored.ServiceScopes.Add(new VaultV2ServiceScope
                {
                    ServiceId = serviceId,
                    Visible = contract?.VisibleToService == true,
                    AllowOverride = contract?.VisibleToService == true && contract.AllowOverride,
                    Export = contract != null && !contract.Excluded,
                    Sync = contract?.Sync,
                    Required = contract?.Required == true,
                    Values = project.Values
                        .Where(v => v.VariableId == variable.Id && v.ServiceId == serviceId &&
                            (!IsOwnerValue(variable, v) || v.StoredInServiceScope))
                        .OrderBy(v => v.EnvironmentId)
                        .Select(ToV2)
                        .ToList()
                });
            }
            return stored;
        }

        private static bool IsOwnerValue(VariableDefinitionModel variable, VariableValueModel value)
        {
            if (string.IsNullOrWhiteSpace(variable.OwnerServiceId))
                return value.Scope == ValueScope.Global || value.Scope == ValueScope.Environment;
            return string.Equals(value.ServiceId, variable.OwnerServiceId, StringComparison.Ordinal) &&
                (value.Scope == ValueScope.Service || value.Scope == ValueScope.ServiceEnvironment);
        }

        private static VaultV2Value ToV2(VariableValueModel value)
        {
            return new VaultV2Value
            {
                Id = value.Id,
                Version = value.Version <= 0 ? 1 : value.Version,
                EnvironmentId = value.EnvironmentId,
                Value = value.IsEncrypted ? null : value.Value,
                EncryptedValue = value.IsEncrypted ? value.EncryptedValue : null,
                Notes = value.Notes,
                UpdatedAt = value.UpdatedAt
            };
        }

        private static VariableDefinitionModel FromV2(VaultV2Variable value)
        {
            if (!Enum.TryParse(value.Type ?? "String", true, out VariableType type)) type = VariableType.String;
            return new VariableDefinitionModel
            {
                Id = value.Id,
                Key = value.Key,
                DisplayName = value.Key,
                Description = value.Description,
                Type = type,
                IsSecret = value.IsSecret,
                AllowSharedSecret = value.AllowSharedSecret,
                AllowNull = value.AllowNull,
                AllowBlank = value.AllowBlank,
                IsActive = value.IsActive,
                DemoValue = value.Demo?.Value,
                DemoComment = value.Demo?.Comment,
                GroupName = value.GroupName,
                OwnerServiceId = value.OwnerServiceId,
                IsGenerated = value.Generator != null,
                GeneratorType = value.Generator?.Type,
                GeneratorLength = value.Generator?.Length ?? 32,
                GeneratorScope = value.Generator?.Scope,
                GeneratorMode = value.Generator?.Mode,
                SortOrder = value.SortOrder,
                Integrity = value.Integrity ?? new IntegrityTagModel()
            };
        }

        private static VariableContractModel ToRuntimeContract(string variableId, string serviceId, bool visible, bool allowOverride, bool export, bool? sync, bool required, int index, bool storedAsServiceScope)
        {
            return new VariableContractModel
            {
                Id = ProjectService.NewId(),
                VariableId = variableId,
                ServiceId = serviceId,
                VisibleToService = visible,
                AllowOverride = visible && allowOverride,
                ShareWithOtherServices = visible,
                Excluded = !export,
                Sync = sync,
                Required = required,
                SortOrder = index * 10,
                StoredAsServiceScope = storedAsServiceScope
            };
        }

        private static VariableValueModel ToRuntimeValue(VariableDefinitionModel variable, string serviceId, VaultV2Value value, bool storedInServiceScope)
        {
            var owner = string.Equals(serviceId, variable.OwnerServiceId, StringComparison.Ordinal);
            var globalOwner = string.IsNullOrWhiteSpace(variable.OwnerServiceId) && string.IsNullOrWhiteSpace(serviceId);
            var hasEnvironment = !string.IsNullOrWhiteSpace(value.EnvironmentId);
            var scope = globalOwner
                ? (hasEnvironment ? ValueScope.Environment : ValueScope.Global)
                : (hasEnvironment ? ValueScope.ServiceEnvironment : ValueScope.Service);
            return new VariableValueModel
            {
                Id = value.Id,
                Version = value.Version <= 0 ? 1 : value.Version,
                VariableId = variable.Id,
                Scope = scope,
                ServiceId = globalOwner ? null : serviceId,
                EnvironmentId = value.EnvironmentId,
                IsEncrypted = value.EncryptedValue != null,
                Value = value.Value,
                EncryptedValue = value.EncryptedValue,
                Notes = value.Notes,
                UpdatedAt = value.UpdatedAt,
                StoredInServiceScope = storedInServiceScope
            };
        }

        private static VaultV2Environment ToV2(EnvironmentModel value) => new VaultV2Environment
        {
            Id = value.Id,
            Name = value.Name,
            ExportNames = new VaultV2ExportNames { CONFIG = value.ConfigName, TOML = value.TomlName, YAML = value.YamlName, XML = value.XmlName, JSON = value.JsonName },
            SortOrder = value.SortOrder,
            IsActive = value.IsActive
        };

        private static EnvironmentModel FromV2(VaultV2Environment value) => new EnvironmentModel
        {
            Id = value.Id,
            Name = value.Name,
            DisplayName = value.Name,
            ConfigName = value.ExportNames?.CONFIG,
            TomlName = value.ExportNames?.TOML,
            YamlName = value.ExportNames?.YAML,
            XmlName = value.ExportNames?.XML,
            JsonName = value.ExportNames?.JSON,
            SortOrder = value.SortOrder,
            IsActive = value.IsActive
        };

        private static VaultV2Service ToV2(ServiceModel value) => new VaultV2Service
        {
            Id = value.Id,
            Name = value.Name,
            Description = value.Description,
            OutputFolder = value.OutputFolder,
            DefaultPrefix = value.DefaultPrefix,
            ExportNames = new VaultV2ExportNames { CONFIG = value.ConfigName, TOML = value.TomlName, YAML = value.YamlName, XML = value.XmlName, JSON = value.JsonName },
            SortOrder = value.SortOrder,
            IsActive = value.IsActive
        };

        private static ServiceModel FromV2(VaultV2Service value) => new ServiceModel
        {
            Id = value.Id,
            Name = value.Name,
            DisplayName = value.Name,
            Description = value.Description,
            OutputFolder = value.OutputFolder,
            DefaultPrefix = value.DefaultPrefix,
            ConfigName = value.ExportNames?.CONFIG,
            TomlName = value.ExportNames?.TOML,
            YamlName = value.ExportNames?.YAML,
            XmlName = value.ExportNames?.XML,
            JsonName = value.ExportNames?.JSON,
            SortOrder = value.SortOrder,
            IsActive = value.IsActive,
            AllowSharedVariablesWithoutContract = false
        };
    }
}

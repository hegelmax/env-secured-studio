using System;

namespace EnvSecured.Core.Models
{
    public sealed class EnvironmentModel
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string ConfigName { get; set; }
        public string TomlName { get; set; }
        public string YamlName { get; set; }
        public string XmlName { get; set; }
        public string JsonName { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public sealed class ServiceModel
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public string OutputFolder { get; set; }
        public string DefaultPrefix { get; set; }
        public string ConfigName { get; set; }
        public string TomlName { get; set; }
        public string YamlName { get; set; }
        public string XmlName { get; set; }
        public string JsonName { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
        public bool AllowSharedVariablesWithoutContract { get; set; } = true;
    }

    public sealed class VariableDefinitionModel
    {
        public string Id { get; set; }
        public string Key { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public VariableType Type { get; set; }
        public bool IsSecret { get; set; }
        public bool AllowSharedSecret { get; set; }
        public bool AllowNull { get; set; }
        public bool AllowBlank { get; set; }
        public bool IsActive { get; set; } = true;
        public string DefaultExampleValue { get; set; }
        public string PlaceholderPattern { get; set; }
        public string GroupName { get; set; }
        public int SortOrder { get; set; }
    }

    public sealed class VariableContractModel
    {
        public string Id { get; set; }
        public string ServiceId { get; set; }
        public string VariableId { get; set; }
        public bool Excluded { get; set; }
        public bool Required { get; set; } = true;
        public string ExampleValue { get; set; }
        public string Notes { get; set; }
        public int SortOrder { get; set; }
    }

    public sealed class VariableValueModel
    {
        public string Id { get; set; }
        public string VariableId { get; set; }
        public ValueScope Scope { get; set; }
        public string EnvironmentId { get; set; }
        public string ServiceId { get; set; }
        public bool IsEncrypted { get; set; }
        public string Value { get; set; }
        public EncryptedPayload EncryptedValue { get; set; }
        public string Notes { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}

using System;
using System.Globalization;
using System.Web.Script.Serialization;

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
        public string DemoValue { get; set; }
        public string DemoComment { get; set; }
        public string GroupName { get; set; }
        public string OwnerServiceId { get; set; }
        public bool IsGenerated { get; set; }
        public string GeneratorType { get; set; }
        public int GeneratorLength { get; set; } = 32;
        public string GeneratorScope { get; set; }
        public string GeneratorMode { get; set; }
        public int SortOrder { get; set; }
    }

    public sealed class VariableContractModel
    {
        public string Id { get; set; }
        public string ServiceId { get; set; }
        public string VariableId { get; set; }
        public bool Excluded { get; set; }
        public bool Required { get; set; } = true;
        public bool ShareWithOtherServices { get; set; } = true;
        public bool VisibleToService { get; set; } = true;
        public bool AllowOverride { get; set; } = true;
        public string DemoValue { get; set; }
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
        public string UpdatedAt { get; set; } = FormatUpdatedAt(DateTime.UtcNow);

        [ScriptIgnore]
        public DateTime UpdatedAtUtc
        {
            get { return ParseUpdatedAt(UpdatedAt); }
            set { UpdatedAt = FormatUpdatedAt(value); }
        }

        private static string FormatUpdatedAt(DateTime value)
        {
            return value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        }

        private static DateTime ParseUpdatedAt(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return DateTime.MinValue;
            }

            if (value.StartsWith("/Date(", StringComparison.Ordinal) && value.EndsWith(")/", StringComparison.Ordinal))
            {
                var milliseconds = value.Substring(6, value.Length - 8);
                var offsetIndex = milliseconds.IndexOfAny(new[] { '+', '-' }, 1);
                if (offsetIndex > 0)
                {
                    milliseconds = milliseconds.Substring(0, offsetIndex);
                }

                if (long.TryParse(milliseconds, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixMilliseconds))
                {
                    return DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds).UtcDateTime;
                }
            }

            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                return parsed;
            }

            return DateTime.MinValue;
        }
    }
}

using System;
using System.Linq;
using System.Security.Cryptography;
using EnvSecured.Core.Models;

namespace EnvSecured.Core.Services
{
    public sealed class GeneratedValueService
    {
        public const string ModeManual = "Manual";
        public const string ModeRotateOnSync = "RotateOnSync";
        public const string ScopeOwnerGlobal = "OwnerGlobal";
        public const string ScopeOwnerEnvironment = "OwnerEnvironment";
        public const string TypePassword = "Password";
        public const string TypeTokenHex = "TokenHex";
        public const string TypeTokenBase62 = "TokenBase62";
        public const string TypeGuid = "Guid";

        private const string PasswordAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%^&*-_=+?";
        private const string Base62Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

        public VariableValueModel Generate(ProjectModel project, VariableDefinitionModel variable, string environmentId, bool overwrite)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            if (variable == null) throw new ArgumentNullException(nameof(variable));
            if (!variable.IsGenerated) throw new InvalidOperationException("Variable is not configured as generated.");

            var target = BuildCanonicalTarget(variable, environmentId);
            var existing = project.Values.LastOrDefault(v =>
                v.VariableId == variable.Id &&
                v.Scope == target.Scope &&
                v.ServiceId == target.ServiceId &&
                v.EnvironmentId == target.EnvironmentId);

            if (existing != null && !overwrite)
            {
                return existing;
            }

            var value = GenerateValue(variable);
            if (existing == null)
            {
                existing = new VariableValueModel
                {
                    Id = ProjectService.NewId(),
                    VariableId = variable.Id,
                    Scope = target.Scope,
                    ServiceId = target.ServiceId,
                    EnvironmentId = target.EnvironmentId
                };
                project.Values.Add(existing);
            }

            existing.Value = value;
            existing.IsEncrypted = variable.IsSecret;
            existing.EncryptedValue = null;
            existing.UpdatedAtUtc = DateTime.UtcNow;
            return existing;
        }

        public CanonicalGeneratedValueTarget BuildCanonicalTarget(VariableDefinitionModel variable, string environmentId)
        {
            var ownerServiceId = string.IsNullOrWhiteSpace(variable.OwnerServiceId) ? null : variable.OwnerServiceId;
            if (NormalizeScope(variable.GeneratorScope) == ScopeOwnerEnvironment)
            {
                if (string.IsNullOrWhiteSpace(environmentId))
                {
                    throw new InvalidOperationException("Environment is required for owner-environment generated values.");
                }

                return ownerServiceId == null
                    ? new CanonicalGeneratedValueTarget(ValueScope.Environment, null, environmentId)
                    : new CanonicalGeneratedValueTarget(ValueScope.ServiceEnvironment, ownerServiceId, environmentId);
            }

            return ownerServiceId == null
                ? new CanonicalGeneratedValueTarget(ValueScope.Global, null, null)
                : new CanonicalGeneratedValueTarget(ValueScope.Service, ownerServiceId, null);
        }

        public static string NormalizeMode(string value)
        {
            value = (value ?? ModeManual).Trim();
            return value.Equals("rotate-on-sync", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("rotateonsync", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("sync", StringComparison.OrdinalIgnoreCase)
                ? ModeRotateOnSync
                : ModeManual;
        }

        public static string NormalizeScope(string value)
        {
            value = (value ?? ScopeOwnerGlobal).Trim();
            return value.Equals("owner-env", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("ownerenvironment", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("owner-environment", StringComparison.OrdinalIgnoreCase)
                ? ScopeOwnerEnvironment
                : ScopeOwnerGlobal;
        }

        public static string NormalizeType(string value)
        {
            value = (value ?? TypeTokenBase62).Trim();
            if (value.Equals("password", StringComparison.OrdinalIgnoreCase)) return TypePassword;
            if (value.Equals("hex", StringComparison.OrdinalIgnoreCase) || value.Equals("token-hex", StringComparison.OrdinalIgnoreCase) || value.Equals("tokenhex", StringComparison.OrdinalIgnoreCase)) return TypeTokenHex;
            if (value.Equals("guid", StringComparison.OrdinalIgnoreCase)) return TypeGuid;
            return TypeTokenBase62;
        }

        public static int NormalizeLength(int length, string type)
        {
            if (NormalizeType(type) == TypeGuid) return 36;
            if (length < 8) return 8;
            if (length > 4096) return 4096;
            return length;
        }

        public string GenerateValue(VariableDefinitionModel variable)
        {
            var type = NormalizeType(variable.GeneratorType);
            var length = NormalizeLength(variable.GeneratorLength, type);
            if (type == TypeGuid) return Guid.NewGuid().ToString("D");
            if (type == TypeTokenHex) return GenerateHex(length);
            if (type == TypePassword) return GenerateFromAlphabet(PasswordAlphabet, length);
            return GenerateFromAlphabet(Base62Alphabet, length);
        }

        private static string GenerateHex(int length)
        {
            var byteCount = (length + 1) / 2;
            var bytes = new byte[byteCount];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            var chars = BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
            return chars.Length == length ? chars : chars.Substring(0, length);
        }

        private static string GenerateFromAlphabet(string alphabet, int length)
        {
            var result = new char[length];
            var bytes = new byte[length * 2];
            var index = 0;
            var max = byte.MaxValue - ((byte.MaxValue + 1) % alphabet.Length);
            using (var rng = RandomNumberGenerator.Create())
            {
                while (index < length)
                {
                    rng.GetBytes(bytes);
                    foreach (var b in bytes)
                    {
                        if (b >= max) continue;
                        result[index++] = alphabet[b % alphabet.Length];
                        if (index == length) break;
                    }
                }
            }

            return new string(result);
        }
    }

    public sealed class CanonicalGeneratedValueTarget
    {
        public CanonicalGeneratedValueTarget(ValueScope scope, string serviceId, string environmentId)
        {
            Scope = scope;
            ServiceId = serviceId;
            EnvironmentId = environmentId;
        }

        public ValueScope Scope { get; }
        public string ServiceId { get; }
        public string EnvironmentId { get; }
    }
}

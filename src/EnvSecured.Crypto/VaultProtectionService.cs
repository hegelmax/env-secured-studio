using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using EnvSecured.Core.Models;
using EnvSecured.Core.Services;

namespace EnvSecured.Crypto
{
    public sealed class AccessTokenCreationResult
    {
        public AccessTokenModel Token { get; set; }
        public string Secret { get; set; }
    }

    public sealed class VaultProtectionService
    {
        private const string AdminPrincipalId = "vault-admin";
        private readonly CryptoService crypto = new CryptoService();
        private readonly GrantService grants = new GrantService();
        private readonly TokenScopePolicyService tokenScopes = new TokenScopePolicyService();
        private readonly VaultFormatConverter converter = new VaultFormatConverter();
        private readonly CanonicalJsonService canonicalJson = new CanonicalJsonService();

        public AccessTokenCreationResult CreateToken(ProjectModel project, byte[] adminRootKey, string id, string name, string type, IEnumerable<string> grantRules, string expiresAt = null, string serviceId = null, string environmentId = null)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            if (adminRootKey == null) throw new ArgumentNullException(nameof(adminRootKey));
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Token id is required.", nameof(id));
            if (project.AccessTokens.Any(x => string.Equals(x.Id, id, StringComparison.Ordinal)))
                throw new InvalidOperationException("A token with this id already exists.");

            var normalizedGrants = (grantRules ?? Enumerable.Empty<string>()).Select(grants.Normalize).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
            if (normalizedGrants.Count == 0) throw new InvalidOperationException("At least one grant is required.");
            var tokenKey = crypto.GenerateRandomKey();
            try
            {
                var token = new AccessTokenModel
                {
                    Id = id,
                    Name = name ?? id,
                    Type = tokenScopes.NormalizeType(type),
                    ServiceId = serviceId,
                    EnvironmentId = environmentId,
                    LookupId = CreateLookupId(project),
                    Version = 1,
                    Status = "Active",
                    Grants = normalizedGrants,
                    CreatedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    ExpiresAt = expiresAt
                };
                tokenScopes.NormalizeMetadata(project, token);
                tokenScopes.Validate(project, token);
                token.AdminWrappedTokenKey = WrapTokenKey(project, token, tokenKey, adminRootKey);
                token.Verifier = Convert.ToBase64String(crypto.ComputeHmac(tokenKey, TokenVerifierText(project, token)));
                project.AccessTokens.Add(token);
                ReconcileKeySlots(project, adminRootKey);
                return new AccessTokenCreationResult { Token = token, Secret = FormatTokenSecret(token, tokenKey) };
            }
            finally
            {
                Clear(tokenKey);
            }
        }

        public string RotateToken(ProjectModel project, byte[] adminRootKey, AccessTokenModel token)
        {
            if (token == null) throw new ArgumentNullException(nameof(token));
            EnsureLookupId(project, token);
            tokenScopes.NormalizeMetadata(project, token);
            tokenScopes.Validate(project, token);
            token.Version = Math.Max(1, token.Version + 1);
            token.Status = "Active";
            token.RotatedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            var tokenKey = crypto.GenerateRandomKey();
            try
            {
                token.AdminWrappedTokenKey = WrapTokenKey(project, token, tokenKey, adminRootKey);
                token.Verifier = Convert.ToBase64String(crypto.ComputeHmac(tokenKey, TokenVerifierText(project, token)));
                ReconcileKeySlots(project, adminRootKey);
                return FormatTokenSecret(token, tokenKey);
            }
            finally
            {
                Clear(tokenKey);
            }
        }

        public string RevealToken(ProjectModel project, byte[] adminRootKey, AccessTokenModel token)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            if (adminRootKey == null) throw new ArgumentNullException(nameof(adminRootKey));
            if (token == null) throw new ArgumentNullException(nameof(token));
            if (!IsTokenActive(token))
                throw new InvalidOperationException("Only an active, non-expired token can be revealed.");
            if (token.AdminWrappedTokenKey == null)
                throw new CryptographicException("The token key is not available in the vault.");

            EnsureLookupId(project, token);
            tokenScopes.NormalizeMetadata(project, token);
            tokenScopes.Validate(project, token);
            var tokenKey = UnwrapTokenKey(project, token, adminRootKey);
            try
            {
                token.Verifier = Convert.ToBase64String(crypto.ComputeHmac(tokenKey, TokenVerifierText(project, token)));
                return FormatTokenSecret(token, tokenKey);
            }
            finally
            {
                Clear(tokenKey);
            }
        }

        public AccessTokenModel UnlockForToken(ProjectModel project, string rawToken, string serviceId, string environmentId, string permission)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            if (string.IsNullOrWhiteSpace(serviceId)) throw new InvalidOperationException("Token access requires a service.");
            if (string.IsNullOrWhiteSpace(environmentId)) throw new InvalidOperationException("Token access requires an environment.");
            var parsed = ParseTokenSecret(rawToken);
            var token = parsed.LookupId == null
                ? project.AccessTokens.FirstOrDefault(x => string.Equals(x.Id, parsed.Id, StringComparison.Ordinal))
                : project.AccessTokens.FirstOrDefault(x => string.Equals(x.LookupId, parsed.LookupId, StringComparison.Ordinal));
            if (token == null || token.Version != parsed.Version || !IsTokenActive(token))
            {
                Clear(parsed.Key);
                throw new UnauthorizedAccessException("Token is unknown, revoked, expired, or has been rotated.");
            }

            try
            {
                tokenScopes.Validate(project, token);
                if (!tokenScopes.AllowsTarget(token, serviceId, environmentId))
                    throw new UnauthorizedAccessException("Token type does not allow the requested service/environment target.");
                var verifier = Convert.ToBase64String(crypto.ComputeHmac(parsed.Key, TokenVerifierText(project, token)));
                if (!FixedTimeEqualsBase64(verifier, token.Verifier))
                    throw new UnauthorizedAccessException("Token authentication failed.");
                var grantMatched = false;
                foreach (var variable in project.Variables.Where(v => v.IsActive))
                {
                    if (!grants.Allows(token.Grants, serviceId, environmentId, variable.Key, permission)) continue;
                    grantMatched = true;
                    if (!ProjectService.IsVariableVisibleToService(project, variable, serviceId)) continue;
                    if (string.Equals(permission, "BatchExport", StringComparison.Ordinal) &&
                        !ProjectService.IsVariableUsedByService(project, variable.Id, serviceId)) continue;

                    var effective = EffectiveConfigService.BuildRawValue(project, variable, serviceId, environmentId);
                    if (effective.Missing) continue;
                    var selected = project.Values.LastOrDefault(v =>
                        v.VariableId == variable.Id && v.Scope == effective.SourceScope &&
                        v.ServiceId == effective.SourceServiceId && v.EnvironmentId == effective.SourceEnvironmentId);
                    if (selected == null || !selected.IsEncrypted || selected.EncryptedValue == null) continue;
                    var slot = selected.EncryptedValue.KeySlots?.FirstOrDefault(x =>
                        x.PrincipalType == "Token" && x.PrincipalId == token.Id && x.KeyVersion == token.Version);
                    if (slot?.WrappedDek == null)
                        throw new UnauthorizedAccessException("Token has no cryptographic key slot for " + variable.Key + ".");

                    selected.Value = DecryptValueWithSlot(project, variable, selected, slot, parsed.Key);
                }
                if (!grantMatched) throw new UnauthorizedAccessException("Token has no matching grant for this target.");
                return token;
            }
            finally
            {
                Clear(parsed.Key);
            }
        }

        public void EncryptValuesForStorage(ProjectModel project, byte[] adminRootKey, Func<VariableDefinitionModel, bool> shouldEncrypt)
        {
            foreach (var value in project.Values)
            {
                var variable = project.Variables.FirstOrDefault(v => v.Id == value.VariableId);
                if (variable == null || !shouldEncrypt(variable))
                {
                    value.EncryptedValue = null;
                    value.IsEncrypted = false;
                    continue;
                }

                var dek = crypto.GenerateRandomKey();
                try
                {
                    var aad = ValueAad(project, variable, value);
                    var payload = crypto.EncryptStringWithAad(value.Value ?? string.Empty, dek, aad);
                    var adminWrapKey = AdminWrapKey(adminRootKey);
                    try
                    {
                        payload.KeySlots.Add(CreateSlot(project, variable, value, "Admin", AdminPrincipalId, 1, dek, adminWrapKey));
                    }
                    finally
                    {
                        Clear(adminWrapKey);
                    }
                    AddTokenSlots(project, variable, value, dek, adminRootKey, payload.KeySlots);
                    value.EncryptedValue = payload;
                    value.Value = null;
                    value.IsEncrypted = true;
                }
                finally
                {
                    Clear(dek);
                }
            }
        }

        public void DecryptValuesForAdmin(ProjectModel project, byte[] adminRootKey)
        {
            foreach (var value in project.Values.Where(v => v.IsEncrypted && v.EncryptedValue != null))
            {
                var variable = project.Variables.FirstOrDefault(v => v.Id == value.VariableId);
                if (variable == null) throw new CryptographicException("Encrypted value references an unknown variable.");
                if (value.EncryptedValue.KeySlots == null || value.EncryptedValue.KeySlots.Count == 0)
                {
                    value.Value = crypto.DecryptString(value.EncryptedValue, adminRootKey);
                    value.EncryptedValue = null;
                    continue;
                }

                var slot = value.EncryptedValue.KeySlots.FirstOrDefault(x => x.PrincipalType == "Admin" && x.PrincipalId == AdminPrincipalId);
                if (slot?.WrappedDek == null) throw new CryptographicException("Encrypted value has no administrator key slot.");
                var adminWrapKey = AdminWrapKey(adminRootKey);
                try
                {
                    value.Value = DecryptValueWithSlot(project, variable, value, slot, adminWrapKey);
                    value.EncryptedValue = null;
                }
                finally
                {
                    Clear(adminWrapKey);
                }
            }

            foreach (var value in project.Values) value.StoredInServiceScope = false;
            foreach (var contract in project.Contracts) contract.StoredAsServiceScope = false;
        }

        public void ReconcileKeySlots(ProjectModel project, byte[] adminRootKey)
        {
            foreach (var value in project.Values.Where(v => v.IsEncrypted && v.EncryptedValue?.KeySlots?.Count > 0))
            {
                var variable = project.Variables.FirstOrDefault(v => v.Id == value.VariableId);
                if (variable == null) continue;
                var adminSlot = value.EncryptedValue.KeySlots.FirstOrDefault(x => x.PrincipalType == "Admin" && x.PrincipalId == AdminPrincipalId);
                if (adminSlot == null) throw new CryptographicException("Encrypted value has no administrator key slot.");
                var adminWrapKey = AdminWrapKey(adminRootKey);
                byte[] dek;
                try
                {
                    dek = UnwrapDek(project, variable, value, adminSlot, adminWrapKey);
                }
                finally
                {
                    Clear(adminWrapKey);
                }
                try
                {
                    value.EncryptedValue.KeySlots.RemoveAll(x => x.PrincipalType == "Token");
                    AddTokenSlots(project, variable, value, dek, adminRootKey, value.EncryptedValue.KeySlots);
                }
                finally
                {
                    Clear(dek);
                }
            }
        }

        public void ProtectIntegrity(ProjectModel project, byte[] adminRootKey)
        {
            project.Version = "2.0";
            foreach (var contract in project.Contracts)
            {
                if (!contract.Sync.HasValue) contract.Sync = !contract.Excluded;
            }
            var variableKey = crypto.DeriveSubkey(adminRootKey, "EnvSecured.VariableIntegrity.v2", 32);
            var tokenKey = crypto.DeriveSubkey(adminRootKey, "EnvSecured.TokenPolicyIntegrity.v2", 32);
            var vaultKey = crypto.DeriveSubkey(adminRootKey, "EnvSecured.VaultIntegrity.v2", 32);
            try
            {
                foreach (var token in project.AccessTokens.OrderBy(x => x.Id, StringComparer.Ordinal))
                {
                    EnsureLookupId(project, token);
                    tokenScopes.NormalizeMetadata(project, token);
                    tokenScopes.Validate(project, token);
                    var tokenKeyMaterial = UnwrapTokenKey(project, token, adminRootKey);
                    try
                    {
                        token.Verifier = Convert.ToBase64String(crypto.ComputeHmac(tokenKeyMaterial, TokenVerifierText(project, token)));
                    }
                    finally
                    {
                        Clear(tokenKeyMaterial);
                    }
                    token.Integrity = token.Integrity ?? new IntegrityTagModel();
                    token.Integrity.Tag = null;
                    token.Integrity.Tag = Convert.ToBase64String(crypto.ComputeHmac(tokenKey,
                        "EnvSecured.TokenPolicy.v2\0" + project.ProjectId + "\0" + canonicalJson.Canonicalize(TokenPolicyMaterial(token))));
                }

                var document = converter.ToV2(project);
                foreach (var variable in document.Variables)
                {
                    variable.Integrity = variable.Integrity ?? new IntegrityTagModel();
                    variable.Integrity.Tag = null;
                    var tag = Convert.ToBase64String(crypto.ComputeHmac(variableKey,
                        "EnvSecured.Variable.v2\0" + project.ProjectId + "\0" + canonicalJson.Canonicalize(variable)));
                    variable.Integrity.Tag = tag;
                    var runtime = project.Variables.First(v => v.Id == variable.Id);
                    runtime.Integrity = new IntegrityTagModel { Tag = tag };
                }

                document = converter.ToV2(project);
                project.VaultIntegrity = new IntegrityTagModel
                {
                    Tag = Convert.ToBase64String(crypto.ComputeHmac(vaultKey,
                        GetVaultIntegrityMaterial(project)))
                };
            }
            finally
            {
                Clear(variableKey);
                Clear(tokenKey);
                Clear(vaultKey);
            }
        }

        public void VerifyIntegrity(ProjectModel project, byte[] adminRootKey)
        {
            if (!string.Equals(project.Version, "2.0", StringComparison.Ordinal)) return;
            VerifyIntegrity(converter.ToV2(project), adminRootKey);
        }

        public void VerifyIntegrity(VaultV2Document document, byte[] adminRootKey)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (!string.Equals(document.Format, "EnvSecured.Vault", StringComparison.Ordinal) || document.Version != 2)
                return;

            var projectId = document.Project?.Id;
            var expectedVaultTag = document.VaultIntegrity?.Tag;
            if (string.IsNullOrWhiteSpace(expectedVaultTag)) throw new CryptographicException("Vault integrity tag is missing.");

            var variableKey = crypto.DeriveSubkey(adminRootKey, "EnvSecured.VariableIntegrity.v2", 32);
            var tokenKey = crypto.DeriveSubkey(adminRootKey, "EnvSecured.TokenPolicyIntegrity.v2", 32);
            var vaultKey = crypto.DeriveSubkey(adminRootKey, "EnvSecured.VaultIntegrity.v2", 32);
            try
            {
                foreach (var variable in document.Variables ?? new List<VaultV2Variable>())
                {
                    var expected = variable.Integrity?.Tag;
                    variable.Integrity = variable.Integrity ?? new IntegrityTagModel();
                    variable.Integrity.Tag = null;
                    try
                    {
                        var text = "EnvSecured.Variable.v2\0" + projectId + "\0" + canonicalJson.Canonicalize(variable);
                        var valid = crypto.VerifyHmac(variableKey, text, expected);
                        if (!valid && VariableHasNullSync(variable))
                        {
                            valid = crypto.VerifyHmac(variableKey,
                                "EnvSecured.Variable.v2\0" + projectId + "\0" + CanonicalVariableWithoutNullSync(variable), expected);
                        }
                        if (!valid)
                            throw new CryptographicException("Variable integrity verification failed: " + variable.Key);
                    }
                    finally
                    {
                        variable.Integrity.Tag = expected;
                    }
                }

                foreach (var token in document.AccessTokens ?? new List<AccessTokenModel>())
                {
                    var expected = token.Integrity?.Tag;
                    token.Integrity = token.Integrity ?? new IntegrityTagModel();
                    token.Integrity.Tag = null;
                    try
                    {
                        var text = "EnvSecured.TokenPolicy.v2\0" + projectId + "\0" + canonicalJson.Canonicalize(TokenPolicyMaterial(token));
                        if (!crypto.VerifyHmac(tokenKey, text, expected))
                            throw new CryptographicException("Token policy integrity verification failed: " + token.Id);
                    }
                    finally
                    {
                        token.Integrity.Tag = expected;
                    }
                }

                var vaultText = GetVaultIntegrityMaterial(document);
                var vaultValid = crypto.VerifyHmac(vaultKey, vaultText, expectedVaultTag);
                if (!vaultValid)
                {
                    vaultValid = crypto.VerifyHmac(vaultKey, GetVaultIntegrityMaterial(document, true), expectedVaultTag);
                }
                if (!vaultValid)
                    throw new CryptographicException("Vault integrity verification failed.");
            }
            finally
            {
                Clear(variableKey);
                Clear(tokenKey);
                Clear(vaultKey);
            }
        }

        public string GetVaultIntegrityMaterial(ProjectModel project)
        {
            return GetVaultIntegrityMaterial(converter.ToV2(project));
        }

        public string GetVaultIntegrityMaterial(VaultV2Document document)
        {
            return GetVaultIntegrityMaterial(document, false);
        }

        private string GetVaultIntegrityMaterial(VaultV2Document document, bool removeNullSync)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var material = serializer.DeserializeObject(serializer.Serialize(document));
            if (material is IDictionary<string, object> root &&
                root.TryGetValue("VaultIntegrity", out var integrity) &&
                integrity is IDictionary<string, object> integrityObject)
            {
                integrityObject["Tag"] = null;
            }
            RemoveNullVaultTokenScopeFields(material);
            if (removeNullSync) RemoveNullVariableSyncFields(material);
            return "EnvSecured.Vault.v2\0" + canonicalJson.Canonicalize(material);
        }

        private string CanonicalVariableWithoutNullSync(VaultV2Variable variable)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var material = serializer.DeserializeObject(serializer.Serialize(variable));
            RemoveNullVariableSyncFieldsFromVariable(material);
            return canonicalJson.Canonicalize(material);
        }

        private static bool VariableHasNullSync(VaultV2Variable variable)
        {
            return variable.Sync == null || (variable.ServiceScopes ?? new List<VaultV2ServiceScope>()).Any(scope => scope.Sync == null);
        }

        private void AddTokenSlots(ProjectModel project, VariableDefinitionModel variable, VariableValueModel value, byte[] dek, byte[] adminRootKey, List<WrappedKeySlot> slots)
        {
            foreach (var token in project.AccessTokens.Where(IsTokenActive))
            {
                if (!TokenNeedsValue(project, token, variable, value)) continue;
                var tokenKey = UnwrapTokenKey(project, token, adminRootKey);
                try
                {
                    slots.Add(CreateSlot(project, variable, value, "Token", token.Id, token.Version, dek, tokenKey));
                }
                finally
                {
                    Clear(tokenKey);
                }
            }
        }

        private bool TokenNeedsValue(ProjectModel project, AccessTokenModel token, VariableDefinitionModel variable, VariableValueModel value)
        {
            foreach (var service in project.Services.Where(s => s.IsActive))
            foreach (var environment in project.Environments.Where(e => e.IsActive))
            {
                var read = grants.Allows(token.Grants, service.Id, environment.Id, variable.Key, "Read");
                var batch = grants.Allows(token.Grants, service.Id, environment.Id, variable.Key, "BatchExport") &&
                    ProjectService.IsVariableUsedByService(project, variable.Id, service.Id);
                if (!read && !batch) continue;
                if (!ProjectService.IsVariableVisibleToService(project, variable, service.Id)) continue;
                var effective = EffectiveConfigService.BuildRawValue(project, variable, service.Id, environment.Id);
                if (effective.Missing) continue;
                var selected = project.Values.LastOrDefault(v =>
                    v.VariableId == variable.Id && v.Scope == effective.SourceScope &&
                    v.ServiceId == effective.SourceServiceId && v.EnvironmentId == effective.SourceEnvironmentId);
                if (ReferenceEquals(selected, value)) return true;
            }
            return false;
        }

        private WrappedKeySlot CreateSlot(ProjectModel project, VariableDefinitionModel variable, VariableValueModel value, string type, string id, int version, byte[] dek, byte[] wrappingKey)
        {
            var slot = new WrappedKeySlot { PrincipalType = type, PrincipalId = id, KeyVersion = version };
            slot.WrappedDek = crypto.EncryptStringWithAad(Convert.ToBase64String(dek), wrappingKey, SlotAad(project, variable, value, slot));
            return slot;
        }

        private byte[] UnwrapDek(ProjectModel project, VariableDefinitionModel variable, VariableValueModel value, WrappedKeySlot slot, byte[] wrappingKey)
        {
            return Convert.FromBase64String(crypto.DecryptStringWithAad(slot.WrappedDek, wrappingKey, SlotAad(project, variable, value, slot)));
        }

        private string DecryptValueWithSlot(ProjectModel project, VariableDefinitionModel variable, VariableValueModel value, WrappedKeySlot slot, byte[] wrappingKey)
        {
            CryptographicException lastError = null;
            foreach (var candidate in ValueAadCandidates(value))
            {
                byte[] dek = null;
                try
                {
                    dek = UnwrapDek(project, variable, candidate, slot, wrappingKey);
                    return crypto.DecryptStringWithAad(value.EncryptedValue, dek, ValueAad(project, variable, candidate));
                }
                catch (CryptographicException ex)
                {
                    lastError = ex;
                }
                finally
                {
                    Clear(dek);
                }
            }
            throw lastError ?? new CryptographicException("Encrypted payload authentication failed.");
        }

        private static IEnumerable<VariableValueModel> ValueAadCandidates(VariableValueModel value)
        {
            yield return value;

            // Early v2 migration kept inconsistent legacy Global/Environment values in a
            // service scope. The stored document signature authenticates that structure,
            // but the runtime model necessarily maps it to Service/ServiceEnvironment.
            if (value.Scope != ValueScope.Service && value.Scope != ValueScope.ServiceEnvironment) yield break;
            var firstLegacyScope = value.Scope == ValueScope.Service ? ValueScope.Global : ValueScope.Environment;
            yield return LegacyAadValue(value, firstLegacyScope);
            yield return LegacyAadValue(value, firstLegacyScope == ValueScope.Global ? ValueScope.Environment : ValueScope.Global);
        }

        private static VariableValueModel LegacyAadValue(VariableValueModel value, ValueScope scope)
        {
            return new VariableValueModel
            {
                Id = value.Id,
                Version = value.Version,
                VariableId = value.VariableId,
                Scope = scope,
                ServiceId = value.ServiceId,
                EnvironmentId = value.EnvironmentId
            };
        }

        private EncryptedPayload WrapTokenKey(ProjectModel project, AccessTokenModel token, byte[] tokenKey, byte[] adminRootKey)
        {
            var registryKey = crypto.DeriveSubkey(adminRootKey, "EnvSecured.TokenRegistry.v2");
            try
            {
                return crypto.EncryptStringWithAad(Convert.ToBase64String(tokenKey), registryKey, TokenAad(project, token));
            }
            finally
            {
                Clear(registryKey);
            }
        }

        private byte[] UnwrapTokenKey(ProjectModel project, AccessTokenModel token, byte[] adminRootKey)
        {
            var registryKey = crypto.DeriveSubkey(adminRootKey, "EnvSecured.TokenRegistry.v2");
            try
            {
                return Convert.FromBase64String(crypto.DecryptStringWithAad(token.AdminWrappedTokenKey, registryKey, TokenAad(project, token)));
            }
            finally
            {
                Clear(registryKey);
            }
        }

        private byte[] AdminWrapKey(byte[] adminRootKey) => crypto.DeriveSubkey(adminRootKey, "EnvSecured.AdminDataWrap.v2");

        private static string ValueAad(ProjectModel project, VariableDefinitionModel variable, VariableValueModel value)
        {
            return string.Join("|", "EnvSecured.Value.v2", project.ProjectId, variable.Id, value.Id, value.Version,
                value.Scope, value.ServiceId ?? string.Empty, value.EnvironmentId ?? string.Empty);
        }

        private static string SlotAad(ProjectModel project, VariableDefinitionModel variable, VariableValueModel value, WrappedKeySlot slot)
        {
            return ValueAad(project, variable, value) + "|" + slot.PrincipalType + "|" + slot.PrincipalId + "|" + slot.KeyVersion;
        }

        private static string TokenAad(ProjectModel project, AccessTokenModel token) =>
            string.Join("|", "EnvSecured.TokenRegistry.v2", project.ProjectId, token.Id, token.Version);

        private string TokenVerifierText(ProjectModel project, AccessTokenModel token)
        {
            var hasExplicitSync = project.Contracts.Any(contract => contract.Sync.HasValue);
            var variables = converter.ToV2(project).Variables
                .OrderBy(v => v.Id, StringComparer.Ordinal)
                .Select(v => new
                {
                    v.Id,
                    v.Key,
                    v.OwnerServiceId,
                    v.IsActive,
                    v.Visible,
                    v.AllowOverride,
                    v.Export,
                    Sync = hasExplicitSync ? v.Sync : null,
                    v.Required,
                    ServiceScopes = v.ServiceScopes.OrderBy(s => s.ServiceId, StringComparer.Ordinal).Select(s => new
                    {
                        s.ServiceId,
                        s.Visible,
                        s.AllowOverride,
                        s.Export,
                        Sync = hasExplicitSync ? s.Sync : null,
                        s.Required
                    }).ToList()
                }).ToList();
            object variablesValue = variables;
            if (!hasExplicitSync)
            {
                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                variablesValue = serializer.DeserializeObject(serializer.Serialize(variables));
                RemoveNullVariableSyncFieldsFromTokenVariables(variablesValue);
            }
            var grantsValue = (token.Grants ?? new List<string>()).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var servicesValue = project.Services.OrderBy(s => s.Id, StringComparer.Ordinal).Select(s => new { s.Id, s.IsActive }).ToList();
            var environmentsValue = project.Environments.OrderBy(e => e.Id, StringComparer.Ordinal).Select(e => new { e.Id, e.IsActive }).ToList();
            if (string.IsNullOrWhiteSpace(token.ServiceId) && string.IsNullOrWhiteSpace(token.EnvironmentId) && string.IsNullOrWhiteSpace(token.LookupId))
            {
                var legacyPolicy = new
                {
                    ProjectId = project.ProjectId,
                    token.Id,
                    token.Version,
                    token.Type,
                    token.Status,
                    token.ExpiresAt,
                    Grants = grantsValue,
                    Services = servicesValue,
                    Environments = environmentsValue,
                    Variables = variablesValue
                };
                return "EnvSecured.TokenVerifier.v2\0" + canonicalJson.Canonicalize(legacyPolicy);
            }
            var policy = new
            {
                ProjectId = project.ProjectId,
                token.Id,
                token.Version,
                token.Type,
                token.ServiceId,
                token.EnvironmentId,
                token.LookupId,
                token.Status,
                token.ExpiresAt,
                Grants = grantsValue,
                Services = servicesValue,
                Environments = environmentsValue,
                Variables = variablesValue
            };
            return "EnvSecured.TokenVerifier.v2\0" + canonicalJson.Canonicalize(policy);
        }

        private static object TokenPolicyMaterial(AccessTokenModel token)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var material = serializer.DeserializeObject(serializer.Serialize(token));
            RemoveNullTokenScopeFields(material);
            return material;
        }

        private static void RemoveNullTokenScopeFields(object value)
        {
            if (value is IDictionary<string, object> dictionary)
            {
                if (dictionary.ContainsKey("ServiceId") && dictionary["ServiceId"] == null) dictionary.Remove("ServiceId");
                if (dictionary.ContainsKey("EnvironmentId") && dictionary["EnvironmentId"] == null) dictionary.Remove("EnvironmentId");
                if (dictionary.ContainsKey("LookupId") && dictionary["LookupId"] == null) dictionary.Remove("LookupId");
            }
        }

        private static void RemoveNullVaultTokenScopeFields(object value)
        {
            if (!(value is IDictionary<string, object> root) || !root.TryGetValue("AccessTokens", out var tokens) ||
                !(tokens is System.Collections.IEnumerable sequence)) return;
            foreach (var token in sequence) RemoveNullTokenScopeFields(token);
        }

        private static void RemoveNullVariableSyncFields(object value)
        {
            if (!(value is IDictionary<string, object> root) || !root.TryGetValue("Variables", out var variables)) return;
            if (!(variables is System.Collections.IEnumerable sequence)) return;
            foreach (var variable in sequence) RemoveNullVariableSyncFieldsFromVariable(variable);
        }

        private static void RemoveNullVariableSyncFieldsFromTokenVariables(object value)
        {
            if (!(value is System.Collections.IEnumerable sequence)) return;
            foreach (var variable in sequence) RemoveNullVariableSyncFieldsFromVariable(variable);
        }

        private static void RemoveNullVariableSyncFieldsFromVariable(object value)
        {
            if (!(value is IDictionary<string, object> variable)) return;
            if (variable.ContainsKey("Sync") && variable["Sync"] == null) variable.Remove("Sync");
            if (!variable.TryGetValue("ServiceScopes", out var scopes) || !(scopes is System.Collections.IEnumerable sequence)) return;
            foreach (var item in sequence)
            {
                if (item is IDictionary<string, object> scope && scope.ContainsKey("Sync") && scope["Sync"] == null)
                    scope.Remove("Sync");
            }
        }

        private static bool IsTokenActive(AccessTokenModel token)
        {
            if (!string.Equals(token.Status, "Active", StringComparison.Ordinal)) return false;
            if (string.IsNullOrWhiteSpace(token.ExpiresAt)) return true;
            return !DateTime.TryParse(token.ExpiresAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var expires) || expires > DateTime.UtcNow;
        }

        private string CreateLookupId(ProjectModel project)
        {
            while (true)
            {
                var bytes = crypto.GenerateRandomKey(16);
                try
                {
                    var value = Convert.ToBase64String(bytes);
                    if (!project.AccessTokens.Any(x => string.Equals(x.LookupId, value, StringComparison.Ordinal))) return value;
                }
                finally
                {
                    Clear(bytes);
                }
            }
        }

        private void EnsureLookupId(ProjectModel project, AccessTokenModel token)
        {
            if (string.IsNullOrWhiteSpace(token.LookupId)) token.LookupId = CreateLookupId(project);
        }

        private static string FormatTokenSecret(AccessTokenModel token, byte[] tokenKey)
        {
            if (tokenKey == null || tokenKey.Length != 64) throw new InvalidOperationException("Token key length is invalid.");
            byte[] lookup;
            try { lookup = Convert.FromBase64String(token.LookupId ?? string.Empty); }
            catch (FormatException) { throw new InvalidOperationException("Token lookup id is invalid."); }
            try
            {
                if (lookup.Length != 16) throw new InvalidOperationException("Token lookup id length is invalid.");
                var payload = new byte[92];
                try
                {
                    payload[0] = (byte)'E';
                    payload[1] = (byte)'V';
                    payload[2] = (byte)'S';
                    payload[3] = 2;
                    Buffer.BlockCopy(lookup, 0, payload, 4, lookup.Length);
                    payload[20] = (byte)(token.Version >> 24);
                    payload[21] = (byte)(token.Version >> 16);
                    payload[22] = (byte)(token.Version >> 8);
                    payload[23] = (byte)token.Version;
                    Buffer.BlockCopy(tokenKey, 0, payload, 24, tokenKey.Length);
                    using (var sha = SHA256.Create())
                    {
                        var checksum = sha.ComputeHash(payload, 0, 88);
                        Buffer.BlockCopy(checksum, 0, payload, 88, 4);
                        Clear(checksum);
                    }
                    return Base62Encode(payload);
                }
                finally
                {
                    Clear(payload);
                }
            }
            finally
            {
                Clear(lookup);
            }
        }

        private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static ParsedTokenSecret ParseTokenSecret(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new UnauthorizedAccessException("Token is required.");
            var text = value.Trim();
            if (text.StartsWith("envs_v2.", StringComparison.Ordinal))
            {
                var parts = text.Split('.');
                if (parts.Length != 4 || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var legacyVersion))
                    throw new UnauthorizedAccessException("Token format is invalid.");
                return new ParsedTokenSecret { Id = parts[1], Version = legacyVersion, Key = FromBase64Url(parts[3]) };
            }

            var payload = Base62Decode(text);
            try
            {
                if (payload.Length != 92 || payload[0] != (byte)'E' || payload[1] != (byte)'V' || payload[2] != (byte)'S' || payload[3] != 2)
                    throw new UnauthorizedAccessException("Token format is invalid.");
                using (var sha = SHA256.Create())
                {
                    var checksum = sha.ComputeHash(payload, 0, 88);
                    var valid = true;
                    for (var index = 0; index < 4; index++) valid &= checksum[index] == payload[88 + index];
                    Clear(checksum);
                    if (!valid) throw new UnauthorizedAccessException("Token checksum is invalid.");
                }
                var lookup = new byte[16];
                var key = new byte[64];
                Buffer.BlockCopy(payload, 4, lookup, 0, lookup.Length);
                Buffer.BlockCopy(payload, 24, key, 0, key.Length);
                var version = (payload[20] << 24) | (payload[21] << 16) | (payload[22] << 8) | payload[23];
                if (version <= 0) { Clear(lookup); Clear(key); throw new UnauthorizedAccessException("Token version is invalid."); }
                var lookupId = Convert.ToBase64String(lookup);
                Clear(lookup);
                return new ParsedTokenSecret { LookupId = lookupId, Version = version, Key = key };
            }
            finally
            {
                Clear(payload);
            }
        }

        private const string Base62Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

        private static string Base62Encode(byte[] value)
        {
            var digits = new List<int> { 0 };
            foreach (var item in value)
            {
                var carry = (int)item;
                for (var index = 0; index < digits.Count; index++)
                {
                    var current = digits[index] * 256 + carry;
                    digits[index] = current % 62;
                    carry = current / 62;
                }
                while (carry > 0) { digits.Add(carry % 62); carry /= 62; }
            }
            var output = new StringBuilder(digits.Count);
            for (var index = digits.Count - 1; index >= 0; index--) output.Append(Base62Alphabet[digits[index]]);
            return output.ToString();
        }

        private static byte[] Base62Decode(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 256)
                throw new UnauthorizedAccessException("Token format is invalid.");
            var bytes = new List<int> { 0 };
            foreach (var character in value)
            {
                var digit = Base62Alphabet.IndexOf(character);
                if (digit < 0) throw new UnauthorizedAccessException("Token contains invalid characters.");
                var carry = digit;
                for (var index = 0; index < bytes.Count; index++)
                {
                    var current = bytes[index] * 62 + carry;
                    bytes[index] = current & 255;
                    carry = current >> 8;
                }
                while (carry > 0) { bytes.Add(carry & 255); carry >>= 8; }
            }
            var output = new byte[bytes.Count];
            for (var index = 0; index < bytes.Count; index++) output[index] = (byte)bytes[bytes.Count - 1 - index];
            return output;
        }

        private static byte[] FromBase64Url(string value)
        {
            var padded = value.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2: padded += "=="; break;
                case 3: padded += "="; break;
                case 1: throw new UnauthorizedAccessException("Token key encoding is invalid.");
            }
            try { return Convert.FromBase64String(padded); }
            catch (FormatException) { throw new UnauthorizedAccessException("Token key encoding is invalid."); }
        }

        private static bool FixedTimeEqualsBase64(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
            byte[] a;
            byte[] b;
            try { a = Convert.FromBase64String(left); b = Convert.FromBase64String(right); }
            catch (FormatException) { return false; }
            if (a.Length != b.Length) return false;
            var diff = 0;
            for (var i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        private sealed class ParsedTokenSecret
        {
            public string Id { get; set; }
            public string LookupId { get; set; }
            public int Version { get; set; }
            public byte[] Key { get; set; }
        }

        private static void Clear(byte[] value)
        {
            if (value != null) Array.Clear(value, 0, value.Length);
        }
    }
}

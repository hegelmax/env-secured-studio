using System;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using EnvSecured.Core.Models;
using EnvSecured.Core.Services;
using EnvSecured.Crypto;
using Xunit;

namespace EnvSecured.Tests
{
    public sealed class VaultProtectionServiceTests
    {
        [Fact]
        public void IntegrityMaterialIsStableAcrossV2RoundTrip()
        {
            var project = new ProjectModel { ProjectId = "p", ProjectName = "P" };
            project.Settings.EncryptionMode = "AllValues";
            project.Services.Add(new ServiceModel { Id = "database", Name = "database" });
            project.Services.Add(new ServiceModel { Id = "backend", Name = "backend" });
            project.Environments.Add(new EnvironmentModel { Id = "dev", Name = "dev" });
            var variable = new VariableDefinitionModel { Id = "url", Key = "DATABASE_URL", OwnerServiceId = "database" };
            project.Variables.Add(variable);
            project.Contracts.Add(new VariableContractModel { VariableId = variable.Id, ServiceId = "database", VisibleToService = true, AllowOverride = true, Excluded = false, Required = true });
            project.Contracts.Add(new VariableContractModel { VariableId = variable.Id, ServiceId = "backend", VisibleToService = true, AllowOverride = false, Excluded = false, Required = true });
            project.Values.Add(new VariableValueModel { Id = "value", VariableId = variable.Id, Scope = ValueScope.ServiceEnvironment, ServiceId = "database", EnvironmentId = "dev", Value = "secret" });

            var crypto = new CryptoService();
            var key = crypto.DeriveKey("password", Enumerable.Range(1, 32).Select(x => (byte)x).ToArray(), 1000);
            var protection = new VaultProtectionService();
            protection.CreateToken(project, key, "backend-dev", "Backend Dev", "ServiceEnvironment", new[] { "backend.dev.*.Read" });
            protection.EncryptValuesForStorage(project, key, _ => true);
            protection.ProtectIntegrity(project, key);
            var before = protection.GetVaultIntegrityMaterial(project);

            var files = new VaultFileService();
            var loaded = files.Deserialize(files.Serialize(project));
            var after = protection.GetVaultIntegrityMaterial(loaded);

            Assert.Equal(before, after);
            protection.VerifyIntegrity(loaded, key);
        }

        [Fact]
        public void TokenDecryptsOnlyGrantedEffectiveValue()
        {
            var project = CreateProject();
            var crypto = new CryptoService();
            var key = crypto.DeriveKey("password", Enumerable.Range(1, 32).Select(x => (byte)x).ToArray(), 1000);
            var protection = new VaultProtectionService();
            var created = protection.CreateToken(project, key, "backend-dev", "Backend Dev", "ServiceEnvironment", new[] { "backend.dev.DATABASE_URL.Read" });
            Assert.Matches("^[0-9A-Za-z]+$", created.Secret);
            Assert.DoesNotContain("backend-dev", created.Secret);
            Assert.DoesNotContain(".", created.Secret);
            Assert.False(string.IsNullOrWhiteSpace(created.Token.LookupId));
            protection.EncryptValuesForStorage(project, key, _ => true);
            protection.ProtectIntegrity(project, key);

            var files = new VaultFileService();
            var loaded = files.Deserialize(files.Serialize(project));
            protection.UnlockForToken(loaded, created.Secret, "backend", "dev", "Read");
            var variable = loaded.Variables.Single(v => v.Key == "DATABASE_URL");
            var effective = EffectiveConfigService.BuildRawValue(loaded, variable, "backend", "dev");

            Assert.Equal("secret", effective.Value);
            Assert.Throws<UnauthorizedAccessException>(() => protection.UnlockForToken(loaded, created.Secret, "backend", "prod", "Read"));
        }

        [Fact]
        public void AdministratorCanRevealCurrentActiveTokenButNotRevokedToken()
        {
            var project = CreateProject();
            var crypto = new CryptoService();
            var key = crypto.DeriveKey("password", Enumerable.Range(1, 32).Select(x => (byte)x).ToArray(), 1000);
            var protection = new VaultProtectionService();
            var created = protection.CreateToken(project, key, "backend-dev", "Backend Dev", "ServiceEnvironment",
                new[] { "backend.dev.DATABASE_URL.Read" }, null, "backend", "dev");

            Assert.Equal(created.Secret, protection.RevealToken(project, key, created.Token));
            created.Token.Status = "Revoked";
            Assert.Throws<InvalidOperationException>(() => protection.RevealToken(project, key, created.Token));
        }

        [Fact]
        public void LegacyTokenTextRemainsReadableAndBase62ChecksumRejectsChanges()
        {
            var project = CreateProject();
            var crypto = new CryptoService();
            var key = crypto.DeriveKey("password", Enumerable.Range(1, 32).Select(x => (byte)x).ToArray(), 1000);
            var protection = new VaultProtectionService();
            var created = protection.CreateToken(project, key, "backend-dev", "Backend Dev", "ServiceEnvironment",
                new[] { "backend.dev.DATABASE_URL.Read" });
            protection.EncryptValuesForStorage(project, key, _ => true);
            protection.ProtectIntegrity(project, key);
            var loaded = new VaultFileService().Deserialize(new VaultFileService().Serialize(project));

            var parse = typeof(VaultProtectionService).GetMethod("ParseTokenSecret", BindingFlags.Static | BindingFlags.NonPublic);
            var parsed = parse.Invoke(null, new object[] { created.Secret });
            var rawKey = (byte[])parsed.GetType().GetProperty("Key").GetValue(parsed);
            var legacyKey = Convert.ToBase64String(rawKey).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var legacy = "envs_v2.backend-dev.1." + legacyKey;
            protection.UnlockForToken(loaded, legacy, "backend", "dev", "Read");
            Array.Clear(rawKey, 0, rawKey.Length);

            var replacement = created.Secret[created.Secret.Length - 1] == '0' ? '1' : '0';
            var changed = created.Secret.Substring(0, created.Secret.Length - 1) + replacement;
            Assert.Throws<UnauthorizedAccessException>(() => protection.UnlockForToken(loaded, changed, "backend", "dev", "Read"));
        }

        [Fact]
        public void IntegrityRejectsManualScopeChange()
        {
            var project = CreateProject();
            var crypto = new CryptoService();
            var key = crypto.DeriveKey("password", Enumerable.Range(1, 32).Select(x => (byte)x).ToArray(), 1000);
            var protection = new VaultProtectionService();
            protection.EncryptValuesForStorage(project, key, _ => true);
            protection.ProtectIntegrity(project, key);
            project.Contracts.Single(c => c.ServiceId == "backend").Excluded = true;

            Assert.Throws<CryptographicException>(() => protection.VerifyIntegrity(project, key));
        }

        [Fact]
        public void IntegrityRejectsManualSyncChange()
        {
            var project = CreateProject();
            var crypto = new CryptoService();
            var key = crypto.DeriveKey("password", Enumerable.Range(1, 32).Select(x => (byte)x).ToArray(), 1000);
            var protection = new VaultProtectionService();
            protection.EncryptValuesForStorage(project, key, _ => true);
            protection.ProtectIntegrity(project, key);
            project.Contracts.Single(c => c.ServiceId == "backend").Sync = false;

            Assert.Throws<CryptographicException>(() => protection.VerifyIntegrity(project, key));
        }

        [Fact]
        public void StoredOwnerServiceScopeIsPreservedAcrossRuntimeConversion()
        {
            var project = CreateProject();
            var crypto = new CryptoService();
            var key = crypto.DeriveKey("password", Enumerable.Range(1, 32).Select(x => (byte)x).ToArray(), 1000);
            var protection = new VaultProtectionService();
            protection.EncryptValuesForStorage(project, key, _ => true);
            protection.ProtectIntegrity(project, key);

            var converter = new VaultFormatConverter();
            var document = converter.ToV2(project);
            var variable = document.Variables.Single();
            variable.ServiceScopes.Add(new VaultV2ServiceScope
            {
                ServiceId = variable.OwnerServiceId,
                Visible = true,
                AllowOverride = true,
                Export = true,
                Sync = true,
                Values = new System.Collections.Generic.List<VaultV2Value>
                {
                    new VaultV2Value
                    {
                        Id = "legacy-owner-scope-value",
                        Version = 1,
                        EnvironmentId = "prod",
                        EncryptedValue = variable.Values.Single().EncryptedValue
                    }
                }
            });
            SignDocument(document, key, crypto, protection);

            protection.VerifyIntegrity(document, key);
            protection.VerifyIntegrity(converter.FromV2(document), key);

            variable.ServiceScopes.Last().Export = false;
            Assert.Throws<CryptographicException>(() => protection.VerifyIntegrity(document, key));
        }

        [Fact]
        public void MigratedLegacyValueUsesAuthenticatedLegacyAadBeforeNormalization()
        {
            var project = CreateProject();
            var legacyValue = project.Values.Single();
            legacyValue.Scope = ValueScope.Global;
            legacyValue.EnvironmentId = null;
            var crypto = new CryptoService();
            var key = crypto.DeriveKey("password", Enumerable.Range(1, 32).Select(x => (byte)x).ToArray(), 1000);
            var protection = new VaultProtectionService();
            protection.EncryptValuesForStorage(project, key, _ => true);
            protection.ProtectIntegrity(project, key);

            var converter = new VaultFormatConverter();
            var document = converter.ToV2(project);
            Assert.Contains(document.Variables.Single().ServiceScopes,
                scope => scope.ServiceId == document.Variables.Single().OwnerServiceId);
            protection.VerifyIntegrity(document, key);

            var loaded = converter.FromV2(document);
            Assert.Equal(ValueScope.Service, loaded.Values.Single().Scope);
            protection.DecryptValuesForAdmin(loaded, key);
            Assert.Equal("secret", loaded.Values.Single().Value);
        }

        [Fact]
        public void TokenRejectsManualExportPolicyChangeEvenWhenReadSlotExists()
        {
            var project = CreateProject();
            var backend = project.Contracts.Single(c => c.ServiceId == "backend");
            backend.Excluded = true;
            var crypto = new CryptoService();
            var key = crypto.DeriveKey("password", Enumerable.Range(1, 32).Select(x => (byte)x).ToArray(), 1000);
            var protection = new VaultProtectionService();
            var created = protection.CreateToken(project, key, "backend-dev", "Backend Dev", "ServiceEnvironment",
                new[] { "backend.dev.DATABASE_URL.Read|BatchExport" });
            protection.EncryptValuesForStorage(project, key, _ => true);
            protection.ProtectIntegrity(project, key);

            var files = new VaultFileService();
            var loaded = files.Deserialize(files.Serialize(project));
            loaded.Contracts.Single(c => c.ServiceId == "backend").Excluded = false;

            Assert.Throws<UnauthorizedAccessException>(() =>
                protection.UnlockForToken(loaded, created.Secret, "backend", "dev", "BatchExport"));
        }

        [Fact]
        public void TypedTokenCreationRejectsGrantOutsideDeclaredScope()
        {
            var project = CreateProject();
            var crypto = new CryptoService();
            var key = crypto.DeriveKey("password", Enumerable.Range(1, 32).Select(x => (byte)x).ToArray(), 1000);
            var protection = new VaultProtectionService();

            Assert.Throws<InvalidOperationException>(() => protection.CreateToken(project, key,
                "service-backend", "Backend", "Service", new[] { "database|backend.*.*.Read" },
                null, "backend", null));
        }

        private static ProjectModel CreateProject()
        {
            var project = new ProjectModel { ProjectId = "p", ProjectName = "P" };
            project.Settings.EncryptionMode = "AllValues";
            project.Services.Add(new ServiceModel { Id = "database", Name = "database" });
            project.Services.Add(new ServiceModel { Id = "backend", Name = "backend" });
            project.Environments.Add(new EnvironmentModel { Id = "dev", Name = "dev" });
            project.Environments.Add(new EnvironmentModel { Id = "prod", Name = "prod" });
            var variable = new VariableDefinitionModel { Id = "url", Key = "DATABASE_URL", OwnerServiceId = "database" };
            project.Variables.Add(variable);
            project.Contracts.Add(new VariableContractModel { VariableId = variable.Id, ServiceId = "database", VisibleToService = true, AllowOverride = true, Excluded = false, Required = true });
            project.Contracts.Add(new VariableContractModel { VariableId = variable.Id, ServiceId = "backend", VisibleToService = true, AllowOverride = false, Excluded = false, Required = true });
            project.Values.Add(new VariableValueModel { Id = "value", VariableId = variable.Id, Scope = ValueScope.ServiceEnvironment, ServiceId = "database", EnvironmentId = "dev", Value = "secret" });
            return project;
        }

        private static void SignDocument(VaultV2Document document, byte[] key, CryptoService crypto, VaultProtectionService protection)
        {
            var canonical = new CanonicalJsonService();
            var variableKey = crypto.DeriveSubkey(key, "EnvSecured.VariableIntegrity.v2", 32);
            var vaultKey = crypto.DeriveSubkey(key, "EnvSecured.VaultIntegrity.v2", 32);
            try
            {
                foreach (var variable in document.Variables)
                {
                    variable.Integrity = variable.Integrity ?? new IntegrityTagModel();
                    variable.Integrity.Tag = null;
                    variable.Integrity.Tag = Convert.ToBase64String(crypto.ComputeHmac(variableKey,
                        "EnvSecured.Variable.v2\0" + document.Project.Id + "\0" + canonical.Canonicalize(variable)));
                }

                document.VaultIntegrity = document.VaultIntegrity ?? new IntegrityTagModel();
                document.VaultIntegrity.Tag = null;
                document.VaultIntegrity.Tag = Convert.ToBase64String(crypto.ComputeHmac(vaultKey,
                    protection.GetVaultIntegrityMaterial(document)));
            }
            finally
            {
                Array.Clear(variableKey, 0, variableKey.Length);
                Array.Clear(vaultKey, 0, vaultKey.Length);
            }
        }
    }
}

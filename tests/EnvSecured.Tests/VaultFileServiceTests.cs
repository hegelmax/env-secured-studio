using System;
using System.IO;
using System.Linq;
using EnvSecured.Core.Models;
using EnvSecured.Core.Services;
using Xunit;

namespace EnvSecured.Tests
{
    public sealed class VaultFileServiceTests
    {
        [Fact]
        public void Save_RemovesTemporaryBackupAfterSuccessfulSave()
        {
            var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var path = Path.Combine(directory, "project.envs");
            try
            {
                Directory.CreateDirectory(directory);
                var service = new VaultFileService();
                service.Save(new ProjectModel { ProjectName = "first" }, path);
                service.Save(new ProjectModel { ProjectName = "second" }, path);

                Assert.True(File.Exists(path));
                Assert.False(File.Exists(path + ".bak"));
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void Load_ReadsLegacyJavaScriptDateValues()
        {
            var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var path = Path.Combine(directory, "project.envs");
            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(path, "{\"Values\":[{\"VariableId\":\"v\",\"Scope\":0,\"Value\":\"x\",\"UpdatedAt\":\"/Date(1779547232801)/\"}]}");

                var project = new VaultFileService().Load(path);
                var value = project.Values.Single();

                Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1779547232801).UtcDateTime, value.UpdatedAtUtc);
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void Save_WritesUpdatedAtAsIsoString()
        {
            var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var path = Path.Combine(directory, "project.envs");
            try
            {
                Directory.CreateDirectory(directory);
                var value = new VariableValueModel { VariableId = "v", Scope = ValueScope.Global, Value = "x" };
                value.UpdatedAtUtc = new DateTime(2026, 5, 23, 12, 34, 56, DateTimeKind.Utc);

                new VaultFileService().Save(new ProjectModel
                {
                    Variables = { new VariableDefinitionModel { Id = "v", Key = "VALUE" } },
                    Values = { value }
                }, path);

                var json = File.ReadAllText(path);
                Assert.DoesNotContain("/Date(", json);
                Assert.Contains("2026-05-23T12:34:56", json);
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void Save_WritesNestedV2FormatWithoutContractsOrRootValues()
        {
            var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var path = Path.Combine(directory, "project.envs");
            try
            {
                Directory.CreateDirectory(directory);
                var project = new ProjectModel { ProjectId = "p", ProjectName = "P" };
                project.Services.Add(new ServiceModel { Id = "database", Name = "database" });
                project.Services.Add(new ServiceModel { Id = "backend", Name = "backend" });
                project.Environments.Add(new EnvironmentModel { Id = "dev", Name = "dev" });
                project.Variables.Add(new VariableDefinitionModel { Id = "url", Key = "DATABASE_URL", OwnerServiceId = "database" });
                project.Contracts.Add(new VariableContractModel { VariableId = "url", ServiceId = "backend", Excluded = false, VisibleToService = true, AllowOverride = true });
                project.Values.Add(new VariableValueModel { Id = "value", VariableId = "url", Scope = ValueScope.ServiceEnvironment, ServiceId = "database", EnvironmentId = "dev", Value = "x" });

                var service = new VaultFileService();
                service.Save(project, path);
                var json = File.ReadAllText(path);
                var loaded = service.Load(path);

                Assert.Contains("\"Format\":\"EnvSecured.Vault\"", json);
                Assert.DoesNotContain("\"Contracts\"", json);
                Assert.DoesNotContain("\"Values\":[{\"Id\":\"value\",\"VariableId\"", json);
                Assert.Contains("\"ServiceScopes\"", json);
                Assert.Contains("\"Settings\":{\"GeneratedFileHeader\"", json);
                Assert.Contains("\"Output\":{\"RootFolder\"", json);
                Assert.Single(loaded.Values);
                Assert.Equal(2, loaded.Contracts.Count);
                Assert.Equal("x", loaded.Values[0].Value);
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void SavingLegacyFileCreatesV1Backup()
        {
            var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var path = Path.Combine(directory, "project.envs");
            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(path, "{\"ProjectId\":\"legacy\"}");
                var service = new VaultFileService();
                service.Save(service.Load(path), path);

                Assert.True(File.Exists(path + ".v1.bak"));
                Assert.Contains("\"Format\":\"EnvSecured.Vault\"", File.ReadAllText(path));
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void MissingV2SyncFlagInheritsExportForBackwardCompatibility(bool export)
        {
            var document = new VaultV2Document
            {
                Project = new VaultV2Project { Id = "p", Name = "P" },
                Variables =
                {
                    new VaultV2Variable
                    {
                        Id = "url",
                        Key = "DATABASE_URL",
                        OwnerServiceId = "database",
                        Export = export,
                        Sync = null
                    }
                }
            };

            var project = new VaultFormatConverter().FromV2(document);
            var contract = project.Contracts.Single();

            Assert.Null(contract.Sync);
            Assert.Equal(export, ProjectService.IsContractSyncEnabled(contract));
        }

        [Fact]
        public void V2RoundTripKeepsSyncIndependentFromExport()
        {
            var project = new ProjectModel { ProjectId = "p", ProjectName = "P" };
            project.Services.Add(new ServiceModel { Id = "database", Name = "database" });
            project.Variables.Add(new VariableDefinitionModel { Id = "url", Key = "DATABASE_URL", OwnerServiceId = "database" });
            project.Contracts.Add(new VariableContractModel
            {
                VariableId = "url",
                ServiceId = "database",
                Excluded = true,
                Sync = true,
                VisibleToService = true
            });

            var service = new VaultFileService();
            var json = service.Serialize(project);
            var loaded = service.Deserialize(json);
            var contract = loaded.Contracts.Single();

            Assert.True(contract.Excluded);
            Assert.True(contract.Sync);
            Assert.Contains("\"Export\":false,\"Sync\":true", json);
        }
    }
}

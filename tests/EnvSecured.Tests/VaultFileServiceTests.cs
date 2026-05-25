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

                new VaultFileService().Save(new ProjectModel { Values = { value } }, path);

                var json = File.ReadAllText(path);
                Assert.DoesNotContain("/Date(", json);
                Assert.Contains("2026-05-23T12:34:56", json);
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EnvSecured.Core.Services;
using Xunit;

namespace EnvSecured.Tests
{
    public sealed class CliGitHubSyncTests
    {
        private static readonly Type CliType = typeof(EnvSecured.WinForms.Forms.MainForm).Assembly
            .GetType("EnvSecured.WinForms.Cli.CliRunner", true);

        [Fact]
        public void SyncCommandsAreRecognizedAsCliRequests()
        {
            var method = CliType.GetMethod("IsCliRequest", BindingFlags.Public | BindingFlags.Static);
            Assert.True((bool)method.Invoke(null, new object[] { new[] { "sync-list" } }));
            Assert.True((bool)method.Invoke(null, new object[] { new[] { "sync-preview" } }));
            Assert.True((bool)method.Invoke(null, new object[] { new[] { "sync-push" } }));
        }

        [Fact]
        public void AddMissingDoesNotUpdateOrDeleteExistingRemoteValues()
        {
            var rows = BuildPlan("AddMissing", true);
            Assert.Contains(rows, r => r.Key == "EXISTING" && r.Action == "Unchanged");
            Assert.Contains(rows, r => r.Key == "NEW" && r.Action == "Create");
            Assert.DoesNotContain(rows, r => r.Action == "Delete");
        }

        [Fact]
        public void ForceReplaceReplacesExistingAndDeletesRemoteOnlyValues()
        {
            var rows = BuildPlan("ForceReplace", true);
            Assert.Contains(rows, r => r.Key == "EXISTING" && r.Action == "Replace");
            Assert.Contains(rows, r => r.Key == "EXTRA" && r.Action == "Delete");
        }

        [Fact]
        public void ForceReplaceReplacesEvenMatchingRemoteValue()
        {
            var rows = BuildPlan("ForceReplace", true, "new-value");
            Assert.Contains(rows, r => r.Key == "EXISTING" && r.Action == "Replace");
        }

        [Fact]
        public void ServiceFilteredPlanSuppressesMirrorDeletions()
        {
            var rows = BuildPlan("Mirror", false);
            Assert.DoesNotContain(rows, r => r.Action == "Delete");
        }

        [Fact]
        public void MatchingFingerprintMakesExistingSecretUnchanged()
        {
            var key = SecretFingerprintService.GenerateKey();
            var fingerprint = SecretFingerprintService.Compute(key, "github-main", "development", "API_TOKEN", "secret-value");
            var rows = BuildSecretPlan(key, fingerprint);

            Assert.Contains(rows, r => r.Type == "Secret" && r.Key == "API_TOKEN" && r.Action == "Unchanged");
            Assert.Contains(rows, r => r.Type == "Fingerprint" && r.Key == SecretFingerprintService.VariableName("API_TOKEN") && r.Action == "Unchanged");
        }

        [Fact]
        public void DifferentFingerprintUpdatesExistingSecretAndCompanionVariable()
        {
            var rows = BuildSecretPlan(SecretFingerprintService.GenerateKey(), "hmac-sha256:v1:OLD");

            Assert.Contains(rows, r => r.Type == "Secret" && r.Action == "Update");
            Assert.Contains(rows, r => r.Type == "Fingerprint" && r.Action == "Update");
        }

        [Fact]
        public void CheckExitCodeIsTwoOnlyWhenDryRunHasDrift()
        {
            var method = CliType.GetMethod("DetermineSyncExitCodeFromActions", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.Equal(2, method.Invoke(null, new object[] { new[] { "Unchanged", "Update" }, false, true }));
            Assert.Equal(0, method.Invoke(null, new object[] { new[] { "Unchanged" }, false, true }));
            Assert.Equal(0, method.Invoke(null, new object[] { new[] { "Update" }, false, false }));
            Assert.Equal(0, method.Invoke(null, new object[] { new[] { "Update" }, true, true }));
            Assert.Equal(2, method.Invoke(null, new object[] { new[] { "Conflict" }, false, false }));
        }

        private static List<PlanRow> BuildPlan(string mode, bool allowDeletes, string existingRemoteValue = "old-value")
        {
            var mappingType = CliType.GetNestedType("CliGitHubEnvironmentMapping", BindingFlags.NonPublic);
            var localType = CliType.GetNestedType("CliGitHubLocalValue", BindingFlags.NonPublic);
            var remoteType = CliType.GetNestedType("CliGitHubRemoteValues", BindingFlags.NonPublic);
            var rowType = CliType.GetNestedType("CliGitHubSyncRow", BindingFlags.NonPublic);

            var mapping = Activator.CreateInstance(mappingType, new object[] { "dev", "development" });
            var localList = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(localType));
            localList.Add(Activator.CreateInstance(localType, new object[] { "EXISTING", "new-value", false, "backend" }));
            localList.Add(Activator.CreateInstance(localType, new object[] { "NEW", "new", false, "backend" }));

            var remote = Activator.CreateInstance(remoteType);
            var variables = (IDictionary)remoteType.GetProperty("Variables").GetValue(remote);
            variables.Add("EXISTING", existingRemoteValue);
            variables.Add("EXTRA", "remote-only");

            var plan = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(rowType));
            var method = CliType.GetMethod("AddCliGitHubPlan", BindingFlags.NonPublic | BindingFlags.Static);
            method.Invoke(null, new[] { plan, localList, remote, mapping, mode, (object)allowDeletes, "github-main", SecretFingerprintService.GenerateKey(), true, true });

            return plan.Cast<object>().Select(row => new PlanRow
            {
                Action = Convert.ToString(rowType.GetProperty("Action").GetValue(row)),
                Key = Convert.ToString(rowType.GetProperty("Key").GetValue(row)),
                Type = Convert.ToString(rowType.GetProperty("Type").GetValue(row))
            }).ToList();
        }

        private static List<PlanRow> BuildSecretPlan(string fingerprintKey, string remoteFingerprint)
        {
            var mappingType = CliType.GetNestedType("CliGitHubEnvironmentMapping", BindingFlags.NonPublic);
            var localType = CliType.GetNestedType("CliGitHubLocalValue", BindingFlags.NonPublic);
            var remoteType = CliType.GetNestedType("CliGitHubRemoteValues", BindingFlags.NonPublic);
            var rowType = CliType.GetNestedType("CliGitHubSyncRow", BindingFlags.NonPublic);
            var mapping = Activator.CreateInstance(mappingType, new object[] { "dev", "development" });
            var localList = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(localType));
            localList.Add(Activator.CreateInstance(localType, new object[] { "API_TOKEN", "secret-value", true, "backend" }));
            var remote = Activator.CreateInstance(remoteType);
            ((ISet<string>)remoteType.GetProperty("Secrets").GetValue(remote)).Add("API_TOKEN");
            ((IDictionary)remoteType.GetProperty("Variables").GetValue(remote)).Add(SecretFingerprintService.VariableName("API_TOKEN"), remoteFingerprint);
            var plan = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(rowType));
            CliType.GetMethod("AddCliGitHubPlan", BindingFlags.NonPublic | BindingFlags.Static).Invoke(
                null, new[] { plan, localList, remote, mapping, "AddUpdate", (object)true, "github-main", fingerprintKey, true, true });
            return plan.Cast<object>().Select(row => new PlanRow
            {
                Action = Convert.ToString(rowType.GetProperty("Action").GetValue(row)),
                Key = Convert.ToString(rowType.GetProperty("Key").GetValue(row)),
                Type = Convert.ToString(rowType.GetProperty("Type").GetValue(row))
            }).ToList();
        }

        private sealed class PlanRow
        {
            public string Action { get; set; }
            public string Key { get; set; }
            public string Type { get; set; }
        }
    }
}

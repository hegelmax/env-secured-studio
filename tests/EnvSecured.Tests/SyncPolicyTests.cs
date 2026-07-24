using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using EnvSecured.Core.Models;
using EnvSecured.Core.Services;
using Xunit;

namespace EnvSecured.Tests
{
    public sealed class SyncPolicyTests
    {
        [Fact]
        public void GitHubRowsUseSyncInsteadOfFileExport()
        {
            var project = new ProjectModel();
            project.Services.Add(new ServiceModel { Id = "backend", Name = "backend", IsActive = true });
            project.Environments.Add(new EnvironmentModel { Id = "dev", Name = "dev", IsActive = true });
            AddVariable(project, "export-only", "EXPORT_ONLY", true, false);
            AddVariable(project, "sync-only", "SYNC_ONLY", false, true);

            var type = typeof(EnvSecured.WinForms.Forms.MainForm);
            var form = FormatterServices.GetUninitializedObject(type);
            SetField(type, form, "project", project);
            SetField(type, form, "effectiveConfigService", new EffectiveConfigService());
            var previewField = type.GetField("githubSyncPreviewRows", BindingFlags.Instance | BindingFlags.NonPublic);
            previewField.SetValue(form, Activator.CreateInstance(previewField.FieldType));

            var method = type.GetMethod("BuildLocalGitHubSyncRows", BindingFlags.Instance | BindingFlags.NonPublic);
            var rows = (IEnumerable)method.Invoke(form, new object[] { new SyncProviderModel(), "dev", "dev" });
            var keys = rows.Cast<object>()
                .Select(row => Convert.ToString(row.GetType().GetProperty("Key").GetValue(row)))
                .ToList();

            Assert.Equal(new[] { "SYNC_ONLY" }, keys);
        }

        private static void SetField(Type type, object target, string name, object value)
        {
            type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        }

        private static void AddVariable(ProjectModel project, string id, string key, bool export, bool sync)
        {
            project.Variables.Add(new VariableDefinitionModel
            {
                Id = id,
                Key = key,
                OwnerServiceId = "backend",
                IsActive = true
            });
            project.Contracts.Add(new VariableContractModel
            {
                VariableId = id,
                ServiceId = "backend",
                VisibleToService = true,
                AllowOverride = true,
                Excluded = !export,
                Sync = sync
            });
            project.Values.Add(new VariableValueModel
            {
                Id = id + "-value",
                VariableId = id,
                Scope = ValueScope.ServiceEnvironment,
                ServiceId = "backend",
                EnvironmentId = "dev",
                Value = id
            });
        }
    }
}

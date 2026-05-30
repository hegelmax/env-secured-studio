using System.Linq;
using EnvSecured.Core.Models;
using EnvSecured.Core.Services;
using Xunit;

namespace EnvSecured.Tests
{
    public sealed class ProjectVaultTransformServiceTests
    {
        [Fact]
        public void Split_IncludesReferencedVariablesByDefault()
        {
            var project = CreateProject();
            project.Variables.Add(new VariableDefinitionModel { Id = "url", Key = "BACKEND_URL", OwnerServiceId = "backend" });
            project.Variables.Add(new VariableDefinitionModel { Id = "host", Key = "BACKEND_HOST", OwnerServiceId = "backend" });
            project.Variables.Add(new VariableDefinitionModel { Id = "unused", Key = "UNUSED", OwnerServiceId = "backend" });
            project.Values.Add(new VariableValueModel { VariableId = "url", Scope = ValueScope.Service, ServiceId = "backend", Value = "https://{{BACKEND_HOST}}" });
            project.Values.Add(new VariableValueModel { VariableId = "host", Scope = ValueScope.Service, ServiceId = "backend", Value = "api.local" });
            project.Values.Add(new VariableValueModel { VariableId = "unused", Scope = ValueScope.Service, ServiceId = "backend", Value = "x" });

            var split = new ProjectVaultTransformService().Split(project, new ProjectSplitOptions
            {
                Keys = { "BACKEND_URL" }
            });

            Assert.Equal(new[] { "BACKEND_HOST", "BACKEND_URL" }, split.Variables.Select(v => v.Key).OrderBy(v => v).ToArray());
            Assert.Equal(2, split.Values.Count);
            Assert.DoesNotContain(split.Variables, v => v.Key == "UNUSED");
        }

        [Fact]
        public void Merge_ReplacesMatchingScopedValues()
        {
            var target = CreateProject();
            target.Variables.Add(new VariableDefinitionModel { Id = "target-url", Key = "BACKEND_URL", OwnerServiceId = "backend" });
            target.Values.Add(new VariableValueModel { VariableId = "target-url", Scope = ValueScope.ServiceEnvironment, ServiceId = "backend", EnvironmentId = "dev", Value = "old" });

            var source = CreateProject();
            source.Variables.Add(new VariableDefinitionModel { Id = "source-url", Key = "BACKEND_URL", OwnerServiceId = "backend" });
            source.Values.Add(new VariableValueModel { VariableId = "source-url", Scope = ValueScope.ServiceEnvironment, ServiceId = "backend", EnvironmentId = "dev", Value = "new" });

            var result = new ProjectVaultTransformService().Merge(target, new[] { source });

            Assert.Equal(1, result.ValuesMerged);
            Assert.Single(target.Values);
            Assert.Equal("new", target.Values[0].Value);
            Assert.Equal("target-url", target.Values[0].VariableId);
        }

        private static ProjectModel CreateProject()
        {
            var project = new ProjectModel();
            project.Services.Add(new ServiceModel { Id = "backend", Name = "backend" });
            project.Environments.Add(new EnvironmentModel { Id = "dev", Name = "dev" });
            return project;
        }
    }
}

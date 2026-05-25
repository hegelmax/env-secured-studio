using System.Linq;
using EnvSecured.Core.Models;
using EnvSecured.Core.Services;
using Xunit;

namespace EnvSecured.Tests
{
    public sealed class ProjectServiceTests
    {
        [Fact]
        public void GetVariablesUsedByService_ReturnsOnlyExportedServiceScopeVariables()
        {
            var project = new ProjectModel();
            project.Services.Add(new ServiceModel { Id = "backend", Name = "backend" });
            var contracted = AddVariable(project, "contracted", "CONTRACTED", 20);
            var global = AddVariable(project, "global", "GLOBAL", 10);
            var unused = AddVariable(project, "unused", "UNUSED", 30);
            var inactive = AddVariable(project, "inactive", "INACTIVE", 40);
            inactive.IsActive = false;
            project.Contracts.Add(new VariableContractModel { ServiceId = "backend", VariableId = contracted.Id });
            project.Contracts.Add(new VariableContractModel { ServiceId = "backend", VariableId = inactive.Id });
            project.Values.Add(new VariableValueModel { VariableId = global.Id, Scope = ValueScope.Global, Value = "value" });

            var variables = ProjectService.GetVariablesUsedByService(project, "backend").Select(v => v.Key).ToList();

            Assert.Equal(new[] { "CONTRACTED" }, variables);
            Assert.DoesNotContain(global.Key, variables);
            Assert.DoesNotContain(unused.Key, variables);
            Assert.DoesNotContain(inactive.Key, variables);
        }

        [Fact]
        public void IsVariableSharedFromService_UsesContractShareFlag()
        {
            var project = new ProjectModel();
            project.Contracts.Add(new VariableContractModel
            {
                VariableId = "v",
                ServiceId = "backend",
                ShareWithOtherServices = false
            });

            Assert.False(ProjectService.IsVariableSharedFromService(project, "v", "backend"));
            Assert.True(ProjectService.IsVariableSharedFromService(project, "other", "backend"));
            Assert.True(ProjectService.IsVariableSharedFromService(project, "v", null));
        }

        [Fact]
        public void ScopedVariableVisibilityAndOverrideUseOwnerAndDependentContracts()
        {
            var project = new ProjectModel();
            var variable = AddVariable(project, "v", "VALUE", 0);
            variable.OwnerServiceId = "owner";
            project.Contracts.Add(new VariableContractModel
            {
                VariableId = variable.Id,
                ServiceId = "dependent",
                VisibleToService = true,
                AllowOverride = false
            });

            Assert.True(ProjectService.IsVariableVisibleToService(project, variable, "owner"));
            Assert.True(ProjectService.CanOverrideVariableForService(project, variable, "owner"));
            Assert.True(ProjectService.IsVariableVisibleToService(project, variable, "dependent"));
            Assert.False(ProjectService.CanOverrideVariableForService(project, variable, "dependent"));
            Assert.False(ProjectService.IsVariableVisibleToService(project, variable, "other"));
        }

        [Fact]
        public void ReplaceInterpolationReferences_UpdatesOnlyExactTokens()
        {
            var project = new ProjectModel();
            project.Values.Add(new VariableValueModel { Value = "{{OLD_KEY}}/{{OLD_KEY_SUFFIX}}/OLD_KEY" });
            project.Values.Add(new VariableValueModel { Value = "prefix {{OLD_KEY}}" });

            var updated = ProjectService.ReplaceInterpolationReferences(project, "OLD_KEY", "NEW_KEY");

            Assert.Equal(2, updated);
            Assert.Equal("{{NEW_KEY}}/{{OLD_KEY_SUFFIX}}/OLD_KEY", project.Values[0].Value);
            Assert.Equal("prefix {{NEW_KEY}}", project.Values[1].Value);
        }

        [Fact]
        public void MoveOwnerValues_MovesServiceValuesWithoutOverwritingTarget()
        {
            var project = new ProjectModel();
            project.Values.Add(new VariableValueModel { VariableId = "v", Scope = ValueScope.Service, ServiceId = "old", Value = "old-global" });
            project.Values.Add(new VariableValueModel { VariableId = "v", Scope = ValueScope.ServiceEnvironment, ServiceId = "old", EnvironmentId = "dev", Value = "old-dev" });
            project.Values.Add(new VariableValueModel { VariableId = "v", Scope = ValueScope.ServiceEnvironment, ServiceId = "new", EnvironmentId = "dev", Value = "new-dev" });

            var moved = ProjectService.MoveOwnerValues(project, "v", "old", "new");

            Assert.Equal(1, moved);
            Assert.Contains(project.Values, v => v.Scope == ValueScope.Service && v.ServiceId == "new" && v.Value == "old-global");
            Assert.Contains(project.Values, v => v.Scope == ValueScope.ServiceEnvironment && v.ServiceId == "old" && v.EnvironmentId == "dev" && v.Value == "old-dev");
            Assert.Contains(project.Values, v => v.Scope == ValueScope.ServiceEnvironment && v.ServiceId == "new" && v.EnvironmentId == "dev" && v.Value == "new-dev");
        }

        [Fact]
        public void MoveOwnerValues_MovesServiceValuesToGlobalOwner()
        {
            var project = new ProjectModel();
            project.Values.Add(new VariableValueModel { VariableId = "v", Scope = ValueScope.Service, ServiceId = "old", Value = "old-global" });
            project.Values.Add(new VariableValueModel { VariableId = "v", Scope = ValueScope.ServiceEnvironment, ServiceId = "old", EnvironmentId = "dev", Value = "old-dev" });

            var moved = ProjectService.MoveOwnerValues(project, "v", "old", null);

            Assert.Equal(2, moved);
            Assert.Contains(project.Values, v => v.Scope == ValueScope.Global && v.ServiceId == null && v.Value == "old-global");
            Assert.Contains(project.Values, v => v.Scope == ValueScope.Environment && v.ServiceId == null && v.EnvironmentId == "dev" && v.Value == "old-dev");
        }

        [Fact]
        public void MoveOwnerValues_MovesGlobalValuesToServiceOwner()
        {
            var project = new ProjectModel();
            project.Values.Add(new VariableValueModel { VariableId = "v", Scope = ValueScope.Global, Value = "global" });
            project.Values.Add(new VariableValueModel { VariableId = "v", Scope = ValueScope.Environment, EnvironmentId = "dev", Value = "dev" });

            var moved = ProjectService.MoveOwnerValues(project, "v", null, "new");

            Assert.Equal(2, moved);
            Assert.Contains(project.Values, v => v.Scope == ValueScope.Service && v.ServiceId == "new" && v.EnvironmentId == null && v.Value == "global");
            Assert.Contains(project.Values, v => v.Scope == ValueScope.ServiceEnvironment && v.ServiceId == "new" && v.EnvironmentId == "dev" && v.Value == "dev");
        }

        private static VariableDefinitionModel AddVariable(ProjectModel project, string id, string key, int sortOrder)
        {
            var variable = new VariableDefinitionModel { Id = id, Key = key, IsActive = true, SortOrder = sortOrder };
            project.Variables.Add(variable);
            return variable;
        }
    }
}

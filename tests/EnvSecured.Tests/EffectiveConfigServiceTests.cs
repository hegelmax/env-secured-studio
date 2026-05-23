using System.Linq;
using EnvSecured.Core.Models;
using EnvSecured.Core.Services;
using Xunit;

namespace EnvSecured.Tests
{
    public sealed class EffectiveConfigServiceTests
    {
        [Fact]
        public void Build_UsesConfiguredPrecedenceForServiceEnvironment()
        {
            var project = CreateProject();
            var variable = AddVariable(project, "url", "URL");
            AddValue(project, variable.Id, ValueScope.Global, null, null, "global");
            AddValue(project, variable.Id, ValueScope.Environment, null, "test", "environment");
            AddValue(project, variable.Id, ValueScope.Service, "frontend", null, "other-service");
            AddValue(project, variable.Id, ValueScope.ServiceEnvironment, "frontend", "test", "other-service-environment");
            AddValue(project, variable.Id, ValueScope.Service, "backend", null, "current-service");
            AddValue(project, variable.Id, ValueScope.ServiceEnvironment, "backend", "test", "current-service-environment");

            var effective = new EffectiveConfigService().Build(project, "backend", "test").Single(v => v.Variable.Id == variable.Id);

            Assert.Equal("current-service-environment", effective.Value);
            Assert.Equal(ValueScope.ServiceEnvironment, effective.SourceScope);
            Assert.Equal("backend", effective.SourceServiceId);
            Assert.Equal("test", effective.SourceEnvironmentId);
        }

        [Fact]
        public void Build_InterpolatesEffectiveValues()
        {
            var project = CreateProject();
            var host = AddVariable(project, "host", "HOST");
            var url = AddVariable(project, "url", "URL");
            AddValue(project, host.Id, ValueScope.Global, null, null, "https://example.test");
            AddValue(project, url.Id, ValueScope.Service, "backend", null, "${HOST}/api");

            var effective = new EffectiveConfigService().Build(project, "backend", "test").Single(v => v.Variable.Id == url.Id);

            Assert.Equal("https://example.test/api", effective.Value);
        }

        [Fact]
        public void Build_DoesNotInterpolateBareBraceTokens()
        {
            var project = CreateProject();
            var host = AddVariable(project, "host", "HOST");
            var path = AddVariable(project, "path", "PATH_VALUE");
            AddValue(project, host.Id, ValueScope.Global, null, null, "example.test");
            AddValue(project, path.Id, ValueScope.Global, null, null, @"apps\{HOST}\data");

            var effective = new EffectiveConfigService().Build(project, "backend", "test").Single(v => v.Variable.Id == path.Id);

            Assert.Equal(@"apps\{HOST}\data", effective.Value);
        }

        [Fact]
        public void Build_ReturnsMissingWhenVariableHasNoValue()
        {
            var project = CreateProject();
            var variable = AddVariable(project, "missing", "MISSING");

            var effective = new EffectiveConfigService().Build(project, "backend", "test").Single(v => v.Variable.Id == variable.Id);

            Assert.True(effective.Missing);
            Assert.Null(effective.Value);
            Assert.Null(effective.SourceScope);
        }

        [Fact]
        public void Build_ExcludesInactiveVariables()
        {
            var project = CreateProject();
            AddVariable(project, "active", "ACTIVE");
            var inactive = AddVariable(project, "inactive", "INACTIVE");
            inactive.IsActive = false;

            var effective = new EffectiveConfigService().Build(project, "backend", "test");

            Assert.Contains(effective, v => v.Variable.Id == "active");
            Assert.DoesNotContain(effective, v => v.Variable.Id == "inactive");
        }

        [Fact]
        public void Build_UsesGlobalValueWithoutServiceOrEnvironment()
        {
            var project = CreateProject();
            var variable = AddVariable(project, "global", "GLOBAL");
            AddValue(project, variable.Id, ValueScope.Global, null, null, "global-value");

            var effective = new EffectiveConfigService().Build(project, null, null).Single(v => v.Variable.Id == variable.Id);

            Assert.Equal("global-value", effective.Value);
            Assert.Equal(ValueScope.Global, effective.SourceScope);
            Assert.Null(effective.SourceServiceId);
            Assert.Null(effective.SourceEnvironmentId);
        }

        private static ProjectModel CreateProject()
        {
            return new ProjectModel
            {
                Services =
                {
                    new ServiceModel { Id = "backend", Name = "backend", SortOrder = 2 },
                    new ServiceModel { Id = "frontend", Name = "frontend", SortOrder = 1 }
                },
                Environments =
                {
                    new EnvironmentModel { Id = "test", Name = "test" }
                }
            };
        }

        private static VariableDefinitionModel AddVariable(ProjectModel project, string id, string key)
        {
            var variable = new VariableDefinitionModel { Id = id, Key = key, Type = VariableType.String, IsActive = true };
            project.Variables.Add(variable);
            return variable;
        }

        private static void AddValue(ProjectModel project, string variableId, ValueScope scope, string serviceId, string environmentId, string value)
        {
            project.Values.Add(new VariableValueModel
            {
                VariableId = variableId,
                Scope = scope,
                ServiceId = serviceId,
                EnvironmentId = environmentId,
                Value = value
            });
        }
    }
}

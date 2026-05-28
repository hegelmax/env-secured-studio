using System.Linq;
using EnvSecured.Core.Models;
using EnvSecured.Core.Services;
using EnvSecured.Core.Validation;
using Xunit;

namespace EnvSecured.Tests
{
    public sealed class GeneratedValueServiceTests
    {
        [Fact]
        public void Generate_OwnerGlobal_StoresOneOwnerServiceValue()
        {
            var project = CreateProject();
            var variable = GeneratedVariable();

            var value = new GeneratedValueService().Generate(project, variable, null, true);

            Assert.Equal(ValueScope.Service, value.Scope);
            Assert.Equal("owner", value.ServiceId);
            Assert.Null(value.EnvironmentId);
            Assert.Equal(32, value.Value.Length);
            Assert.All(value.Value, ch => Assert.True(char.IsLetterOrDigit(ch)));
        }

        [Fact]
        public void Generate_OwnerEnvironment_StoresEnvironmentValue()
        {
            var project = CreateProject();
            var variable = GeneratedVariable();
            variable.GeneratorScope = GeneratedValueService.ScopeOwnerEnvironment;

            var value = new GeneratedValueService().Generate(project, variable, "prod", true);

            Assert.Equal(ValueScope.ServiceEnvironment, value.Scope);
            Assert.Equal("owner", value.ServiceId);
            Assert.Equal("prod", value.EnvironmentId);
        }

        [Fact]
        public void Validate_ReportsMissingGeneratedValue()
        {
            var project = CreateProject();
            project.Variables.Add(GeneratedVariable());

            var results = new ValidationService().Validate(project);

            Assert.Contains(results, r => r.Code == "GENERATED_VALUE_MISSING" && r.Severity == ValidationSeverity.Error);
        }

        private static ProjectModel CreateProject()
        {
            return new ProjectModel
            {
                Services = { new ServiceModel { Id = "owner", Name = "owner" } },
                Environments = { new EnvironmentModel { Id = "prod", Name = "prod" } }
            };
        }

        private static VariableDefinitionModel GeneratedVariable()
        {
            return new VariableDefinitionModel
            {
                Id = "token",
                Key = "TOKEN",
                OwnerServiceId = "owner",
                IsActive = true,
                IsSecret = true,
                IsGenerated = true,
                GeneratorType = GeneratedValueService.TypeTokenBase62,
                GeneratorLength = 32,
                GeneratorScope = GeneratedValueService.ScopeOwnerGlobal,
                GeneratorMode = GeneratedValueService.ModeManual
            };
        }
    }
}

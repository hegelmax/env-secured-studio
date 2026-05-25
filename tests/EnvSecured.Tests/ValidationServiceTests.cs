using System.Linq;
using EnvSecured.Core.Models;
using EnvSecured.Core.Validation;
using Xunit;

namespace EnvSecured.Tests
{
    public sealed class ValidationServiceTests
    {
        [Fact]
        public void Validate_ReportsRequiredValueMissing()
        {
            var project = CreateProject();
            var variable = AddVariable(project, "required", "REQUIRED_VALUE");
            project.Contracts.Add(new VariableContractModel { ServiceId = "backend", VariableId = variable.Id, Required = true });

            var results = new ValidationService().Validate(project, "backend", "test");

            Assert.Contains(results, r => r.Code == "REQUIRED_VALUE_MISSING" && r.Severity == ValidationSeverity.Error);
        }

        [Fact]
        public void Validate_ReportsInterpolationCycle()
        {
            var project = CreateProject();
            var first = AddVariable(project, "first", "FIRST");
            var second = AddVariable(project, "second", "SECOND");
            project.Contracts.Add(new VariableContractModel { ServiceId = "backend", VariableId = first.Id, Required = true });
            project.Contracts.Add(new VariableContractModel { ServiceId = "backend", VariableId = second.Id, Required = true });
            AddValue(project, first.Id, ValueScope.Global, null, null, "{{SECOND}}");
            AddValue(project, second.Id, ValueScope.Global, null, null, "{{FIRST}}");

            var results = new ValidationService().Validate(project, "backend", "test");

            Assert.Contains(results, r => r.Code == "INTERPOLATION_CYCLE" && r.Severity == ValidationSeverity.Error);
        }

        [Fact]
        public void Validate_DoesNotTreatBareBraceTextAsInterpolation()
        {
            var project = CreateProject();
            var variable = AddVariable(project, "path", "PATH_VALUE");
            project.Contracts.Add(new VariableContractModel { ServiceId = "backend", VariableId = variable.Id, Required = true });
            AddValue(project, variable.Id, ValueScope.Global, null, null, @"apps\{MISSING}\data");

            var results = new ValidationService().Validate(project, "backend", "test");

            Assert.DoesNotContain(results, r => r.Code.StartsWith("INTERPOLATION_"));
        }

        [Fact]
        public void Validate_DoesNotTreatDollarBraceTextAsInterpolation()
        {
            var project = CreateProject();
            var variable = AddVariable(project, "path", "PATH_VALUE");
            project.Contracts.Add(new VariableContractModel { ServiceId = "backend", VariableId = variable.Id, Required = true });
            AddValue(project, variable.Id, ValueScope.Global, null, null, @"apps\${MISSING}\data");

            var results = new ValidationService().Validate(project, "backend", "test");

            Assert.DoesNotContain(results, r => r.Code.StartsWith("INTERPOLATION_"));
        }

        [Fact]
        public void Validate_ReportsRepeatedEnvironmentSecret()
        {
            var project = CreateProject();
            project.Environments.Add(new EnvironmentModel { Id = "prod", Name = "prod" });
            var variable = AddVariable(project, "token", "TOKEN");
            variable.IsSecret = true;
            AddValue(project, variable.Id, ValueScope.Environment, null, "test", "same-secret");
            AddValue(project, variable.Id, ValueScope.Environment, null, "prod", "same-secret");

            var results = new ValidationService().Validate(project);

            Assert.Contains(results, r => r.Code == "SECRET_REUSED_ACROSS_ENVIRONMENTS" && r.Severity == ValidationSeverity.Warning);
        }

        [Fact]
        public void Validate_ReportsDuplicateScopedValue()
        {
            var project = CreateProject();
            var variable = AddVariable(project, "duplicate", "DUPLICATE");
            AddValue(project, variable.Id, ValueScope.Global, null, null, "first");
            AddValue(project, variable.Id, ValueScope.Global, null, null, "second");

            var results = new ValidationService().Validate(project, "backend", "test");

            Assert.Contains(results, r => r.Code == "DUPLICATE_SCOPED_VALUE" && r.Severity == ValidationSeverity.Warning);
        }

        [Fact]
        public void Validate_ReportsRequiredValueBlank()
        {
            var project = CreateProject();
            var variable = AddVariable(project, "blank", "BLANK");
            variable.AllowBlank = false;
            project.Contracts.Add(new VariableContractModel { ServiceId = "backend", VariableId = variable.Id, Required = true });
            AddValue(project, variable.Id, ValueScope.Global, null, null, string.Empty);

            var results = new ValidationService().Validate(project, "backend", "test");

            Assert.Contains(results, r => r.Code == "REQUIRED_VALUE_BLANK" && r.Severity == ValidationSeverity.Error);
        }

        [Fact]
        public void Validate_ReportsSecretGlobalForAllEnvironments()
        {
            var project = CreateProject();
            var variable = AddVariable(project, "secret", "SECRET");
            variable.IsSecret = true;
            variable.AllowSharedSecret = false;
            AddValue(project, variable.Id, ValueScope.Global, null, null, "shared-secret");

            var results = new ValidationService().Validate(project);

            Assert.Contains(results, r => r.Code == "SECRET_GLOBAL_FOR_ALL_ENVIRONMENTS" && r.Severity == ValidationSeverity.Warning);
        }

        [Fact]
        public void Validate_DoesNotReportRequiredValueMissingWhenAllowNullIsTrue()
        {
            var project = CreateProject();
            var variable = AddVariable(project, "nullable", "NULLABLE");
            variable.AllowNull = true;
            project.Contracts.Add(new VariableContractModel { ServiceId = "backend", VariableId = variable.Id, Required = true });

            var results = new ValidationService().Validate(project, "backend", "test");

            Assert.DoesNotContain(results, r => r.Code == "REQUIRED_VALUE_MISSING");
        }

        [Fact]
        public void Validate_DoesNotReportInterpolationContractMissingWhenReferenceIsInScopeButNotExported()
        {
            var project = CreateProject();
            var source = AddVariable(project, "source", "SOURCE_VALUE");
            source.OwnerServiceId = "global";
            var composed = AddVariable(project, "composed", "COMPOSED_VALUE");
            composed.OwnerServiceId = "backend";
            project.Services.Add(new ServiceModel { Id = "global", Name = "global" });
            project.Contracts.Add(new VariableContractModel { ServiceId = "backend", VariableId = source.Id, VisibleToService = true, AllowOverride = false, Excluded = true });
            project.Contracts.Add(new VariableContractModel { ServiceId = "backend", VariableId = composed.Id, VisibleToService = true, Excluded = false });
            AddValue(project, source.Id, ValueScope.Service, "global", null, "value");
            AddValue(project, composed.Id, ValueScope.Service, "backend", null, "{{SOURCE_VALUE}}");

            var results = new ValidationService().Validate(project, "backend", "test");

            Assert.DoesNotContain(results, r => r.Code == "INTERPOLATION_CONTRACT_MISSING");
        }

        private static ProjectModel CreateProject()
        {
            return new ProjectModel
            {
                Services =
                {
                    new ServiceModel { Id = "backend", Name = "backend" }
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

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using EnvSecured.Core.Models;
using EnvSecured.Core.Services;

namespace EnvSecured.Core.Validation
{
    public sealed class ValidationService
    {
        private static readonly Regex TokenRegex = new Regex(@"\$\{(?<key>[A-Za-z_][A-Za-z0-9_]*)\}|\{(?<key>[A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled);

        public IReadOnlyList<ValidationResult> Validate(ProjectModel project, string serviceId = null, string environmentId = null)
        {
            var results = new List<ValidationResult>();
            AddDuplicates(results, project.Variables.Select(v => v.Key), "DUPLICATE_VARIABLE_KEY", "Duplicate variable key.");
            AddDuplicates(results, project.Services.Select(s => s.Name), "DUPLICATE_SERVICE_NAME", "Duplicate service name.");
            AddDuplicates(results, project.Environments.Select(e => e.Name), "DUPLICATE_ENVIRONMENT_NAME", "Duplicate environment name.");

            foreach (var contract in project.Contracts)
            {
                if (!project.Variables.Any(v => v.Id == contract.VariableId))
                {
                    results.Add(Error("MISSING_CONTRACT_VARIABLE", "Contract references a missing variable.", contract.VariableId, contract.ServiceId, null));
                }
            }

            foreach (var value in project.Values)
            {
                if (!project.Variables.Any(v => v.Id == value.VariableId))
                {
                    results.Add(Error("MISSING_VALUE_VARIABLE", "Value references a missing variable.", value.VariableId, value.ServiceId, value.EnvironmentId));
                }
                if (value.ServiceId != null && !project.Services.Any(s => s.Id == value.ServiceId))
                {
                    results.Add(Error("MISSING_VALUE_SERVICE", "Value references a missing service.", value.VariableId, value.ServiceId, value.EnvironmentId));
                }
                if (value.EnvironmentId != null && !project.Environments.Any(e => e.Id == value.EnvironmentId))
                {
                    results.Add(Error("MISSING_VALUE_ENVIRONMENT", "Value references a missing environment.", value.VariableId, value.ServiceId, value.EnvironmentId));
                }
            }
            ValidateRepeatedEnvironmentSecrets(project, results);

            if (serviceId != null && environmentId != null)
            {
                ValidateEffectiveValues(project, serviceId, environmentId, results);
                ValidateInterpolation(project, serviceId, environmentId, results);
            }
            else
            {
                foreach (var service in project.Services.Where(s => s.IsActive))
                {
                    foreach (var environment in project.Environments.Where(e => e.IsActive))
                    {
                        ValidateEffectiveValues(project, service.Id, environment.Id, results);
                        ValidateInterpolation(project, service.Id, environment.Id, results);
                    }
                }
            }

            return results;
        }

        private static void ValidateEffectiveValues(ProjectModel project, string serviceId, string environmentId, List<ValidationResult> results)
        {
            var effective = new EffectiveConfigService().Build(project, serviceId, environmentId).ToDictionary(x => x.Variable.Id);
            foreach (var contract in project.Contracts.Where(c => c.ServiceId == serviceId && !c.Excluded && c.Required))
            {
                if (!effective.ContainsKey(contract.VariableId) || effective[contract.VariableId].Missing)
                {
                    var variable = project.Variables.FirstOrDefault(v => v.Id == contract.VariableId);
                    if (variable?.AllowNull != true)
                    {
                        results.Add(Error("REQUIRED_VALUE_MISSING", "Required variable has no effective value.", contract.VariableId, serviceId, environmentId));
                    }
                }
                else if (!effective[contract.VariableId].Variable.AllowBlank && effective[contract.VariableId].Value == string.Empty)
                {
                    results.Add(Error("REQUIRED_VALUE_BLANK", "Required variable is blank.", contract.VariableId, serviceId, environmentId));
                }
            }
        }

        private static void ValidateInterpolation(ProjectModel project, string serviceId, string environmentId, List<ValidationResult> results)
        {
            var effective = project.Variables
                .Where(v => v.IsActive)
                .OrderBy(v => v.SortOrder)
                .ThenBy(v => v.Key)
                .Select(v => BuildRawEffective(project, v, serviceId, environmentId))
                .ToDictionary(x => x.Variable.Key);
            foreach (var item in effective.Values.Where(x => !x.Missing && !string.IsNullOrEmpty(x.Value)))
            {
                foreach (var token in ExtractTokens(item.Value).Distinct())
                {
                    if (!project.Variables.Any(v => v.Key == token))
                    {
                        results.Add(Error("INTERPOLATION_VARIABLE_NOT_FOUND", $"Interpolated variable '{token}' is not defined.", item.Variable.Id, serviceId, environmentId));
                        continue;
                    }

                    if (!effective.TryGetValue(token, out var referenced) || referenced.Missing || referenced.Value == null)
                    {
                        results.Add(Error("INTERPOLATION_VALUE_MISSING", $"Interpolated variable '{token}' has no effective value.", item.Variable.Id, serviceId, environmentId));
                    }
                }
            }

            foreach (var cycle in FindInterpolationCycles(effective))
            {
                var variable = project.Variables.FirstOrDefault(v => v.Key == cycle.FirstOrDefault());
                results.Add(Error("INTERPOLATION_CYCLE", "Interpolation cycle detected: " + string.Join(" -> ", cycle), variable?.Id, serviceId, environmentId));
            }
        }

        private static void ValidateRepeatedEnvironmentSecrets(ProjectModel project, List<ValidationResult> results)
        {
            foreach (var variable in project.Variables.Where(v => v.IsSecret))
            {
                var globalValue = project.Values.LastOrDefault(v =>
                    v.VariableId == variable.Id &&
                    v.Scope == ValueScope.Global &&
                    v.EnvironmentId == null &&
                    v.ServiceId == null &&
                    !string.IsNullOrEmpty(v.Value));
                if (globalValue != null)
                {
                    results.Add(Warning(
                        "SECRET_GLOBAL_FOR_ALL_ENVIRONMENTS",
                        "Secret is defined globally and will be reused across all environments.",
                        variable.Id,
                        null,
                        null));
                }

                var environmentValues = project.Values
                    .Where(v =>
                        v.VariableId == variable.Id &&
                        v.EnvironmentId != null &&
                        !string.IsNullOrEmpty(v.Value))
                    .GroupBy(v => new { v.ServiceId, v.Value })
                    .Where(g => g.Select(v => v.EnvironmentId).Distinct().Count() > 1);

                foreach (var group in environmentValues)
                {
                    var environments = group
                        .Select(v => project.Environments.FirstOrDefault(e => e.Id == v.EnvironmentId)?.Name ?? v.EnvironmentId)
                        .Distinct()
                        .OrderBy(x => x)
                        .ToList();
                    results.Add(Warning(
                        "SECRET_REUSED_ACROSS_ENVIRONMENTS",
                        $"Secret has the same environment-specific value in multiple environments: {string.Join(", ", environments)}. Move it to Global if this is intentional.",
                        variable.Id,
                        group.Key.ServiceId,
                        null));
                }
            }
        }

        private static EffectiveValue BuildRawEffective(ProjectModel project, VariableDefinitionModel variable, string serviceId, string environmentId)
        {
            VariableValueModel selected = null;
            foreach (var scope in new[] { ValueScope.Global, ValueScope.Environment, ValueScope.Service, ValueScope.ServiceEnvironment })
            {
                var candidate = project.Values.LastOrDefault(v =>
                    v.VariableId == variable.Id &&
                    v.Scope == scope &&
                    Matches(v, scope, serviceId, environmentId));
                if (candidate != null)
                {
                    selected = candidate;
                }
            }

            return new EffectiveValue
            {
                Variable = variable,
                Value = selected?.Value,
                SourceScope = selected?.Scope
            };
        }

        private static bool Matches(VariableValueModel value, ValueScope scope, string serviceId, string environmentId)
        {
            if (scope == ValueScope.Global) return value.ServiceId == null && value.EnvironmentId == null;
            if (scope == ValueScope.Environment) return value.ServiceId == null && value.EnvironmentId == environmentId;
            if (scope == ValueScope.Service) return value.ServiceId == serviceId && value.EnvironmentId == null;
            return value.ServiceId == serviceId && value.EnvironmentId == environmentId;
        }

        private static IEnumerable<string> ExtractTokens(string value)
        {
            foreach (Match match in TokenRegex.Matches(value ?? string.Empty))
            {
                yield return match.Groups["key"].Value;
            }
        }

        private static IEnumerable<List<string>> FindInterpolationCycles(Dictionary<string, EffectiveValue> effective)
        {
            var graph = effective
                .Where(pair => !pair.Value.Missing && !string.IsNullOrEmpty(pair.Value.Value))
                .ToDictionary(
                    pair => pair.Key,
                    pair => ExtractTokens(pair.Value.Value).Where(effective.ContainsKey).Distinct().ToList());
            var visited = new HashSet<string>();
            var stack = new List<string>();
            var emitted = new HashSet<string>();

            foreach (var key in graph.Keys)
            {
                foreach (var cycle in FindCyclesFrom(key, graph, visited, stack, emitted))
                {
                    yield return cycle;
                }
            }
        }

        private static IEnumerable<List<string>> FindCyclesFrom(string key, Dictionary<string, List<string>> graph, HashSet<string> visited, List<string> stack, HashSet<string> emitted)
        {
            if (stack.Contains(key))
            {
                var cycle = stack.Skip(stack.IndexOf(key)).Concat(new[] { key }).ToList();
                var signature = string.Join("|", cycle.OrderBy(x => x));
                if (emitted.Add(signature))
                {
                    yield return cycle;
                }
                yield break;
            }

            if (!visited.Add(key)) yield break;
            stack.Add(key);
            if (graph.TryGetValue(key, out var next))
            {
                foreach (var child in next)
                {
                    foreach (var cycle in FindCyclesFrom(child, graph, visited, stack, emitted))
                    {
                        yield return cycle;
                    }
                }
            }
            stack.RemoveAt(stack.Count - 1);
        }

        private static void AddDuplicates(List<ValidationResult> results, IEnumerable<string> values, string code, string message)
        {
            foreach (var group in values.Where(v => !string.IsNullOrWhiteSpace(v)).GroupBy(v => v).Where(g => g.Count() > 1))
            {
                results.Add(Error(code, $"{message} '{group.Key}'.", null, null, null));
            }
        }

        private static ValidationResult Error(string code, string message, string variableId, string serviceId, string environmentId)
        {
            return new ValidationResult { Severity = ValidationSeverity.Error, Code = code, Message = message, VariableId = variableId, ServiceId = serviceId, EnvironmentId = environmentId };
        }

        private static ValidationResult Warning(string code, string message, string variableId, string serviceId, string environmentId)
        {
            return new ValidationResult { Severity = ValidationSeverity.Warning, Code = code, Message = message, VariableId = variableId, ServiceId = serviceId, EnvironmentId = environmentId };
        }
    }
}

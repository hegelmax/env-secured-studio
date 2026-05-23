using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using EnvSecured.Core.Models;
using EnvSecured.Core.Services;

namespace EnvSecured.Core.Validation
{
    public sealed class ValidationService
    {
        private static readonly Regex TokenRegex = new Regex(@"\$\{(?<key>[A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled);

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
            ValidateDuplicateValues(project, results);
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
                .Select(v => EffectiveConfigService.BuildRawValue(project, v, serviceId, environmentId))
                .ToDictionary(x => x.Variable.Key);
            foreach (var item in effective.Values.Where(x =>
                !x.Missing &&
                !string.IsNullOrEmpty(x.Value) &&
                HasExplicitContract(project, serviceId, x.Variable.Id)))
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
                        continue;
                    }

                    var referencedVariable = project.Variables.First(v => v.Key == token);
                    if (referencedVariable.Id != item.Variable.Id &&
                        IsServiceScoped(referenced) &&
                        !HasExplicitContract(project, serviceId, referencedVariable.Id))
                    {
                        var service = project.Services.FirstOrDefault(s => s.Id == serviceId);
                        var message = $"Interpolated variable '{token}' has no explicit contract for this service.";
                        if (service?.AllowSharedVariablesWithoutContract == false)
                        {
                            results.Add(Error("INTERPOLATION_CONTRACT_MISSING", message, item.Variable.Id, serviceId, environmentId));
                        }
                        else
                        {
                            results.Add(Warning("INTERPOLATION_CONTRACT_MISSING", message, item.Variable.Id, serviceId, environmentId));
                        }
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
                    HasStoredValue(v));
                if (globalValue != null && !variable.AllowSharedSecret)
                {
                    results.Add(Warning(
                        "SECRET_GLOBAL_FOR_ALL_ENVIRONMENTS",
                        "Secret is defined globally and will be reused across all environments.",
                        variable.Id,
                        null,
                        null));
                }

                if (variable.AllowSharedSecret) continue;

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

        private static void ValidateDuplicateValues(ProjectModel project, List<ValidationResult> results)
        {
            foreach (var group in project.Values
                .GroupBy(v => new { v.VariableId, v.Scope, v.ServiceId, v.EnvironmentId })
                .Where(g => g.Count() > 1))
            {
                results.Add(Warning(
                    "DUPLICATE_SCOPED_VALUE",
                    $"Multiple values exist for the same variable and scope. The last one wins; remove stale duplicates.",
                    group.Key.VariableId,
                    group.Key.ServiceId,
                    group.Key.EnvironmentId));
            }
        }

        private static bool HasStoredValue(VariableValueModel value)
        {
            return !string.IsNullOrEmpty(value.Value) || value.EncryptedValue != null;
        }

        private static bool HasExplicitContract(ProjectModel project, string serviceId, string variableId)
        {
            return project.Contracts.Any(c =>
                c.ServiceId == serviceId &&
                c.VariableId == variableId &&
                !c.Excluded);
        }

        private static bool IsServiceScoped(EffectiveValue value)
        {
            return value.SourceScope == ValueScope.Service || value.SourceScope == ValueScope.ServiceEnvironment;
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
            var stackIndex = new Dictionary<string, int>();
            var emitted = new HashSet<string>();

            foreach (var key in graph.Keys)
            {
                foreach (var cycle in FindCyclesFrom(key, graph, visited, stack, stackIndex, emitted))
                {
                    yield return cycle;
                }
            }
        }

        private static IEnumerable<List<string>> FindCyclesFrom(string key, Dictionary<string, List<string>> graph, HashSet<string> visited, List<string> stack, Dictionary<string, int> stackIndex, HashSet<string> emitted)
        {
            if (stackIndex.TryGetValue(key, out var cycleStart))
            {
                var cycle = stack.Skip(cycleStart).Concat(new[] { key }).ToList();
                var signature = string.Join("|", cycle.OrderBy(x => x));
                if (emitted.Add(signature))
                {
                    yield return cycle;
                }
                yield break;
            }

            if (!visited.Add(key)) yield break;
            stackIndex[key] = stack.Count;
            stack.Add(key);
            if (graph.TryGetValue(key, out var next))
            {
                foreach (var child in next)
                {
                    foreach (var cycle in FindCyclesFrom(child, graph, visited, stack, stackIndex, emitted))
                    {
                        yield return cycle;
                    }
                }
            }
            stack.RemoveAt(stack.Count - 1);
            stackIndex.Remove(key);
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

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using EnvSecured.Core.Models;

namespace EnvSecured.Core.Services
{
    public sealed class EffectiveValue
    {
        public VariableDefinitionModel Variable { get; set; }
        public string Value { get; set; }
        public ValueScope? SourceScope { get; set; }
        public string SourceServiceId { get; set; }
        public string SourceEnvironmentId { get; set; }
        public bool Missing => SourceScope == null;
    }

    public sealed class EffectiveConfigService
    {
        private static readonly Regex TokenRegex = new Regex(@"\$\{(?<key>[A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled);

        public IReadOnlyList<EffectiveValue> Build(ProjectModel project, string serviceId, string environmentId)
        {
            var values = project.Variables
                .Where(v => v.IsActive)
                .OrderBy(v => v.SortOrder)
                .ThenBy(v => v.Key)
                .Select(v => BuildRawValue(project, v, serviceId, environmentId))
                .ToList();
            var valuesByKey = values
                .Where(v => !v.Missing)
                .ToDictionary(v => v.Variable.Key, v => v.Value);

            foreach (var value in values.Where(v => !v.Missing))
            {
                value.Value = Interpolate(value.Value, valuesByKey);
                valuesByKey[value.Variable.Key] = value.Value;
            }

            return values;
        }

        public string Interpolate(string value, IDictionary<string, string> valuesByKey, ISet<string> stack = null)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            stack = stack ?? new HashSet<string>();
            return TokenRegex.Replace(value, match =>
            {
                var key = match.Groups["key"].Value;
                if (!valuesByKey.ContainsKey(key) || stack.Contains(key))
                {
                    return match.Value;
                }

                stack.Add(key);
                var resolved = Interpolate(valuesByKey[key], valuesByKey, stack);
                stack.Remove(key);
                return resolved;
            });
        }

        public static EffectiveValue BuildRawValue(ProjectModel project, VariableDefinitionModel variable, string serviceId, string environmentId)
        {
            VariableValueModel selected = null;
            foreach (var candidate in BuildValuePrecedence(project, variable.Id, serviceId, environmentId))
            {
                if (candidate != null)
                {
                    selected = candidate;
                }
            }

            return new EffectiveValue
            {
                Variable = variable,
                Value = selected?.Value,
                SourceScope = selected?.Scope,
                SourceServiceId = selected?.ServiceId,
                SourceEnvironmentId = selected?.EnvironmentId
            };
        }

        private static IEnumerable<VariableValueModel> BuildValuePrecedence(ProjectModel project, string variableId, string serviceId, string environmentId)
        {
            yield return FindLastValue(project, variableId, ValueScope.Global, null, null);
            yield return FindLastValue(project, variableId, ValueScope.Environment, null, environmentId);

            if (!string.IsNullOrWhiteSpace(serviceId))
            {
                foreach (var service in project.Services
                    .Where(s => s.Id != serviceId)
                    .OrderBy(s => s.SortOrder)
                    .ThenBy(s => s.Name))
                {
                    yield return FindLastValue(project, variableId, ValueScope.Service, service.Id, null);
                    yield return FindLastValue(project, variableId, ValueScope.ServiceEnvironment, service.Id, environmentId);
                }

                yield return FindLastValue(project, variableId, ValueScope.Service, serviceId, null);
                yield return FindLastValue(project, variableId, ValueScope.ServiceEnvironment, serviceId, environmentId);
            }
        }

        private static VariableValueModel FindLastValue(ProjectModel project, string variableId, ValueScope scope, string serviceId, string environmentId)
        {
            return project.Values.LastOrDefault(v =>
                v.VariableId == variableId &&
                v.Scope == scope &&
                v.ServiceId == serviceId &&
                v.EnvironmentId == environmentId);
        }
    }
}

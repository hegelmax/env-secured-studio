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
        public bool Missing => SourceScope == null;
    }

    public sealed class EffectiveConfigService
    {
        private static readonly Regex TokenRegex = new Regex(@"\$\{(?<key>[A-Za-z_][A-Za-z0-9_]*)\}|\{(?<key>[A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled);

        public IReadOnlyList<EffectiveValue> Build(ProjectModel project, string serviceId, string environmentId)
        {
            var values = project.Variables
                .Where(v => v.IsActive)
                .OrderBy(v => v.SortOrder)
                .ThenBy(v => v.Key)
                .Select(v => BuildOne(project, v, serviceId, environmentId))
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

        private static EffectiveValue BuildOne(ProjectModel project, VariableDefinitionModel variable, string serviceId, string environmentId)
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
    }
}

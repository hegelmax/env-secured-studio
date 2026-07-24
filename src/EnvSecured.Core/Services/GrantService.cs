using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace EnvSecured.Core.Services
{
    public sealed class GrantRule
    {
        public IReadOnlyList<string> Services { get; set; }
        public IReadOnlyList<string> Environments { get; set; }
        public IReadOnlyList<string> Variables { get; set; }
        public IReadOnlyList<string> Permissions { get; set; }
    }

    public sealed class GrantService
    {
        private static readonly Regex IdRegex = new Regex(@"^[a-z0-9_-]+$", RegexOptions.Compiled);
        private static readonly Regex VariablePatternRegex = new Regex(@"^[A-Z0-9_*]+$", RegexOptions.Compiled);
        private static readonly HashSet<string> AllowedPermissions = new HashSet<string>(StringComparer.Ordinal)
        {
            "Read",
            "BatchExport"
        };

        public GrantRule Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new FormatException("Grant cannot be empty.");
            var parts = text.Split('.');
            if (parts.Length != 4) throw new FormatException("Grant must contain exactly four dot-separated selectors.");

            return new GrantRule
            {
                Services = ParseSelector(parts[0], IsId, "service"),
                Environments = ParseSelector(parts[1], IsId, "environment"),
                Variables = ParseSelector(parts[2], IsVariablePattern, "variable"),
                Permissions = ParseSelector(parts[3], AllowedPermissions.Contains, "permission")
            };
        }

        public string Normalize(string text)
        {
            var rule = Parse(text);
            return string.Join(".",
                NormalizeSelector(rule.Services),
                NormalizeSelector(rule.Environments),
                NormalizeSelector(rule.Variables.Select(NormalizeStars)),
                NormalizeSelector(rule.Permissions.OrderBy(PermissionOrder)));
        }

        public bool Allows(IEnumerable<string> grants, string serviceId, string environmentId, string variableKey, string permission)
        {
            if (!AllowedPermissions.Contains(permission)) return false;
            foreach (var text in grants ?? Enumerable.Empty<string>())
            {
                var rule = Parse(text);
                if (MatchesExactSelector(rule.Services, serviceId) &&
                    MatchesExactSelector(rule.Environments, environmentId) &&
                    rule.Variables.Any(pattern => GlobMatches(pattern, variableKey)) &&
                    rule.Permissions.Contains(permission, StringComparer.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static IReadOnlyList<string> ParseSelector(string text, Func<string, bool> validate, string name)
        {
            var values = text.Split('|');
            if (values.Length == 0 || values.Any(string.IsNullOrWhiteSpace))
                throw new FormatException("Grant " + name + " selector contains an empty value.");
            foreach (var value in values)
            {
                if (!validate(value)) throw new FormatException("Invalid " + name + " selector value: " + value);
            }
            return values;
        }

        private static bool IsId(string value) => value == "*" || IdRegex.IsMatch(value);
        private static bool IsVariablePattern(string value) => value == "*" || (VariablePatternRegex.IsMatch(value) && value.Any(c => c != '*'));

        private static string NormalizeSelector(IEnumerable<string> values)
        {
            return string.Join("|", values.Distinct(StringComparer.Ordinal));
        }

        private static int PermissionOrder(string value) => value == "Read" ? 0 : 1;

        private static string NormalizeStars(string value)
        {
            while (value.Contains("**")) value = value.Replace("**", "*");
            return value;
        }

        private static bool MatchesExactSelector(IEnumerable<string> selector, string value)
        {
            return selector.Any(x => x == "*" || string.Equals(x, value ?? string.Empty, StringComparison.Ordinal));
        }

        private static bool GlobMatches(string pattern, string value)
        {
            if (value == null) return false;
            var regex = "^" + Regex.Escape(NormalizeStars(pattern)).Replace("\\*", ".*") + "$";
            return Regex.IsMatch(value, regex, RegexOptions.CultureInvariant);
        }
    }
}

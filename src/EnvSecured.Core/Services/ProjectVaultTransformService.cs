using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using EnvSecured.Core.Models;

namespace EnvSecured.Core.Services
{
    public sealed class ProjectVaultTransformService
    {
        private static readonly Regex TokenRegex = new Regex(@"\{\{(?<key>[A-Za-z_][A-Za-z0-9_]*)\}\}", RegexOptions.Compiled);

        public ProjectModel Split(ProjectModel source, ProjectSplitOptions options)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (options == null) throw new ArgumentNullException(nameof(options));

            ProjectService.EnsureProjectCollections(source);
            var variableIds = SelectVariableIds(source, options);
            if (options.IncludeReferences)
            {
                AddReferencedVariables(source, variableIds);
            }

            if (variableIds.Count == 0)
            {
                throw new InvalidOperationException("Split selection does not match any variables.");
            }

            var result = Clone(source);
            result.ProjectName = string.IsNullOrWhiteSpace(options.ProjectName)
                ? source.ProjectName + "-split"
                : options.ProjectName.Trim();
            result.ProjectId = string.IsNullOrWhiteSpace(options.ProjectId)
                ? Slug(result.ProjectName)
                : options.ProjectId.Trim();

            result.Variables = result.Variables.Where(v => variableIds.Contains(v.Id)).ToList();
            result.Contracts = result.Contracts.Where(c => variableIds.Contains(c.VariableId)).ToList();
            result.Values = result.Values.Where(v => variableIds.Contains(v.VariableId)).ToList();
            result.Crypto = new VaultCryptoMetadata();
            if (result.Settings != null)
            {
                result.Settings.CliExportPasswordRequiredEncrypted = null;
            }
            return result;
        }

        public ProjectMergeResult Merge(ProjectModel target, IEnumerable<ProjectModel> sources, bool overwriteValues = true)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (sources == null) throw new ArgumentNullException(nameof(sources));

            ProjectService.EnsureProjectCollections(target);
            var result = new ProjectMergeResult();

            foreach (var rawSource in sources)
            {
                if (rawSource == null) continue;
                var source = Clone(rawSource);
                ProjectService.EnsureProjectCollections(source);

                var serviceMap = MergeServices(target, source, result);
                var environmentMap = MergeEnvironments(target, source, result);
                var variableMap = MergeVariables(target, source, result);
                MergeContracts(target, source, serviceMap, variableMap, result);
                MergeValues(target, source, serviceMap, environmentMap, variableMap, overwriteValues, result);
                MergeOutputTargets(target, source, serviceMap, environmentMap);
            }

            return result;
        }

        private static HashSet<string> SelectVariableIds(ProjectModel project, ProjectSplitOptions options)
        {
            var selected = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in options.Keys ?? Enumerable.Empty<string>())
            {
                var variable = project.Variables.FirstOrDefault(v =>
                    string.Equals(v.Key, key, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(v.Id, key, StringComparison.OrdinalIgnoreCase));
                if (variable == null)
                {
                    throw new InvalidOperationException("Variable not found: " + key);
                }
                selected.Add(variable.Id);
            }

            foreach (var serviceId in options.OwnerServiceIds ?? Enumerable.Empty<string>())
            {
                var service = FindService(project, serviceId);
                selected.UnionWith(project.Variables
                    .Where(v => string.Equals(v.OwnerServiceId, service.Id, StringComparison.Ordinal))
                    .Select(v => v.Id));
            }

            foreach (var serviceId in options.ScopeServiceIds ?? Enumerable.Empty<string>())
            {
                var service = FindService(project, serviceId);
                selected.UnionWith(project.Variables
                    .Where(v => ProjectService.IsVariableVisibleToService(project, v, service.Id))
                    .Select(v => v.Id));
            }

            if (options.All)
            {
                selected.UnionWith(project.Variables.Select(v => v.Id));
            }

            return selected;
        }

        private static void AddReferencedVariables(ProjectModel project, HashSet<string> variableIds)
        {
            var byKey = project.Variables.ToDictionary(v => v.Key ?? string.Empty, v => v, StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>(variableIds);
            while (queue.Count > 0)
            {
                var variableId = queue.Dequeue();
                foreach (var value in project.Values.Where(v => v.VariableId == variableId && !string.IsNullOrEmpty(v.Value)))
                {
                    foreach (Match match in TokenRegex.Matches(value.Value))
                    {
                        var key = match.Groups["key"].Value;
                        if (!byKey.TryGetValue(key, out var referenced)) continue;
                        if (variableIds.Add(referenced.Id))
                        {
                            queue.Enqueue(referenced.Id);
                        }
                    }
                }
            }
        }

        private static Dictionary<string, string> MergeServices(ProjectModel target, ProjectModel source, ProjectMergeResult result)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var service in source.Services)
            {
                var existing = target.Services.FirstOrDefault(s =>
                    string.Equals(s.Id, service.Id, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(s.Name, service.Name, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    var clone = Clone(service);
                    clone.Id = UniqueId(target.Services.Select(s => s.Id), clone.Id);
                    target.Services.Add(clone);
                    existing = clone;
                    result.ServicesAdded++;
                }
                map[service.Id] = existing.Id;
            }
            return map;
        }

        private static Dictionary<string, string> MergeEnvironments(ProjectModel target, ProjectModel source, ProjectMergeResult result)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var environment in source.Environments)
            {
                var existing = target.Environments.FirstOrDefault(e =>
                    string.Equals(e.Id, environment.Id, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(e.Name, environment.Name, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    var clone = Clone(environment);
                    clone.Id = UniqueId(target.Environments.Select(e => e.Id), clone.Id);
                    target.Environments.Add(clone);
                    existing = clone;
                    result.EnvironmentsAdded++;
                }
                map[environment.Id] = existing.Id;
            }
            return map;
        }

        private static Dictionary<string, string> MergeVariables(ProjectModel target, ProjectModel source, ProjectMergeResult result)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var variable in source.Variables)
            {
                var existing = target.Variables.FirstOrDefault(v => string.Equals(v.Key, variable.Key, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    var clone = Clone(variable);
                    clone.Id = UniqueId(target.Variables.Select(v => v.Id), clone.Id);
                    target.Variables.Add(clone);
                    existing = clone;
                    result.VariablesAdded++;
                }
                else
                {
                    result.VariablesMatched++;
                }
                map[variable.Id] = existing.Id;
            }
            return map;
        }

        private static void MergeContracts(ProjectModel target, ProjectModel source, Dictionary<string, string> serviceMap, Dictionary<string, string> variableMap, ProjectMergeResult result)
        {
            foreach (var contract in source.Contracts)
            {
                if (!variableMap.TryGetValue(contract.VariableId, out var variableId)) continue;
                if (string.IsNullOrWhiteSpace(contract.ServiceId) || !serviceMap.TryGetValue(contract.ServiceId, out var serviceId)) continue;

                target.Contracts.RemoveAll(c => c.VariableId == variableId && c.ServiceId == serviceId);
                var clone = Clone(contract);
                clone.Id = ProjectService.NewId();
                clone.VariableId = variableId;
                clone.ServiceId = serviceId;
                target.Contracts.Add(clone);
                result.ContractsMerged++;
            }
        }

        private static void MergeValues(ProjectModel target, ProjectModel source, Dictionary<string, string> serviceMap, Dictionary<string, string> environmentMap, Dictionary<string, string> variableMap, bool overwriteValues, ProjectMergeResult result)
        {
            foreach (var value in source.Values)
            {
                if (!variableMap.TryGetValue(value.VariableId, out var variableId)) continue;
                var serviceId = MapNullable(serviceMap, value.ServiceId);
                var environmentId = MapNullable(environmentMap, value.EnvironmentId);

                var exists = target.Values.Any(v =>
                    v.VariableId == variableId &&
                    v.Scope == value.Scope &&
                    SameNullable(v.ServiceId, serviceId) &&
                    SameNullable(v.EnvironmentId, environmentId));
                if (exists && !overwriteValues)
                {
                    continue;
                }

                if (exists)
                {
                    target.Values.RemoveAll(v =>
                        v.VariableId == variableId &&
                        v.Scope == value.Scope &&
                        SameNullable(v.ServiceId, serviceId) &&
                        SameNullable(v.EnvironmentId, environmentId));
                }

                var clone = Clone(value);
                clone.Id = ProjectService.NewId();
                clone.VariableId = variableId;
                clone.ServiceId = serviceId;
                clone.EnvironmentId = environmentId;
                target.Values.Add(clone);
                result.ValuesMerged++;
            }
        }

        private static void MergeOutputTargets(ProjectModel target, ProjectModel source, Dictionary<string, string> serviceMap, Dictionary<string, string> environmentMap)
        {
            if (target.Settings == null || source.Settings?.OutputTargets == null) return;
            target.Settings.OutputTargets = target.Settings.OutputTargets ?? new List<OutputTargetSetting>();
            foreach (var sourceTarget in source.Settings.OutputTargets)
            {
                var serviceId = MapNullable(serviceMap, sourceTarget.ServiceId);
                var environmentId = MapNullable(environmentMap, sourceTarget.EnvironmentId);
                var targetSetting = target.Settings.OutputTargets.FirstOrDefault(t => SameNullable(t.ServiceId, serviceId) && SameNullable(t.EnvironmentId, environmentId));
                if (targetSetting == null)
                {
                    target.Settings.OutputTargets.Add(new OutputTargetSetting { ServiceId = serviceId, EnvironmentId = environmentId, Enabled = sourceTarget.Enabled });
                }
                else
                {
                    targetSetting.Enabled = sourceTarget.Enabled;
                }
            }
        }

        private static ServiceModel FindService(ProjectModel project, string value)
        {
            var service = project.Services.FirstOrDefault(s =>
                string.Equals(s.Id, value, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.Name, value, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.DisplayName, value, StringComparison.OrdinalIgnoreCase));
            if (service == null) throw new InvalidOperationException("Service not found: " + value);
            return service;
        }

        private static string MapNullable(Dictionary<string, string> map, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return map.TryGetValue(value, out var mapped) ? mapped : value;
        }

        private static bool SameNullable(string a, string b)
        {
            return string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.Ordinal);
        }

        private static string UniqueId(IEnumerable<string> existingIds, string preferred)
        {
            var existing = new HashSet<string>(existingIds.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);
            var baseId = string.IsNullOrWhiteSpace(preferred) ? ProjectService.NewId() : preferred;
            var candidate = baseId;
            var index = 2;
            while (existing.Contains(candidate))
            {
                candidate = baseId + "-" + index++;
            }
            return candidate;
        }

        private static string Slug(string value)
        {
            var slug = new string((value ?? string.Empty).Trim().ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
            return string.IsNullOrWhiteSpace(slug) ? "split-project" : slug;
        }

        private static T Clone<T>(T value)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            return serializer.Deserialize<T>(serializer.Serialize(value));
        }
    }

    public sealed class ProjectSplitOptions
    {
        public bool All { get; set; }
        public List<string> Keys { get; set; } = new List<string>();
        public List<string> OwnerServiceIds { get; set; } = new List<string>();
        public List<string> ScopeServiceIds { get; set; } = new List<string>();
        public bool IncludeReferences { get; set; } = true;
        public string ProjectName { get; set; }
        public string ProjectId { get; set; }
    }

    public sealed class ProjectMergeResult
    {
        public int ServicesAdded { get; set; }
        public int EnvironmentsAdded { get; set; }
        public int VariablesAdded { get; set; }
        public int VariablesMatched { get; set; }
        public int ContractsMerged { get; set; }
        public int ValuesMerged { get; set; }
    }
}

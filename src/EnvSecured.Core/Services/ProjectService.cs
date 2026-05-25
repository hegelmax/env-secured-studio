using System;
using System.Collections.Generic;
using System.Linq;
using EnvSecured.Core.Models;

namespace EnvSecured.Core.Services
{
    public sealed class ProjectService
    {
        public const string GlobalServiceId = "global";

        public ProjectModel CreateProject(string projectName, string projectId)
        {
            var project = new ProjectModel
            {
                ProjectName = projectName,
                ProjectId = projectId,
                Description = string.Empty
            };
            EnsureProjectCollections(project);
            return project;
        }

        public static string NewId()
        {
            return Guid.NewGuid().ToString("N");
        }

        public static bool IsVariableUsedByService(ProjectModel project, string variableId, string serviceId)
        {
            var contract = project.Contracts.FirstOrDefault(c => c.VariableId == variableId && c.ServiceId == serviceId);
            return contract != null && !contract.Excluded;
        }

        public static bool IsVariableSharedFromService(ProjectModel project, string variableId, string sourceServiceId)
        {
            if (string.IsNullOrWhiteSpace(sourceServiceId)) return true;
            var contract = project.Contracts.FirstOrDefault(c => c.VariableId == variableId && c.ServiceId == sourceServiceId);
            return contract?.ShareWithOtherServices != false;
        }

        public static bool IsVariableVisibleToService(ProjectModel project, VariableDefinitionModel variable, string serviceId)
        {
            if (variable == null) return false;
            if (string.Equals(variable.OwnerServiceId, serviceId, StringComparison.Ordinal)) return true;
            if (string.IsNullOrWhiteSpace(serviceId)) return false;
            var contract = project.Contracts.FirstOrDefault(c => c.VariableId == variable.Id && c.ServiceId == serviceId);
            return contract?.VisibleToService == true;
        }

        public static bool CanOverrideVariableForService(ProjectModel project, VariableDefinitionModel variable, string serviceId)
        {
            if (variable == null) return false;
            if (string.IsNullOrWhiteSpace(serviceId)) return false;
            if (string.Equals(variable.OwnerServiceId, serviceId, StringComparison.Ordinal)) return true;
            var contract = project.Contracts.FirstOrDefault(c => c.VariableId == variable.Id && c.ServiceId == serviceId);
            return contract?.VisibleToService == true && contract.AllowOverride;
        }

        public static bool HasGlobalValue(ProjectModel project, string variableId)
        {
            return project.Values.Any(v =>
                v.VariableId == variableId &&
                v.Scope == ValueScope.Global &&
                v.ServiceId == null &&
                v.EnvironmentId == null);
        }

        public static IReadOnlyList<VariableDefinitionModel> GetVariablesUsedByService(ProjectModel project, string serviceId)
        {
            return project.Variables
                .Where(v => v.IsActive && IsVariableUsedByService(project, v.Id, serviceId))
                .OrderBy(v => v.SortOrder)
                .ThenBy(v => v.Key)
                .ToList();
        }

        public static string EnsureGlobalService(ProjectModel project)
        {
            if (project == null) return GlobalServiceId;
            EnsureProjectCollections(project);
            var globalService = project.Services.FirstOrDefault(s => string.Equals(s.Id, GlobalServiceId, StringComparison.OrdinalIgnoreCase));
            return globalService?.Id;
        }

        public static void EnsureProjectCollections(ProjectModel project)
        {
            if (project == null) return;
            project.Services = project.Services ?? new List<ServiceModel>();
            project.Variables = project.Variables ?? new List<VariableDefinitionModel>();
            project.Contracts = project.Contracts ?? new List<VariableContractModel>();
            project.Values = project.Values ?? new List<VariableValueModel>();
        }

        public static VariableContractModel EnsureServiceScopeContract(ProjectModel project, string variableId, string serviceId, bool export, bool visible, bool allowOverride)
        {
            var contract = project.Contracts.FirstOrDefault(c => c.VariableId == variableId && c.ServiceId == serviceId);
            if (contract == null)
            {
                contract = new VariableContractModel
                {
                    Id = NewId(),
                    VariableId = variableId,
                    ServiceId = serviceId,
                    SortOrder = project.Contracts.Count * 10
                };
                project.Contracts.Add(contract);
            }

            contract.Excluded = !export;
            contract.Required = export;
            contract.VisibleToService = visible;
            contract.AllowOverride = visible && allowOverride;
            contract.ShareWithOtherServices = visible;
            return contract;
        }

        public static int ReplaceInterpolationReferences(ProjectModel project, string oldKey, string newKey)
        {
            if (project == null || string.IsNullOrWhiteSpace(oldKey) || string.IsNullOrWhiteSpace(newKey)) return 0;
            var oldToken = "{{" + oldKey.Trim().ToUpperInvariant() + "}}";
            var newToken = "{{" + newKey.Trim().ToUpperInvariant() + "}}";
            if (string.Equals(oldToken, newToken, StringComparison.Ordinal)) return 0;

            var changed = 0;
            foreach (var value in project.Values.Where(v => !string.IsNullOrEmpty(v.Value) && v.Value.Contains(oldToken)))
            {
                value.Value = value.Value.Replace(oldToken, newToken);
                changed++;
            }
            return changed;
        }

        public static int MoveOwnerValues(ProjectModel project, string variableId, string oldOwnerServiceId, string newOwnerServiceId)
        {
            if (project == null || string.IsNullOrWhiteSpace(variableId)) return 0;
            if (string.Equals(oldOwnerServiceId, newOwnerServiceId, StringComparison.Ordinal)) return 0;

            var moved = 0;
            foreach (var value in project.Values
                .Where(v => v.VariableId == variableId && IsOwnedValue(v, oldOwnerServiceId))
                .ToList())
            {
                var targetScope = OwnerMoveTargetScope(value.Scope, oldOwnerServiceId, newOwnerServiceId);
                var targetServiceId = string.IsNullOrWhiteSpace(newOwnerServiceId) ? null : newOwnerServiceId;
                var targetEnvironmentId = value.Scope == ValueScope.ServiceEnvironment || value.Scope == ValueScope.Environment
                    ? value.EnvironmentId
                    : null;

                var targetExists = project.Values.Any(v =>
                    v != value &&
                    v.VariableId == variableId &&
                    v.Scope == targetScope &&
                    v.ServiceId == targetServiceId &&
                    v.EnvironmentId == targetEnvironmentId);
                if (targetExists)
                {
                    continue;
                }

                value.Scope = targetScope;
                value.ServiceId = targetServiceId;
                value.EnvironmentId = targetEnvironmentId;
                moved++;
            }

            return moved;
        }

        private static ValueScope OwnerMoveTargetScope(ValueScope sourceScope, string oldOwnerServiceId, string newOwnerServiceId)
        {
            var oldIsGlobal = string.IsNullOrWhiteSpace(oldOwnerServiceId);
            var newIsGlobal = string.IsNullOrWhiteSpace(newOwnerServiceId);

            if (oldIsGlobal && !newIsGlobal)
            {
                return sourceScope == ValueScope.Environment ? ValueScope.ServiceEnvironment : ValueScope.Service;
            }

            if (!oldIsGlobal && newIsGlobal)
            {
                return sourceScope == ValueScope.ServiceEnvironment ? ValueScope.Environment : ValueScope.Global;
            }

            return sourceScope;
        }

        private static bool IsOwnedValue(VariableValueModel value, string ownerServiceId)
        {
            if (string.IsNullOrWhiteSpace(ownerServiceId))
            {
                return value.Scope == ValueScope.Global || value.Scope == ValueScope.Environment;
            }

            return (value.Scope == ValueScope.Service || value.Scope == ValueScope.ServiceEnvironment) &&
                string.Equals(value.ServiceId, ownerServiceId, StringComparison.Ordinal);
        }
    }
}

using System;
using System.Linq;
using EnvSecured.Core.Models;

namespace EnvSecured.Core.Services
{
    public sealed class ProjectService
    {
        public ProjectModel CreateProject(string projectName, string projectId)
        {
            return new ProjectModel
            {
                ProjectName = projectName,
                ProjectId = projectId,
                Description = string.Empty
            };
        }

        public static string NewId()
        {
            return Guid.NewGuid().ToString("N");
        }

        public static bool IsVariableUsedByService(ProjectModel project, string variableId, string serviceId)
        {
            var contract = project.Contracts.FirstOrDefault(c => c.VariableId == variableId && c.ServiceId == serviceId);
            if (contract != null) return !contract.Excluded;
            return HasGlobalValue(project, variableId);
        }

        public static bool HasGlobalValue(ProjectModel project, string variableId)
        {
            return project.Values.Any(v =>
                v.VariableId == variableId &&
                v.Scope == ValueScope.Global &&
                v.ServiceId == null &&
                v.EnvironmentId == null);
        }
    }
}

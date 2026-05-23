using System;
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
    }
}

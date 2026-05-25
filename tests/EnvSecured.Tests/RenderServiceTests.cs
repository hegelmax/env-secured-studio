using System;
using System.IO;
using EnvSecured.Core.Models;
using EnvSecured.Core.Rendering;
using Xunit;

namespace EnvSecured.Tests
{
    public sealed class RenderServiceTests
    {
        [Fact]
        public void ExportExamples_AllowsSingleEmptyOutputFolderAtAppsRoot()
        {
            var directory = CreateTempDirectory();
            try
            {
                var project = new ProjectModel();
                project.Services.Add(new ServiceModel { Id = "project", Name = "project", OutputFolder = "", IsActive = true });

                new RenderService().ExportExamples(project, directory);

                Assert.True(File.Exists(Path.Combine(directory, "apps", ".env.example")));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void ExportExamples_RejectsDuplicateEmptyOutputFolders()
        {
            var directory = CreateTempDirectory();
            var project = new ProjectModel();
            try
            {
                project.Services.Add(new ServiceModel { Id = "project", Name = "project", OutputFolder = "", IsActive = true });
                project.Services.Add(new ServiceModel { Id = "backend", Name = "backend", OutputFolder = " ", IsActive = true });

                Assert.Throws<InvalidOperationException>(() => new RenderService().ExportExamples(project, directory));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static string CreateTempDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "envsecured-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}

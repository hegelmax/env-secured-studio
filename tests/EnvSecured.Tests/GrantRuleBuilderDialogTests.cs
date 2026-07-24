using System;
using System.Reflection;
using System.Threading;
using EnvSecured.Core.Models;
using Xunit;

namespace EnvSecured.Tests
{
    public sealed class GrantRuleBuilderDialogTests
    {
        [Fact]
        public void ExistingRuleCanInitializeBeforeWindowHandleExists()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var project = new ProjectModel();
                    project.Services.Add(new ServiceModel { Id = "database", Name = "database", IsActive = true });
                    project.Environments.Add(new EnvironmentModel { Id = "dev", Name = "dev", IsActive = true });
                    project.Variables.Add(new VariableDefinitionModel { Id = "url", Key = "DATABASE_URL", IsActive = true });
                    var type = typeof(EnvSecured.WinForms.Forms.MainForm).Assembly.GetType("EnvSecured.WinForms.Forms.GrantRuleBuilderDialog", true);
                    using (var dialog = (IDisposable)Activator.CreateInstance(type,
                        BindingFlags.Instance | BindingFlags.NonPublic, null,
                        new object[] { project, "database.dev.DATABASE_URL.Read|BatchExport", null, null }, null))
                    {
                    }
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Dialog initialization timed out.");
            Assert.Null(failure);
        }
    }
}

using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using EnvSecured.Core.Models;
using Xunit;

namespace EnvSecured.Tests
{
    public sealed class TokenEditorDialogTests
    {
        [Fact]
        public void SelectingServiceTypeGeneratesIdNameAndDefaultGrant()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var project = new ProjectModel();
                    project.Services.Add(new ServiceModel { Id = "database", Name = "database", IsActive = true });
                    var type = typeof(EnvSecured.WinForms.Forms.MainForm).Assembly.GetType("EnvSecured.WinForms.Forms.TokenEditorDialog", true);
                    using (var dialog = (IDisposable)Activator.CreateInstance(type,
                        BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { project, null }, null))
                    {
                        var typeBox = (ComboBox)type.GetField("typeBox", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dialog);
                        var idBox = (TextBox)type.GetField("idBox", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dialog);
                        var nameBox = (TextBox)type.GetField("nameBox", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dialog);
                        var grantsList = (ListBox)type.GetField("grantsList", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dialog);
                        typeBox.SelectedItem = "Service";

                        Assert.Equal("service-database", idBox.Text);
                        Assert.Equal("database", nameBox.Text);
                        Assert.Single(grantsList.Items);
                        Assert.Equal("database.*.*.Read|BatchExport", grantsList.Items[0]);
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

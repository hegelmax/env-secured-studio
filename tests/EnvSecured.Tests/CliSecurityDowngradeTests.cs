using System;
using System.IO;
using System.Reflection;
using EnvSecured.Core.Models;
using EnvSecured.Core.Services;
using Xunit;

namespace EnvSecured.Tests
{
    public sealed class CliSecurityDowngradeTests
    {
        [Fact]
        public void Settings_RejectsEncryptionDowngradeWithoutExplicitConfirmation()
        {
            var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var path = Path.Combine(directory, "project.envs");
            try
            {
                Directory.CreateDirectory(directory);
                new VaultFileService().Save(new ProjectModel
                {
                    Settings = new ProjectSettings { EncryptionMode = "AllValues", EncryptAllValues = true }
                }, path);

                var code = RunCli("settings", "--file", path, "--encryption", "open");
                var project = new VaultFileService().Load(path);

                Assert.Equal(1, code);
                Assert.Equal("AllValues", project.Settings.EncryptionMode);
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void Settings_AllowsEncryptionDowngradeWithExplicitConfirmation()
        {
            var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var path = Path.Combine(directory, "project.envs");
            try
            {
                Directory.CreateDirectory(directory);
                new VaultFileService().Save(new ProjectModel
                {
                    Settings = new ProjectSettings { EncryptionMode = "AllValues", EncryptAllValues = true }
                }, path);

                var code = RunCli("settings", "--file", path, "--encryption", "open", "--allow-security-downgrade", "true");
                var project = new VaultFileService().Load(path);

                Assert.Equal(0, code);
                Assert.Equal("Open", project.Settings.EncryptionMode);
                Assert.False(project.Settings.EncryptAllValues);
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        private static int RunCli(params string[] args)
        {
            var type = typeof(EnvSecured.WinForms.Forms.MainForm).Assembly.GetType("EnvSecured.WinForms.Cli.CliRunner");
            Assert.NotNull(type);
            var method = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(method);

            var originalOut = Console.Out;
            var originalError = Console.Error;
            try
            {
                Console.SetOut(TextWriter.Null);
                Console.SetError(TextWriter.Null);
                return (int)method.Invoke(null, new object[] { args });
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }
        }
    }
}

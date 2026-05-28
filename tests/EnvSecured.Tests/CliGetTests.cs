using System;
using System.IO;
using System.Reflection;
using EnvSecured.Core.Models;
using EnvSecured.Core.Services;
using Xunit;

namespace EnvSecured.Tests
{
    public sealed class CliGetTests
    {
        [Fact]
        public void Get_ReturnsCalculatedValueByDefault()
        {
            var result = RunWithProject(project =>
            {
                AddValue(project, AddVariable(project, "host", "HOST").Id, ValueScope.Global, null, null, "example.test");
                AddValue(project, AddVariable(project, "url", "URL").Id, ValueScope.Service, "backend", null, "https://{{HOST}}/api");
            }, "get", "--key", "URL", "--service", "backend", "--env", "prod");

            Assert.Equal(0, result.Code);
            Assert.Equal("https://example.test/api", result.Out.Trim());
        }

        [Fact]
        public void Get_JsonOutputIncludesMetadata()
        {
            var result = RunWithProject(project =>
            {
                AddValue(project, AddVariable(project, "url", "URL").Id, ValueScope.Service, "backend", null, "https://example.test", "2026-05-25T12:34:56.0000000Z");
            }, "get", "--key", "URL", "--service", "backend", "--env", "prod", "--format", "json");

            Assert.Equal(0, result.Code);
            Assert.Contains("\"key\":\"URL\"", result.Out);
            Assert.Contains("\"value\":\"https://example.test\"", result.Out);
            Assert.Contains("\"updatedAt\":\"2026-05-25T12:34:56.0000000Z\"", result.Out);
        }

        [Fact]
        public void Get_ReturnsRawEffectiveValueWhenCalculatedIsFalse()
        {
            var result = RunWithProject(project =>
            {
                AddValue(project, AddVariable(project, "host", "HOST").Id, ValueScope.Global, null, null, "example.test");
                AddValue(project, AddVariable(project, "url", "URL").Id, ValueScope.Service, "backend", null, "https://{{HOST}}/api");
            }, "get", "--key", "URL", "--service", "backend", "--env", "prod", "--raw", "--calculated", "false");

            Assert.Equal(0, result.Code);
            Assert.Equal("https://{{HOST}}/api", result.Out.Trim());
        }

        [Fact]
        public void Get_MasksSecretsUnlessShowSecretsIsPassed()
        {
            var masked = RunWithProject(project =>
            {
                var secret = AddVariable(project, "password", "PASSWORD");
                secret.IsSecret = true;
                AddValue(project, secret.Id, ValueScope.Service, "backend", null, "secret-value");
            }, "get", "--key", "PASSWORD", "--service", "backend");

            var shown = RunWithProject(project =>
            {
                var secret = AddVariable(project, "password", "PASSWORD");
                secret.IsSecret = true;
                AddValue(project, secret.Id, ValueScope.Service, "backend", null, "secret-value");
            }, "get", "--key", "PASSWORD", "--service", "backend", "--show-secrets");

            Assert.Equal(0, masked.Code);
            Assert.Equal("********", masked.Out.Trim());
            Assert.Equal(0, shown.Code);
            Assert.Equal("secret-value", shown.Out.Trim());
        }

        [Fact]
        public void Get_ReturnsErrorWhenValueIsMissing()
        {
            var result = RunWithProject(project =>
            {
                AddVariable(project, "missing", "MISSING");
            }, "get", "--key", "MISSING", "--service", "backend", "--env", "prod");

            Assert.Equal(1, result.Code);
        }

        private static CliResult RunWithProject(Action<ProjectModel> configure, params string[] args)
        {
            var directory = Path.Combine(Path.GetTempPath(), "envsecured-tests-" + Guid.NewGuid().ToString("N"));
            var path = Path.Combine(directory, "project.envs");
            try
            {
                Directory.CreateDirectory(directory);
                var project = new ProjectModel
                {
                    Settings = new ProjectSettings { CliExportPasswordRequiredPolicy = false },
                    Services = { new ServiceModel { Id = "backend", Name = "backend" } },
                    Environments = { new EnvironmentModel { Id = "prod", Name = "prod" } }
                };
                configure(project);
                new VaultFileService().Save(project, path);

                var fullArgs = new string[args.Length + 2];
                fullArgs[0] = args[0];
                fullArgs[1] = "--file";
                fullArgs[2] = path;
                Array.Copy(args, 1, fullArgs, 3, args.Length - 1);
                return RunCli(fullArgs);
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        private static CliResult RunCli(params string[] args)
        {
            var type = typeof(EnvSecured.WinForms.Forms.MainForm).Assembly.GetType("EnvSecured.WinForms.Cli.CliRunner");
            Assert.NotNull(type);
            var method = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(method);

            var originalOut = Console.Out;
            var originalError = Console.Error;
            using (var output = new StringWriter())
            using (var error = new StringWriter())
            {
                try
                {
                    Console.SetOut(output);
                    Console.SetError(error);
                    var code = (int)method.Invoke(null, new object[] { args });
                    return new CliResult(code, output.ToString(), error.ToString());
                }
                finally
                {
                    Console.SetOut(originalOut);
                    Console.SetError(originalError);
                }
            }
        }

        private static VariableDefinitionModel AddVariable(ProjectModel project, string id, string key)
        {
            var variable = new VariableDefinitionModel { Id = id, Key = key, IsActive = true };
            project.Variables.Add(variable);
            return variable;
        }

        private static void AddValue(ProjectModel project, string variableId, ValueScope scope, string serviceId, string environmentId, string value, string updatedAt = null)
        {
            project.Values.Add(new VariableValueModel
            {
                VariableId = variableId,
                Scope = scope,
                ServiceId = serviceId,
                EnvironmentId = environmentId,
                Value = value,
                UpdatedAt = updatedAt
            });
        }

        private sealed class CliResult
        {
            public CliResult(int code, string output, string error)
            {
                Code = code;
                Out = output;
                Error = error;
            }

            public int Code { get; }
            public string Out { get; }
            public string Error { get; }
        }
    }
}

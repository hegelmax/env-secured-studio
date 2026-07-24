using System;
using System.IO;
using System.Reflection;
using EnvSecured.Core.Services;
using Xunit;

namespace EnvSecured.Tests
{
    public sealed class CliHelpAndSyncProviderTests
    {
        private static readonly Type CliType = typeof(EnvSecured.WinForms.Forms.MainForm).Assembly
            .GetType("EnvSecured.WinForms.Cli.CliRunner", true);

        [Fact]
        public void TopLevelHelpOffersCommandsAndMarkdownDocs()
        {
            var result = Run("--help");

            Assert.Equal(0, result.Code);
            Assert.Contains("EnvSecured Studio 1.2.0.10", result.Output);
            Assert.Contains("--help commands", result.Output);
            Assert.Contains("--help docs", result.Output);
            Assert.DoesNotContain("Project commands:", result.Output);
        }

        [Fact]
        public void HelpCommandsPrintsCompleteCommandList()
        {
            var result = Run("--help", "commands");

            Assert.Equal(0, result.Code);
            Assert.Contains("EnvSecured Studio CLI 1.2.0.10", result.Output);
            Assert.Contains("Project commands:", result.Output);
            Assert.Contains("sync-provider-add", result.Output);
        }

        [Fact]
        public void HelpDocsPrintsEmbeddedMarkdown()
        {
            var result = Run("--help", "docs");

            Assert.Equal(0, result.Code);
            Assert.StartsWith("# CLI Reference", result.Output.TrimStart('\uFEFF'));
            Assert.Contains("## GitHub synchronization", result.Output);
        }

        [Fact]
        public void SyncPushHelpIsCommandSpecificAndDocumentsCiGuards()
        {
            var result = Run("sync-push", "--help");

            Assert.Equal(0, result.Code);
            Assert.Contains("--apply true", result.Output);
            Assert.Contains("--allow-delete true", result.Output);
            Assert.Contains("--check true", result.Output);
            Assert.Contains("GH_TOKEN/GITHUB_TOKEN", result.Output);
            Assert.DoesNotContain("Project commands:", result.Output);
        }

        [Fact]
        public void SyncNamespaceHelpDoesNotOpenTheUi()
        {
            var result = Run("sync", "--help");

            Assert.Equal(0, result.Code);
            Assert.Contains("sync-preview", result.Output);
            Assert.Contains("sync-provider-init", result.Output);
        }

        [Fact]
        public void ProviderCanBeCreatedAndReadHeadlesslyWithEncryptedSettings()
        {
            var path = Path.Combine(Path.GetTempPath(), "envsecured-sync-provider-" + Guid.NewGuid().ToString("N") + ".envs");
            try
            {
                Assert.Equal(0, Run("new", "--file", path, "--name", "CLI Sync Test").Code);
                var add = Run(
                    "sync-provider-add", "--file", path,
                    "--id", "github-main", "--name", "Main GitHub",
                    "--organization", "example-owner", "--repository", "example-repo",
                    "--mode", "mirror", "--password", "test-password");
                Assert.Equal(0, add.Code);

                var raw = File.ReadAllText(path);
                Assert.DoesNotContain("example-owner", raw);
                Assert.DoesNotContain("secretFingerprintKey", raw);
                var stored = new VaultFileService().Load(path);
                Assert.NotNull(stored.Sync.Providers[0].EncryptedSettings);

                var list = Run("sync-list", "--file", path, "--format", "json", "--password", "test-password");
                Assert.Equal(0, list.Code);
                Assert.Contains("example-owner", list.Output);
                Assert.Contains("Mirror", list.Output);

                Assert.Equal(0, Run("sync-provider-init", "--file", path, "--provider", "github-main", "--password", "test-password").Code);
                Assert.Equal(0, Run("sync-key-rotate", "--file", path, "--provider", "github-main", "--password", "test-password").Code);
                Assert.Equal(0, Run("sync-provider-edit", "--file", path, "--provider", "github-main", "--mode", "force-replace", "--enabled", "false", "--password", "test-password").Code);
                var edited = Run("sync-list", "--file", path, "--format", "json", "--password", "test-password");
                Assert.Contains("ForceReplace", edited.Output);
                Assert.Contains("\"IsEnabled\":false", edited.Output);

                Assert.Equal(0, Run("sync-provider-delete", "--file", path, "--provider", "github-main", "--password", "test-password").Code);
                Assert.Empty(new VaultFileService().Load(path).Sync.Providers);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".recovery.bak")) File.Delete(path + ".recovery.bak");
            }
        }

        private static CliResult Run(params string[] args)
        {
            var oldOut = Console.Out;
            var oldError = Console.Error;
            var output = new StringWriter();
            var error = new StringWriter();
            try
            {
                Console.SetOut(output);
                Console.SetError(error);
                var code = (int)CliType.GetMethod("Run", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { args });
                return new CliResult(code, output.ToString(), error.ToString());
            }
            finally
            {
                Console.SetOut(oldOut);
                Console.SetError(oldError);
            }
        }

        private sealed class CliResult
        {
            public CliResult(int code, string output, string error) { Code = code; Output = output; Error = error; }
            public int Code { get; }
            public string Output { get; }
            public string Error { get; }
        }
    }
}

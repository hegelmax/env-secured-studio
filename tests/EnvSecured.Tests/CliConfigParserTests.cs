using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace EnvSecured.Tests
{
    public sealed class CliConfigParserTests
    {
        [Theory]
        [InlineData("KEY=\"", "\"")]
        [InlineData("KEY='", "'")]
        [InlineData("KEY=\"value\"", "value")]
        [InlineData("KEY='value'", "value")]
        public void ParseConfigFile_HandlesSingleAndQuotedValues(string line, string expectedValue)
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".env");
            try
            {
                File.WriteAllText(path, line);

                var values = Parse(path).ToList();

                Assert.Single(values);
                Assert.Equal("KEY", values[0].Key);
                Assert.Equal(expectedValue, values[0].Value);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private static IEnumerable<KeyValuePair<string, string>> Parse(string path)
        {
            var type = typeof(EnvSecured.WinForms.Forms.MainForm).Assembly.GetType("EnvSecured.WinForms.Cli.CliRunner");
            Assert.NotNull(type);
            var method = type.GetMethod("ParseConfigFile", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            return (IEnumerable<KeyValuePair<string, string>>)method.Invoke(null, new object[] { path });
        }
    }
}

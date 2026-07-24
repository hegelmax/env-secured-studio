using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace EnvSecured.Core.Services
{
    public sealed class ExternalCommandService
    {
        public bool IsCommandAvailable(string command)
        {
            return TryFindCommand(command) != null;
        }

        public string TryFindCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return null;

            var direct = command.Trim();
            if (File.Exists(direct)) return Path.GetFullPath(direct);

            var extensions = CandidateExtensions(command);
            foreach (var directory in CandidatePathDirectories())
            {
                if (string.IsNullOrWhiteSpace(directory)) continue;
                foreach (var extension in extensions)
                {
                    var candidate = Path.Combine(directory.Trim(), command + extension);
                    if (File.Exists(candidate)) return candidate;
                }
            }

            var fromPowerShell = TryResolveWithPowerShell(command);
            if (fromPowerShell != null) return fromPowerShell;

            return null;
        }

        private static IEnumerable<string> CandidatePathDirectories()
        {
            var values = new[]
            {
                Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process),
                Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User),
                Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine),
                Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Process),
                Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User),
                Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine),
                TryReadRegistryPath(Registry.CurrentUser, @"Environment", "Path"),
                TryReadRegistryPath(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment", "Path")
            };

            foreach (var value in values.Where(v => !string.IsNullOrWhiteSpace(v)))
            {
                foreach (var directory in value.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
                {
                    yield return Environment.ExpandEnvironmentVariables(directory.Trim());
                }
            }

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(programFiles)) yield return Path.Combine(programFiles, "GitHub CLI");
            if (!string.IsNullOrWhiteSpace(localAppData)) yield return Path.Combine(localAppData, "Programs", "GitHub CLI");
        }

        private static string TryReadRegistryPath(RegistryKey root, string subKeyName, string valueName)
        {
            try
            {
                using (var key = root.OpenSubKey(subKeyName))
                {
                    return Convert.ToString(key?.GetValue(valueName));
                }
            }
            catch
            {
                return null;
            }
        }

        private static string TryResolveWithPowerShell(string command)
        {
            try
            {
                using (var process = new Process())
                {
                    process.StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"(Get-Command '" + command.Replace("'", "''") + "' -ErrorAction SilentlyContinue).Source\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    process.Start();
                    var output = process.StandardOutput.ReadToEnd().Trim();
                    if (!process.WaitForExit(5000))
                    {
                        try { process.Kill(); } catch { }
                        return null;
                    }
                    return File.Exists(output) ? output : null;
                }
            }
            catch
            {
                return null;
            }
        }

        private static string[] CandidateExtensions(string command)
        {
            if (Path.HasExtension(command)) return new[] { string.Empty };

            var pathExt = Environment.GetEnvironmentVariable("PATHEXT");
            if (string.IsNullOrWhiteSpace(pathExt))
            {
                return new[] { ".exe", ".cmd", ".bat", string.Empty };
            }

            return pathExt
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(e => e.StartsWith(".", StringComparison.Ordinal) ? e : "." + e)
                .Concat(new[] { string.Empty })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }
}

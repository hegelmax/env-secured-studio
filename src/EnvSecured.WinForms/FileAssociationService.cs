using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace EnvSecured.WinForms
{
    internal enum FileAssociationStatus
    {
        NotRegistered,
        RegisteredToCurrentExe,
        RegisteredToOtherExe
    }

    internal static class FileAssociationService
    {
        public const string Extension = ".envs";
        private const string ProgId = "EnvSecured.Studio.Vault";

        public static bool IsVaultFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var extension = Path.GetExtension(path);
            return string.Equals(extension, Extension, StringComparison.OrdinalIgnoreCase);
        }

        public static FileAssociationStatus GetStatus()
        {
            var associatedExePath = GetAssociatedExecutablePath();
            if (string.IsNullOrWhiteSpace(associatedExePath))
            {
                return FileAssociationStatus.NotRegistered;
            }

            return string.Equals(
                Path.GetFullPath(associatedExePath),
                Path.GetFullPath(GetCurrentExecutablePath()),
                StringComparison.OrdinalIgnoreCase)
                ? FileAssociationStatus.RegisteredToCurrentExe
                : FileAssociationStatus.RegisteredToOtherExe;
        }

        public static string GetAssociatedExecutablePath()
        {
            using (var classesKey = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + Extension))
            {
                var progId = classesKey?.GetValue(null) as string;
                if (string.IsNullOrWhiteSpace(progId)) return null;

                using (var commandKey = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + progId + @"\shell\open\command"))
                {
                    return ExtractExecutablePathFromCommand(commandKey?.GetValue(null) as string);
                }
            }
        }

        public static void Register()
        {
            var currentExePath = GetCurrentExecutablePath();
            var command = $"\"{currentExePath}\" \"%1\"";

            using (var extensionKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + Extension))
            {
                extensionKey.SetValue(null, ProgId, RegistryValueKind.String);
            }

            using (var progIdKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ProgId))
            {
                progIdKey.SetValue(null, "EnvSecured Studio vault", RegistryValueKind.String);
            }

            using (var commandKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ProgId + @"\shell\open\command"))
            {
                commandKey.SetValue(null, command, RegistryValueKind.String);
            }
        }

        public static void Unregister()
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\" + Extension, false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\" + ProgId, false);
        }

        public static void EnsureInteractive(IWin32Window owner)
        {
            try
            {
                var status = GetStatus();
                if (status == FileAssociationStatus.RegisteredToCurrentExe)
                {
                    return;
                }

                if (status == FileAssociationStatus.NotRegistered)
                {
                    if (MessageBox.Show(
                        owner,
                        "Register .envs vault files to open with this EnvSecured.exe?",
                        "File Association",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question) == DialogResult.Yes)
                    {
                        Register();
                    }
                    return;
                }

                var registeredPath = GetAssociatedExecutablePath();
                var currentPath = GetCurrentExecutablePath();
                if (MessageBox.Show(
                    owner,
                    ".envs vault files are currently associated with another executable:\r\n\r\n" +
                    (string.IsNullOrWhiteSpace(registeredPath) ? "(unknown)" : registeredPath) +
                    "\r\n\r\nReplace association with this executable?\r\n\r\n" +
                    currentPath,
                    "File Association",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    Register();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, "File association check failed.\r\n\r\n" + ex.Message, "File Association", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        public static string GetCurrentExecutablePath()
        {
            return Path.GetFullPath(Assembly.GetExecutingAssembly().Location);
        }

        private static string ExtractExecutablePathFromCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return null;
            var trimmed = command.Trim();
            var quoted = Regex.Match(trimmed, "^\"(?<path>[^\"]+)\"", RegexOptions.IgnoreCase);
            if (quoted.Success) return quoted.Groups["path"].Value;

            var unquoted = Regex.Match(trimmed, @"^(?<path>\S+?\.exe)(?:\s|$)", RegexOptions.IgnoreCase);
            return unquoted.Success ? unquoted.Groups["path"].Value : null;
        }
    }
}

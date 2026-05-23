using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace EnvSecured.WinForms
{
    internal sealed class UpdateInfo
    {
        public Version CurrentVersion { get; set; }
        public Version LatestVersion { get; set; }
        public bool UpdateAvailable => LatestVersion != null && CurrentVersion != null && LatestVersion > CurrentVersion;
        public string DownloadUrl { get; set; }
        public string ReleasePageUrl { get; set; }
    }

    internal static class UpdateService
    {
        private const string VersionUrl = "https://raw.githubusercontent.com/hegelmax/env-secured-studio/main/app.version.cs";
        private const string DownloadUrlFormat = "https://github.com/hegelmax/env-secured-studio/raw/main/bin/EnvSecured_v{0}.exe";
        private const string ReleasePageUrl = "https://github.com/hegelmax/env-secured-studio/releases/latest";

        public static UpdateInfo Check(int timeoutMs = 5000)
        {
            EnableTls12();
            var content = DownloadString(VersionUrl, timeoutMs);
            var latestText = ExtractVersion(content);
            if (string.IsNullOrWhiteSpace(latestText))
            {
                throw new InvalidOperationException("Could not read latest version.");
            }

            if (!Version.TryParse(latestText, out var latestVersion))
            {
                throw new InvalidOperationException("Invalid latest version: " + latestText);
            }

            return new UpdateInfo
            {
                CurrentVersion = Assembly.GetExecutingAssembly().GetName().Version,
                LatestVersion = latestVersion,
                DownloadUrl = string.Format(DownloadUrlFormat, latestVersion.ToString(3)),
                ReleasePageUrl = ReleasePageUrl
            };
        }

        public static string Download(UpdateInfo info, int timeoutMs = 30000)
        {
            if (info == null) throw new ArgumentNullException(nameof(info));
            EnableTls12();
            var exeFolder = AppDomain.CurrentDomain.BaseDirectory;
            var version = info.LatestVersion?.ToString() ?? DateTime.Now.ToString("yyyyMMddHHmmss");
            var targetPath = Path.Combine(exeFolder, "EnvSecured_v" + version + ".exe");
            var tempPath = targetPath + ".tmp";

            var request = (HttpWebRequest)WebRequest.Create(info.DownloadUrl);
            request.Method = "GET";
            request.UserAgent = "EnvSecured Studio";
            request.Timeout = timeoutMs;
            request.ReadWriteTimeout = timeoutMs;

            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var stream = response.GetResponseStream())
                using (var output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    if (stream == null)
                    {
                        throw new InvalidOperationException("Update download response has no content stream.");
                    }

                    stream.CopyTo(output);
                }

                if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                }

                File.Move(tempPath, targetPath);
            }
            catch
            {
                try
                {
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                }
                catch
                {
                }
                throw;
            }

            return targetPath;
        }

        public static void CheckInteractive(IWin32Window owner)
        {
            try
            {
                var info = Check();
                if (!info.UpdateAvailable)
                {
                    return;
                }

                var result = MessageBox.Show(
                    owner,
                    $"EnvSecured Studio {info.LatestVersion} is available.\r\nCurrent version: {info.CurrentVersion}\r\n\r\nDownload update now?",
                    "Update Available",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);
                if (result != DialogResult.Yes)
                {
                    return;
                }

                var path = Download(info);
                MessageBox.Show(owner, "Update downloaded:\r\n\r\n" + path, "Update Downloaded", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Process.Start("explorer.exe", "/select,\"" + path + "\"");
            }
            catch
            {
                // Startup update checks must not block using the vault.
            }
        }

        private static string DownloadString(string url, int timeoutMs)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.UserAgent = "EnvSecured Studio";
            request.Timeout = timeoutMs;
            request.ReadWriteTimeout = timeoutMs;
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream))
            {
                return reader.ReadToEnd();
            }
        }

        private static string ExtractVersion(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return null;
            var match = Regex.Match(content, @"<FileVersion>\s*([^<]+)\s*</FileVersion>", RegexOptions.IgnoreCase);
            if (match.Success) return match.Groups[1].Value.Trim();

            match = Regex.Match(content, @"<Version>\s*([^<]+)\s*</Version>", RegexOptions.IgnoreCase);
            if (match.Success) return match.Groups[1].Value.Trim();

            match = Regex.Match(content, @"AssemblyFileVersion(?:Attribute)?\(\s*""([^""]+)""\s*\)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }

        private static void EnableTls12()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch
            {
            }
        }
    }
}

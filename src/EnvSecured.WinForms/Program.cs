using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using EnvSecured.WinForms.Cli;
using EnvSecured.WinForms.Forms;

namespace EnvSecured.WinForms
{
    internal static class Program
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();

        [STAThread]
        private static int Main(string[] args)
        {
            if (args != null && args.Length > 0)
            {
                if (string.Equals(args[0], "--register-association", StringComparison.OrdinalIgnoreCase))
                {
                    FileAssociationService.Register();
                    Console.WriteLine(".envs file association registered.");
                    return 0;
                }

                if (string.Equals(args[0], "--unregister-association", StringComparison.OrdinalIgnoreCase))
                {
                    FileAssociationService.Unregister();
                    Console.WriteLine(".envs file association removed.");
                    return 0;
                }

                if (string.Equals(args[0], "--check-update", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var info = UpdateService.Check(15000);
                        Console.WriteLine($"Current: {info.CurrentVersion}");
                        Console.WriteLine($"Latest: {info.LatestVersion}");
                        Console.WriteLine(info.UpdateAvailable ? "Update available." : "No update available.");
                        Console.WriteLine($"Download: {info.ReleasePageUrl}");
                        return info.UpdateAvailable ? 10 : 0;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine("Update check failed: " + ex.Message);
                        return 2;
                    }
                }

                if (string.Equals(args[0], "--download-update", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var info = UpdateService.Check(15000);
                        if (!info.UpdateAvailable)
                        {
                            Console.WriteLine($"No update available. Current {info.CurrentVersion}, latest {info.LatestVersion}.");
                            return 0;
                        }

                        Console.WriteLine("Downloading " + info.DownloadUrl);
                        Console.WriteLine("Saved: " + UpdateService.Download(info));
                        return 0;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine("Update download failed: " + ex.Message);
                        return 2;
                    }
                }
            }

            if (CliRunner.IsCliRequest(args))
            {
                return CliRunner.Run(args);
            }

            try
            {
                FreeConsole();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                var initialFilePath = args != null && args.Length > 0 && FileAssociationService.IsVaultFile(args[0]) ? args[0] : null;
                FileAssociationService.EnsureInteractive(null);
                UpdateService.CheckInteractive(null);
                Application.Run(new MainForm(initialFilePath));
                return 0;
            }
            catch (Exception ex)
            {
                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "EnvSecured.crash.log");
                File.WriteAllText(logPath, ex.ToString());
                MessageBox.Show(ex.ToString(), "EnvSecured Studio startup failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}

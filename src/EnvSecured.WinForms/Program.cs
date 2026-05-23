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
            if (CliRunner.IsCliRequest(args))
            {
                return CliRunner.Run(args);
            }

            try
            {
                FreeConsole();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
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

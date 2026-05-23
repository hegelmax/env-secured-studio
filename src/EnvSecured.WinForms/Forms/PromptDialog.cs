using System.Windows.Forms;

namespace EnvSecured.WinForms.Forms
{
    internal sealed class PromptDialog : Form
    {
        private readonly TextBox textBox;

        private PromptDialog(string title, string label, string defaultValue, bool password)
        {
            Text = title;
            Width = 430;
            Height = 150;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;

            Controls.Add(new Label { Left = 12, Top = 14, Width = 390, Text = label });
            textBox = new TextBox { Left = 12, Top = 38, Width = 390, Text = defaultValue, UseSystemPasswordChar = password };
            Controls.Add(textBox);
            Controls.Add(new Button { Left = 246, Top = 74, Width = 75, Text = "OK", DialogResult = DialogResult.OK });
            Controls.Add(new Button { Left = 327, Top = 74, Width = 75, Text = "Cancel", DialogResult = DialogResult.Cancel });
            AcceptButton = Controls[2] as Button;
            CancelButton = Controls[3] as Button;
        }

        public static string Show(IWin32Window owner, string title, string label, string defaultValue)
        {
            using (var dialog = new PromptDialog(title, label, defaultValue, false))
            {
                return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.textBox.Text : null;
            }
        }

        public static string ShowPassword(IWin32Window owner, string title, string label)
        {
            using (var dialog = new PromptDialog(title, label, string.Empty, true))
            {
                return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.textBox.Text : null;
            }
        }
    }
}

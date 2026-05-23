using System.Windows.Forms;
using EnvSecured.Core.Models;

namespace EnvSecured.WinForms.Forms
{
    internal sealed class ScopeDialog : Form
    {
        private readonly ComboBox comboBox;

        public ScopeDialog()
        {
            Text = "Value Scope";
            Width = 330;
            Height = 145;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;

            Controls.Add(new Label { Left = 12, Top = 14, Width = 280, Text = "Scope:" });
            comboBox = new ComboBox { Left = 12, Top = 38, Width = 285, DropDownStyle = ComboBoxStyle.DropDownList };
            comboBox.Items.Add(ValueScope.Global);
            comboBox.Items.Add(ValueScope.Environment);
            comboBox.Items.Add(ValueScope.Service);
            comboBox.Items.Add(ValueScope.ServiceEnvironment);
            comboBox.SelectedIndex = 0;
            Controls.Add(comboBox);
            Controls.Add(new Button { Left = 141, Top = 74, Width = 75, Text = "OK", DialogResult = DialogResult.OK });
            Controls.Add(new Button { Left = 222, Top = 74, Width = 75, Text = "Cancel", DialogResult = DialogResult.Cancel });
            AcceptButton = Controls[2] as Button;
            CancelButton = Controls[3] as Button;
        }

        public ValueScope SelectedScope => (ValueScope)comboBox.SelectedItem;
    }
}

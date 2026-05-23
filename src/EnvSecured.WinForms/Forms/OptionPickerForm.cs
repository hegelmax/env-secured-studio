using System.Windows.Forms;

namespace EnvSecured.WinForms.Forms
{
    internal sealed class OptionPickerForm : Form
    {
        private readonly ComboBox comboBox;

        public OptionPickerForm(string title, string[] options)
        {
            Text = title;
            Width = 360;
            Height = 135;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;

            comboBox = new ComboBox { Left = 12, Top = 12, Width = 320, DropDownStyle = ComboBoxStyle.DropDownList };
            comboBox.Items.AddRange(options);
            if (comboBox.Items.Count > 0)
            {
                comboBox.SelectedIndex = 0;
            }

            Controls.Add(comboBox);
            Controls.Add(new Button { Left = 176, Top = 52, Width = 75, Text = "OK", DialogResult = DialogResult.OK });
            Controls.Add(new Button { Left = 257, Top = 52, Width = 75, Text = "Cancel", DialogResult = DialogResult.Cancel });
            AcceptButton = Controls[1] as Button;
            CancelButton = Controls[2] as Button;
        }

        public string SelectedValue => comboBox.SelectedItem?.ToString();
    }
}

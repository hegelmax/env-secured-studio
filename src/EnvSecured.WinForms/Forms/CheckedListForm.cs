using System.Windows.Forms;

namespace EnvSecured.WinForms.Forms
{
    internal sealed class CheckedListForm : Form
    {
        private readonly CheckedListBox list;

        public CheckedListForm(string title, string[] items, bool[] checkedItems)
        {
            Text = title;
            Width = 360;
            Height = 420;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;

            list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true };
            for (var i = 0; i < items.Length; i++)
            {
                list.Items.Add(items[i], i < checkedItems.Length && checkedItems[i]);
            }

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft };
            buttons.Controls.Add(new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 80 });
            buttons.Controls.Add(new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 80 });

            Controls.Add(list);
            Controls.Add(buttons);
        }

        public bool[] Checked
        {
            get
            {
                var result = new bool[list.Items.Count];
                for (var i = 0; i < list.Items.Count; i++)
                {
                    result[i] = list.GetItemChecked(i);
                }
                return result;
            }
        }
    }
}

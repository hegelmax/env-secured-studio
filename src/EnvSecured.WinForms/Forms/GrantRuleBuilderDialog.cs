using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using EnvSecured.Core.Models;
using EnvSecured.Core.Services;

namespace EnvSecured.WinForms.Forms
{
    internal sealed class GrantRuleBuilderDialog : Form
    {
        private const string AllVariablesMode = "All variables";
        private const string SelectedVariablesMode = "Selected variables";
        private const string PatternVariablesMode = "Variable masks";

        private readonly GrantService grantService = new GrantService();
        private readonly CheckBox allServicesBox = new CheckBox { Text = "All services (*)", Dock = DockStyle.Top, Height = 26 };
        private readonly CheckBox allEnvironmentsBox = new CheckBox { Text = "All environments (*)", Dock = DockStyle.Top, Height = 26 };
        private readonly CheckedListBox servicesList = CreateCheckedList();
        private readonly CheckedListBox environmentsList = CreateCheckedList();
        private readonly CheckedListBox variablesList = CreateCheckedList();
        private readonly ComboBox variableModeBox = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly TextBox variableMasksBox = new TextBox { Dock = DockStyle.Bottom, Font = new Font("Consolas", 9f) };
        private readonly CheckBox readBox = new CheckBox { Text = "Read", AutoSize = true, Checked = true };
        private readonly CheckBox batchExportBox = new CheckBox { Text = "BatchExport", AutoSize = true };
        private readonly TextBox previewBox = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 9f), BackColor = Color.White };
        private readonly string fixedServiceId;
        private readonly string fixedEnvironmentId;
        private bool initializing = true;

        private GrantRuleBuilderDialog(ProjectModel project, string existingRule, string fixedServiceId = null, string fixedEnvironmentId = null)
        {
            this.fixedServiceId = fixedServiceId;
            this.fixedEnvironmentId = fixedEnvironmentId;
            Text = existingRule == null ? "Add Grant Rule" : "Edit Grant Rule";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(790, 650);
            MinimumSize = new Size(700, 590);
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;

            project = project ?? new ProjectModel();
            ProjectService.EnsureProjectCollections(project);
            foreach (var service in project.Services.Where(x => x.IsActive).OrderBy(x => x.SortOrder).ThenBy(x => x.Name))
                servicesList.Items.Add(new SelectorItem(service.Id, Display(service.Id, service.Name)));
            foreach (var environment in project.Environments.Where(x => x.IsActive).OrderBy(x => x.SortOrder).ThenBy(x => x.Name))
                environmentsList.Items.Add(new SelectorItem(environment.Id, Display(environment.Id, environment.Name)));
            foreach (var variable in project.Variables.Where(x => x.IsActive).OrderBy(x => x.SortOrder).ThenBy(x => x.Key))
                variablesList.Items.Add(new SelectorItem(variable.Key, variable.Key));

            variableModeBox.Items.AddRange(new object[] { AllVariablesMode, SelectedVariablesMode, PatternVariablesMode });
            variableModeBox.SelectedItem = AllVariablesMode;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(12) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            root.Controls.Add(new Label
            {
                Text = "Build <services>.<environments>.<variables>.<permissions>. Select several items to join them with |.",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);

            var targets = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            targets.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            targets.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            targets.Controls.Add(BuildSelectorGroup("Services", allServicesBox, servicesList), 0, 0);
            targets.Controls.Add(BuildSelectorGroup("Environments", allEnvironmentsBox, environmentsList), 1, 0);
            root.Controls.Add(targets, 0, 1);
            root.Controls.Add(BuildVariablesGroup(), 0, 2);

            var permissions = new GroupBox { Text = "Permissions", Dock = DockStyle.Fill };
            var permissionFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8, 5, 0, 0), WrapContents = false };
            permissionFlow.Controls.Add(readBox);
            permissionFlow.Controls.Add(batchExportBox);
            permissions.Controls.Add(permissionFlow);
            root.Controls.Add(permissions, 0, 3);

            var preview = new GroupBox { Text = "Rule preview", Dock = DockStyle.Fill, Padding = new Padding(8) };
            preview.Controls.Add(previewBox);
            root.Controls.Add(preview, 0, 4);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            var ok = new Button { Text = "Apply", Width = 92, Height = 28 };
            var cancel = new Button { Text = "Cancel", Width = 92, Height = 28, DialogResult = DialogResult.Cancel };
            ok.Click += (s, e) => ApplyAndClose();
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            root.Controls.Add(buttons, 0, 5);
            Controls.Add(root);
            AcceptButton = ok;
            CancelButton = cancel;

            WireUpdates();
            if (existingRule == null)
            {
                allServicesBox.Checked = true;
                allEnvironmentsBox.Checked = true;
            }
            else
            {
                LoadRule(existingRule);
            }
            ApplyFixedScope(allServicesBox, servicesList, fixedServiceId);
            ApplyFixedScope(allEnvironmentsBox, environmentsList, fixedEnvironmentId);
            initializing = false;
            UpdateControlState();
            UpdatePreview();
        }

        public string Result { get; private set; }

        public static string Show(IWin32Window owner, ProjectModel project, string existingRule = null, string fixedServiceId = null, string fixedEnvironmentId = null)
        {
            using (var dialog = new GrantRuleBuilderDialog(project, existingRule, fixedServiceId, fixedEnvironmentId))
                return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.Result : null;
        }

        private static CheckedListBox CreateCheckedList() => new CheckedListBox
        {
            Dock = DockStyle.Fill,
            CheckOnClick = true,
            IntegralHeight = false,
            BorderStyle = BorderStyle.FixedSingle
        };

        private static GroupBox BuildSelectorGroup(string title, CheckBox allBox, CheckedListBox list)
        {
            var group = new GroupBox { Text = title, Dock = DockStyle.Fill, Padding = new Padding(8) };
            var panel = new Panel { Dock = DockStyle.Fill };
            list.Dock = DockStyle.Fill;
            allBox.Dock = DockStyle.Top;
            panel.Controls.Add(list);
            panel.Controls.Add(allBox);
            group.Controls.Add(panel);
            return group;
        }

        private GroupBox BuildVariablesGroup()
        {
            var group = new GroupBox { Text = "Variables", Dock = DockStyle.Fill, Padding = new Padding(8) };
            var panel = new Panel { Dock = DockStyle.Fill };
            var modePanel = new TableLayoutPanel { Dock = DockStyle.Top, Height = 30, ColumnCount = 2 };
            modePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            modePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            modePanel.Controls.Add(new Label { Text = "Selection:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            modePanel.Controls.Add(variableModeBox, 1, 0);
            var maskPanel = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 32, ColumnCount = 2 };
            maskPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            maskPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            maskPanel.Controls.Add(new Label { Text = "Masks:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            maskPanel.Controls.Add(variableMasksBox, 1, 0);
            panel.Controls.Add(variablesList);
            panel.Controls.Add(maskPanel);
            panel.Controls.Add(modePanel);
            group.Controls.Add(panel);
            return group;
        }

        private void WireUpdates()
        {
            allServicesBox.CheckedChanged += (s, e) => RefreshFromInput();
            allEnvironmentsBox.CheckedChanged += (s, e) => RefreshFromInput();
            variableModeBox.SelectedIndexChanged += (s, e) => RefreshFromInput();
            variableMasksBox.TextChanged += (s, e) => RefreshFromInput();
            readBox.CheckedChanged += (s, e) => RefreshFromInput();
            batchExportBox.CheckedChanged += (s, e) => RefreshFromInput();
            servicesList.ItemCheck += CheckedItemChanged;
            environmentsList.ItemCheck += CheckedItemChanged;
            variablesList.ItemCheck += CheckedItemChanged;
        }

        private void CheckedItemChanged(object sender, ItemCheckEventArgs e)
        {
            if (initializing || !IsHandleCreated || IsDisposed) return;
            BeginInvoke(new Action(UpdatePreview));
        }

        private void RefreshFromInput()
        {
            if (initializing) return;
            UpdateControlState();
            UpdatePreview();
        }

        private void UpdateControlState()
        {
            servicesList.Enabled = fixedServiceId == null && !allServicesBox.Checked;
            environmentsList.Enabled = fixedEnvironmentId == null && !allEnvironmentsBox.Checked;
            allServicesBox.Enabled = fixedServiceId == null;
            allEnvironmentsBox.Enabled = fixedEnvironmentId == null;
            var mode = Convert.ToString(variableModeBox.SelectedItem);
            variablesList.Enabled = mode == SelectedVariablesMode;
            variableMasksBox.Enabled = mode == PatternVariablesMode;
        }

        private void LoadRule(string text)
        {
            var rule = grantService.Parse(text);
            LoadSelector(rule.Services, allServicesBox, servicesList);
            LoadSelector(rule.Environments, allEnvironmentsBox, environmentsList);

            if (rule.Variables.Count == 1 && rule.Variables[0] == "*")
            {
                variableModeBox.SelectedItem = AllVariablesMode;
            }
            else if (rule.Variables.All(value => FindIndex(variablesList, value) >= 0))
            {
                variableModeBox.SelectedItem = SelectedVariablesMode;
                foreach (var value in rule.Variables) variablesList.SetItemChecked(FindIndex(variablesList, value), true);
            }
            else
            {
                variableModeBox.SelectedItem = PatternVariablesMode;
                variableMasksBox.Text = string.Join("|", rule.Variables);
            }
            readBox.Checked = rule.Permissions.Contains("Read", StringComparer.Ordinal);
            batchExportBox.Checked = rule.Permissions.Contains("BatchExport", StringComparer.Ordinal);
        }

        private static void LoadSelector(IReadOnlyList<string> values, CheckBox allBox, CheckedListBox list)
        {
            if (values.Contains("*", StringComparer.Ordinal))
            {
                allBox.Checked = true;
                return;
            }
            allBox.Checked = false;
            foreach (var value in values)
            {
                var index = FindIndex(list, value);
                if (index < 0)
                {
                    index = list.Items.Add(new SelectorItem(value, value + " (not in project)"));
                }
                list.SetItemChecked(index, true);
            }
        }

        private static int FindIndex(CheckedListBox list, string id)
        {
            for (var index = 0; index < list.Items.Count; index++)
                if (string.Equals(((SelectorItem)list.Items[index]).Id, id, StringComparison.Ordinal)) return index;
            return -1;
        }

        private static void ApplyFixedScope(CheckBox allBox, CheckedListBox list, string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            allBox.Checked = false;
            for (var index = 0; index < list.Items.Count; index++) list.SetItemChecked(index, false);
            var selectedIndex = FindIndex(list, id);
            if (selectedIndex < 0) selectedIndex = list.Items.Add(new SelectorItem(id, id + " (not in project)"));
            list.SetItemChecked(selectedIndex, true);
        }

        private void UpdatePreview()
        {
            try
            {
                previewBox.Text = BuildRule();
                previewBox.ForeColor = Color.Black;
            }
            catch (Exception ex)
            {
                previewBox.Text = ex.Message;
                previewBox.ForeColor = Color.Firebrick;
            }
        }

        private string BuildRule()
        {
            var services = SelectorValue(allServicesBox, servicesList, "Select at least one service.");
            var environments = SelectorValue(allEnvironmentsBox, environmentsList, "Select at least one environment.");
            string variables;
            var mode = Convert.ToString(variableModeBox.SelectedItem);
            if (mode == AllVariablesMode)
            {
                variables = "*";
            }
            else if (mode == SelectedVariablesMode)
            {
                variables = CheckedIds(variablesList);
                if (variables.Length == 0) throw new InvalidOperationException("Select at least one variable.");
            }
            else
            {
                variables = variableMasksBox.Text.Trim().ToUpperInvariant();
                if (variables.Length == 0) throw new InvalidOperationException("Enter one or more variable masks separated by |.");
            }

            var permissions = new List<string>();
            if (readBox.Checked) permissions.Add("Read");
            if (batchExportBox.Checked) permissions.Add("BatchExport");
            if (permissions.Count == 0) throw new InvalidOperationException("Select at least one permission.");
            return grantService.Normalize(services + "." + environments + "." + variables + "." + string.Join("|", permissions));
        }

        private static string SelectorValue(CheckBox allBox, CheckedListBox list, string error)
        {
            if (allBox.Checked) return "*";
            var selected = CheckedIds(list);
            if (selected.Length == 0) throw new InvalidOperationException(error);
            return selected;
        }

        private static string CheckedIds(CheckedListBox list) => string.Join("|",
            list.CheckedItems.Cast<SelectorItem>().Select(x => x.Id));

        private void ApplyAndClose()
        {
            try
            {
                Result = BuildRule();
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Grant Rule", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static string Display(string id, string name) =>
            string.IsNullOrWhiteSpace(name) || string.Equals(id, name, StringComparison.OrdinalIgnoreCase) ? id : id + " — " + name;

        private sealed class SelectorItem
        {
            public SelectorItem(string id, string text) { Id = id; Text = text; }
            public string Id { get; }
            public string Text { get; }
            public override string ToString() => Text;
        }
    }
}

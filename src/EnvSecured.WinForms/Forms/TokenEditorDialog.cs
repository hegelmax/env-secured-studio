using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using EnvSecured.Core.Models;
using EnvSecured.Core.Services;

namespace EnvSecured.WinForms.Forms
{
    internal sealed class TokenEditResult
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public string ServiceId { get; set; }
        public string EnvironmentId { get; set; }
        public string ExpiresAt { get; set; }
        public List<string> Grants { get; set; }
    }

    internal sealed class TokenEditorDialog : Form
    {
        private readonly TextBox idBox = new TextBox { Dock = DockStyle.Fill };
        private readonly TextBox nameBox = new TextBox { Dock = DockStyle.Fill };
        private readonly ComboBox typeBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly ComboBox serviceBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly ComboBox environmentBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly TextBox expiresBox = new TextBox { Dock = DockStyle.Fill };
        private readonly ListBox grantsList = new ListBox { Dock = DockStyle.Fill, Font = new Font("Consolas", 9f), HorizontalScrollbar = true };
        private readonly GrantService grantService = new GrantService();
        private readonly TokenScopePolicyService tokenScopes = new TokenScopePolicyService();
        private readonly ProjectModel project;
        private readonly bool isNew;
        private bool initializing = true;

        private TokenEditorDialog(ProjectModel project, AccessTokenModel token)
        {
            this.project = project;
            isNew = token == null;
            Text = token == null ? "Create Access Token" : "Edit Access Token";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(720, 480);
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;

            typeBox.Items.AddRange(new object[] { "Custom", "ServiceEnvironment", "Service", "Environment" });
            foreach (var service in project.Services.Where(x => x.IsActive).OrderBy(x => x.SortOrder).ThenBy(x => x.Name))
                serviceBox.Items.Add(new ScopeItem(service.Id, service.Name));
            foreach (var environment in project.Environments.Where(x => x.IsActive).OrderBy(x => x.SortOrder).ThenBy(x => x.Name))
                environmentBox.Items.Add(new ScopeItem(environment.Id, environment.Name));
            typeBox.SelectedItem = token?.Type ?? "Custom";
            SelectScope(serviceBox, tokenScopes.ResolveServiceId(token));
            SelectScope(environmentBox, tokenScopes.ResolveEnvironmentId(token));
            idBox.Text = token?.Id ?? string.Empty;
            idBox.ReadOnly = token != null;
            nameBox.Text = token?.Name ?? string.Empty;
            expiresBox.Text = token?.ExpiresAt ?? string.Empty;
            foreach (var grant in token?.Grants ?? new List<string>()) grantsList.Items.Add(grant);

            Size = new Size(760, 560);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 8, Padding = new Padding(12) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            AddRow(layout, 0, "Id:", idBox);
            AddRow(layout, 1, "Name:", nameBox);
            AddRow(layout, 2, "Type:", typeBox);
            AddRow(layout, 3, "Service:", serviceBox);
            AddRow(layout, 4, "Environment:", environmentBox);
            AddRow(layout, 5, "Expires (UTC):", expiresBox);
            var grantsPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            grantsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            grantsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            grantsPanel.Controls.Add(grantsList, 0, 0);
            var grantButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 4, 0, 0) };
            var addGrant = new Button { Text = "Add rule...", Width = 100, Height = 27 };
            var editGrant = new Button { Text = "Edit...", Width = 82, Height = 27 };
            var removeGrant = new Button { Text = "Remove", Width = 82, Height = 27 };
            var advancedGrant = new Button { Text = "Advanced text...", Width = 120, Height = 27 };
            addGrant.Click += (s, e) => AddGrant();
            editGrant.Click += (s, e) => EditGrant();
            removeGrant.Click += (s, e) => RemoveGrant();
            advancedGrant.Click += (s, e) => EditGrantsAsText();
            grantsList.DoubleClick += (s, e) => EditGrant();
            grantButtons.Controls.AddRange(new Control[] { addGrant, editGrant, removeGrant, advancedGrant });
            grantsPanel.Controls.Add(grantButtons, 0, 1);
            AddRow(layout, 6, "Grant rules:", grantsPanel);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            var ok = new Button { Text = "OK", Width = 90, Height = 28 };
            var cancel = new Button { Text = "Cancel", Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
            ok.Click += (s, e) => ValidateAndClose();
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            layout.Controls.Add(buttons, 1, 7);
            Controls.Add(layout);
            AcceptButton = ok;
            CancelButton = cancel;
            typeBox.SelectedIndexChanged += (s, e) => ScopeSelectionChanged();
            serviceBox.SelectedIndexChanged += (s, e) => ScopeSelectionChanged();
            environmentBox.SelectedIndexChanged += (s, e) => ScopeSelectionChanged();
            initializing = false;
            UpdateScopeControls();
            if (isNew && Convert.ToString(typeBox.SelectedItem) != "Custom") ApplyTypeDefaults(true);
        }

        public TokenEditResult Result { get; private set; }

        public static TokenEditResult Show(IWin32Window owner, ProjectModel project, AccessTokenModel token = null)
        {
            using (var dialog = new TokenEditorDialog(project, token))
            {
                return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.Result : null;
            }
        }

        public static void ShowSecret(IWin32Window owner, string title, string secret)
        {
            secret = secret ?? string.Empty;
            var multiple = secret.IndexOfAny(new[] { '\r', '\n' }) >= 0;
            using (var dialog = new Form
            {
                Text = title,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.Sizable,
                ClientSize = multiple ? new Size(940, 500) : new Size(820, 220),
                MinimumSize = multiple ? new Size(720, 360) : new Size(680, 210),
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = false
            })
            {
                var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(12) };
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
                var label = new Label
                {
                    Dock = DockStyle.Fill,
                    Text = multiple
                        ? "Created token secrets. Copy or save the complete list now and store it securely."
                        : "Bearer token: anyone who copies it receives its granted access.",
                    TextAlign = ContentAlignment.MiddleLeft
                };
                var box = new TextBox
                {
                    Dock = DockStyle.Fill,
                    ReadOnly = true,
                    Multiline = true,
                    WordWrap = false,
                    ScrollBars = ScrollBars.Both,
                    Text = secret,
                    Font = new Font("Consolas", 9f)
                };
                var buttons = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.RightToLeft,
                    WrapContents = false,
                    Padding = new Padding(0, 8, 0, 0)
                };
                var close = new Button { Width = 90, Height = 28, Text = "Close", DialogResult = DialogResult.OK };
                var copy = new Button { Width = 100, Height = 28, Text = multiple ? "Copy All" : "Copy" };
                var save = new Button { Width = 100, Height = 28, Text = "Save..." };
                copy.Click += (s, e) => Clipboard.SetText(secret);
                save.Click += (s, e) =>
                {
                    using (var saveDialog = new SaveFileDialog
                    {
                        Title = "Save Access Tokens",
                        Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                        FileName = multiple ? "envsecured-access-tokens.txt" : "envsecured-access-token.txt"
                    })
                    {
                        if (saveDialog.ShowDialog(dialog) != DialogResult.OK) return;
                        try
                        {
                            File.WriteAllText(saveDialog.FileName, secret);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(dialog, ex.Message, "Save Access Tokens", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                };
                buttons.Controls.Add(close);
                buttons.Controls.Add(copy);
                buttons.Controls.Add(save);
                layout.Controls.Add(label, 0, 0);
                layout.Controls.Add(box, 0, 1);
                layout.Controls.Add(buttons, 0, 2);
                dialog.Controls.Add(layout);
                dialog.AcceptButton = close;
                dialog.ShowDialog(owner);
            }
        }

        private void ValidateAndClose()
        {
            try
            {
                var id = idBox.Text.Trim().ToLowerInvariant();
                var type = tokenScopes.NormalizeType(Convert.ToString(typeBox.SelectedItem));
                var serviceId = SelectedScopeId(serviceBox);
                var environmentId = SelectedScopeId(environmentBox);
                if ((type == "Service" || type == "ServiceEnvironment") && string.IsNullOrWhiteSpace(serviceId))
                    throw new InvalidOperationException(type + " token requires a service.");
                if ((type == "Environment" || type == "ServiceEnvironment") && string.IsNullOrWhiteSpace(environmentId))
                    throw new InvalidOperationException(type + " token requires an environment.");
                if (string.IsNullOrWhiteSpace(id))
                {
                    id = tokenScopes.DefaultId(type, serviceId, environmentId);
                    idBox.Text = id ?? string.Empty;
                }
                if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("Custom token id is required.");
                var rules = grantsList.Items.Cast<object>().Select(Convert.ToString).Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(grantService.Normalize).Distinct(StringComparer.Ordinal).ToList();
                if (rules.Count == 0) throw new InvalidOperationException("At least one grant is required.");
                if (!string.IsNullOrWhiteSpace(expiresBox.Text) && !DateTime.TryParse(expiresBox.Text, out _))
                    throw new InvalidOperationException("Expires must be an ISO-8601 date/time or blank.");
                var candidate = new AccessTokenModel
                {
                    Id = id,
                    Name = string.IsNullOrWhiteSpace(nameBox.Text) ? id : nameBox.Text.Trim(),
                    Type = type,
                    ServiceId = type == "Service" || type == "ServiceEnvironment" ? serviceId : null,
                    EnvironmentId = type == "Environment" || type == "ServiceEnvironment" ? environmentId : null,
                    ExpiresAt = string.IsNullOrWhiteSpace(expiresBox.Text) ? null : DateTime.Parse(expiresBox.Text).ToUniversalTime().ToString("o"),
                    Grants = rules
                };
                tokenScopes.Validate(project, candidate);
                Result = new TokenEditResult
                {
                    Id = candidate.Id,
                    Name = candidate.Name,
                    Type = candidate.Type,
                    ServiceId = candidate.ServiceId,
                    EnvironmentId = candidate.EnvironmentId,
                    ExpiresAt = candidate.ExpiresAt,
                    Grants = candidate.Grants
                };
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Access Token", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void AddGrant()
        {
            var rule = GrantRuleBuilderDialog.Show(this, project, null, FixedServiceId(), FixedEnvironmentId());
            if (rule == null) return;
            if (!ValidateRuleForCurrentScope(rule)) return;
            if (!grantsList.Items.Cast<object>().Any(x => string.Equals(Convert.ToString(x), rule, StringComparison.Ordinal)))
                grantsList.Items.Add(rule);
            grantsList.SelectedItem = rule;
        }

        private void EditGrant()
        {
            if (grantsList.SelectedIndex < 0) return;
            var rule = GrantRuleBuilderDialog.Show(this, project, Convert.ToString(grantsList.SelectedItem), FixedServiceId(), FixedEnvironmentId());
            if (rule == null) return;
            if (!ValidateRuleForCurrentScope(rule)) return;
            grantsList.Items[grantsList.SelectedIndex] = rule;
        }

        private void ScopeSelectionChanged()
        {
            if (initializing) return;
            UpdateScopeControls();
            ApplyTypeDefaults(true);
        }

        private void UpdateScopeControls()
        {
            var type = tokenScopes.NormalizeType(Convert.ToString(typeBox.SelectedItem));
            serviceBox.Enabled = type == "Service" || type == "ServiceEnvironment";
            environmentBox.Enabled = type == "Environment" || type == "ServiceEnvironment";
        }

        private void ApplyTypeDefaults(bool replaceGrants)
        {
            var type = tokenScopes.NormalizeType(Convert.ToString(typeBox.SelectedItem));
            if (type == "Custom") return;
            var serviceId = SelectedScopeId(serviceBox);
            var environmentId = SelectedScopeId(environmentBox);
            var defaultId = tokenScopes.DefaultId(type, serviceId, environmentId);
            var defaultGrant = tokenScopes.DefaultGrant(type, serviceId, environmentId);
            if (isNew && !string.IsNullOrWhiteSpace(defaultId)) idBox.Text = defaultId;
            if (string.IsNullOrWhiteSpace(nameBox.Text))
            {
                if (type == "Service") nameBox.Text = SelectedScopeName(serviceBox);
                else if (type == "Environment") nameBox.Text = SelectedScopeName(environmentBox);
                else nameBox.Text = SelectedScopeName(serviceBox) + " / " + SelectedScopeName(environmentBox);
            }
            if (replaceGrants && !string.IsNullOrWhiteSpace(defaultGrant))
            {
                grantsList.Items.Clear();
                grantsList.Items.Add(defaultGrant);
                grantsList.SelectedIndex = 0;
            }
        }

        private bool ValidateRuleForCurrentScope(string rule)
        {
            try
            {
                var type = tokenScopes.NormalizeType(Convert.ToString(typeBox.SelectedItem));
                tokenScopes.Validate(project, new AccessTokenModel
                {
                    Type = type,
                    ServiceId = type == "Service" || type == "ServiceEnvironment" ? SelectedScopeId(serviceBox) : null,
                    EnvironmentId = type == "Environment" || type == "ServiceEnvironment" ? SelectedScopeId(environmentBox) : null,
                    Grants = new List<string> { rule }
                });
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Grant Scope", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        private static void SelectScope(ComboBox box, string id)
        {
            var index = 0;
            if (!string.IsNullOrWhiteSpace(id))
            {
                for (var i = 0; i < box.Items.Count; i++)
                    if (string.Equals(((ScopeItem)box.Items[i]).Id, id, StringComparison.Ordinal)) { index = i; break; }
            }
            if (box.Items.Count > 0) box.SelectedIndex = index;
        }

        private static string SelectedScopeId(ComboBox box) => (box.SelectedItem as ScopeItem)?.Id;
        private static string SelectedScopeName(ComboBox box) => (box.SelectedItem as ScopeItem)?.Name ?? string.Empty;
        private string FixedServiceId()
        {
            var type = tokenScopes.NormalizeType(Convert.ToString(typeBox.SelectedItem));
            return type == "Service" || type == "ServiceEnvironment" ? SelectedScopeId(serviceBox) : null;
        }
        private string FixedEnvironmentId()
        {
            var type = tokenScopes.NormalizeType(Convert.ToString(typeBox.SelectedItem));
            return type == "Environment" || type == "ServiceEnvironment" ? SelectedScopeId(environmentBox) : null;
        }

        private sealed class ScopeItem
        {
            public ScopeItem(string id, string name) { Id = id; Name = string.IsNullOrWhiteSpace(name) ? id : name; }
            public string Id { get; }
            public string Name { get; }
            public override string ToString() => string.Equals(Id, Name, StringComparison.OrdinalIgnoreCase) ? Id : Id + " — " + Name;
        }

        private void RemoveGrant()
        {
            var index = grantsList.SelectedIndex;
            if (index < 0) return;
            grantsList.Items.RemoveAt(index);
            if (grantsList.Items.Count > 0) grantsList.SelectedIndex = Math.Min(index, grantsList.Items.Count - 1);
        }

        private void EditGrantsAsText()
        {
            using (var dialog = new Form
            {
                Text = "Advanced Grant Rules",
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.Sizable,
                ClientSize = new Size(700, 360),
                MinimizeBox = false,
                ShowInTaskbar = false
            })
            {
                var box = new TextBox
                {
                    Dock = DockStyle.Fill,
                    Multiline = true,
                    ScrollBars = ScrollBars.Both,
                    Font = new Font("Consolas", 9f),
                    Text = string.Join(Environment.NewLine, grantsList.Items.Cast<object>().Select(Convert.ToString))
                };
                var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
                var ok = new Button { Text = "Apply", Width = 90, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel };
                buttons.Controls.Add(ok);
                buttons.Controls.Add(cancel);
                dialog.Controls.Add(box);
                dialog.Controls.Add(buttons);
                dialog.AcceptButton = ok;
                dialog.CancelButton = cancel;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var rules = box.Lines.Select(x => x.Trim()).Where(x => x.Length > 0).Select(grantService.Normalize).Distinct(StringComparer.Ordinal).ToList();
                    grantsList.Items.Clear();
                    foreach (var rule in rules) grantsList.Items.Add(rule);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Grant Rules", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private static void AddRow(TableLayoutPanel layout, int row, string label, Control control)
        {
            layout.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
            layout.Controls.Add(control, 1, row);
        }
    }
}

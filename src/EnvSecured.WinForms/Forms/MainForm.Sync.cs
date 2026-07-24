using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using EnvSecured.Core.Models;
using EnvSecured.Core.Services;

namespace EnvSecured.WinForms.Forms
{
    public sealed partial class MainForm
    {
        private const string PreferenceGitHubCliPath = "Sync.GitHubCliPath";
        private readonly List<GitHubSyncPreviewRow> githubSyncPreviewRows = new List<GitHubSyncPreviewRow>();

        private void RenderSyncProvidersView()
        {
            ProjectService.EnsureProjectCollections(project);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 6, ColumnCount = 1, Padding = new Padding(8) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 210));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 70));

            root.Controls.Add(BuildPageHeader("Synchronization / Providers", "Configure external sync providers. GitHub support starts with encrypted provider settings and local CLI availability checks."), 0, 0);

            var commands = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            var ghPath = GetGitHubCliPath();
            var addGitHub = AddCommand(commands, "Add GitHub", AddGitHubSyncProvider, "Sync", 118);
            addGitHub.Enabled = ghPath != null;
            AddCommand(commands, "Edit", EditSyncProvider, "Edit", 86);
            AddCommand(commands, "Delete", DeleteSyncProvider, "Delete", 96);
            AddCommand(commands, "Test CLI", TestGitHubCliForSelectedProvider, "Validate", 96);
            AddCommand(commands, "Preview Changes", PreviewGitHubSyncChanges, "Validate", 132);
            root.Controls.Add(commands, 0, 1);

            root.Controls.Add(BuildGitHubCliStatusPanel(ghPath), 0, 2);

            mainGrid = BuildSyncProvidersGrid();
            root.Controls.Add(mainGrid, 0, 3);
            root.Controls.Add(BuildGitHubSyncScopeModePanel(SelectedSyncProviderOrFirst()), 0, 4);
            root.Controls.Add(BuildGitHubSyncPreviewPanel(), 0, 5);
            contentPanel.Controls.Add(root);
        }

        private Control BuildGitHubCliStatusPanel(string ghPath)
        {
            if (ghPath != null)
            {
                return new Label
                {
                    Text = "GitHub CLI: " + ghPath,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft,
                    ForeColor = Color.FromArgb(0, 120, 40)
                };
            }

            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            panel.Controls.Add(new Label
            {
                Text = "GitHub CLI: not found. Install GitHub CLI (`gh`) to enable GitHub Actions provider configuration:",
                AutoSize = true,
                Padding = new Padding(0, 8, 4, 0),
                ForeColor = Color.Firebrick
            });
            var link = new LinkLabel
            {
                Text = "https://cli.github.com/",
                AutoSize = true,
                Padding = new Padding(0, 8, 0, 0)
            };
            link.LinkClicked += (s, e) => OpenExternalUrl("https://cli.github.com/");
            panel.Controls.Add(link);
            return panel;
        }

        private void OpenExternalUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Cannot open link.\r\n\r\n" + ex.Message, "Open Link", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private DataGridView BuildSyncProvidersGrid()
        {
            var grid = BuildGrid();
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "Id", Visible = false });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Name", FillWeight = 130 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Type", HeaderText = "Type", FillWeight = 120 });
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = "Enabled", FillWeight = 55 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Organization", HeaderText = "Organization", FillWeight = 110 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Repository", HeaderText = "Repository", FillWeight = 130 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Environment", HeaderText = "Environment", FillWeight = 100 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Auth", HeaderText = "Auth", FillWeight = 80 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", FillWeight = 120 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "LastChecked", HeaderText = "Last checked", FillWeight = 110 });

            foreach (var provider in project.Sync.Providers.OrderBy(p => p.Name).ThenBy(p => p.Id))
            {
                grid.Rows.Add(
                    provider.Id,
                    provider.Name,
                    ProviderTypeDisplayName(provider.Type),
                    provider.IsEnabled,
                    ProviderSetting(provider, "organization"),
                    ProviderSetting(provider, "repository"),
                    ProviderEnvironmentSummary(provider),
                    ProviderSetting(provider, "authMode"),
                    provider.LastStatus,
                    provider.LastCheckedAt);
            }

            AttachColumnWidthPersistence(grid, "SyncProviders");
            RestoreColumnWidths(grid, "SyncProviders");
            return grid;
        }

        private SyncProviderModel SelectedSyncProviderOrFirst()
        {
            return SelectedSyncProvider() ?? project.Sync.Providers.OrderBy(p => p.Name).ThenBy(p => p.Id).FirstOrDefault();
        }

        private Control BuildGitHubSyncScopeModePanel(SyncProviderModel provider)
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));

            var scopeGroup = new GroupBox { Dock = DockStyle.Fill, Text = "Sync Scope" };
            var scope = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Padding = new Padding(8) };
            scope.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            scope.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            scope.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));

            scope.Controls.Add(BuildSyncEnvironmentChecks(provider), 0, 0);
            scope.Controls.Add(BuildSyncServiceChecks(provider), 1, 0);
            scope.Controls.Add(BuildSyncFilterChecks(provider), 2, 0);
            scopeGroup.Controls.Add(scope);

            var modeGroup = new GroupBox { Dock = DockStyle.Fill, Text = "Sync Mode" };
            var modePanel = BuildSyncModeRadios(provider);
            modeGroup.Controls.Add(modePanel);

            root.Controls.Add(scopeGroup, 0, 0);
            root.Controls.Add(modeGroup, 1, 0);
            return root;
        }

        private Control BuildSyncEnvironmentChecks(SyncProviderModel provider)
        {
            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            panel.Controls.Add(new Label { Text = "Environments", Font = new Font(Font, FontStyle.Bold), AutoSize = true });
            var selected = LoadProviderStringSet(provider, "syncEnvironments");
            foreach (var mapping in ProviderEnvironmentMappings(provider))
            {
                var check = new CheckBox
                {
                    Text = mapping.LocalEnvironment,
                    AutoSize = true,
                    Checked = selected.Count == 0 || selected.Contains(mapping.LocalEnvironment),
                    Tag = mapping.LocalEnvironment
                };
                check.CheckedChanged += (s, e) => SaveCheckedStringSet(provider, panel, "syncEnvironments");
                panel.Controls.Add(check);
            }
            return panel;
        }

        private Control BuildSyncServiceChecks(SyncProviderModel provider)
        {
            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            panel.Controls.Add(new Label { Text = "Services", Font = new Font(Font, FontStyle.Bold), AutoSize = true });
            var selected = LoadProviderStringSet(provider, "syncServices");
            foreach (var service in project.Services.Where(s => s.IsActive).OrderBy(s => s.SortOrder).ThenBy(s => s.Name))
            {
                var check = new CheckBox
                {
                    Text = service.Name,
                    AutoSize = true,
                    Checked = selected.Count == 0 || selected.Contains(service.Id),
                    Tag = service.Id
                };
                check.CheckedChanged += (s, e) => SaveCheckedStringSet(provider, panel, "syncServices");
                panel.Controls.Add(check);
            }
            return panel;
        }

        private Control BuildSyncFilterChecks(SyncProviderModel provider)
        {
            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            panel.Controls.Add(new Label { Text = "Variable Filters", Font = new Font(Font, FontStyle.Bold), AutoSize = true });
            AddProviderBoolCheck(panel, provider, "syncSecrets", "Secrets", true);
            AddProviderBoolCheck(panel, provider, "syncVariables", "Variables", true);
            panel.Controls.Add(new CheckBox { Text = "Sync Enabled Only", Checked = true, Enabled = false, AutoSize = true });
            AddProviderBoolCheck(panel, provider, "syncRequiredOnly", "Required Only", false);
            return panel;
        }

        private void AddProviderBoolCheck(FlowLayoutPanel panel, SyncProviderModel provider, string key, string text, bool defaultValue)
        {
            var check = new CheckBox
            {
                Text = text,
                AutoSize = true,
                Checked = ProviderBool(provider, key, defaultValue)
            };
            check.CheckedChanged += (s, e) =>
            {
                SetProviderSetting(provider, key, check.Checked ? "true" : "false");
                SyncProviderSettingsChanged();
            };
            panel.Controls.Add(check);
        }

        private Control BuildSyncModeRadios(SyncProviderModel provider)
        {
            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(8) };
            var current = ProviderSetting(provider, "syncMode");
            if (string.IsNullOrWhiteSpace(current)) current = "AddUpdate";
            AddSyncModeRadio(panel, provider, "AddMissing", "Add Missing Only", "Create only missing variables/secrets", current);
            AddSyncModeRadio(panel, provider, "AddUpdate", "Add and Update", "Create and update changed variables/secrets", current);
            AddSyncModeRadio(panel, provider, "Mirror", "Mirror (Delete Extra)", "Make remote exactly the same as local", current);
            AddSyncModeRadio(panel, provider, "ForceReplace", "Force Replace", "Delete all and replace with local", current);
            return panel;
        }

        private void AddSyncModeRadio(FlowLayoutPanel panel, SyncProviderModel provider, string value, string title, string description, string current)
        {
            var radio = new RadioButton { Text = title, AutoSize = true, Checked = string.Equals(current, value, StringComparison.OrdinalIgnoreCase), Tag = value };
            radio.CheckedChanged += (s, e) =>
            {
                if (!radio.Checked) return;
                SetProviderSetting(provider, "syncMode", Convert.ToString(radio.Tag));
                SyncProviderSettingsChanged();
            };
            panel.Controls.Add(radio);
            panel.Controls.Add(new Label { Text = description, AutoSize = true, ForeColor = Color.FromArgb(80, 90, 105), Padding = new Padding(20, 0, 0, 6) });
        }

        private Control BuildGitHubSyncPreviewPanel()
        {
            var group = new GroupBox { Dock = DockStyle.Fill, Text = "Preview Changes" };
            var grid = BuildGrid();
            grid.Name = "GitHubSyncPreviewGrid";
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Action", HeaderText = "Action", FillWeight = 70 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Type", HeaderText = "Type", FillWeight = 60 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Key", HeaderText = "Key", FillWeight = 150 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Local", HeaderText = "Local", FillWeight = 140 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Remote", HeaderText = "Remote", FillWeight = 140 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "VaultEnvironment", HeaderText = "Vault env", FillWeight = 90 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "GitHubEnvironment", HeaderText = "GitHub env", FillWeight = 90 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Service", HeaderText = "Service", FillWeight = 90 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", FillWeight = 110 });
            foreach (var row in githubSyncPreviewRows)
            {
                grid.Rows.Add(row.Action, row.Type, row.Key, row.LocalValue, row.RemoteValue, row.VaultEnvironment, row.GitHubEnvironment, row.Service, row.Status);
            }
            group.Controls.Add(grid);
            return group;
        }

        private void AddGitHubSyncProvider()
        {
            if (GetGitHubCliPath() == null)
            {
                MessageBox.Show(this, "GitHub CLI (`gh`) is not available on this computer. GitHub provider is blocked until GitHub CLI is installed.\r\n\r\nDownload:\r\nhttps://cli.github.com/", "GitHub Provider", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!EnsureGitHubAuthenticatedInteractive(this))
            {
                return;
            }

            var provider = new SyncProviderModel
            {
                Id = ProjectService.NewId(),
                Type = SyncProviderGitHubActions,
                Name = "GitHub Actions",
                IsEnabled = true,
                Settings = new Dictionary<string, string>
                {
                    { "authMode", "GitHub CLI" }
                }
            };

            if (!ShowGitHubProviderDialog(provider, true))
            {
                return;
            }

            project.Sync.Providers.Add(provider);
            Changed();
            RenderCurrentView();
        }

        private void EditSyncProvider()
        {
            var provider = SelectedSyncProvider();
            if (provider == null) return;
            if (!string.Equals(provider.Type, SyncProviderGitHubActions, StringComparison.Ordinal))
            {
                MessageBox.Show(this, "Only GitHub Actions providers are supported in this release.", "Sync Provider", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (ShowGitHubProviderDialog(provider, false))
            {
                Changed();
                RenderCurrentView();
            }
        }

        private void DeleteSyncProvider()
        {
            var provider = SelectedSyncProvider();
            if (provider == null) return;
            if (MessageBox.Show(this, $"Delete sync provider '{provider.Name}'?", "Delete Provider", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            project.Sync.Providers.Remove(provider);
            Changed();
            RenderCurrentView();
        }

        private void TestGitHubCliForSelectedProvider()
        {
            var provider = SelectedSyncProvider();
            var ghPath = GetGitHubCliPath();
            if (provider != null)
            {
                provider.LastCheckedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                provider.LastStatus = ghPath == null ? "GitHub CLI not found" : "GitHub CLI available";
                Changed();
            }

            MessageBox.Show(this, ghPath == null ? "GitHub CLI (`gh`) was not found.\r\n\r\nDownload:\r\nhttps://cli.github.com/" : "GitHub CLI found:\r\n\r\n" + ghPath, "GitHub CLI", MessageBoxButtons.OK, ghPath == null ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            RenderCurrentView();
        }

        private void PreviewGitHubSyncChanges()
        {
            var provider = SelectedSyncProvider();
            if (provider == null)
            {
                MessageBox.Show(this, "Select a GitHub provider first.", "GitHub Preview", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!string.Equals(provider.Type, SyncProviderGitHubActions, StringComparison.Ordinal))
            {
                MessageBox.Show(this, "Only GitHub Actions providers are supported in this release.", "GitHub Preview", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var organization = ProviderSetting(provider, "organization");
            var repository = ProviderSetting(provider, "repository");
            if (string.IsNullOrWhiteSpace(organization) || string.IsNullOrWhiteSpace(repository))
            {
                MessageBox.Show(this, "Provider organization and repository are required.", "GitHub Preview", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            githubSyncPreviewRows.Clear();
            try
            {
                foreach (var mapping in ProviderSyncEnvironmentMappings(provider))
                {
                    var localRows = BuildLocalGitHubSyncRows(provider, mapping.LocalEnvironment, mapping.RemoteEnvironment);
                    var remote = LoadRemoteGitHubEnvironmentValues(organization, repository, mapping.RemoteEnvironment);
                    AddGitHubSyncPreviewRows(localRows, remote, mapping, provider);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Cannot preview GitHub sync.\r\n\r\n" + ex.Message, "GitHub Preview", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            RenderCurrentView();
        }

        private List<GitHubEnvironmentMapping> ProviderEnvironmentMappings(SyncProviderModel provider)
        {
            var map = LoadProviderEnvironmentMap(provider);
            var included = LoadProviderEnvironmentInclude(provider);
            return map
                .Where(p => included.Count == 0 || included.Contains(p.Key))
                .Where(p => !string.IsNullOrWhiteSpace(p.Key) && !string.IsNullOrWhiteSpace(p.Value))
                .OrderBy(p => p.Key)
                .Select(p => new GitHubEnvironmentMapping(p.Key, p.Value))
                .ToList();
        }

        private List<GitHubEnvironmentMapping> ProviderSyncEnvironmentMappings(SyncProviderModel provider)
        {
            var selected = LoadProviderStringSet(provider, "syncEnvironments");
            return ProviderEnvironmentMappings(provider)
                .Where(m => selected.Count == 0 || selected.Contains(m.LocalEnvironment))
                .ToList();
        }

        private HashSet<string> LoadProviderStringSet(SyncProviderModel provider, string key)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var json = ProviderSetting(provider, key);
            if (string.IsNullOrWhiteSpace(json)) return result;
            try
            {
                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var values = serializer.Deserialize<string[]>(json);
                if (values == null) return result;
                foreach (var value in values.Where(v => !string.IsNullOrWhiteSpace(v)))
                {
                    result.Add(value);
                }
            }
            catch
            {
            }
            return result;
        }

        private void SaveCheckedStringSet(SyncProviderModel provider, Control parent, string key)
        {
            var values = parent.Controls
                .OfType<CheckBox>()
                .Where(c => c.Checked && c.Tag != null)
                .Select(c => Convert.ToString(c.Tag))
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToArray();
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            SetProviderSetting(provider, key, serializer.Serialize(values));
            SyncProviderSettingsChanged();
        }

        private void SyncProviderSettingsChanged()
        {
            githubSyncPreviewRows.Clear();
            Changed();
        }

        private static bool ProviderBool(SyncProviderModel provider, string key, bool defaultValue)
        {
            var value = ProviderSetting(provider, key);
            if (string.IsNullOrWhiteSpace(value)) return defaultValue;
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        private List<GitHubLocalSyncValue> BuildLocalGitHubSyncRows(SyncProviderModel provider, string localEnvironmentName, string remoteEnvironmentName)
        {
            var environment = project.Environments.FirstOrDefault(e =>
                string.Equals(e.Name, localEnvironmentName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(e.Id, localEnvironmentName, StringComparison.OrdinalIgnoreCase));
            if (environment == null)
            {
                return new List<GitHubLocalSyncValue>();
            }

            var byKey = new Dictionary<string, GitHubLocalSyncValue>(StringComparer.OrdinalIgnoreCase);
            var selectedServices = LoadProviderStringSet(provider, "syncServices");
            var includeSecrets = ProviderBool(provider, "syncSecrets", true);
            var includeVariables = ProviderBool(provider, "syncVariables", true);
            var requiredOnly = ProviderBool(provider, "syncRequiredOnly", false);

            foreach (var service in project.Services.Where(s => s.IsActive).OrderBy(s => s.SortOrder).ThenBy(s => s.Name))
            {
                if (selectedServices.Count > 0 && !selectedServices.Contains(service.Id))
                {
                    continue;
                }

                var effectiveValues = effectiveConfigService.Build(project, service.Id, environment.Id)
                    .Where(x => !x.Missing)
                    .Where(x => ProjectService.IsVariableVisibleToService(project, x.Variable, service.Id))
                    .Where(x => ProjectService.IsVariableSyncEnabledForService(project, x.Variable.Id, service.Id));

                foreach (var effective in effectiveValues)
                {
                    var variable = effective.Variable;
                    if (variable == null) continue;
                    if (variable.IsSecret && !includeSecrets) continue;
                    if (!variable.IsSecret && !includeVariables) continue;
                    if (requiredOnly && !IsRequiredForService(variable.Id, service.Id)) continue;

                    var value = effective.Value ?? string.Empty;
                    if (byKey.TryGetValue(variable.Key, out var existing))
                    {
                        if (!string.Equals(existing.Value, value, StringComparison.Ordinal))
                        {
                            githubSyncPreviewRows.Add(new GitHubSyncPreviewRow
                            {
                                Action = "Conflict",
                                Type = variable.IsSecret ? "Secret" : "Variable",
                                Key = variable.Key,
                                LocalValue = Mask(variable, existing.Value) + " / " + Mask(variable, value),
                                RemoteValue = "-",
                                VaultEnvironment = localEnvironmentName,
                                GitHubEnvironment = remoteEnvironmentName,
                                Service = existing.ServiceName + " / " + service.Name,
                                Status = "Different local values"
                            });
                        }
                        continue;
                    }

                    byKey[variable.Key] = new GitHubLocalSyncValue
                    {
                        Key = variable.Key,
                        Value = value,
                        IsSecret = variable.IsSecret,
                        ServiceName = service.Name
                    };
                }
            }

            return byKey.Values.OrderBy(v => v.Key).ToList();
        }

        private bool IsRequiredForService(string variableId, string serviceId)
        {
            return project.Contracts.Any(c =>
                c.VariableId == variableId &&
                c.ServiceId == serviceId &&
                ProjectService.IsContractSyncEnabled(c) &&
                c.Required);
        }

        private GitHubRemoteEnvironmentValues LoadRemoteGitHubEnvironmentValues(string organization, string repository, string environment)
        {
            var result = new GitHubRemoteEnvironmentValues();
            var ghPath = GetGitHubCliPath();
            if (ghPath == null)
            {
                throw new InvalidOperationException("GitHub CLI (`gh`) was not found.");
            }

            var basePath = "repos/" + organization.Trim() + "/" + repository.Trim() + "/environments/" + Uri.EscapeDataString(environment.Trim());
            if (RunGitHubCommand(ghPath, "api " + basePath + "/variables --paginate", out var variablesJson, out _))
            {
                ReadGitHubVariables(variablesJson, result.Variables);
            }

            if (RunGitHubCommand(ghPath, "api " + basePath + "/secrets --paginate", out var secretsJson, out _))
            {
                ReadGitHubSecrets(secretsJson, result.Secrets);
            }

            return result;
        }

        private static void ReadGitHubVariables(string json, Dictionary<string, string> target)
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            foreach (var root in ReadGitHubJsonObjectPages(json))
            {
                if (root == null || !root.TryGetValue("variables", out var raw) || !(raw is IEnumerable items)) continue;
                foreach (var item in items.Cast<object>().OfType<Dictionary<string, object>>())
                {
                    var name = item.TryGetValue("name", out var rawName) ? Convert.ToString(rawName) : null;
                    var value = item.TryGetValue("value", out var rawValue) ? Convert.ToString(rawValue) : string.Empty;
                    if (!string.IsNullOrWhiteSpace(name)) target[name] = value ?? string.Empty;
                }
            }
        }

        private static void ReadGitHubSecrets(string json, HashSet<string> target)
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            foreach (var root in ReadGitHubJsonObjectPages(json))
            {
                if (root == null || !root.TryGetValue("secrets", out var raw) || !(raw is IEnumerable items)) continue;
                foreach (var item in items.Cast<object>().OfType<Dictionary<string, object>>())
                {
                    var name = item.TryGetValue("name", out var rawName) ? Convert.ToString(rawName) : null;
                    if (!string.IsNullOrWhiteSpace(name)) target.Add(name);
                }
            }
        }

        private static IEnumerable<Dictionary<string, object>> ReadGitHubJsonObjectPages(string json)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            foreach (var page in SplitConcatenatedJsonObjects(json))
            {
                if (string.IsNullOrWhiteSpace(page)) continue;
                yield return serializer.Deserialize<Dictionary<string, object>>(page);
            }
        }

        private static IEnumerable<string> SplitConcatenatedJsonObjects(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) yield break;

            var start = -1;
            var depth = 0;
            var inString = false;
            var escaped = false;

            for (var i = 0; i < json.Length; i++)
            {
                var ch = json[i];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }
                    if (ch == '\\')
                    {
                        escaped = true;
                        continue;
                    }
                    if (ch == '"')
                    {
                        inString = false;
                    }
                    continue;
                }

                if (ch == '"')
                {
                    inString = true;
                    continue;
                }
                if (ch == '{')
                {
                    if (depth == 0) start = i;
                    depth++;
                    continue;
                }
                if (ch == '}')
                {
                    if (depth == 0) continue;
                    depth--;
                    if (depth == 0 && start >= 0)
                    {
                        yield return json.Substring(start, i - start + 1);
                        start = -1;
                    }
                }
            }

            if (start < 0) yield break;

            var remaining = json.Substring(start).Trim();
            if (!string.IsNullOrWhiteSpace(remaining))
            {
                yield return remaining;
            }
        }

        private void AddGitHubSyncPreviewRows(List<GitHubLocalSyncValue> localRows, GitHubRemoteEnvironmentValues remote, GitHubEnvironmentMapping mapping, SyncProviderModel provider)
        {
            var mode = ProviderSetting(provider, "syncMode");
            if (string.IsNullOrWhiteSpace(mode)) mode = "AddUpdate";
            var fingerprintKeyMaterial = ProviderSetting(provider, SecretFingerprintService.ProviderSettingKey);
            var localKeys = new HashSet<string>(localRows.Select(v => v.Key), StringComparer.OrdinalIgnoreCase);
            var localSecretKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var localVariableKeys = new HashSet<string>(localRows.Where(v => !v.IsSecret).Select(v => v.Key), StringComparer.OrdinalIgnoreCase);

            foreach (var local in localRows)
            {
                if (local.IsSecret)
                {
                    localSecretKeys.Add(local.Key);
                    var fingerprintVariable = SecretFingerprintService.VariableName(local.Key);
                    localVariableKeys.Add(fingerprintVariable);
                    if (localKeys.Contains(fingerprintVariable))
                    {
                        githubSyncPreviewRows.Add(new GitHubSyncPreviewRow
                        {
                            Action = "Conflict", Type = "Fingerprint", Key = fingerprintVariable,
                            LocalValue = "-", RemoteValue = "-", VaultEnvironment = mapping.LocalEnvironment,
                            GitHubEnvironment = mapping.RemoteEnvironment, Service = local.ServiceName,
                            Status = "Name is already used by a vault variable"
                        });
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(fingerprintKeyMaterial))
                    {
                        githubSyncPreviewRows.Add(new GitHubSyncPreviewRow
                        {
                            Action = "Blocked", Type = "Secret", Key = local.Key,
                            LocalValue = "********", RemoteValue = remote.Secrets.Contains(local.Key) ? "********" : "-",
                            VaultEnvironment = mapping.LocalEnvironment, GitHubEnvironment = mapping.RemoteEnvironment,
                            Service = local.ServiceName, Status = "Save provider settings once to initialize secret fingerprints"
                        });
                        continue;
                    }

                    var exists = remote.Secrets.Contains(local.Key);
                    var fingerprint = SecretFingerprintService.Compute(fingerprintKeyMaterial, provider.Id, mapping.RemoteEnvironment, local.Key, local.Value);
                    var hasFingerprint = remote.Variables.TryGetValue(fingerprintVariable, out var remoteFingerprint);
                    var fingerprintMatches = hasFingerprint && string.Equals(fingerprint, remoteFingerprint, StringComparison.Ordinal);
                    var secretAction = !exists ? "Create" :
                        string.Equals(mode, "AddMissing", StringComparison.OrdinalIgnoreCase) ? "Unchanged" :
                        string.Equals(mode, "ForceReplace", StringComparison.OrdinalIgnoreCase) ? "Replace" :
                        fingerprintMatches ? "Unchanged" : "Update";
                    githubSyncPreviewRows.Add(new GitHubSyncPreviewRow
                    {
                        Action = secretAction,
                        Type = "Secret",
                        Key = local.Key,
                        LocalValue = "********",
                        RemoteValue = exists ? "********" : "-",
                        VaultEnvironment = mapping.LocalEnvironment,
                        GitHubEnvironment = mapping.RemoteEnvironment,
                        Service = local.ServiceName,
                        Status = !exists ? "Missing" : fingerprintMatches ? "Fingerprint matches" : "Fingerprint missing or changed"
                    });

                    if (!exists || !string.Equals(mode, "AddMissing", StringComparison.OrdinalIgnoreCase) || fingerprintMatches)
                    {
                        githubSyncPreviewRows.Add(new GitHubSyncPreviewRow
                        {
                            Action = !hasFingerprint ? "Create" :
                                string.Equals(mode, "ForceReplace", StringComparison.OrdinalIgnoreCase) ? "Replace" :
                                fingerprintMatches ? "Unchanged" : "Update",
                            Type = "Fingerprint", Key = fingerprintVariable,
                            LocalValue = fingerprint, RemoteValue = hasFingerprint ? remoteFingerprint : "-",
                            VaultEnvironment = mapping.LocalEnvironment, GitHubEnvironment = mapping.RemoteEnvironment,
                            Service = local.ServiceName, Status = !hasFingerprint ? "Missing" : fingerprintMatches ? "Same" : "Changed"
                        });
                    }
                    continue;
                }

                var hasRemote = remote.Variables.TryGetValue(local.Key, out var remoteValue);
                var same = hasRemote && string.Equals(local.Value, remoteValue, StringComparison.Ordinal);
                var variableAction = !hasRemote ? "Create" :
                    string.Equals(mode, "AddMissing", StringComparison.OrdinalIgnoreCase) ? "Unchanged" :
                    string.Equals(mode, "ForceReplace", StringComparison.OrdinalIgnoreCase) ? "Replace" :
                    same ? "Unchanged" : "Update";
                githubSyncPreviewRows.Add(new GitHubSyncPreviewRow
                {
                    Action = variableAction,
                    Type = "Variable",
                    Key = local.Key,
                    LocalValue = local.Value,
                    RemoteValue = hasRemote ? remoteValue : "-",
                    VaultEnvironment = mapping.LocalEnvironment,
                    GitHubEnvironment = mapping.RemoteEnvironment,
                    Service = local.ServiceName,
                    Status = !hasRemote ? "Missing" : same ? "Same" : "Changed"
                });
            }

            if (!string.Equals(mode, "Mirror", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(mode, "ForceReplace", StringComparison.OrdinalIgnoreCase)) return;

            if (ProviderBool(provider, "syncSecrets", true))
            {
                foreach (var key in remote.Secrets.Where(k => !localSecretKeys.Contains(k)).OrderBy(k => k))
                {
                    githubSyncPreviewRows.Add(new GitHubSyncPreviewRow
                    {
                        Action = "Delete", Type = "Secret", Key = key, LocalValue = "-", RemoteValue = "********",
                        VaultEnvironment = mapping.LocalEnvironment, GitHubEnvironment = mapping.RemoteEnvironment,
                        Service = "-", Status = "Remote-only"
                    });
                }
            }
            var includeRemoteVariable = ProviderBool(provider, "syncVariables", true)
                ? new Func<KeyValuePair<string, string>, bool>(p => !localVariableKeys.Contains(p.Key))
                : p => ProviderBool(provider, "syncSecrets", true) &&
                    !localVariableKeys.Contains(p.Key) &&
                    remote.Secrets.Any(secret => string.Equals(SecretFingerprintService.VariableName(secret), p.Key, StringComparison.OrdinalIgnoreCase));
            foreach (var pair in remote.Variables.Where(includeRemoteVariable).OrderBy(p => p.Key))
            {
                githubSyncPreviewRows.Add(new GitHubSyncPreviewRow
                {
                    Action = "Delete", Type = "Variable", Key = pair.Key, LocalValue = "-", RemoteValue = pair.Value,
                    VaultEnvironment = mapping.LocalEnvironment, GitHubEnvironment = mapping.RemoteEnvironment,
                    Service = "-", Status = "Remote-only"
                });
            }
        }

        private SyncProviderModel SelectedSyncProvider()
        {
            if (mainGrid == null || mainGrid.SelectedRows.Count == 0) return null;
            var id = Convert.ToString(mainGrid.SelectedRows[0].Cells["Id"].Value);
            if (string.IsNullOrWhiteSpace(id)) return null;
            return project.Sync.Providers.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));
        }

        private bool ShowGitHubProviderDialog(SyncProviderModel provider, bool isNew)
        {
            provider.Settings = provider.Settings ?? new Dictionary<string, string>();

            using (var dialog = new Form())
            {
                dialog.Text = isNew ? "Add GitHub Provider" : "Edit GitHub Provider";
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.MinimizeBox = false;
                dialog.MaximizeBox = false;
                dialog.ClientSize = new Size(720, 470);

                var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 370));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

                var form = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5 };
                form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
                form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                for (var i = 0; i < 4; i++) form.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
                form.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

                var nameBox = new TextBox { Dock = DockStyle.Fill, Text = provider.Name ?? string.Empty };
                var orgBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown, Text = ProviderSetting(provider, "organization") };
                var repoBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown, Text = ProviderSetting(provider, "repository") };
                var enabledPanel = new Panel { Dock = DockStyle.Fill };
                var enabledBox = new CheckBox { AutoSize = true, Checked = provider.IsEnabled, Location = new Point(0, 8) };
                enabledPanel.Controls.Add(enabledBox);
                var testStatus = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(80, 90, 105) };
                var environmentMapGrid = BuildGitHubEnvironmentMapGrid(LoadProviderEnvironmentMap(provider), LoadProviderEnvironmentInclude(provider));
                var refreshEnvironmentsButton = new Button { Text = "Load GitHub environments", Width = 168, Height = 26, Margin = new Padding(0, 0, 8, 0) };
                refreshEnvironmentsButton.Click += (s, e) => RefreshGitHubEnvironmentList(dialog, orgBox.Text, repoBox.Text, environmentMapGrid);
                orgBox.DropDown += (s, e) => RefreshGitHubOrganizations(dialog, orgBox, false);
                orgBox.SelectedIndexChanged += (s, e) => RefreshGitHubRepositories(dialog, orgBox.Text, repoBox, false);
                repoBox.DropDown += (s, e) => RefreshGitHubRepositories(dialog, orgBox.Text, repoBox, false);

                form.Controls.Add(BuildFieldLabel("Name:"), 0, 0);
                form.Controls.Add(nameBox, 1, 0);
                form.Controls.Add(BuildFieldLabel("Organization:"), 0, 1);
                form.Controls.Add(orgBox, 1, 1);
                form.Controls.Add(BuildFieldLabel("Repository:"), 0, 2);
                form.Controls.Add(repoBox, 1, 2);
                form.Controls.Add(BuildFieldLabel("Enabled:"), 0, 3);
                form.Controls.Add(enabledPanel, 1, 3);
                form.Controls.Add(BuildFieldLabel("Env mapping:"), 0, 4);
                var mapPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
                mapPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
                mapPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                var mapButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
                mapButtons.Controls.Add(refreshEnvironmentsButton);
                mapPanel.Controls.Add(mapButtons, 0, 0);
                mapPanel.Controls.Add(environmentMapGrid, 0, 1);
                form.Controls.Add(mapPanel, 1, 4);

                var testPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
                var testButton = new Button { Text = "Test Connection", Width = 118, Height = 26 };
                testButton.Click += (s, e) =>
                {
                    testStatus.Text = "Checking...";
                    testStatus.ForeColor = Color.FromArgb(80, 90, 105);
                    testStatus.Refresh();
                    var result = TestGitHubProviderConnection(orgBox.Text, repoBox.Text, SelectedRemoteEnvironmentForTest(environmentMapGrid));
                    testStatus.Text = result.Message;
                    testStatus.ForeColor = result.Success ? Color.FromArgb(0, 120, 40) : Color.Firebrick;
                };
                testPanel.Controls.Add(testButton);
                testPanel.Controls.Add(testStatus);

                var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 88 };
                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 88 };
                buttons.Controls.Add(ok);
                buttons.Controls.Add(cancel);

                root.Controls.Add(form, 0, 0);
                root.Controls.Add(testPanel, 0, 1);
                root.Controls.Add(buttons, 0, 2);
                dialog.Controls.Add(root);
                dialog.AcceptButton = ok;
                dialog.CancelButton = cancel;
                dialog.Shown += (s, e) =>
                {
                    RefreshGitHubOrganizations(dialog, orgBox, true);
                    if (!string.IsNullOrWhiteSpace(orgBox.Text))
                    {
                        RefreshGitHubRepositories(dialog, orgBox.Text, repoBox, true);
                    }
                };

                ok.Click += (s, e) =>
                {
                    if (string.IsNullOrWhiteSpace(nameBox.Text) || string.IsNullOrWhiteSpace(orgBox.Text) || string.IsNullOrWhiteSpace(repoBox.Text))
                    {
                        MessageBox.Show(dialog, "Name, organization, and repository are required.", dialog.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        dialog.DialogResult = DialogResult.None;
                        return;
                    }

                    var result = TestGitHubProviderConnection(orgBox.Text, repoBox.Text, SelectedRemoteEnvironmentForTest(environmentMapGrid));
                    if (!result.Success)
                    {
                        if (result.AuthenticationRequired)
                        {
                            EnsureGitHubAuthenticatedInteractive(dialog);
                        }
                        MessageBox.Show(dialog, result.Message, "GitHub Provider", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        dialog.DialogResult = DialogResult.None;
                        return;
                    }

                    if (!ValidateGitHubEnvironmentMap(dialog, orgBox.Text, repoBox.Text, environmentMapGrid))
                    {
                        dialog.DialogResult = DialogResult.None;
                        return;
                    }

                    var missingLocalEnvironments = MissingMappedLocalEnvironments(environmentMapGrid);
                    if (missingLocalEnvironments.Count > 0)
                    {
                        var create = MessageBox.Show(
                            dialog,
                            "These vault environments do not exist yet:\r\n\r\n" + string.Join("\r\n", missingLocalEnvironments) + "\r\n\r\nCreate them in the current vault?",
                            "GitHub Environment Mapping",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question);
                        if (create != DialogResult.Yes)
                        {
                            dialog.DialogResult = DialogResult.None;
                            return;
                        }
                        CreateProjectEnvironments(missingLocalEnvironments);
                    }

                    if (!EnsureGitHubEnvironmentMappings(dialog, orgBox.Text, repoBox.Text, environmentMapGrid))
                    {
                        dialog.DialogResult = DialogResult.None;
                        return;
                    }
                };

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return false;
                }

                provider.Name = nameBox.Text.Trim();
                provider.Type = SyncProviderGitHubActions;
                provider.IsEnabled = enabledBox.Checked;
                SetProviderSetting(provider, "organization", orgBox.Text.Trim());
                SetProviderSetting(provider, "repository", repoBox.Text.Trim());
                EnsureProviderSecretFingerprintKey(provider);
                SaveProviderEnvironmentMap(provider, environmentMapGrid);
                SaveProviderEnvironmentInclude(provider, environmentMapGrid);
                SetProviderSetting(provider, "authMode", "GitHub CLI");
                provider.LastStatus = GetGitHubCliPath() != null ? "Configured" : "Blocked: GitHub CLI not found";
                provider.LastCheckedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                return true;
            }
        }

        private void RefreshGitHubEnvironmentList(IWin32Window owner, string organization, string repository, DataGridView environmentMapGrid)
        {
            if (!TryLoadGitHubEnvironments(organization, repository, out var environments, out var error))
            {
                MessageBox.Show(owner, error + "\r\n\r\nYou can still type GitHub environment names manually in the mapping table.", "GitHub Environments", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SetGitHubEnvironmentMapOptions(environmentMapGrid, environments);
        }

        private void RefreshGitHubOrganizations(IWin32Window owner, ComboBox organizationCombo, bool silent)
        {
            if (organizationCombo.Items.Count > 0) return;
            if (!TryLoadGitHubOrganizations(out var organizations, out var error))
            {
                if (!silent)
                {
                    MessageBox.Show(owner, error, "GitHub Organizations", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return;
            }

            var current = organizationCombo.Text;
            organizationCombo.BeginUpdate();
            try
            {
                organizationCombo.Items.Clear();
                foreach (var organization in organizations)
                {
                    organizationCombo.Items.Add(organization);
                }

                if (!string.IsNullOrWhiteSpace(current))
                {
                    organizationCombo.Text = current;
                }
                else if (organizations.Count == 1)
                {
                    organizationCombo.Text = organizations[0];
                }
            }
            finally
            {
                organizationCombo.EndUpdate();
            }
        }

        private void RefreshGitHubRepositories(IWin32Window owner, string organization, ComboBox repositoryCombo, bool silent)
        {
            if (string.IsNullOrWhiteSpace(organization)) return;
            if (!TryLoadGitHubRepositories(organization, out var repositories, out var error))
            {
                if (!silent)
                {
                    MessageBox.Show(owner, error, "GitHub Repositories", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return;
            }

            var current = repositoryCombo.Text;
            repositoryCombo.BeginUpdate();
            try
            {
                repositoryCombo.Items.Clear();
                foreach (var repository in repositories)
                {
                    repositoryCombo.Items.Add(repository);
                }

                if (!string.IsNullOrWhiteSpace(current))
                {
                    repositoryCombo.Text = current;
                }
                else if (repositories.Count == 1)
                {
                    repositoryCombo.Text = repositories[0];
                }
            }
            finally
            {
                repositoryCombo.EndUpdate();
            }
        }

        private List<string> ProjectEnvironmentNames()
        {
            return project.Environments
                .OrderBy(e => e.SortOrder)
                .ThenBy(e => e.Name)
                .Select(e => e.Name ?? e.Id)
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private DataGridView BuildGitHubEnvironmentMapGrid(Dictionary<string, string> environmentMap, HashSet<string> included)
        {
            var grid = BuildEditableGrid();
            grid.RowHeadersVisible = false;
            grid.AllowUserToAddRows = true;
            grid.AllowUserToDeleteRows = true;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.MinimumSize = new Size(0, 220);
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Include", HeaderText = "Use", FillWeight = 16 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Local", HeaderText = "Vault environment", FillWeight = 42 });
            grid.Columns.Add(BuildGitHubEnvironmentComboColumn(environmentMap.Values));
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "NewRemote", HeaderText = "New GitHub name", FillWeight = 42 });
            grid.CellValueChanged += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                if (grid.Columns[e.ColumnIndex].Name == "Remote")
                {
                    UpdateGitHubEnvironmentComboCells(grid);
                }
            };
            grid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (grid.IsCurrentCellDirty)
                {
                    grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }
            };
            var names = ProjectEnvironmentNames();
            if (names.Count == 0)
            {
                grid.Rows.Add(false, "(no vault environments)", string.Empty);
                grid.Rows[0].ReadOnly = true;
                return grid;
            }

            foreach (var localName in names)
            {
                var remoteName = environmentMap.TryGetValue(localName, out var mapped) && !string.IsNullOrWhiteSpace(mapped)
                    ? mapped
                    : string.Empty;
                var use = included.Contains(localName);
                grid.Rows.Add(use, localName, remoteName, string.Empty);
            }
            UpdateGitHubEnvironmentComboCells(grid);
            return grid;
        }

        private DataGridViewComboBoxColumn BuildGitHubEnvironmentComboColumn(IEnumerable<string> values)
        {
            var column = new DataGridViewComboBoxColumn
            {
                Name = "Remote",
                HeaderText = "Existing GitHub environment",
                FillWeight = 44,
                FlatStyle = FlatStyle.Flat
            };
            column.Items.Add(string.Empty);
            foreach (var value in values.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(v => v))
            {
                column.Items.Add(value);
            }
            return column;
        }

        private void SetGitHubEnvironmentMapOptions(DataGridView grid, IEnumerable<string> remoteEnvironments)
        {
            var column = grid.Columns["Remote"] as DataGridViewComboBoxColumn;
            if (column != null)
            {
                var values = column.Items.Cast<object>().Select(Convert.ToString).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
                foreach (var environment in remoteEnvironments.Where(e => !string.IsNullOrWhiteSpace(e)))
                {
                    if (!values.Contains(environment, StringComparer.OrdinalIgnoreCase))
                    {
                        column.Items.Add(environment);
                        values.Add(environment);
                    }
                }
            }

            var existingRemoteValues = grid.Rows
                .Cast<DataGridViewRow>()
                .Where(r => !r.IsNewRow)
                .Select(r => Convert.ToString(r.Cells["Remote"].Value))
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();

            foreach (var environment in remoteEnvironments.Where(e => !string.IsNullOrWhiteSpace(e)))
            {
                var matched = false;
                foreach (DataGridViewRow row in grid.Rows)
                {
                    if (row.IsNewRow) continue;
                    var local = Convert.ToString(row.Cells["Local"].Value)?.Trim();
                    var currentRemote = Convert.ToString(row.Cells["Remote"].Value)?.Trim();
                    if (!string.IsNullOrWhiteSpace(local) &&
                        string.IsNullOrWhiteSpace(currentRemote) &&
                        string.Equals(local, environment, StringComparison.OrdinalIgnoreCase))
                    {
                        row.Cells["Include"].Value = true;
                        row.Cells["Remote"].Value = environment;
                        existingRemoteValues.Add(environment);
                        matched = true;
                        break;
                    }
                }
                if (matched)
                {
                    continue;
                }

                if (existingRemoteValues.Contains(environment, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }
                grid.Rows.Add(false, string.Empty, environment, string.Empty);
                existingRemoteValues.Add(environment);
            }
            UpdateGitHubEnvironmentComboCells(grid);
        }

        private void UpdateGitHubEnvironmentComboCells(DataGridView grid)
        {
            var column = grid.Columns["Remote"] as DataGridViewComboBoxColumn;
            if (column == null) return;

            var allValues = column.Items
                .Cast<object>()
                .Select(Convert.ToString)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v)
                .ToList();

            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.IsNewRow) continue;
                var current = Convert.ToString(row.Cells["Remote"].Value);
                var usedByOtherRows = grid.Rows
                    .Cast<DataGridViewRow>()
                    .Where(r => !r.IsNewRow && r != row)
                    .Select(r => Convert.ToString(r.Cells["Remote"].Value))
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .ToList();

                var cell = row.Cells["Remote"] as DataGridViewComboBoxCell;
                if (cell == null) continue;
                cell.Items.Clear();
                cell.Items.Add(string.Empty);
                foreach (var value in allValues)
                {
                    if (string.Equals(value, current, StringComparison.OrdinalIgnoreCase) ||
                        !usedByOtherRows.Contains(value, StringComparer.OrdinalIgnoreCase))
                    {
                        cell.Items.Add(value);
                    }
                }
            }
        }

        private Dictionary<string, string> LoadProviderEnvironmentMap(SyncProviderModel provider)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var json = ProviderSetting(provider, "environmentMap");
            if (string.IsNullOrWhiteSpace(json)) return result;

            try
            {
                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var parsed = serializer.Deserialize<Dictionary<string, string>>(json);
                if (parsed == null) return result;
                foreach (var pair in parsed)
                {
                    if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                    {
                        result[pair.Key] = pair.Value;
                    }
                }
            }
            catch
            {
                return result;
            }

            return result;
        }

        private HashSet<string> LoadProviderEnvironmentInclude(SyncProviderModel provider)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var json = ProviderSetting(provider, "environmentInclude");
            if (string.IsNullOrWhiteSpace(json)) return result;

            try
            {
                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var parsed = serializer.Deserialize<string[]>(json);
                if (parsed == null) return result;
                foreach (var value in parsed.Where(v => !string.IsNullOrWhiteSpace(v)))
                {
                    result.Add(value);
                }
            }
            catch
            {
                return result;
            }

            return result;
        }

        private void SaveProviderEnvironmentMap(SyncProviderModel provider, DataGridView grid)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.IsNewRow) continue;
                var local = Convert.ToString(row.Cells["Local"].Value)?.Trim();
                var remote = EffectiveGitHubEnvironmentName(row, false);
                if (string.IsNullOrWhiteSpace(local) || string.IsNullOrWhiteSpace(remote)) continue;
                map[local] = remote;
            }

            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            SetProviderSetting(provider, "environmentMap", serializer.Serialize(map));
        }

        private void SaveProviderEnvironmentInclude(SyncProviderModel provider, DataGridView grid)
        {
            var included = new List<string>();
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.IsNewRow) continue;
                var local = Convert.ToString(row.Cells["Local"].Value)?.Trim();
                var use = Convert.ToBoolean(row.Cells["Include"].Value ?? false);
                if (use && !string.IsNullOrWhiteSpace(local))
                {
                    included.Add(local);
                }
            }

            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            SetProviderSetting(provider, "environmentInclude", serializer.Serialize(included));
        }

        private List<string> MissingMappedLocalEnvironments(DataGridView grid)
        {
            var existing = new HashSet<string>(ProjectEnvironmentNames(), StringComparer.OrdinalIgnoreCase);
            var missing = new List<string>();
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.IsNewRow) continue;
                var use = Convert.ToBoolean(row.Cells["Include"].Value ?? false);
                if (!use) continue;
                var local = Convert.ToString(row.Cells["Local"].Value)?.Trim();
                if (string.IsNullOrWhiteSpace(local) || existing.Contains(local) || missing.Contains(local, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }
                missing.Add(local);
            }
            return missing;
        }

        private void CreateProjectEnvironments(IEnumerable<string> names)
        {
            foreach (var name in names.Where(n => !string.IsNullOrWhiteSpace(n)))
            {
                var clean = name.Trim();
                if (project.Environments.Any(e => string.Equals(e.Name, clean, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
                project.Environments.Add(new EnvironmentModel
                {
                    Id = UniqueEnvironmentId(Slug(clean)),
                    Name = clean,
                    DisplayName = clean,
                    IsActive = true,
                    SortOrder = project.Environments.Count == 0 ? 0 : project.Environments.Max(e => e.SortOrder) + 10
                });
            }
        }

        private static string SelectedRemoteEnvironmentForTest(DataGridView grid)
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.IsNewRow) continue;
                var use = Convert.ToBoolean(row.Cells["Include"].Value ?? false);
                if (!use) continue;
                var remote = EffectiveGitHubEnvironmentName(row, false);
                if (!string.IsNullOrWhiteSpace(remote)) return remote;
            }
            return null;
        }

        private bool ValidateGitHubEnvironmentMap(IWin32Window owner, string organization, string repository, DataGridView grid)
        {
            TryLoadGitHubEnvironments(organization, repository, out var existingEnvironments, out _);
            existingEnvironments = existingEnvironments ?? new List<string>();
            var newNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var selectedRemoteNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.IsNewRow) continue;
                var use = Convert.ToBoolean(row.Cells["Include"].Value ?? false);
                if (!use) continue;

                var local = Convert.ToString(row.Cells["Local"].Value)?.Trim();
                var remote = Convert.ToString(row.Cells["Remote"].Value)?.Trim();
                var newRemote = Convert.ToString(row.Cells["NewRemote"].Value)?.Trim();

                if (string.IsNullOrWhiteSpace(local))
                {
                    MessageBox.Show(owner, "Vault environment name is required for every selected mapping row.", "GitHub Environment Mapping", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(remote) && !string.IsNullOrWhiteSpace(newRemote))
                {
                    MessageBox.Show(owner, $"Choose either an existing GitHub environment or a new GitHub name for '{local}', not both.", "GitHub Environment Mapping", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(remote) && !selectedRemoteNames.Add(remote))
                {
                    MessageBox.Show(owner, $"GitHub environment '{remote}' is selected more than once.", "GitHub Environment Mapping", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(newRemote))
                {
                    if (existingEnvironments.Contains(newRemote, StringComparer.OrdinalIgnoreCase))
                    {
                        MessageBox.Show(owner, $"GitHub environment '{newRemote}' already exists. Select it from the existing environment list instead of using New GitHub name.", "GitHub Environment Mapping", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return false;
                    }
                    if (!newNames.Add(newRemote))
                    {
                        MessageBox.Show(owner, $"New GitHub environment '{newRemote}' is used more than once.", "GitHub Environment Mapping", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return false;
                    }
                }
            }

            return true;
        }

        private static string EffectiveGitHubEnvironmentName(DataGridViewRow row, bool defaultToLocal)
        {
            var remote = Convert.ToString(row.Cells["Remote"].Value)?.Trim();
            if (!string.IsNullOrWhiteSpace(remote)) return remote;
            var newRemote = Convert.ToString(row.Cells["NewRemote"].Value)?.Trim();
            if (!string.IsNullOrWhiteSpace(newRemote)) return newRemote;
            return defaultToLocal ? Convert.ToString(row.Cells["Local"].Value)?.Trim() : null;
        }

        private bool EnsureGitHubEnvironmentMappings(IWin32Window owner, string organization, string repository, DataGridView grid)
        {
            TryLoadGitHubEnvironments(organization, repository, out var existingEnvironments, out _);
            existingEnvironments = existingEnvironments ?? new List<string>();

            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.IsNewRow) continue;
                var use = Convert.ToBoolean(row.Cells["Include"].Value ?? false);
                if (!use) continue;

                var local = Convert.ToString(row.Cells["Local"].Value)?.Trim();
                if (string.IsNullOrWhiteSpace(local)) continue;

                var remote = EffectiveGitHubEnvironmentName(row, true);

                if (existingEnvironments.Contains(remote, StringComparer.OrdinalIgnoreCase))
                {
                    row.Cells["Remote"].Value = remote;
                    continue;
                }

                var create = MessageBox.Show(
                    owner,
                    $"GitHub environment '{remote}' was not found for vault environment '{local}'.\r\n\r\nCreate it in GitHub now?",
                    "GitHub Environment",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);
                if (create == DialogResult.Cancel)
                {
                    return false;
                }
                if (create == DialogResult.No)
                {
                    MessageBox.Show(owner, "Select an existing GitHub environment or create one before saving this provider.", "GitHub Environment", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                var requestedName = PromptDialog.Show(owner, "Create GitHub Environment", $"GitHub environment name for vault environment '{local}':", remote);
                if (requestedName == null) return false;
                requestedName = requestedName.Trim();
                if (string.IsNullOrWhiteSpace(requestedName))
                {
                    MessageBox.Show(owner, "GitHub environment name cannot be empty.", "GitHub Environment", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                if (!CreateGitHubEnvironment(organization, repository, requestedName, out var error))
                {
                    MessageBox.Show(owner, error, "GitHub Environment", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                existingEnvironments.Add(requestedName);
                SetGitHubEnvironmentMapOptions(grid, existingEnvironments);
                row.Cells["Remote"].Value = requestedName;
                row.Cells["NewRemote"].Value = string.Empty;
            }

            return true;
        }

        private bool CreateGitHubEnvironment(string organization, string repository, string environment, out string error)
        {
            error = null;
            var ghPath = GetGitHubCliPath();
            if (ghPath == null)
            {
                error = "GitHub CLI (`gh`) was not found.\r\n\r\nDownload:\r\nhttps://cli.github.com/";
                return false;
            }

            var endpoint = "repos/" + organization.Trim() + "/" + repository.Trim() + "/environments/" + Uri.EscapeDataString(environment.Trim());
            if (!RunGitHubCommand(ghPath, "api -X PUT " + endpoint, out _, out var commandError))
            {
                error = "Cannot create GitHub environment.\r\n\r\n" + ShortCommandError(commandError);
                return false;
            }

            return true;
        }

        private GitHubProviderTestResult TestGitHubProviderConnection(string organization, string repository, string environment)
        {
            organization = (organization ?? string.Empty).Trim();
            repository = (repository ?? string.Empty).Trim();
            environment = (environment ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(organization) || string.IsNullOrWhiteSpace(repository))
            {
                return GitHubProviderTestResult.Fail("Organization and repository are required.");
            }

            var ghPath = GetGitHubCliPath();
            if (ghPath == null)
            {
                return GitHubProviderTestResult.Fail("GitHub CLI not found.");
            }

            if (!RunGitHubCommand(ghPath, "auth status", out _, out var authError))
            {
                return GitHubProviderTestResult.Fail("GitHub authentication failed: " + ShortCommandError(authError), true);
            }

            if (!RunGitHubCommand(ghPath, "repo view " + organization + "/" + repository + " --json name", out _, out var repoError))
            {
                return GitHubProviderTestResult.Fail("Repository is not accessible: " + ShortCommandError(repoError));
            }

            if (string.IsNullOrWhiteSpace(environment))
            {
                return GitHubProviderTestResult.Ok("Connected. Environment is not selected.");
            }

            if (!TryLoadGitHubEnvironments(organization, repository, out var environments, out var envError))
            {
                return GitHubProviderTestResult.Ok("Connected. Environment list unavailable; typed name will be used.");
            }

            return environments.Contains(environment, StringComparer.OrdinalIgnoreCase)
                ? GitHubProviderTestResult.Ok("Connected. Environment exists.")
                : GitHubProviderTestResult.Ok("Connected. Environment does not exist yet; it can be created by GitHub later.");
        }

        private bool EnsureGitHubAuthenticatedInteractive(IWin32Window owner)
        {
            var ghPath = GetGitHubCliPath();
            if (ghPath == null)
            {
                MessageBox.Show(owner, "GitHub CLI (`gh`) was not found.\r\n\r\nDownload:\r\nhttps://cli.github.com/", "GitHub Authentication", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (RunGitHubCommand(ghPath, "auth status", out _, out _))
            {
                return true;
            }

            var result = MessageBox.Show(
                owner,
                "GitHub CLI is not authenticated. Open a command prompt to run `gh auth login` now?",
                "GitHub Authentication",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (result != DialogResult.Yes)
            {
                return false;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/k \"\"" + ghPath + "\" auth login\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, "Cannot open GitHub login command.\r\n\r\n" + ex.Message, "GitHub Authentication", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            return false;
        }

        private static bool RunGitHubCommand(string ghPath, string arguments, out string output, out string error)
        {
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = ghPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                process.OutputDataReceived += (s, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
                try
                {
                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    if (!process.WaitForExit(15000))
                    {
                        try { process.Kill(); } catch { }
                        output = stdout.ToString();
                        error = "Command timed out.";
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    output = stdout.ToString();
                    error = ex.Message;
                    return false;
                }

                output = stdout.ToString();
                error = stderr.ToString();
                return process.ExitCode == 0;
            }
        }

        private static string ShortCommandError(string value)
        {
            value = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value)) return "no details";
            var firstLine = value.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).FirstOrDefault();
            return string.IsNullOrWhiteSpace(firstLine) ? value : firstLine.Trim();
        }

        private bool TryLoadGitHubEnvironments(string organization, string repository, out List<string> environments, out string error)
        {
            environments = new List<string>();
            error = null;
            organization = (organization ?? string.Empty).Trim();
            repository = (repository ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(organization) || string.IsNullOrWhiteSpace(repository))
            {
                error = "Organization and repository are required before loading GitHub environments.";
                return false;
            }

            var ghPath = GetGitHubCliPath();
            if (ghPath == null)
            {
                error = "GitHub CLI (`gh`) was not found.\r\n\r\nDownload:\r\nhttps://cli.github.com/";
                return false;
            }

            var output = new StringBuilder();
            var errors = new StringBuilder();
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = ghPath,
                    Arguments = "api repos/" + organization + "/" + repository + "/environments --paginate",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                process.OutputDataReceived += (s, e) => { if (e.Data != null) output.AppendLine(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) errors.AppendLine(e.Data); };

                try
                {
                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    if (!process.WaitForExit(15000))
                    {
                        try { process.Kill(); } catch { }
                        error = "GitHub environment request timed out.";
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    error = "Cannot run GitHub CLI.\r\n\r\n" + ex.Message;
                    return false;
                }

                if (process.ExitCode != 0)
                {
                    error = "GitHub CLI returned an error.\r\n\r\n" + errors.ToString().Trim();
                    return false;
                }
            }

            try
            {
                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var data = serializer.Deserialize<Dictionary<string, object>>(output.ToString());
                if (data != null && data.TryGetValue("environments", out var raw) && raw is IEnumerable array)
                {
                    environments = array
                        .Cast<object>()
                        .Select(item => item as Dictionary<string, object>)
                        .Where(e => e != null)
                        .Select(e => e.TryGetValue("name", out var name) ? Convert.ToString(name) : null)
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(n => n)
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                error = "Cannot parse GitHub environments response.\r\n\r\n" + ex.Message;
                return false;
            }

            if (environments.Count == 0)
            {
                error = "No GitHub environments were found for this repository.";
                return false;
            }

            return true;
        }

        private bool TryLoadGitHubOrganizations(out List<string> organizations, out string error)
        {
            organizations = new List<string>();
            error = null;

            var ghPath = GetGitHubCliPath();
            if (ghPath == null)
            {
                error = "GitHub CLI (`gh`) was not found.\r\n\r\nDownload:\r\nhttps://cli.github.com/";
                return false;
            }

            if (!RunGitHubCommand(ghPath, "api user", out var userJson, out var userError))
            {
                error = "Cannot read current GitHub user.\r\n\r\n" + ShortCommandError(userError);
                return false;
            }

            try
            {
                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var user = serializer.Deserialize<Dictionary<string, object>>(userJson);
                if (user != null && user.TryGetValue("login", out var login))
                {
                    var value = Convert.ToString(login);
                    if (!string.IsNullOrWhiteSpace(value)) organizations.Add(value);
                }
            }
            catch (Exception ex)
            {
                error = "Cannot parse current GitHub user.\r\n\r\n" + ex.Message;
                return false;
            }

            if (RunGitHubCommand(ghPath, "api user/orgs --paginate", out var orgJson, out _))
            {
                try
                {
                    var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                    var orgs = serializer.Deserialize<object[]>(orgJson) ?? new object[0];
                    foreach (var org in orgs.OfType<Dictionary<string, object>>())
                    {
                        if (org.TryGetValue("login", out var login))
                        {
                            var value = Convert.ToString(login);
                            if (!string.IsNullOrWhiteSpace(value)) organizations.Add(value);
                        }
                    }
                }
                catch
                {
                    // User account is still usable even if organization list parsing fails.
                }
            }

            organizations = organizations
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(o => o)
                .ToList();
            if (organizations.Count == 0)
            {
                error = "No GitHub account or organization was found.";
                return false;
            }

            return true;
        }

        private bool TryLoadGitHubRepositories(string organization, out List<string> repositories, out string error)
        {
            repositories = new List<string>();
            error = null;
            organization = (organization ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(organization))
            {
                error = "Organization is required before loading repositories.";
                return false;
            }

            var ghPath = GetGitHubCliPath();
            if (ghPath == null)
            {
                error = "GitHub CLI (`gh`) was not found.\r\n\r\nDownload:\r\nhttps://cli.github.com/";
                return false;
            }

            if (!RunGitHubCommand(ghPath, "repo list " + organization + " --json name --limit 200", out var json, out var commandError))
            {
                error = "Cannot read GitHub repositories.\r\n\r\n" + ShortCommandError(commandError);
                return false;
            }

            try
            {
                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var data = serializer.Deserialize<object[]>(json) ?? new object[0];
                repositories = data
                    .OfType<Dictionary<string, object>>()
                    .Select(r => r.TryGetValue("name", out var name) ? Convert.ToString(name) : null)
                    .Where(r => !string.IsNullOrWhiteSpace(r))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(r => r)
                    .ToList();
            }
            catch (Exception ex)
            {
                error = "Cannot parse GitHub repositories response.\r\n\r\n" + ex.Message;
                return false;
            }

            if (repositories.Count == 0)
            {
                error = "No repositories were found for this GitHub account or organization.";
                return false;
            }

            return true;
        }

        private string GetGitHubCliPath()
        {
            var cached = recentProjectsService.LoadStringPreference(PreferenceGitHubCliPath);
            if (!string.IsNullOrWhiteSpace(cached) && System.IO.File.Exists(cached))
            {
                return cached;
            }

            if (!string.IsNullOrWhiteSpace(cached))
            {
                recentProjectsService.SaveStringPreference(PreferenceGitHubCliPath, null);
            }

            var found = externalCommandService.TryFindCommand("gh");
            if (!string.IsNullOrWhiteSpace(found) && System.IO.File.Exists(found))
            {
                recentProjectsService.SaveStringPreference(PreferenceGitHubCliPath, found);
                return found;
            }

            return null;
        }

        private sealed class GitHubProviderTestResult
        {
            private GitHubProviderTestResult(bool success, string message, bool authenticationRequired)
            {
                Success = success;
                Message = message;
                AuthenticationRequired = authenticationRequired;
            }

            public bool Success { get; }
            public string Message { get; }
            public bool AuthenticationRequired { get; }

            public static GitHubProviderTestResult Ok(string message) => new GitHubProviderTestResult(true, message, false);
            public static GitHubProviderTestResult Fail(string message, bool authenticationRequired = false) => new GitHubProviderTestResult(false, message, authenticationRequired);
        }

        private static string ProviderTypeDisplayName(string type)
        {
            return string.Equals(type, SyncProviderGitHubActions, StringComparison.Ordinal) ? "GitHub Actions" : type;
        }

        private static string ProviderSetting(SyncProviderModel provider, string key)
        {
            if (provider?.Settings == null || string.IsNullOrWhiteSpace(key)) return string.Empty;
            return provider.Settings.TryGetValue(key, out var value) ? value ?? string.Empty : string.Empty;
        }

        private static void SetProviderSetting(SyncProviderModel provider, string key, string value)
        {
            provider.Settings = provider.Settings ?? new Dictionary<string, string>();
            if (string.IsNullOrWhiteSpace(value))
            {
                provider.Settings.Remove(key);
            }
            else
            {
                provider.Settings[key] = value;
            }
        }

        private static void EnsureProviderSecretFingerprintKey(SyncProviderModel provider)
        {
            if (!string.IsNullOrWhiteSpace(ProviderSetting(provider, SecretFingerprintService.ProviderSettingKey))) return;
            SetProviderSetting(provider, SecretFingerprintService.ProviderSettingKey, SecretFingerprintService.GenerateKey());
        }

        private static string ProviderEnvironmentSummary(SyncProviderModel provider)
        {
            var mapJson = ProviderSetting(provider, "environmentMap");
            var includeJson = ProviderSetting(provider, "environmentInclude");
            if (string.IsNullOrWhiteSpace(mapJson)) return string.Empty;

            try
            {
                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var map = serializer.Deserialize<Dictionary<string, string>>(mapJson) ?? new Dictionary<string, string>();
                var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(includeJson))
                {
                    var values = serializer.Deserialize<string[]>(includeJson);
                    if (values != null)
                    {
                        foreach (var value in values.Where(v => !string.IsNullOrWhiteSpace(v)))
                        {
                            included.Add(value);
                        }
                    }
                }

                var pairs = map
                    .Where(p => included.Count == 0 || included.Contains(p.Key))
                    .OrderBy(p => p.Key)
                    .Select(p => string.Equals(p.Key, p.Value, StringComparison.OrdinalIgnoreCase) ? p.Key : p.Key + " -> " + p.Value)
                    .ToList();
                return string.Join("; ", pairs);
            }
            catch
            {
                return string.Empty;
            }
        }

        private sealed class GitHubEnvironmentMapping
        {
            public GitHubEnvironmentMapping(string localEnvironment, string remoteEnvironment)
            {
                LocalEnvironment = localEnvironment;
                RemoteEnvironment = remoteEnvironment;
            }

            public string LocalEnvironment { get; }
            public string RemoteEnvironment { get; }
        }

        private sealed class GitHubLocalSyncValue
        {
            public string Key { get; set; }
            public string Value { get; set; }
            public bool IsSecret { get; set; }
            public string ServiceName { get; set; }
        }

        private sealed class GitHubRemoteEnvironmentValues
        {
            public Dictionary<string, string> Variables { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> Secrets { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class GitHubSyncPreviewRow
        {
            public string Action { get; set; }
            public string Type { get; set; }
            public string Key { get; set; }
            public string LocalValue { get; set; }
            public string RemoteValue { get; set; }
            public string VaultEnvironment { get; set; }
            public string GitHubEnvironment { get; set; }
            public string Service { get; set; }
            public string Status { get; set; }
        }
    }
}

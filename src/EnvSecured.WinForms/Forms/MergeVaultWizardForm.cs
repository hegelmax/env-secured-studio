using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using EnvSecured.Core.Models;

namespace EnvSecured.WinForms.Forms
{
    internal sealed class MergeVaultWizardForm : Form
    {
        private const string NoneOption = "(none)";
        private readonly ProjectModel targetProject;
        private readonly List<MergeVaultSource> sources;
        private readonly Dictionary<string, string> environmentMap = new Dictionary<string, string>();
        private readonly Dictionary<string, string> serviceMap = new Dictionary<string, string>();
        private readonly Dictionary<string, string> variableMap = new Dictionary<string, string>();
        private readonly DataGridView mappingGrid = new DataGridView();
        private readonly Label titleLabel = new Label();
        private readonly Label hintLabel = new Label();
        private readonly Button backButton = new Button();
        private readonly Button nextButton = new Button();
        private readonly Button clearButton = new Button();
        private bool suppressGridEvents;
        private int step;

        public MergeVaultWizardForm(ProjectModel targetProject, IEnumerable<MergeVaultSource> sources)
        {
            this.targetProject = targetProject;
            this.sources = sources.ToList();
            Text = "Merge Vaults";
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(980, 640);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(10) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

            var header = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            titleLabel.Dock = DockStyle.Fill;
            titleLabel.Font = new Font(Font.FontFamily, 13, FontStyle.Bold);
            hintLabel.Dock = DockStyle.Fill;
            hintLabel.ForeColor = Color.FromArgb(80, 90, 105);
            header.Controls.Add(titleLabel, 0, 0);
            header.Controls.Add(hintLabel, 0, 1);
            root.Controls.Add(header, 0, 0);

            BuildMappingGrid();
            root.Controls.Add(mappingGrid, 0, 1);

            var commands = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            clearButton.Text = "Clear Selected Mapping";
            clearButton.Width = 160;
            clearButton.Click += (s, e) => ClearSelectedMapping();
            commands.Controls.Add(clearButton);
            root.Controls.Add(commands, 0, 2);

            var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            var cancelButton = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel };
            nextButton.Text = "Next";
            nextButton.Width = 90;
            nextButton.Click += (s, e) => MoveNext();
            backButton.Text = "Back";
            backButton.Width = 90;
            backButton.Click += (s, e) => MoveBack();
            footer.Controls.Add(cancelButton);
            footer.Controls.Add(nextButton);
            footer.Controls.Add(backButton);
            root.Controls.Add(footer, 0, 3);

            Controls.Add(root);
            CancelButton = cancelButton;
            InitializeAutomaticMappings();
            RenderStep();
        }

        public List<ProjectModel> BuildMappedSources()
        {
            return sources.Select(source =>
            {
                var clone = Clone(source.Project);
                ApplyEnvironmentMappings(source, clone);
                ApplyServiceMappings(source, clone);
                ApplyVariableMappings(source, clone);
                return clone;
            }).ToList();
        }

        private void BuildMappingGrid()
        {
            mappingGrid.Dock = DockStyle.Fill;
            mappingGrid.AllowUserToAddRows = false;
            mappingGrid.AllowUserToDeleteRows = false;
            mappingGrid.RowHeadersVisible = false;
            mappingGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            mappingGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            mappingGrid.MultiSelect = false;
            mappingGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Incoming", HeaderText = "Incoming from vault", FillWeight = 45 });
            mappingGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Target", HeaderText = "Current project target", FillWeight = 45 });
            mappingGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Action", HeaderText = "Action", ReadOnly = true, FillWeight = 20 });
            mappingGrid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (mappingGrid.IsCurrentCellDirty)
                {
                    mappingGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }
            };
            mappingGrid.CellValueChanged += (s, e) =>
            {
                if (suppressGridEvents || e.RowIndex < 0 || e.ColumnIndex < 0) return;
                var columnName = mappingGrid.Columns[e.ColumnIndex].Name;
                if (columnName == "Incoming")
                {
                    ApplyGridMappingChange(e.RowIndex);
                }
                else if (columnName == "Target")
                {
                    ApplyCreateNameChange(e.RowIndex);
                }
            };
            mappingGrid.DataError += (s, e) => { e.ThrowException = false; };
        }

        private void InitializeAutomaticMappings()
        {
            foreach (var source in sources)
            {
                foreach (var environment in source.Project.Environments)
                {
                    var target = targetProject.Environments.FirstOrDefault(e =>
                        string.Equals(e.Id, environment.Id, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(e.Name, environment.Name, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(e.DisplayName, environment.Name, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(e.Name, environment.DisplayName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(e.DisplayName, environment.DisplayName, StringComparison.OrdinalIgnoreCase));
                    if (target != null) environmentMap[EntityKey(source, environment.Id)] = target.Id;
                }

                foreach (var service in source.Project.Services)
                {
                    var target = targetProject.Services.FirstOrDefault(s =>
                        string.Equals(s.Id, service.Id, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(s.Name, service.Name, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(s.DisplayName, service.Name, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(s.Name, service.DisplayName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(s.DisplayName, service.DisplayName, StringComparison.OrdinalIgnoreCase));
                    if (target != null) serviceMap[EntityKey(source, service.Id)] = target.Id;
                }

                foreach (var variable in source.Project.Variables)
                {
                    var target = targetProject.Variables.FirstOrDefault(v => string.Equals(v.Key, variable.Key, StringComparison.OrdinalIgnoreCase));
                    if (target != null) variableMap[EntityKey(source, variable.Id)] = target.Id;
                }
            }
        }

        private void MoveNext()
        {
            if (!ConfirmUnmappedIncoming()) return;
            if (step >= 2)
            {
                DialogResult = DialogResult.OK;
                Close();
                return;
            }
            step++;
            RenderStep();
        }

        private void MoveBack()
        {
            if (step <= 0) return;
            step--;
            RenderStep();
        }

        private bool ConfirmUnmappedIncoming()
        {
            var unmapped = IncomingItems().Where(i => !CurrentMap().ContainsKey(i.MapKey)).ToList();
            if (unmapped.Count == 0) return true;

            var type = step == 0 ? "environment(s)" : step == 1 ? "service(s)" : "variable(s)";
            var message = "There are " + unmapped.Count + " unmatched incoming " + type + "." + Environment.NewLine +
                "They will be created as new items. Continue?" + Environment.NewLine + Environment.NewLine +
                string.Join(Environment.NewLine, unmapped.Take(12).Select(i => i.Display));
            if (unmapped.Count > 12) message += Environment.NewLine + "...";

            var conflicts = FindCreateNameConflicts(unmapped);
            if (conflicts.Count > 0)
            {
                MessageBox.Show(
                    this,
                    "Some unmatched incoming items have names that already exist or duplicate another incoming item." + Environment.NewLine +
                    "Map them to existing items or rename them before continuing." + Environment.NewLine + Environment.NewLine +
                    string.Join(Environment.NewLine, conflicts.Take(12).Select(i => i.Display)),
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return false;
            }

            if (MessageBox.Show(this, message, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return false;
            }

            return EnsureUnmappedNamesCanBeCreated(unmapped);
        }

        private List<MergeEntityItem> FindCreateNameConflicts(List<MergeEntityItem> unmapped)
        {
            var existing = new HashSet<string>(CurrentItems().Select(i => i.Name), StringComparer.OrdinalIgnoreCase);
            var createNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return unmapped
                .Where(item => string.IsNullOrWhiteSpace(item.Name) || existing.Contains(item.Name) || !createNames.Add(item.Name))
                .ToList();
        }

        private bool EnsureUnmappedNamesCanBeCreated(List<MergeEntityItem> unmapped)
        {
            var existing = new HashSet<string>(CurrentItems().Select(i => i.Name), StringComparer.OrdinalIgnoreCase);
            foreach (var item in unmapped)
            {
                var name = item.Name;
                while (string.IsNullOrWhiteSpace(name) || existing.Contains(name))
                {
                    var prompt = string.IsNullOrWhiteSpace(name)
                        ? "Name is required for new item:"
                        : "Name already exists. Enter a unique name for:";
                    var newName = PromptDialog.Show(this, Text, prompt + Environment.NewLine + item.Display, string.IsNullOrWhiteSpace(name) ? item.Name : name + "-new");
                    if (newName == null) return false;
                    name = newName.Trim();
                }

                if (!string.Equals(name, item.Name, StringComparison.Ordinal))
                {
                    RenameIncomingItem(item, name);
                }
                existing.Add(name);
            }
            return true;
        }

        private void RenameIncomingItem(MergeEntityItem item, string newName)
        {
            if (item?.Source == null) return;
            if (step == 0)
            {
                var environment = item.Source.Project.Environments.FirstOrDefault(e => e.Id == item.Id);
                if (environment != null)
                {
                    environment.Name = newName;
                    environment.DisplayName = newName;
                }
            }
            else if (step == 1)
            {
                var service = item.Source.Project.Services.FirstOrDefault(s => s.Id == item.Id);
                if (service != null)
                {
                    service.Name = newName;
                    service.DisplayName = newName;
                }
            }
            else
            {
                var variable = item.Source.Project.Variables.FirstOrDefault(v => v.Id == item.Id);
                if (variable != null)
                {
                    variable.Key = newName.Trim().ToUpperInvariant();
                }
            }
        }

        private void RenderStep()
        {
            backButton.Enabled = step > 0;
            nextButton.Text = step == 2 ? "Review" : "Next";
            if (step == 0)
            {
                titleLabel.Text = "Step 1: Map environments";
                hintLabel.Text = "Choose an incoming environment for each existing environment. Unmatched incoming environments are shown as Create new.";
            }
            else if (step == 1)
            {
                titleLabel.Text = "Step 2: Map services";
                hintLabel.Text = "Choose an incoming service for each existing service, for example jobs -> prefect-worker. Unmatched incoming services are shown as Create new.";
            }
            else
            {
                titleLabel.Text = "Step 3: Map variables";
                hintLabel.Text = "Variables are auto-matched by key. Choose an incoming variable only when names differ but meaning is the same.";
            }

            RefreshMappingGrid();
        }

        private void RefreshMappingGrid()
        {
            var currentTag = mappingGrid.CurrentRow?.Tag;
            var currentColumnName = mappingGrid.CurrentCell != null ? mappingGrid.Columns[mappingGrid.CurrentCell.ColumnIndex].Name : null;
            var firstDisplayedRow = mappingGrid.FirstDisplayedScrollingRowIndex >= 0 ? mappingGrid.FirstDisplayedScrollingRowIndex : -1;
            suppressGridEvents = true;
            try
            {
                mappingGrid.Rows.Clear();
                var incoming = IncomingItems();
                var current = CurrentItems();
                var map = CurrentMap();
                var selectedIncoming = new HashSet<string>(map.Keys, StringComparer.Ordinal);
                foreach (var target in current)
                {
                    var mappedIncomingKey = map.FirstOrDefault(pair => string.Equals(pair.Value, target.Id, StringComparison.Ordinal)).Key;
                    var rowIndex = mappingGrid.Rows.Add();
                    var row = mappingGrid.Rows[rowIndex];
                    row.Tag = target;
                    var cell = new DataGridViewComboBoxCell { FlatStyle = FlatStyle.Flat };
                    cell.Items.Add(new IncomingOption(null, NoneOption));
                    foreach (var option in incoming.Where(i => !selectedIncoming.Contains(i.MapKey) || string.Equals(i.MapKey, mappedIncomingKey, StringComparison.Ordinal)))
                    {
                        cell.Items.Add(new IncomingOption(option.MapKey, option.Display));
                    }
                    row.Cells["Incoming"] = cell;
                    row.Cells["Incoming"].Value = string.IsNullOrWhiteSpace(mappedIncomingKey)
                        ? new IncomingOption(null, NoneOption)
                        : new IncomingOption(mappedIncomingKey, incoming.First(i => i.MapKey == mappedIncomingKey).Display);
                    row.Cells["Target"].Value = target.Name;
                    row.Cells["Target"].ReadOnly = true;
                    row.Cells["Action"].Value = string.IsNullOrWhiteSpace(mappedIncomingKey) ? "Keep current" : "Mapped";
                }

                var existingNames = new HashSet<string>(current.Select(i => i.Name), StringComparer.OrdinalIgnoreCase);
                var createNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var option in incoming.Where(i => !map.ContainsKey(i.MapKey)))
                {
                    var conflict = existingNames.Contains(option.Name) || !createNames.Add(option.Name);
                    var rowIndex = mappingGrid.Rows.Add(option.Display, option.Name, conflict ? "Name conflict" : "Create new");
                    mappingGrid.Rows[rowIndex].Tag = option;
                    mappingGrid.Rows[rowIndex].Cells["Incoming"].ReadOnly = true;
                    mappingGrid.Rows[rowIndex].Cells["Action"].ReadOnly = true;
                    mappingGrid.Rows[rowIndex].Cells["Target"].ToolTipText = "Edit this name before creating the new item.";
                    if (conflict)
                    {
                        mappingGrid.Rows[rowIndex].DefaultCellStyle.BackColor = Color.FromArgb(255, 236, 214);
                        mappingGrid.Rows[rowIndex].Cells["Action"].ToolTipText = "This incoming item cannot be created until it is mapped or renamed.";
                    }
                }
            }
            finally
            {
                suppressGridEvents = false;
            }

            RestoreGridPosition(currentTag, currentColumnName, firstDisplayedRow);
        }

        private void RestoreGridPosition(object currentTag, string currentColumnName, int firstDisplayedRow)
        {
            if (mappingGrid.Rows.Count == 0) return;
            var rowIndex = -1;
            if (currentTag != null)
            {
                for (var i = 0; i < mappingGrid.Rows.Count; i++)
                {
                    if (SameGridTag(mappingGrid.Rows[i].Tag, currentTag))
                    {
                        rowIndex = i;
                        break;
                    }
                }
            }
            if (rowIndex < 0)
            {
                rowIndex = Math.Min(Math.Max(firstDisplayedRow, 0), mappingGrid.Rows.Count - 1);
            }

            var columnIndex = !string.IsNullOrWhiteSpace(currentColumnName) && mappingGrid.Columns.Contains(currentColumnName)
                ? mappingGrid.Columns[currentColumnName].Index
                : 0;
            try
            {
                if (firstDisplayedRow >= 0 && firstDisplayedRow < mappingGrid.Rows.Count)
                {
                    mappingGrid.FirstDisplayedScrollingRowIndex = firstDisplayedRow;
                }
                mappingGrid.CurrentCell = mappingGrid.Rows[rowIndex].Cells[columnIndex];
            }
            catch
            {
                // Best effort only; grid can reject invisible/read-only current cells while rebuilding combo cells.
            }
        }

        private static bool SameGridTag(object left, object right)
        {
            if (ReferenceEquals(left, right)) return true;
            var leftItem = left as MergeEntityItem;
            var rightItem = right as MergeEntityItem;
            if (leftItem == null || rightItem == null) return false;
            return string.Equals(leftItem.MapKey, rightItem.MapKey, StringComparison.Ordinal) &&
                string.Equals(leftItem.Id, rightItem.Id, StringComparison.Ordinal);
        }

        private void ApplyGridMappingChange(int rowIndex)
        {
            if (!(mappingGrid.Rows[rowIndex].Tag is MergeEntityItem target)) return;
            var selected = mappingGrid.Rows[rowIndex].Cells["Incoming"].Value as IncomingOption;
            var map = CurrentMap();
            foreach (var key in map.Where(pair => string.Equals(pair.Value, target.Id, StringComparison.Ordinal)).Select(pair => pair.Key).ToList())
            {
                map.Remove(key);
            }
            if (selected != null && !string.IsNullOrWhiteSpace(selected.MapKey))
            {
                map[selected.MapKey] = target.Id;
            }
            RefreshMappingGrid();
        }

        private void ApplyCreateNameChange(int rowIndex)
        {
            if (!(mappingGrid.Rows[rowIndex].Tag is MergeEntityItem incoming) || incoming.Source == null) return;
            var newName = Convert.ToString(mappingGrid.Rows[rowIndex].Cells["Target"].Value)?.Trim();
            if (string.IsNullOrWhiteSpace(newName)) return;
            if (!string.Equals(newName, incoming.Name, StringComparison.Ordinal))
            {
                RenameIncomingItem(incoming, newName);
                RefreshMappingGrid();
            }
        }

        private void ClearSelectedMapping()
        {
            if (!(mappingGrid.CurrentRow?.Tag is MergeEntityItem target)) return;
            var map = CurrentMap();
            foreach (var key in map.Where(pair => string.Equals(pair.Value, target.Id, StringComparison.Ordinal)).Select(pair => pair.Key).ToList())
            {
                map.Remove(key);
            }
            RefreshMappingGrid();
        }

        private Dictionary<string, string> CurrentMap()
        {
            if (step == 0) return environmentMap;
            if (step == 1) return serviceMap;
            return variableMap;
        }

        private List<MergeEntityItem> CurrentItems()
        {
            if (step == 0)
            {
                return targetProject.Environments.OrderBy(e => e.SortOrder).ThenBy(e => e.Name)
                    .Select(e => new MergeEntityItem(e.Id, e.Id, e.Name, e.Name, null)).ToList();
            }
            if (step == 1)
            {
                return targetProject.Services.OrderBy(s => s.SortOrder).ThenBy(s => s.Name)
                    .Select(s => new MergeEntityItem(s.Id, s.Id, s.Name, s.Name, null)).ToList();
            }
            return targetProject.Variables.OrderBy(v => v.SortOrder).ThenBy(v => v.Key)
                .Select(v => new MergeEntityItem(v.Id, v.Id, v.Key, v.Key, null)).ToList();
        }

        private List<MergeEntityItem> IncomingItems()
        {
            if (step == 0)
            {
                return sources.SelectMany(source => source.Project.Environments
                    .OrderBy(e => e.SortOrder).ThenBy(e => e.Name)
                    .Select(e => new MergeEntityItem(EntityKey(source, e.Id), e.Id, e.Name, source.Label + ": " + e.Name, source))).ToList();
            }
            if (step == 1)
            {
                return sources.SelectMany(source => source.Project.Services
                    .OrderBy(s => s.SortOrder).ThenBy(s => s.Name)
                    .Select(s => new MergeEntityItem(EntityKey(source, s.Id), s.Id, s.Name, source.Label + ": " + s.Name, source))).ToList();
            }
            return sources.SelectMany(source => source.Project.Variables
                .OrderBy(v => v.SortOrder).ThenBy(v => v.Key)
                .Select(v => new MergeEntityItem(EntityKey(source, v.Id), v.Id, v.Key, source.Label + ": " + v.Key, source))).ToList();
        }

        private void ApplyEnvironmentMappings(MergeVaultSource source, ProjectModel clone)
        {
            foreach (var environment in clone.Environments)
            {
                if (!environmentMap.TryGetValue(EntityKey(source, environment.Id), out var targetId)) continue;
                var target = targetProject.Environments.FirstOrDefault(e => e.Id == targetId);
                if (target == null) continue;
                environment.Id = target.Id;
                environment.Name = target.Name;
                environment.DisplayName = target.DisplayName;
            }
            foreach (var value in clone.Values.Where(v => !string.IsNullOrWhiteSpace(v.EnvironmentId)))
            {
                if (environmentMap.TryGetValue(EntityKey(source, value.EnvironmentId), out var targetId)) value.EnvironmentId = targetId;
            }
            foreach (var target in clone.Settings?.OutputTargets ?? Enumerable.Empty<OutputTargetSetting>())
            {
                if (!string.IsNullOrWhiteSpace(target.EnvironmentId) && environmentMap.TryGetValue(EntityKey(source, target.EnvironmentId), out var targetId))
                {
                    target.EnvironmentId = targetId;
                }
            }
        }

        private void ApplyServiceMappings(MergeVaultSource source, ProjectModel clone)
        {
            foreach (var service in clone.Services)
            {
                if (!serviceMap.TryGetValue(EntityKey(source, service.Id), out var targetId)) continue;
                var target = targetProject.Services.FirstOrDefault(s => s.Id == targetId);
                if (target == null) continue;
                service.Id = target.Id;
                service.Name = target.Name;
                service.DisplayName = target.DisplayName;
            }
            foreach (var variable in clone.Variables.Where(v => !string.IsNullOrWhiteSpace(v.OwnerServiceId)))
            {
                if (serviceMap.TryGetValue(EntityKey(source, variable.OwnerServiceId), out var targetId)) variable.OwnerServiceId = targetId;
            }
            foreach (var contract in clone.Contracts.Where(c => !string.IsNullOrWhiteSpace(c.ServiceId)))
            {
                if (serviceMap.TryGetValue(EntityKey(source, contract.ServiceId), out var targetId)) contract.ServiceId = targetId;
            }
            foreach (var value in clone.Values.Where(v => !string.IsNullOrWhiteSpace(v.ServiceId)))
            {
                if (serviceMap.TryGetValue(EntityKey(source, value.ServiceId), out var targetId)) value.ServiceId = targetId;
            }
            foreach (var target in clone.Settings?.OutputTargets ?? Enumerable.Empty<OutputTargetSetting>())
            {
                if (!string.IsNullOrWhiteSpace(target.ServiceId) && serviceMap.TryGetValue(EntityKey(source, target.ServiceId), out var targetId))
                {
                    target.ServiceId = targetId;
                }
            }
        }

        private void ApplyVariableMappings(MergeVaultSource source, ProjectModel clone)
        {
            foreach (var variable in clone.Variables)
            {
                if (!variableMap.TryGetValue(EntityKey(source, variable.Id), out var targetId)) continue;
                var target = targetProject.Variables.FirstOrDefault(v => v.Id == targetId);
                if (target == null) continue;
                variable.Key = target.Key;
            }
        }

        private static string EntityKey(MergeVaultSource source, string id)
        {
            return source.Index + ":" + id;
        }

        private static T Clone<T>(T value)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            return serializer.Deserialize<T>(serializer.Serialize(value));
        }

        private sealed class IncomingOption
        {
            public IncomingOption(string mapKey, string display)
            {
                MapKey = mapKey;
                Display = display;
            }

            public string MapKey { get; }
            public string Display { get; }
            public override string ToString() => Display;
            public override bool Equals(object obj) => obj is IncomingOption other && string.Equals(MapKey ?? string.Empty, other.MapKey ?? string.Empty, StringComparison.Ordinal);
            public override int GetHashCode() => (MapKey ?? string.Empty).GetHashCode();
        }

        private sealed class MergeEntityItem
        {
            public MergeEntityItem(string mapKey, string id, string name, string display, MergeVaultSource source)
            {
                MapKey = mapKey;
                Id = id;
                Name = name;
                Display = display;
                Source = source;
            }

            public string MapKey { get; }
            public string Id { get; }
            public string Name { get; }
            public string Display { get; }
            public MergeVaultSource Source { get; }
            public override string ToString() => Display;
        }
    }

    internal sealed class MergeVaultSource
    {
        public int Index { get; set; }
        public string Path { get; set; }
        public string Label { get; set; }
        public ProjectModel Project { get; set; }
    }
}

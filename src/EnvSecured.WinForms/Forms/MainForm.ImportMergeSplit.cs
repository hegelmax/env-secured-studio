using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using EnvSecured.Core.Models;
using EnvSecured.Core.Services;

namespace EnvSecured.WinForms.Forms
{
    public sealed partial class MainForm
    {
        private void RenderImportView()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 62));

            var fileButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            AddCommand(fileButtons, "Add Files", AddImportFiles, "FilesAdd", 110);
            AddCommand(fileButtons, "Select All", () => SetImportFilesSelected(true), "SelectAll", 108);
            AddCommand(fileButtons, "Select None", () => SetImportFilesSelected(false), "SelectNone", 116);
            removeImportFilesButton = AddCommand(fileButtons, "Remove Files", RemoveImportFile, "FilesRemove", 124);
            setImportEnvironmentButton = AddCommand(fileButtons, "Set Env For Selected", SetImportEnvironmentForSelected, "Environments", 154);
            setImportServiceButton = AddCommand(fileButtons, "Set Service For Selected", SetImportServiceForSelected, "Services", 166);
            UpdateImportFileActionButtons();
            fileButtons.Controls.Add(new Label
            {
                Text = "Import files: preview updates automatically after editing Environment or Service",
                AutoSize = true,
                Padding = new Padding(12, 7, 0, 0),
                Font = new Font(Font, FontStyle.Bold)
            });

            var importFilesControl = importFiles.Count == 0
                ? BuildEmptyImportPanel("No files added. Click Add Files.")
                : BuildImportFilesGrid();

            var previewButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            AddCommand(previewButtons, "Select All", () => SetImportPreviewIncluded(true), "SelectAll", 108);
            AddCommand(previewButtons, "Select None", () => SetImportPreviewIncluded(false), "SelectNone", 116);
            AddCommand(previewButtons, "Set Env For Included", SetImportPreviewEnvironmentForIncluded, "Environments", 154);
            AddCommand(previewButtons, "Set Service For Included", SetImportPreviewServiceForIncluded, "Services", 166);
            AddCommand(previewButtons, "Secret On", () => SetImportPreviewSecretForIncluded(true), "Key", 110);
            AddCommand(previewButtons, "Secret Off", () => SetImportPreviewSecretForIncluded(false), "Unlock", 110);
            AddCommand(previewButtons, "Required On", () => SetImportPreviewRequiredForIncluded(true), "Warning", 120);
            AddCommand(previewButtons, "Required Off", () => SetImportPreviewRequiredForIncluded(false), "Info", 120);
            AddCommand(previewButtons, "Apply Import", ApplyImportPreview, "Apply", 124);
            previewButtons.Controls.Add(new Label
            {
                Text = "Preview: edit Include, Environment, Service, Key and NewValue before applying",
                AutoSize = true,
                Padding = new Padding(12, 7, 0, 0),
                Font = new Font(Font, FontStyle.Bold)
            });

            var importPreviewControl = importPreviewRows.Count == 0
                ? BuildEmptyImportPanel("No preview yet. Add files and click Preview.")
                : BuildImportPreviewGrid();

            root.Controls.Add(fileButtons, 0, 0);
            root.Controls.Add(importFilesControl, 0, 1);
            root.Controls.Add(previewButtons, 0, 2);
            root.Controls.Add(importPreviewControl, 0, 3);
            contentPanel.Controls.Add(root);
        }

        private static Control BuildEmptyImportPanel(string text)
        {
            return new Panel
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = SystemColors.ControlLight,
                Controls =
                {
                    new Label
                    {
                        Dock = DockStyle.Fill,
                        Text = text,
                        TextAlign = ContentAlignment.MiddleCenter
                    }
                }
            };
        }

        private void RenderMergeVaultView()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(8) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            root.Controls.Add(BuildPageHeader("Merge Vaults", "Compare external .envs vault files with the current project and choose what to import."), 0, 0);

            var commands = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            AddCommand(commands, "Open Vaults", MergeVaultFilesFromUi, "Merge", 120);
            AddCommand(commands, "Select All", () => SetMergePreviewIncluded(true), "SelectAll", 108);
            AddCommand(commands, "Select None", () => SetMergePreviewIncluded(false), "SelectNone", 116);
            AddCommand(commands, "Apply Merge", ApplyMergePreview, "Apply", 124);
            root.Controls.Add(commands, 0, 1);

            var preview = mergePreviewRows.Count == 0
                ? BuildEmptyImportPanel("No merge preview. Click Open Vaults.")
                : BuildMergePreviewGrid();
            root.Controls.Add(preview, 0, 2);
            contentPanel.Controls.Add(root);
        }

        private void RenderSplitVaultView()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(8) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 184));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var header = BuildPageHeader("Split Vault", "Export selected variables from the current project into a separate .envs vault file with its own password.");
            root.Controls.Add(header, 0, 0);

            var form = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 6 };
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            for (var i = 0; i < 6; i++) form.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

            form.Controls.Add(BuildFieldLabel("Target file:"), 0, 0);
            form.Controls.Add(new TextBox { Name = "SplitTargetBox", Dock = DockStyle.Fill }, 1, 0);
            AddCommandCell(form, "Browse...", BrowseSplitTargetFile, "Open", 2, 0);

            form.Controls.Add(BuildFieldLabel("Project name:"), 0, 1);
            form.Controls.Add(new TextBox { Name = "SplitProjectNameBox", Dock = DockStyle.Fill, Text = (project.ProjectName ?? "project") + "-split" }, 1, 1);

            form.Controls.Add(BuildFieldLabel("Selection:"), 0, 2);
            var modeCombo = new ComboBox { Name = "SplitModeCombo", Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            modeCombo.Items.AddRange(new object[] { "Selected variables", "Owner service", "Scope service", "All variables" });
            modeCombo.SelectedIndex = 0;
            modeCombo.SelectedIndexChanged += (s, e) => UpdateSplitViewState();
            form.Controls.Add(modeCombo, 1, 2);

            form.Controls.Add(BuildFieldLabel("Service:"), 0, 3);
            var serviceCombo = new ComboBox { Name = "SplitServiceCombo", Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var service in project.Services.OrderBy(s => s.SortOrder).ThenBy(s => s.Name))
            {
                serviceCombo.Items.Add(new ScopeSelectorItem(service.Id, service.Name));
            }
            if (serviceCombo.Items.Count > 0) serviceCombo.SelectedIndex = 0;
            form.Controls.Add(serviceCombo, 1, 3);

            form.Controls.Add(BuildFieldLabel("Storage:"), 0, 4);
            var encryptionCombo = new ComboBox { Name = "SplitEncryptionCombo", Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            encryptionCombo.Items.AddRange(new object[] { EncryptionOpen, EncryptionSecrets, EncryptionAllValues, EncryptionWholeJson });
            encryptionCombo.SelectedItem = EncryptionWholeJson;
            encryptionCombo.SelectedIndexChanged += (s, e) => UpdateSplitViewState();
            form.Controls.Add(encryptionCombo, 1, 4);

            form.Controls.Add(BuildFieldLabel("New password:"), 0, 5);
            form.Controls.Add(new TextBox { Name = "SplitPasswordBox", Dock = DockStyle.Fill, UseSystemPasswordChar = true }, 1, 5);
            form.Controls.Add(new CheckBox { Name = "SplitIncludeRefsBox", Text = "Include references", Checked = true, Dock = DockStyle.Fill }, 2, 5);
            root.Controls.Add(form, 0, 1);

            var commands = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            AddCommand(commands, "Create Split Vault", SplitVaultFileFromUi, "Split", 150);
            root.Controls.Add(commands, 0, 2);

            var variables = new CheckedListBox { Name = "SplitVariablesList", Dock = DockStyle.Fill, CheckOnClick = true };
            foreach (var variable in project.Variables.OrderBy(v => v.SortOrder).ThenBy(v => v.Key))
            {
                variables.Items.Add(variable.Key, false);
            }
            root.Controls.Add(variables, 0, 3);
            contentPanel.Controls.Add(root);
            UpdateSplitViewState();
        }

        private void RenderVaultActionView(string title, string description, string buttonText, string imageKey, Action action)
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(8) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));

            var commands = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            AddCommand(commands, buttonText, action, imageKey, 150);

            root.Controls.Add(BuildPageHeader(title, description), 0, 0);
            root.Controls.Add(commands, 0, 1);
            contentPanel.Controls.Add(root);
        }

        private FlowLayoutPanel BuildPageHeader(string title, string description)
        {
            var header = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            header.Controls.Add(new Label
            {
                Text = title,
                AutoSize = true,
                Font = new Font(Font.FontFamily, 16, FontStyle.Bold),
                Padding = new Padding(0, 4, 12, 0)
            });
            header.Controls.Add(new Label
            {
                Text = description,
                AutoSize = true,
                ForeColor = Color.FromArgb(80, 90, 105),
                Padding = new Padding(0, 10, 0, 0)
            });
            return header;
        }

        private static Label BuildFieldLabel(string text)
        {
            return new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        }

        private void AddCommandCell(TableLayoutPanel table, string text, Action action, string imageKey, int column, int row)
        {
            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(4, 0, 0, 0) };
            AddCommand(panel, text, action, imageKey, 100);
            table.Controls.Add(panel, column, row);
        }

        private DataGridView BuildImportFilesGrid()
        {
            importFilesGrid = BuildEditableGrid();
            importFilesGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Selected", HeaderText = "Selected", FillWeight = 45 });
            importFilesGrid.Columns.Add("FilePath", "FilePath");
            importFilesGrid.Columns.Add(BuildEnvironmentComboColumn());
            importFilesGrid.Columns.Add(BuildServiceComboColumn());
            importFilesGrid.CellEndEdit += (s, e) => RebuildImportPreviewFromGrid();
            importFilesGrid.CellValueChanged += (s, e) =>
            {
                HandleImportComboAddNew(importFilesGrid, e.RowIndex, e.ColumnIndex, true);
                SyncImportFilesFromGrid();
                UpdateImportFileActionButtons();
            };
            importFilesGrid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (importFilesGrid.IsCurrentCellDirty)
                {
                    importFilesGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }
            };
            foreach (var file in importFiles)
            {
                importFilesGrid.Rows.Add(file.Selected, file.FilePath, NormalizeEnvironmentOption(file.Environment), NormalizeServiceOption(file.Service));
            }
            AttachColumnWidthPersistence(importFilesGrid, "ImportFiles");
            RestoreColumnWidths(importFilesGrid, "ImportFiles");
            return importFilesGrid;
        }

        private DataGridView BuildMergePreviewGrid()
        {
            mergePreviewGrid = BuildEditableGrid();
            mergePreviewGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Include", HeaderText = "Include", FillWeight = 45 });
            mergePreviewGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Action", HeaderText = "Action", ReadOnly = true, FillWeight = 80 });
            mergePreviewGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Key", HeaderText = "Key", ReadOnly = true, FillWeight = 150 });
            mergePreviewGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Scope", HeaderText = "Scope", ReadOnly = true, FillWeight = 90 });
            mergePreviewGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Service", HeaderText = "Service", ReadOnly = true, FillWeight = 90 });
            mergePreviewGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Environment", HeaderText = "Environment", ReadOnly = true, FillWeight = 90 });
            mergePreviewGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Current", HeaderText = "Current", ReadOnly = true, FillWeight = 160 });
            mergePreviewGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Incoming", HeaderText = "Incoming", ReadOnly = true, FillWeight = 160 });
            mergePreviewGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "File", HeaderText = "File", ReadOnly = true, FillWeight = 160 });
            mergePreviewGrid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (mergePreviewGrid.IsCurrentCellDirty)
                {
                    mergePreviewGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }
            };
            mergePreviewGrid.CellValueChanged += (s, e) => SyncMergePreviewFromGrid();
            foreach (var row in mergePreviewRows)
            {
                mergePreviewGrid.Rows.Add(row.Include, row.Action, row.Key, row.Scope, row.Service, row.Environment, row.CurrentValue, row.IncomingValue, row.FilePath);
            }
            AttachColumnWidthPersistence(mergePreviewGrid, "MergePreview");
            RestoreColumnWidths(mergePreviewGrid, "MergePreview");
            return mergePreviewGrid;
        }

        private DataGridView BuildImportPreviewGrid()
        {
            importPreviewGrid = BuildEditableGrid();
            importPreviewGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Include", HeaderText = "Include" });
            importPreviewGrid.Columns.Add("Action", "Action");
            importPreviewGrid.Columns.Add("Key", "Key");
            importPreviewGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Secret", HeaderText = "Secret" });
            importPreviewGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Required", HeaderText = "Required" });
            importPreviewGrid.Columns.Add(BuildEnvironmentComboColumn());
            importPreviewGrid.Columns.Add(BuildServiceComboColumn());
            importPreviewGrid.Columns.Add("Scope", "Scope");
            importPreviewGrid.Columns.Add("OldValue", "OldValue");
            importPreviewGrid.Columns.Add("NewValue", "NewValue");
            importPreviewGrid.Columns.Add("FilePath", "FilePath");
            importPreviewGrid.CellValueChanged += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
                var columnName = importPreviewGrid.Columns[e.ColumnIndex].Name;
                var isAddNew = Convert.ToString(importPreviewGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value) == AddNewOption;
                HandleImportComboAddNew(importPreviewGrid, e.RowIndex, e.ColumnIndex, false);
                if (isAddNew) return;
                if (columnName == "Environment" || columnName == "Service")
                {
                    SyncImportPreviewFromGrid();
                    foreach (var row in importPreviewRows)
                    {
                        RefreshImportPreviewRowTarget(row);
                    }
                    RenderCurrentView();
                }
            };
            importPreviewGrid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (importPreviewGrid.IsCurrentCellDirty)
                {
                    importPreviewGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }
            };
            foreach (var row in importPreviewRows)
            {
                importPreviewGrid.Rows.Add(row.Include, row.Action, row.Key, row.Secret, row.Required, NormalizeEnvironmentOption(row.Environment), NormalizeServiceOption(row.Service), row.Scope, row.OldValue, row.NewValue, row.FilePath);
            }
            AttachColumnWidthPersistence(importPreviewGrid, "ImportPreview");
            RestoreColumnWidths(importPreviewGrid, "ImportPreview");
            return importPreviewGrid;
        }

        private DataGridViewComboBoxColumn BuildEnvironmentComboColumn()
        {
            var column = new DataGridViewComboBoxColumn { Name = "Environment", HeaderText = "Environment", FlatStyle = FlatStyle.Flat };
            column.Items.Add(GlobalOption);
            foreach (var env in project.Environments.OrderBy(e => e.SortOrder))
            {
                column.Items.Add(env.Name);
            }
            column.Items.Add(AddNewOption);
            return column;
        }

        private DataGridViewComboBoxColumn BuildServiceComboColumn()
        {
            var column = new DataGridViewComboBoxColumn { Name = "Service", HeaderText = "Service", FlatStyle = FlatStyle.Flat };
            column.Items.Add(AllServicesOption);
            foreach (var service in project.Services.OrderBy(s => s.SortOrder))
            {
                column.Items.Add(service.Name);
            }
            column.Items.Add(AddNewOption);
            return column;
        }

        private static DataGridView BuildEditableGrid()
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                MultiSelect = false,
                RowHeadersVisible = false
            };
            grid.DataError += (s, e) => { e.ThrowException = false; };
            grid.DataBindingComplete += (s, e) =>
            {
                if (grid.Rows.Count == 0)
                {
                    grid.ClearSelection();
                    grid.CurrentCell = null;
                }
            };
            return grid;
        }

        private void HandleImportComboAddNew(DataGridView grid, int rowIndex, int columnIndex, bool rebuildPreview)
        {
            if (suppressImportComboAddNew) return;
            if (rowIndex < 0 || columnIndex < 0) return;
            var columnName = grid.Columns[columnIndex].Name;
            if (columnName != "Environment" && columnName != "Service") return;
            if (Convert.ToString(grid.Rows[rowIndex].Cells[columnIndex].Value) != AddNewOption) return;

            if (columnName == "Environment")
            {
                var name = PromptDialog.Show(this, "Add Environment", "Name:", "dev");
                if (string.IsNullOrWhiteSpace(name))
                {
                    SetImportComboCellValue(grid, rowIndex, columnIndex, GlobalOption);
                    RefreshImportAfterComboChange(rebuildPreview);
                    return;
                }

                var id = Slug(name);
                if (!project.Environments.Any(e => e.Id == id || string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    project.Environments.Add(new EnvironmentModel { Id = id, Name = id, DisplayName = name, SortOrder = project.Environments.Count * 10 });
                    modified = true;
                    SaveRecoveryBackupIfPossible();
                }
                SetImportComboCellValue(grid, rowIndex, columnIndex, id);
            }
            else
            {
                var name = PromptDialog.Show(this, "Add Service", "Name:", "backend");
                if (string.IsNullOrWhiteSpace(name))
                {
                    SetImportComboCellValue(grid, rowIndex, columnIndex, AllServicesOption);
                    RefreshImportAfterComboChange(rebuildPreview);
                    return;
                }

                var id = Slug(name);
                if (!project.Services.Any(s => s.Id == id || string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    var prefix = PromptDialog.Show(this, "Add Service", "Default prefix:", id.ToUpperInvariant() + "_");
                    if (prefix == null)
                    {
                        SetImportComboCellValue(grid, rowIndex, columnIndex, AllServicesOption);
                        RefreshImportAfterComboChange(rebuildPreview);
                        return;
                    }
                    project.Services.Add(new ServiceModel { Id = id, Name = id, DisplayName = name, OutputFolder = id, DefaultPrefix = prefix, SortOrder = project.Services.Count * 10 });
                    AutoAssignVariablesToService(project.Services.Last());
                    modified = true;
                    SaveRecoveryBackupIfPossible();
                }
                SetImportComboCellValue(grid, rowIndex, columnIndex, id);
            }

            RefreshImportAfterComboChange(rebuildPreview);
        }

        private void SetImportComboCellValue(DataGridView grid, int rowIndex, int columnIndex, string value)
        {
            suppressImportComboAddNew = true;
            try
            {
                grid.Rows[rowIndex].Cells[columnIndex].Value = value;
                grid.EndEdit();
            }
            finally
            {
                suppressImportComboAddNew = false;
            }
        }

        private void RefreshImportAfterComboChange(bool rebuildPreview)
        {
            if (rebuildPreview)
            {
                RebuildImportPreviewFromGrid();
            }
            else
            {
                SyncImportPreviewFromGrid();
                foreach (var row in importPreviewRows)
                {
                    RefreshImportPreviewRowTarget(row);
                }
                RenderCurrentView();
            }
        }

        private void AddImportFiles()
        {
            using (var dialog = new OpenFileDialog
            {
                Filter = "Config files (*.env;*.config;*.txt)|*.env;*.config;*.txt|All files (*.*)|*.*",
                Multiselect = true
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                foreach (var path in dialog.FileNames)
                {
                    if (importFiles.Any(f => string.Equals(f.FilePath, path, StringComparison.OrdinalIgnoreCase))) continue;
                    importFiles.Add(new ImportFileRow
                    {
                    FilePath = path,
                    Selected = false,
                    Environment = NormalizeEnvironmentOption(InferEnvironmentName(path)),
                    Service = NormalizeServiceOption(InferServiceName(path))
                });
            }
            }
            BuildImportPreviewRows();
            RenderCurrentView();
        }

        private void MergeVaultFilesFromUi()
        {
            using (var dialog = new OpenFileDialog
            {
                Filter = "EnvSecured Studio vault (*.envs)|*.envs|All files (*.*)|*.*",
                Multiselect = true
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                var loaded = new List<MergeVaultSource>();
                mergePreviewRows.Clear();
                for (var i = 0; i < dialog.FileNames.Length; i++)
                {
                    var path = dialog.FileNames[i];
                    try
                    {
                        var source = LoadVaultFileWithIsolatedKey(path);
                        if (source == null) return;
                        loaded.Add(new MergeVaultSource
                        {
                            Index = i,
                            Path = path,
                            Label = Path.GetFileName(path),
                            Project = source
                        });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, "Could not open vault file.\r\n\r\n" + path + "\r\n\r\n" + ex.Message, "Merge Vaults", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }

                using (var wizard = new MergeVaultWizardForm(project, loaded))
                {
                    if (wizard.ShowDialog(this) != DialogResult.OK) return;
                    var mappedSources = wizard.BuildMappedSources();
                    for (var i = 0; i < mappedSources.Count; i++)
                    {
                        AddMergePreviewRows(mappedSources[i], loaded[i].Path);
                    }
                }

                RenderCurrentView();
                if (mergePreviewRows.Count == 0)
                {
                    MessageBox.Show(this, "No differences found.", "Merge Vaults", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void AddMergePreviewRows(ProjectModel source, string path)
        {
            ProjectService.EnsureProjectCollections(source);
            foreach (var sourceVariable in source.Variables.OrderBy(v => v.SortOrder).ThenBy(v => v.Key))
            {
                var targetVariable = project.Variables.FirstOrDefault(v => string.Equals(v.Key, sourceVariable.Key, StringComparison.OrdinalIgnoreCase));
                var values = source.Values.Where(v => v.VariableId == sourceVariable.Id).ToList();
                if (targetVariable == null && values.Count == 0)
                {
                    mergePreviewRows.Add(new MergePreviewRow
                    {
                        Include = true,
                        Action = "Add variable",
                        Key = sourceVariable.Key,
                        CurrentValue = "-",
                        IncomingValue = OwnerDisplayName(source, sourceVariable),
                        FilePath = path,
                        SourceProject = source,
                        SourceVariableId = sourceVariable.Id
                    });
                    continue;
                }

                foreach (var sourceValue in values)
                {
                    var mappedService = FindMatchingService(project, source, sourceValue.ServiceId);
                    var mappedEnvironment = FindMatchingEnvironment(project, source, sourceValue.EnvironmentId);
                    var targetValue = targetVariable == null
                        ? null
                        : project.Values.LastOrDefault(v =>
                            v.VariableId == targetVariable.Id &&
                            v.Scope == sourceValue.Scope &&
                            SameNullable(v.ServiceId, mappedService?.Id) &&
                            SameNullable(v.EnvironmentId, mappedEnvironment?.Id));
                    if (targetVariable != null && targetValue != null && string.Equals(targetValue.Value ?? string.Empty, sourceValue.Value ?? string.Empty, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    mergePreviewRows.Add(new MergePreviewRow
                    {
                        Include = true,
                        Action = targetVariable == null ? "Add variable value" : targetValue == null ? "Add value" : "Update value",
                        Key = sourceVariable.Key,
                        Scope = sourceValue.Scope.ToString(),
                        Service = ServiceName(source, sourceValue.ServiceId),
                        Environment = EnvironmentName(source, sourceValue.EnvironmentId),
                        CurrentValue = targetValue?.Value ?? "-",
                        IncomingValue = sourceValue.Value ?? string.Empty,
                        FilePath = path,
                        SourceProject = source,
                        SourceVariableId = sourceVariable.Id,
                        SourceValueId = sourceValue.Id
                    });
                }
            }
        }

        private static ServiceModel FindMatchingService(ProjectModel target, ProjectModel source, string sourceServiceId)
        {
            if (string.IsNullOrWhiteSpace(sourceServiceId)) return null;
            var sourceService = source.Services.FirstOrDefault(s => string.Equals(s.Id, sourceServiceId, StringComparison.Ordinal));
            return target.Services.FirstOrDefault(s =>
                string.Equals(s.Id, sourceServiceId, StringComparison.OrdinalIgnoreCase) ||
                (sourceService != null && string.Equals(s.Name, sourceService.Name, StringComparison.OrdinalIgnoreCase)));
        }

        private static EnvironmentModel FindMatchingEnvironment(ProjectModel target, ProjectModel source, string sourceEnvironmentId)
        {
            if (string.IsNullOrWhiteSpace(sourceEnvironmentId)) return null;
            var sourceEnvironment = source.Environments.FirstOrDefault(e => string.Equals(e.Id, sourceEnvironmentId, StringComparison.Ordinal));
            return target.Environments.FirstOrDefault(e =>
                string.Equals(e.Id, sourceEnvironmentId, StringComparison.OrdinalIgnoreCase) ||
                (sourceEnvironment != null && string.Equals(e.Name, sourceEnvironment.Name, StringComparison.OrdinalIgnoreCase)));
        }

        private static string OwnerDisplayName(ProjectModel source, VariableDefinitionModel variable)
        {
            if (variable == null || string.IsNullOrWhiteSpace(variable.OwnerServiceId)) return "(none)";
            return source.Services.FirstOrDefault(s => s.Id == variable.OwnerServiceId)?.Name ?? variable.OwnerServiceId;
        }

        private static string ServiceName(ProjectModel source, string serviceId)
        {
            if (string.IsNullOrWhiteSpace(serviceId)) return string.Empty;
            return source.Services.FirstOrDefault(s => s.Id == serviceId)?.Name ?? serviceId;
        }

        private static string EnvironmentName(ProjectModel source, string environmentId)
        {
            if (string.IsNullOrWhiteSpace(environmentId)) return string.Empty;
            return source.Environments.FirstOrDefault(e => e.Id == environmentId)?.Name ?? environmentId;
        }

        private void SyncMergePreviewFromGrid()
        {
            if (mergePreviewGrid == null) return;
            mergePreviewGrid.EndEdit();
            for (var i = 0; i < mergePreviewGrid.Rows.Count && i < mergePreviewRows.Count; i++)
            {
                mergePreviewRows[i].Include = Convert.ToBoolean(mergePreviewGrid.Rows[i].Cells["Include"].Value ?? false);
            }
        }

        private void SetMergePreviewIncluded(bool included)
        {
            SyncMergePreviewFromGrid();
            foreach (var row in mergePreviewRows)
            {
                row.Include = included;
            }
            RenderCurrentView();
        }

        private void ApplyMergePreview()
        {
            SyncMergePreviewFromGrid();
            var selected = mergePreviewRows.Where(r => r.Include).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show(this, "No merge rows selected.", "Merge Vaults", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var sources = new List<ProjectModel>();
            foreach (var group in selected.GroupBy(r => r.SourceProject))
            {
                var temp = CloneProject(group.Key);
                var variableIds = new HashSet<string>(group.Select(r => r.SourceVariableId).Where(id => !string.IsNullOrWhiteSpace(id)));
                var valueIds = new HashSet<string>(group.Select(r => r.SourceValueId).Where(id => !string.IsNullOrWhiteSpace(id)));
                temp.Variables = temp.Variables.Where(v => variableIds.Contains(v.Id)).ToList();
                temp.Contracts = temp.Contracts.Where(c => variableIds.Contains(c.VariableId)).ToList();
                temp.Values = temp.Values.Where(v => valueIds.Contains(v.Id)).ToList();
                sources.Add(temp);
            }

            var result = vaultTransformService.Merge(project, sources, true);
            modified = true;
            SaveRecoveryBackupIfPossible();
            mergePreviewRows.Clear();
            RenderCurrentView();
            MessageBox.Show(this,
                "Merged selected rows." + Environment.NewLine +
                "Variables added: " + result.VariablesAdded + Environment.NewLine +
                "Variables matched: " + result.VariablesMatched + Environment.NewLine +
                "Values merged: " + result.ValuesMerged,
                "Merge Vaults",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void SplitVaultFileFromUi()
        {
            var targetBox = contentPanel.Controls.Find("SplitTargetBox", true).FirstOrDefault() as TextBox;
            var nameBox = contentPanel.Controls.Find("SplitProjectNameBox", true).FirstOrDefault() as TextBox;
            var modeCombo = contentPanel.Controls.Find("SplitModeCombo", true).FirstOrDefault() as ComboBox;
            var serviceCombo = contentPanel.Controls.Find("SplitServiceCombo", true).FirstOrDefault() as ComboBox;
            var encryptionCombo = contentPanel.Controls.Find("SplitEncryptionCombo", true).FirstOrDefault() as ComboBox;
            var passwordBox = contentPanel.Controls.Find("SplitPasswordBox", true).FirstOrDefault() as TextBox;
            var includeRefsBox = contentPanel.Controls.Find("SplitIncludeRefsBox", true).FirstOrDefault() as CheckBox;
            var variablesList = contentPanel.Controls.Find("SplitVariablesList", true).FirstOrDefault() as CheckedListBox;

            var targetPath = targetBox?.Text?.Trim();
            var encryptionMode = SplitEncryptionMode(encryptionCombo);
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                MessageBox.Show(this, "Target file is required.", "Split Vault", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!string.Equals(encryptionMode, "Open", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(passwordBox?.Text))
            {
                MessageBox.Show(this, "New password is required for encrypted vault files.", "Split Vault", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (File.Exists(targetPath) &&
                MessageBox.Show(this, "Target file exists. Replace it?", "Split Vault", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            var mode = modeCombo?.SelectedIndex ?? 0;
            var serviceId = (serviceCombo?.SelectedItem as ScopeSelectorItem)?.Id;
            var options = new ProjectSplitOptions
            {
                All = mode == 3,
                IncludeReferences = includeRefsBox?.Checked == true,
                ProjectName = nameBox?.Text?.Trim()
            };
            if (mode == 0)
            {
                options.Keys.AddRange(variablesList?.CheckedItems.Cast<string>() ?? Enumerable.Empty<string>());
                if (options.Keys.Count == 0)
                {
                    MessageBox.Show(this, "Select at least one variable.", "Split Vault", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            else if (mode == 1)
            {
                if (string.IsNullOrWhiteSpace(serviceId))
                {
                    MessageBox.Show(this, "Service is required.", "Split Vault", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                options.OwnerServiceIds.Add(serviceId);
            }
            else if (mode == 2)
            {
                if (string.IsNullOrWhiteSpace(serviceId))
                {
                    MessageBox.Show(this, "Service is required.", "Split Vault", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                options.ScopeServiceIds.Add(serviceId);
            }

            ProjectModel split;
            try
            {
                split = vaultTransformService.Split(project, options);
                split.Settings = split.Settings ?? new ProjectSettings();
                split.Settings.EncryptionMode = encryptionMode;
                split.Settings.EncryptAllValues = string.Equals(encryptionMode, "AllValues", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Split Vault", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!SaveSplitVaultFile(split, targetPath, passwordBox?.Text))
            {
                return;
            }

            MessageBox.Show(this, "Split " + split.Variables.Count + " variable(s) into:" + Environment.NewLine + targetPath, "Split Vault", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void BrowseSplitTargetFile()
        {
            var targetBox = contentPanel.Controls.Find("SplitTargetBox", true).FirstOrDefault() as TextBox;
            using (var dialog = new SaveFileDialog
            {
                Filter = "EnvSecured Studio vault (*.envs)|*.envs|All files (*.*)|*.*",
                FileName = "split.envs",
                DefaultExt = "envs",
                AddExtension = true
            })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK && targetBox != null)
                {
                    targetBox.Text = dialog.FileName;
                }
            }
        }

        private void UpdateSplitViewState()
        {
            var modeCombo = contentPanel.Controls.Find("SplitModeCombo", true).FirstOrDefault() as ComboBox;
            var serviceCombo = contentPanel.Controls.Find("SplitServiceCombo", true).FirstOrDefault() as ComboBox;
            var encryptionCombo = contentPanel.Controls.Find("SplitEncryptionCombo", true).FirstOrDefault() as ComboBox;
            var passwordBox = contentPanel.Controls.Find("SplitPasswordBox", true).FirstOrDefault() as TextBox;
            var variablesList = contentPanel.Controls.Find("SplitVariablesList", true).FirstOrDefault() as CheckedListBox;
            var mode = modeCombo?.SelectedIndex ?? 0;
            if (variablesList != null) variablesList.Enabled = mode == 0;
            if (serviceCombo != null) serviceCombo.Enabled = mode == 1 || mode == 2;
            if (passwordBox != null) passwordBox.Enabled = !string.Equals(SplitEncryptionMode(encryptionCombo), "Open", StringComparison.OrdinalIgnoreCase);
        }

        private static string SplitEncryptionMode(ComboBox comboBox)
        {
            var label = Convert.ToString(comboBox?.SelectedItem);
            if (string.Equals(label, EncryptionWholeJson, StringComparison.OrdinalIgnoreCase)) return "WholeJson";
            if (string.Equals(label, EncryptionAllValues, StringComparison.OrdinalIgnoreCase)) return "AllValues";
            if (string.Equals(label, EncryptionSecrets, StringComparison.OrdinalIgnoreCase)) return "SecretsOnly";
            return "Open";
        }

        private ProjectModel LoadVaultFileWithIsolatedKey(string path)
        {
            var previousKey = vaultKey == null ? null : (byte[])vaultKey.Clone();
            try
            {
                return LoadVaultFile(path, false);
            }
            finally
            {
                ClearVaultKey();
                if (previousKey != null)
                {
                    SetVaultKey(previousKey);
                }
            }
        }

        private void SyncImportFilesFromGrid()
        {
            if (importFilesGrid == null) return;
            importFilesGrid.EndEdit();
            importFiles.Clear();
            foreach (DataGridViewRow row in importFilesGrid.Rows)
            {
                var path = Convert.ToString(row.Cells["FilePath"].Value);
                if (string.IsNullOrWhiteSpace(path)) continue;
                importFiles.Add(new ImportFileRow
                {
                    Selected = Convert.ToBoolean(row.Cells["Selected"].Value ?? false),
                    FilePath = path,
                    Environment = NormalizeEnvironmentOption(Convert.ToString(row.Cells["Environment"].Value)),
                    Service = NormalizeServiceOption(Convert.ToString(row.Cells["Service"].Value))
                });
            }
        }

        private void SetImportEnvironmentForSelected()
        {
            SyncImportFilesFromGrid();
            var value = ChooseEnvironmentOption();
            if (value == null) return;
            foreach (var file in importFiles.Where(f => f.Selected))
            {
                file.Environment = value;
            }
            BuildImportPreviewRows();
            RenderCurrentView();
        }

        private void SetImportServiceForSelected()
        {
            SyncImportFilesFromGrid();
            var value = ChooseServiceOption();
            if (value == null) return;
            foreach (var file in importFiles.Where(f => f.Selected))
            {
                file.Service = value;
            }
            BuildImportPreviewRows();
            RenderCurrentView();
        }

        private void UpdateImportFileActionButtons()
        {
            var hasSelected = importFiles.Any(f => f.Selected);
            if (removeImportFilesButton != null) removeImportFilesButton.Enabled = hasSelected;
            if (setImportEnvironmentButton != null) setImportEnvironmentButton.Enabled = hasSelected;
            if (setImportServiceButton != null) setImportServiceButton.Enabled = hasSelected;
        }

        private void SetImportFilesSelected(bool selected)
        {
            SyncImportFilesFromGrid();
            foreach (var file in importFiles)
            {
                file.Selected = selected;
            }
            RenderCurrentView();
        }

        private void SetVariableEnvironmentFilter()
        {
            if (environmentCombo == null) return;
            var value = ChooseEnvironmentOption();
            if (value == null) return;
            SelectComboByName(environmentCombo, NormalizeEnvironmentOption(value));
            RefreshVariableGrid();
        }

        private void SetVariableServiceFilter()
        {
            if (scopeFilterCombo == null) return;
            var value = ChooseServiceOption();
            if (value == null) return;
            SelectComboByName(scopeFilterCombo, NormalizeServiceOption(value));
            RefreshVariableGrid();
        }

        private static void SelectComboByName(ComboBox comboBox, string name)
        {
            for (var i = 0; i < comboBox.Items.Count; i++)
            {
                var item = comboBox.Items[i];
                var itemName = item.GetType().GetProperty("Name")?.GetValue(item, null)?.ToString() ?? item.ToString();
                if (string.Equals(itemName, name, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedIndex = i;
                    return;
                }
            }
        }

        private string ChooseEnvironmentOption()
        {
            using (var dialog = new OptionPickerForm("Set Environment", new[] { GlobalOption }.Concat(project.Environments.OrderBy(e => e.SortOrder).Select(e => e.Name)).Concat(new[] { AddNewOption }).ToArray()))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return null;
                if (dialog.SelectedValue == AddNewOption)
                {
                    var name = PromptDialog.Show(this, "Add Environment", "Name:", "dev");
                    if (string.IsNullOrWhiteSpace(name)) return null;
                    var id = Slug(name);
                    if (!project.Environments.Any(e => e.Id == id || string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        project.Environments.Add(new EnvironmentModel { Id = id, Name = id, DisplayName = name, SortOrder = project.Environments.Count * 10 });
                        modified = true;
                        SaveRecoveryBackupIfPossible();
                    }
                    return id;
                }
                return dialog.SelectedValue;
            }
        }

        private string ChooseServiceOption()
        {
            using (var dialog = new OptionPickerForm("Set Service", new[] { AllServicesOption }.Concat(project.Services.OrderBy(s => s.SortOrder).Select(s => s.Name)).Concat(new[] { AddNewOption }).ToArray()))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return null;
                if (dialog.SelectedValue == AddNewOption)
                {
                    var name = PromptDialog.Show(this, "Add Service", "Name:", "backend");
                    if (string.IsNullOrWhiteSpace(name)) return null;
                    var id = Slug(name);
                    if (!project.Services.Any(s => s.Id == id || string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        var prefix = PromptDialog.Show(this, "Add Service", "Default prefix:", id.ToUpperInvariant() + "_");
                        project.Services.Add(new ServiceModel { Id = id, Name = id, DisplayName = name, OutputFolder = id, DefaultPrefix = prefix, SortOrder = project.Services.Count * 10 });
                        AutoAssignVariablesToService(project.Services.Last());
                        modified = true;
                        SaveRecoveryBackupIfPossible();
                    }
                    return id;
                }
                return dialog.SelectedValue;
            }
        }

        private void SyncImportPreviewFromGrid()
        {
            if (importPreviewGrid == null) return;
            importPreviewGrid.EndEdit();
            importPreviewRows.Clear();
            foreach (DataGridViewRow row in importPreviewGrid.Rows)
            {
                importPreviewRows.Add(new ImportPreviewRow
                {
                    Include = Convert.ToBoolean(row.Cells["Include"].Value ?? false),
                    Action = Convert.ToString(row.Cells["Action"].Value),
                    Key = Convert.ToString(row.Cells["Key"].Value),
                    Secret = Convert.ToBoolean(row.Cells["Secret"].Value ?? false),
                    Required = Convert.ToBoolean(row.Cells["Required"].Value ?? false),
                    Environment = NormalizeEnvironmentOption(Convert.ToString(row.Cells["Environment"].Value)),
                    Service = NormalizeServiceOption(Convert.ToString(row.Cells["Service"].Value)),
                    Scope = Convert.ToString(row.Cells["Scope"].Value),
                    OldValue = Convert.ToString(row.Cells["OldValue"].Value),
                    NewValue = Convert.ToString(row.Cells["NewValue"].Value),
                    FilePath = Convert.ToString(row.Cells["FilePath"].Value)
                });
            }
        }

        private void RemoveImportFile()
        {
            SyncImportFilesFromGrid();
            importFiles.RemoveAll(f => f.Selected);
            BuildImportPreviewRows();
            RenderCurrentView();
        }

        private void RebuildImportPreviewFromGrid()
        {
            SyncImportFilesFromGrid();
            BuildImportPreviewRows();
            RenderCurrentView();
        }

        private void BuildImportPreviewRows()
        {
            importPreviewRows.Clear();
            foreach (var file in importFiles.ToList())
            {
                foreach (var pair in ParseConfigFile(file.FilePath))
                {
                    var target = ResolveImportTarget(file.Environment, file.Service);
                    var variable = project.Variables.FirstOrDefault(v => string.Equals(v.Key, pair.Key, StringComparison.OrdinalIgnoreCase));
                    var contract = target.ServiceId == null || variable == null
                        ? null
                        : project.Contracts.FirstOrDefault(c => c.VariableId == variable.Id && c.ServiceId == target.ServiceId);
                    var oldValue = FindRawValue(pair.Key, target.Scope, target.EnvironmentId, target.ServiceId);
                    importPreviewRows.Add(new ImportPreviewRow
                    {
                        Include = oldValue != pair.Value,
                        FilePath = file.FilePath,
                        Environment = file.Environment,
                        Service = file.Service,
                        Scope = target.Scope.ToString(),
                        Key = pair.Key,
                        Secret = variable?.IsSecret ?? IsSecretImportKey(pair.Key),
                        Required = contract?.Required ?? true,
                        OldValue = oldValue,
                        NewValue = pair.Value,
                        Action = oldValue == null ? "Add" : oldValue == pair.Value ? "No change" : "Overwrite"
                    });
                }
            }
        }

        private void SetImportPreviewIncluded(bool included)
        {
            SyncImportPreviewFromGrid();
            foreach (var row in importPreviewRows)
            {
                row.Include = included;
            }
            RenderCurrentView();
        }

        private void SetImportPreviewEnvironmentForIncluded()
        {
            SyncImportPreviewFromGrid();
            var value = ChooseEnvironmentOption();
            if (value == null) return;
            foreach (var row in importPreviewRows.Where(r => r.Include))
            {
                row.Environment = value;
                RefreshImportPreviewRowTarget(row);
            }
            RenderCurrentView();
        }

        private void SetImportPreviewServiceForIncluded()
        {
            SyncImportPreviewFromGrid();
            var value = ChooseServiceOption();
            if (value == null) return;
            foreach (var row in importPreviewRows.Where(r => r.Include))
            {
                row.Service = value;
                RefreshImportPreviewRowTarget(row);
            }
            RenderCurrentView();
        }

        private void SetImportPreviewSecretForIncluded(bool secret)
        {
            SyncImportPreviewFromGrid();
            foreach (var row in importPreviewRows.Where(r => r.Include))
            {
                row.Secret = secret;
            }
            RenderCurrentView();
        }

        private void SetImportPreviewRequiredForIncluded(bool required)
        {
            SyncImportPreviewFromGrid();
            foreach (var row in importPreviewRows.Where(r => r.Include))
            {
                row.Required = required;
            }
            RenderCurrentView();
        }

        private void RefreshImportPreviewRowTarget(ImportPreviewRow row)
        {
            var target = ResolveImportTarget(row.Environment, row.Service);
            row.Scope = target.Scope.ToString();
            row.OldValue = FindRawValue(row.Key, target.Scope, target.EnvironmentId, target.ServiceId);
            row.Action = row.OldValue == null ? "Add" : row.OldValue == row.NewValue ? "No change" : "Overwrite";
        }

        private void ApplyImportPreview()
        {
            SyncImportPreviewFromGrid();
            var applied = 0;
            foreach (var row in importPreviewRows.Where(r => r.Include).ToList())
            {
                var target = ResolveImportTarget(row.Environment, row.Service);
                var variable = project.Variables.FirstOrDefault(v => string.Equals(v.Key, row.Key, StringComparison.OrdinalIgnoreCase));
                if (variable == null)
                {
                    if (string.IsNullOrWhiteSpace(target.ServiceId))
                    {
                        MessageBox.Show(this, $"New variable {row.Key} requires a service owner. Set Service before applying import.", "Import", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    variable = new VariableDefinitionModel
                    {
                        Id = UniqueVariableId(row.Key),
                        Key = row.Key.Trim().ToUpperInvariant(),
                        DisplayName = row.Key.Trim(),
                        Type = row.Secret ? VariableType.Password : VariableType.String,
                        IsSecret = row.Secret,
                        OwnerServiceId = target.ServiceId,
                        SortOrder = project.Variables.Count * 10
                    };
                    project.Variables.Add(variable);
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(variable.OwnerServiceId))
                    {
                        variable.OwnerServiceId = target.ServiceId;
                    }
                    variable.IsSecret = row.Secret;
                    variable.Type = row.Secret ? VariableType.Password : VariableType.String;
                }

                var existing = project.Values.LastOrDefault(v =>
                    v.VariableId == variable.Id &&
                    v.Scope == target.Scope &&
                    v.EnvironmentId == target.EnvironmentId &&
                    v.ServiceId == target.ServiceId);

                if (existing == null)
                {
                    project.Values.Add(new VariableValueModel
                    {
                        Id = ProjectService.NewId(),
                        VariableId = variable.Id,
                        Scope = target.Scope,
                        EnvironmentId = target.EnvironmentId,
                        ServiceId = target.ServiceId,
                        Value = row.NewValue,
                        IsEncrypted = row.Secret,
                        UpdatedAtUtc = DateTime.UtcNow
                    });
                }
                else
                {
                    existing.Value = row.NewValue;
                    existing.IsEncrypted = row.Secret;
                    existing.UpdatedAtUtc = DateTime.UtcNow;
                }

                if (target.ServiceId != null)
                {
                    var export = true;
                    var allowOverride = true;
                    var contract = ProjectService.EnsureServiceScopeContract(project, variable.Id, target.ServiceId, export, true, allowOverride);
                    contract.Required = row.Required;
                }
                applied++;
            }

            modified = true;
            SaveRecoveryBackupIfPossible();
            MessageBox.Show(this, $"Imported {applied} value(s).", "Import");
            importFiles.Clear();
            importPreviewRows.Clear();
            currentView = "Variables";
            RenderCurrentView();
        }
    }
}

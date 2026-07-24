using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
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
        private void RenderExportView()
        {
            project.Settings = project.Settings ?? new ProjectSettings();

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(8) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            AddCommand(buttons, "Apply", () => ApplyExportSettingsFromView(), "Apply", 96);
            AddCommand(buttons, "Export", () =>
            {
                ApplyExportSettingsFromView();
                RenderOutputFiles();
            }, "Export", 96);
            buttons.Controls.Add(new Label { Text = "Export Settings", AutoSize = true, Padding = new Padding(12, 7, 0, 0), Font = new Font(Font, FontStyle.Bold) });

            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(0, 8, 16, 0) };
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 352));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var form = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 11, Margin = Padding.Empty };
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (var i = 0; i < 11; i++) form.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

            form.Controls.Add(new Label { Text = "Out folder:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            var outputRootPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            outputRootPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            outputRootPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
            var outputRootBox = new TextBox { Name = "OutputRootBox", Dock = DockStyle.Fill, Text = project.Settings.OutputRootFolder ?? string.Empty };
            var browseOutputRootButton = new Button { Text = "Browse...", Dock = DockStyle.Fill, Height = 24 };
            browseOutputRootButton.Click += (s, e) => BrowseOutputRootFolder(outputRootBox);
            outputRootPanel.Controls.Add(outputRootBox, 0, 0);
            outputRootPanel.Controls.Add(browseOutputRootButton, 1, 0);
            form.Controls.Add(outputRootPanel, 1, 0);
            form.Controls.Add(new Label { Text = "Format:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
            var formatCombo = new ComboBox { Name = "OutputFormatCombo", Dock = DockStyle.Left, Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
            formatCombo.Items.AddRange(new object[] { "CONFIG", "TOML", "YAML", "XML", "JSON" });
            formatCombo.SelectedItem = NormalizeOutputFormat(project.Settings.OutputFormat);
            var extensionBox = new TextBox { Name = "OutputExtensionBox", Left = 150, Width = 120, Text = NormalizeOutputExtension(project.Settings.OutputExtension, Convert.ToString(formatCombo.SelectedItem)) };
            formatCombo.SelectedIndexChanged += (s, e) => extensionBox.Text = DefaultOutputExtension(Convert.ToString(formatCombo.SelectedItem));
            var formatPanel = new Panel { Dock = DockStyle.Fill };
            formatPanel.Controls.Add(formatCombo);
            formatPanel.Controls.Add(new Label { Left = 150, Top = 6, Width = 68, Text = "Ext:" });
            extensionBox.Left = 220;
            formatPanel.Controls.Add(extensionBox);
            form.Controls.Add(formatPanel, 1, 1);
            form.Controls.Add(new Label { Text = "Global mask:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
            form.Controls.Add(new TextBox { Name = "OutputGlobalMaskBox", Dock = DockStyle.Fill, Text = DefaultIfBlank(project.Settings.OutputGlobalMask, @"apps\.env{.ext}") }, 1, 2);
            form.Controls.Add(new Label { Text = "Global + env:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 3);
            form.Controls.Add(new TextBox { Name = "OutputEnvironmentMaskBox", Dock = DockStyle.Fill, Text = DefaultIfBlank(project.Settings.OutputEnvironmentMask, @"apps\.env.{env}{.ext}") }, 1, 3);
            form.Controls.Add(new Label { Text = "Service:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 4);
            form.Controls.Add(new TextBox { Name = "OutputServiceMaskBox", Dock = DockStyle.Fill, Text = DefaultIfBlank(project.Settings.OutputServiceMask, @"apps\{service}\.env{.ext}") }, 1, 4);
            form.Controls.Add(new Label { Text = "Service + env:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 5);
            form.Controls.Add(new TextBox { Name = "OutputServiceEnvironmentMaskBox", Dock = DockStyle.Fill, Text = DefaultIfBlank(project.Settings.OutputServiceEnvironmentMask, @"apps\{service}\.env.{env}{.ext}") }, 1, 5);
            form.Controls.Add(new Label { Text = "Structured output:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 6);
            form.Controls.Add(new CheckBox { Name = "OutputStructuredSingleFileBox", Dock = DockStyle.Left, AutoSize = true, Text = "Single file for TOML / YAML / XML / JSON", Checked = project.Settings.OutputStructuredSingleFile }, 1, 6);
            form.Controls.Add(new Label { Text = "Single file mask:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 7);
            form.Controls.Add(new TextBox { Name = "OutputStructuredSingleFileMaskBox", Dock = DockStyle.Fill, Text = DefaultIfBlank(project.Settings.OutputStructuredSingleFileMask, @"{project_name}{.ext}") }, 1, 7);
            form.Controls.Add(new Label { Text = "Export mode:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 8);
            var renderMode = new ComboBox { Name = "OutputRenderModeCombo", Dock = DockStyle.Left, Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
            renderMode.Items.AddRange(new object[] { "Data files", "Manifest files", "Data + manifest" });
            renderMode.SelectedItem = GetOutputRenderModeLabel(project.Settings);
            form.Controls.Add(renderMode, 1, 8);
            form.Controls.Add(new Label { Text = "Manifest mask:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 9);
            form.Controls.Add(new TextBox { Name = "OutputServiceManifestMaskBox", Dock = DockStyle.Fill, Text = DefaultIfBlank(project.Settings.OutputServiceManifestMask, @"apps\{service}\.env.example") }, 1, 9);
            form.Controls.Add(new Label { Text = "Manifest values:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 10);
            var manifestValueMode = new ComboBox { Name = "OutputServiceManifestValueModeCombo", Dock = DockStyle.Left, Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
            manifestValueMode.Items.AddRange(new object[] { "Empty", "Demo" });
            manifestValueMode.SelectedItem = NormalizeManifestValueMode(project.Settings.OutputServiceManifestValueMode);
            form.Controls.Add(manifestValueMode, 1, 10);

            var targetHeader = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            targetHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            targetHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            targetHeader.Controls.Add(new Label { Text = "Export matrix:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            var targetButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            var outputTargetGrid = BuildOutputTargetGrid();
            AddCommand(targetButtons, "Select All", () => SetOutputTargetGrid(outputTargetGrid, true, true), "SelectAll", 108);
            AddCommand(targetButtons, "Select None", () => SetOutputTargetGrid(outputTargetGrid, false, true), "SelectNone", 116);
            targetHeader.Controls.Add(targetButtons, 1, 0);

            body.Controls.Add(form, 0, 0);
            body.Controls.Add(targetHeader, 0, 1);
            body.Controls.Add(outputTargetGrid, 0, 2);
            root.Controls.Add(buttons, 0, 0);
            root.Controls.Add(body, 0, 1);
            contentPanel.Controls.Add(root);
        }

        private void BrowseOutputRootFolder(TextBox outputRootBox)
        {
            var selectedPath = ResolveOutputRootFolder(outputRootBox.Text);
            using (var dialog = new FolderBrowserDialog
            {
                Description = "Select output folder",
                ShowNewFolderButton = true,
                SelectedPath = Directory.Exists(selectedPath) ? selectedPath : string.Empty
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var outputRootPath = ChooseOutputRootPath(dialog.SelectedPath);
                if (outputRootPath != null) outputRootBox.Text = outputRootPath;
            }
        }

        private string ChooseOutputRootPath(string selectedPath)
        {
            var absolutePath = Path.GetFullPath(selectedPath);
            var projectFolder = GetProjectFolder();
            var canUseRelative = !string.IsNullOrWhiteSpace(projectFolder) && AreSamePathRoot(projectFolder, absolutePath);
            var relativePath = canUseRelative ? MakeRelativeOutputRootFolder(projectFolder, absolutePath) : string.Empty;

            using (var dialog = new Form
            {
                Text = "Output Folder Path",
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                ClientSize = new Size(680, 190)
            })
            {
                var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12) };
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

                var relativeCheckBox = new CheckBox
                {
                    Text = canUseRelative ? "Save path relative to project file" : "Save path relative to project file (not available)",
                    Checked = canUseRelative,
                    Enabled = canUseRelative,
                    Dock = DockStyle.Fill
                };
                var absolutePreview = new TextBox { ReadOnly = true, Dock = DockStyle.Fill, Text = absolutePath };
                var relativePreview = new TextBox { ReadOnly = true, Dock = DockStyle.Fill, Text = canUseRelative ? relativePath : "Project file and output folder are on different drives." };
                var selectedPreview = new TextBox { ReadOnly = true, Dock = DockStyle.Fill };

                Action refreshPreview = () => selectedPreview.Text = relativeCheckBox.Checked && canUseRelative ? relativePath : absolutePath;
                relativeCheckBox.CheckedChanged += (s, e) => refreshPreview();
                refreshPreview();

                layout.Controls.Add(relativeCheckBox, 0, 0);
                layout.Controls.Add(BuildLabeledPreview("Absolute:", absolutePreview), 0, 1);
                layout.Controls.Add(BuildLabeledPreview("Relative:", relativePreview), 0, 2);
                layout.Controls.Add(BuildLabeledPreview("Will save:", selectedPreview), 0, 3);

                var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
                var okButton = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 90, Height = 26 };
                var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90, Height = 26 };
                buttons.Controls.Add(okButton);
                buttons.Controls.Add(cancelButton);
                layout.Controls.Add(buttons, 0, 4);
                dialog.Controls.Add(layout);
                dialog.AcceptButton = okButton;
                dialog.CancelButton = cancelButton;

                return dialog.ShowDialog(this) == DialogResult.OK ? selectedPreview.Text : null;
            }
        }

        private static Control BuildLabeledPreview(string labelText, TextBox preview)
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.Controls.Add(new Label { Text = labelText, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            panel.Controls.Add(preview, 1, 0);
            return panel;
        }

        private string GetProjectFolder()
        {
            return !string.IsNullOrWhiteSpace(currentFilePath)
                ? Path.GetDirectoryName(Path.GetFullPath(currentFilePath))
                : null;
        }

        private static bool AreSamePathRoot(string leftPath, string rightPath)
        {
            var leftRoot = Path.GetPathRoot(Path.GetFullPath(leftPath));
            var rightRoot = Path.GetPathRoot(Path.GetFullPath(rightPath));
            return string.Equals(leftRoot, rightRoot, StringComparison.OrdinalIgnoreCase);
        }

        private static string MakeRelativeOutputRootFolder(string baseFolder, string targetFolder)
        {
            var baseUri = new Uri(EnsureDirectorySeparator(Path.GetFullPath(baseFolder)));
            var targetUri = new Uri(EnsureDirectorySeparator(Path.GetFullPath(targetFolder)));
            var relative = Uri.UnescapeDataString(baseUri.MakeRelativeUri(targetUri).ToString()).Replace('/', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(relative)) return @".\";
            if (relative == ".") return @".\";
            return relative.StartsWith("..", StringComparison.Ordinal) ? relative : @".\" + relative;
        }

        private static string EnsureDirectorySeparator(string path)
        {
            return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ? path : path + Path.DirectorySeparatorChar;
        }

        private void ApplyExportSettingsFromView(bool markChanged = true)
        {
            var outputRootBox = contentPanel.Controls.Find("OutputRootBox", true).FirstOrDefault() as TextBox;
            var outputFormatCombo = contentPanel.Controls.Find("OutputFormatCombo", true).FirstOrDefault() as ComboBox;
            var outputExtensionBox = contentPanel.Controls.Find("OutputExtensionBox", true).FirstOrDefault() as TextBox;
            var outputGlobalMaskBox = contentPanel.Controls.Find("OutputGlobalMaskBox", true).FirstOrDefault() as TextBox;
            var outputEnvironmentMaskBox = contentPanel.Controls.Find("OutputEnvironmentMaskBox", true).FirstOrDefault() as TextBox;
            var outputServiceMaskBox = contentPanel.Controls.Find("OutputServiceMaskBox", true).FirstOrDefault() as TextBox;
            var outputServiceEnvironmentMaskBox = contentPanel.Controls.Find("OutputServiceEnvironmentMaskBox", true).FirstOrDefault() as TextBox;
            var outputStructuredSingleFileBox = contentPanel.Controls.Find("OutputStructuredSingleFileBox", true).FirstOrDefault() as CheckBox;
            var outputStructuredSingleFileMaskBox = contentPanel.Controls.Find("OutputStructuredSingleFileMaskBox", true).FirstOrDefault() as TextBox;
            var outputRenderModeCombo = contentPanel.Controls.Find("OutputRenderModeCombo", true).FirstOrDefault() as ComboBox;
            var outputServiceManifestMaskBox = contentPanel.Controls.Find("OutputServiceManifestMaskBox", true).FirstOrDefault() as TextBox;
            var outputServiceManifestValueModeCombo = contentPanel.Controls.Find("OutputServiceManifestValueModeCombo", true).FirstOrDefault() as ComboBox;
            if (outputFormatCombo == null) return;

            project.Settings.OutputRootFolder = outputRootBox?.Text;
            project.Settings.OutputFormat = Convert.ToString(outputFormatCombo.SelectedItem) ?? "CONFIG";
            project.Settings.OutputExtension = outputExtensionBox?.Text;
            project.Settings.OutputGlobalMask = outputGlobalMaskBox?.Text;
            project.Settings.OutputEnvironmentMask = outputEnvironmentMaskBox?.Text;
            project.Settings.OutputServiceMask = outputServiceMaskBox?.Text;
            project.Settings.OutputServiceEnvironmentMask = outputServiceEnvironmentMaskBox?.Text;
            project.Settings.OutputStructuredSingleFile = outputStructuredSingleFileBox?.Checked == true;
            project.Settings.OutputStructuredSingleFileMask = outputStructuredSingleFileMaskBox?.Text;
            SetOutputRenderMode(project.Settings, Convert.ToString(outputRenderModeCombo?.SelectedItem));
            project.Settings.OutputServiceManifestMask = outputServiceManifestMaskBox?.Text;
            project.Settings.OutputServiceManifestValueMode = NormalizeManifestValueMode(Convert.ToString(outputServiceManifestValueModeCombo?.SelectedItem));
            SaveOutputTargetsFromView(false);
            if (markChanged)
            {
                Changed();
            }
        }

        private static string GetOutputRenderModeLabel(ProjectSettings settings)
        {
            var data = settings?.OutputDataFiles != false;
            var manifest = settings?.OutputServiceManifest == true;
            if (data && manifest) return "Data + manifest";
            if (manifest) return "Manifest files";
            return "Data files";
        }

        private static void SetOutputRenderMode(ProjectSettings settings, string label)
        {
            if (settings == null) return;
            if (string.Equals(label, "Manifest files", StringComparison.OrdinalIgnoreCase))
            {
                settings.OutputDataFiles = false;
                settings.OutputServiceManifest = true;
            }
            else if (string.Equals(label, "Data + manifest", StringComparison.OrdinalIgnoreCase))
            {
                settings.OutputDataFiles = true;
                settings.OutputServiceManifest = true;
            }
            else
            {
                settings.OutputDataFiles = true;
                settings.OutputServiceManifest = false;
            }
        }

        private static string NormalizeManifestValueMode(string value)
        {
            if (string.Equals(value, "Demo", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "Example", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "Placeholder", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "Pattern", StringComparison.OrdinalIgnoreCase))
            {
                return "Demo";
            }
            return "Empty";
        }

        private void SetProjectEncryptionMode(string label)
        {
            project.Settings = project.Settings ?? new ProjectSettings();
            if (label == EncryptionWholeJson)
            {
                project.Settings.EncryptionMode = "WholeJson";
                project.Settings.EncryptAllValues = false;
            }
            else if (label == EncryptionAllValues)
            {
                project.Settings.EncryptionMode = "AllValues";
                project.Settings.EncryptAllValues = true;
            }
            else if (label == EncryptionSecrets)
            {
                project.Settings.EncryptionMode = "SecretsOnly";
                project.Settings.EncryptAllValues = false;
            }
            else
            {
                project.Settings.EncryptionMode = "Open";
                project.Settings.EncryptAllValues = false;
            }
        }

        private static string GetProjectEncryptionMode(ProjectModel targetProject)
        {
            var settings = targetProject?.Settings;
            if (settings == null) return "Open";
            if (!string.IsNullOrWhiteSpace(settings.EncryptionMode)) return settings.EncryptionMode;
            return settings.EncryptAllValues ? "AllValues" : "Open";
        }

        private static string GetProjectEncryptionModeLabel(ProjectModel targetProject)
        {
            var mode = GetProjectEncryptionMode(targetProject);
            if (string.Equals(mode, "WholeJson", StringComparison.OrdinalIgnoreCase)) return EncryptionWholeJson;
            if (string.Equals(mode, "AllValues", StringComparison.OrdinalIgnoreCase)) return EncryptionAllValues;
            if (string.Equals(mode, "SecretsOnly", StringComparison.OrdinalIgnoreCase)) return EncryptionSecrets;
            return EncryptionOpen;
        }

        private void RenderOutputFiles()
        {
            if (project == null) return;
            project.Settings = project.Settings ?? new ProjectSettings();
            if (string.IsNullOrWhiteSpace(project.Settings.OutputRootFolder))
            {
                MessageBox.Show(this, "Set Export -> Out folder before exporting files.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                currentView = "Export";
                RenderCurrentView();
                return;
            }

            var outputRootFolder = ResolveOutputRootFolder(project.Settings.OutputRootFolder);
            if (!EnsureOutputRootFolderExists(outputRootFolder))
            {
                return;
            }

            var targets = GetConfiguredOutputTargets();
            if (targets == null || targets.Count == 0) return;

            var format = NormalizeOutputFormat(project.Settings.OutputFormat);
            var rendered = 0;
            if (project.Settings.OutputDataFiles != false && project.Settings.OutputStructuredSingleFile && format != "CONFIG")
            {
                var path = BuildStructuredOutputPath();
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(path, FormatStructuredOutput(targets, format));
                rendered++;
            }
            else if (project.Settings.OutputDataFiles != false)
            {
                foreach (var target in targets)
                {
                    var values = BuildOutputValues(target.Service, target.Environment);
                    var path = BuildOutputPath(target);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, FormatOutputValues(values, format));
                    rendered++;
                }
            }

            if (project.Settings.OutputServiceManifest)
            {
                rendered += RenderServiceManifestFiles(targets);
            }

            MessageBox.Show(this, $"Exported {rendered} file(s).", "Export");
        }

        private int RenderServiceManifestFiles(List<OutputTarget> targets)
        {
            var rendered = 0;
            foreach (var service in targets
                .Select(t => t.Service)
                .Where(s => s != null)
                .GroupBy(s => s.Id)
                .Select(g => g.First())
                .OrderBy(s => s.SortOrder)
                .ThenBy(s => s.Name))
            {
                var content = BuildServiceManifestContent(service);
                var path = BuildServiceManifestPath(service);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, content);
                rendered++;
            }
            return rendered;
        }

        private bool EnsureOutputRootFolderExists(string outputRootFolder)
        {
            if (Directory.Exists(outputRootFolder)) return true;

            var result = MessageBox.Show(
                this,
                "Output folder does not exist. Create it now?" + Environment.NewLine + outputRootFolder,
                "Export",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (result != DialogResult.Yes) return false;

            try
            {
                Directory.CreateDirectory(outputRootFolder);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not create output folder." + Environment.NewLine + ex.Message, "Export", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private DataGridView BuildOutputTargetGrid()
        {
            var grid = new DataGridView
            {
                Name = "OutputTargetGrid",
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Service", HeaderText = "Service", ReadOnly = true });
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "env_global", HeaderText = "Global environment" });
            foreach (var env in project.Environments.OrderBy(e => e.SortOrder))
            {
                grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "env_" + env.Id, HeaderText = env.Name, Tag = env });
            }

            foreach (var service in project.Services.OrderBy(s => s.SortOrder))
            {
                AddOutputTargetRow(grid, service);
            }

            grid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            grid.CellValueChanged += (s, e) =>
            {
                if (suppressOutputTargetGridEvents) return;
                if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
                if (grid.Columns[e.ColumnIndex].Name == "Service") return;
                SaveOutputTargetsFromGrid(grid, true);
            };
            return grid;
        }

        private void AddOutputTargetRow(DataGridView grid, ServiceModel service)
        {
            var rowIndex = grid.Rows.Add();
            var row = grid.Rows[rowIndex];
            row.Tag = service;
            row.Cells["Service"].Value = service.Name;
            foreach (DataGridViewColumn column in grid.Columns)
            {
                if (column.Name != "Service") row.Cells[column.Name].Value = IsOutputTargetEnabled(service.Id, (column.Tag as EnvironmentModel)?.Id);
            }
        }

        private void SetOutputTargetGrid(DataGridView grid, bool value, bool persist)
        {
            suppressOutputTargetGridEvents = true;
            grid.SuspendLayout();
            try
            {
                foreach (DataGridViewRow row in grid.Rows)
                {
                    foreach (DataGridViewColumn column in grid.Columns)
                    {
                        if (column.Name != "Service") row.Cells[column.Name].Value = value;
                    }
                }
            }
            finally
            {
                suppressOutputTargetGridEvents = false;
                grid.ResumeLayout();
                grid.Invalidate();
            }

            if (persist) SaveOutputTargetsFromGrid(grid, true);
        }

        private bool IsOutputTargetEnabled(string serviceId, string environmentId)
        {
            var targets = project.Settings?.OutputTargets;
            if (targets == null || targets.Count == 0) return true;
            var target = targets.FirstOrDefault(x => SameNullable(x.ServiceId, serviceId) && SameNullable(x.EnvironmentId, environmentId));
            return target?.Enabled == true;
        }

        private static bool SameNullable(string left, string right)
        {
            return string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        private void SaveOutputTargetsFromView(bool markChanged)
        {
            var grid = contentPanel.Controls.Find("OutputTargetGrid", true).FirstOrDefault() as DataGridView;
            if (grid == null) return;
            SaveOutputTargetsFromGrid(grid, markChanged);
        }

        private void SaveOutputTargetsFromGrid(DataGridView grid, bool markChanged)
        {
            project.Settings = project.Settings ?? new ProjectSettings();
            project.Settings.OutputTargets = new List<OutputTargetSetting>();
            foreach (DataGridViewRow row in grid.Rows)
            {
                var service = row.Tag as ServiceModel;
                foreach (DataGridViewColumn column in grid.Columns)
                {
                    if (column.Name == "Service") continue;
                    project.Settings.OutputTargets.Add(new OutputTargetSetting
                    {
                        ServiceId = service?.Id,
                        EnvironmentId = (column.Tag as EnvironmentModel)?.Id,
                        Enabled = Convert.ToBoolean(row.Cells[column.Name].Value ?? false)
                    });
                }
            }

            if (markChanged) Changed();
        }

        private List<OutputTarget> GetConfiguredOutputTargets()
        {
            SaveOutputTargetsFromView(false);
            var result = new List<OutputTarget>();
            var services = project.Services.OrderBy(s => s.SortOrder).ToList();
            var environments = new EnvironmentModel[] { null }.Concat(project.Environments.OrderBy(e => e.SortOrder)).ToList();
            foreach (var service in services)
            {
                foreach (var environment in environments)
                {
                    if (IsOutputTargetEnabled(service.Id, environment?.Id))
                    {
                        result.Add(new OutputTarget(service, environment));
                    }
                }
            }

            if (result.Count == 0)
            {
                MessageBox.Show(this, "No export targets selected.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            return result;
        }

        private Dictionary<string, string> BuildOutputValues(ServiceModel service, EnvironmentModel environment)
        {
            var effective = effectiveConfigService.Build(project, service?.Id, environment?.Id).Where(x => !x.Missing);
            if (service != null)
            {
                effective = effective.Where(x => IsVariableUsedByService(x.Variable.Id, service.Id));
            }

            return effective
                .OrderBy(x => x.Variable.SortOrder)
                .ThenBy(x => x.Variable.Key)
                .ToDictionary(x => x.Variable.Key, x => x.Value ?? string.Empty);
        }

        private string BuildServiceManifestContent(ServiceModel service)
        {
            var mode = NormalizeManifestValueMode(project.Settings.OutputServiceManifestValueMode);
            return string.Join(Environment.NewLine, ProjectService.GetVariablesUsedByService(project, service.Id)
                .Select(v => ManifestLine(v, mode)));
        }

        private static string ManifestLine(VariableDefinitionModel variable, string mode)
        {
            if (!string.Equals(mode, "Demo", StringComparison.OrdinalIgnoreCase))
            {
                return variable.Key + "=";
            }

            var line = variable.Key + "=" + (variable.DemoValue ?? string.Empty);
            var comment = SingleLineComment(variable.DemoComment);
            return string.IsNullOrWhiteSpace(comment) ? line : line + " # " + comment;
        }

        private static string SingleLineComment(string value)
        {
            return (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        }

        private string BuildOutputPath(OutputTarget target)
        {
            var mask = target.Service == null && target.Environment == null
                ? DefaultIfBlank(project.Settings.OutputGlobalMask, @"apps\.env{.ext}")
                : target.Service == null
                    ? DefaultIfBlank(project.Settings.OutputEnvironmentMask, @"apps\.env.{env}{.ext}")
                    : target.Environment == null
                        ? DefaultIfBlank(project.Settings.OutputServiceMask, @"apps\{service}\.env{.ext}")
                        : DefaultIfBlank(project.Settings.OutputServiceEnvironmentMask, @"apps\{service}\.env.{env}{.ext}");
            var ext = NormalizeOutputExtension(project.Settings.OutputExtension, project.Settings.OutputFormat);
            var relative = ApplyOutputMaskPlaceholders(mask, ext, ExportServiceName(target.Service, "CONFIG", true), ExportEnvironmentName(target.Environment, "CONFIG"));
            relative = relative.TrimStart('\\', '/');
            return Path.Combine(ResolveOutputRootFolder(project.Settings.OutputRootFolder), relative);
        }

        private string BuildStructuredOutputPath()
        {
            var ext = NormalizeOutputExtension(project.Settings.OutputExtension, project.Settings.OutputFormat);
            var relative = ApplyOutputMaskPlaceholders(DefaultIfBlank(project.Settings.OutputStructuredSingleFileMask, @"{project_name}{.ext}"), ext, string.Empty, string.Empty)
                .TrimStart('\\', '/');
            return Path.Combine(ResolveOutputRootFolder(project.Settings.OutputRootFolder), relative);
        }

        private string BuildServiceManifestPath(ServiceModel service)
        {
            var relative = ApplyOutputMaskPlaceholders(DefaultIfBlank(project.Settings.OutputServiceManifestMask, @"apps\{service}\.env.example"), string.Empty, ExportServiceName(service, "CONFIG", true), string.Empty)
                .TrimStart('\\', '/');
            return Path.Combine(ResolveOutputRootFolder(project.Settings.OutputRootFolder), relative);
        }

        private string ResolveOutputRootFolder(string outputRootFolder)
        {
            if (string.IsNullOrWhiteSpace(outputRootFolder) || Path.IsPathRooted(outputRootFolder))
            {
                return outputRootFolder;
            }

            var baseFolder = !string.IsNullOrWhiteSpace(currentFilePath)
                ? Path.GetDirectoryName(Path.GetFullPath(currentFilePath))
                : Environment.CurrentDirectory;
            return Path.GetFullPath(Path.Combine(baseFolder ?? Environment.CurrentDirectory, outputRootFolder));
        }

        private string ApplyOutputMaskPlaceholders(string mask, string extension, string serviceName, string environmentName)
        {
            var projectName = SafeOutputName(DefaultIfBlank(project.ProjectName, project.ProjectId));
            var result = (mask ?? string.Empty)
                .Replace("{project_name}", projectName)
                .Replace("{project}", projectName)
                .Replace("{service}", serviceName ?? string.Empty)
                .Replace("{env}", environmentName ?? string.Empty)
                .Replace("{.ext}", extension)
                .Replace("{ext}", extension.TrimStart('.'));
            return NormalizeRelativeOutputMask(result);
        }

        private static string NormalizeRelativeOutputMask(string value)
        {
            value = value ?? string.Empty;
            while (value.Contains(@"\\"))
            {
                value = value.Replace(@"\\", @"\");
            }
            while (value.Contains("//"))
            {
                value = value.Replace("//", "/");
            }
            return value;
        }

        private static string SafeOutputName(string value)
        {
            value = string.IsNullOrWhiteSpace(value) ? "project" : value.Trim();
            foreach (var invalid in Path.GetInvalidFileNameChars().Concat(Path.GetInvalidPathChars()).Distinct())
            {
                value = value.Replace(invalid, '-');
            }
            return value;
        }

        private string FormatStructuredOutput(List<OutputTarget> targets, string format)
        {
            if (format == "JSON")
            {
                return new JavaScriptSerializer().Serialize(BuildStructuredOutputObject(targets));
            }
            if (format == "XML")
            {
                return FormatStructuredXml(targets);
            }
            if (format == "YAML")
            {
                return FormatStructuredYaml(targets);
            }
            if (format == "TOML")
            {
                return FormatStructuredToml(targets);
            }

            return string.Join(Environment.NewLine, targets.SelectMany(target => BuildOutputValues(target.Service, target.Environment)).Select(pair => $"{pair.Key}={pair.Value}"));
        }

        private Dictionary<string, object> BuildStructuredOutputObject(List<OutputTarget> targets)
        {
            var result = new Dictionary<string, object>();
            var environments = new Dictionary<string, object>();
            var services = new Dictionary<string, object>();

            foreach (var target in targets)
            {
                var values = BuildOutputValues(target.Service, target.Environment);
                if (target.Service == null && target.Environment == null)
                {
                    result["global"] = values;
                }
                else if (target.Service == null)
                {
                    environments[ExportEnvironmentName(target.Environment, "JSON")] = values;
                }
                else
                {
                    var serviceName = ExportServiceName(target.Service, "JSON", false);
                    if (!services.TryGetValue(serviceName, out var serviceObject))
                    {
                        serviceObject = new Dictionary<string, object>();
                        services[serviceName] = serviceObject;
                    }

                    var serviceMap = (Dictionary<string, object>)serviceObject;
                    if (target.Environment == null)
                    {
                        serviceMap["global"] = values;
                    }
                    else
                    {
                        if (!serviceMap.TryGetValue("environments", out var serviceEnvironmentsObject))
                        {
                            serviceEnvironmentsObject = new Dictionary<string, object>();
                            serviceMap["environments"] = serviceEnvironmentsObject;
                        }
                        ((Dictionary<string, object>)serviceEnvironmentsObject)[ExportEnvironmentName(target.Environment, "JSON")] = values;
                    }
                }
            }

            if (environments.Count > 0) result["environments"] = environments;
            if (services.Count > 0) result["services"] = services;
            return result;
        }

        private string FormatStructuredToml(List<OutputTarget> targets)
        {
            var lines = new List<string>();
            foreach (var target in targets)
            {
                var table = target.Service == null && target.Environment == null
                    ? "global"
                    : target.Service == null
                        ? "environments." + TomlPathSegment(ExportEnvironmentName(target.Environment, "TOML"))
                        : target.Environment == null
                            ? "services." + TomlPathSegment(ExportServiceName(target.Service, "TOML", false)) + ".global"
                            : "services." + TomlPathSegment(ExportServiceName(target.Service, "TOML", false)) + ".environments." + TomlPathSegment(ExportEnvironmentName(target.Environment, "TOML"));
                if (lines.Count > 0) lines.Add(string.Empty);
                lines.Add("[" + table + "]");
                lines.AddRange(BuildOutputValues(target.Service, target.Environment).Select(pair => TomlKey(pair.Key) + " = " + QuoteJson(pair.Value)));
            }
            return string.Join(Environment.NewLine, lines);
        }

        private string FormatStructuredYaml(List<OutputTarget> targets)
        {
            var lines = new List<string>();
            var global = targets.FirstOrDefault(t => t.Service == null && t.Environment == null);
            if (global != null)
            {
                lines.Add("global:");
                AppendYamlValues(lines, BuildOutputValues(null, null), 2);
            }

            var environmentTargets = targets.Where(t => t.Service == null && t.Environment != null).ToList();
            if (environmentTargets.Count > 0)
            {
                lines.Add("environments:");
                foreach (var target in environmentTargets)
                {
                    lines.Add("  " + YamlKey(ExportEnvironmentName(target.Environment, "YAML")) + ":");
                    AppendYamlValues(lines, BuildOutputValues(null, target.Environment), 4);
                }
            }

            var serviceTargets = targets.Where(t => t.Service != null).GroupBy(t => t.Service.Id).ToList();
            if (serviceTargets.Count > 0)
            {
                lines.Add("services:");
                foreach (var serviceGroup in serviceTargets)
                {
                    var service = serviceGroup.First().Service;
                    lines.Add("  " + YamlKey(ExportServiceName(service, "YAML", false)) + ":");
                    var serviceGlobal = serviceGroup.FirstOrDefault(t => t.Environment == null);
                    if (serviceGlobal != null)
                    {
                        lines.Add("    global:");
                        AppendYamlValues(lines, BuildOutputValues(service, null), 6);
                    }

                    var serviceEnvironmentTargets = serviceGroup.Where(t => t.Environment != null).ToList();
                    if (serviceEnvironmentTargets.Count > 0)
                    {
                        lines.Add("    environments:");
                        foreach (var target in serviceEnvironmentTargets)
                        {
                            lines.Add("      " + YamlKey(ExportEnvironmentName(target.Environment, "YAML")) + ":");
                            AppendYamlValues(lines, BuildOutputValues(service, target.Environment), 8);
                        }
                    }
                }
            }

            return string.Join(Environment.NewLine, lines);
        }

        private string FormatStructuredXml(List<OutputTarget> targets)
        {
            var lines = new List<string> { "<?xml version=\"1.0\" encoding=\"utf-8\"?>", "<config>" };
            foreach (var target in targets)
            {
                if (target.Service == null && target.Environment == null)
                {
                    lines.Add("  <global>");
                    AppendXmlValues(lines, BuildOutputValues(null, null), 4);
                    lines.Add("  </global>");
                }
                else if (target.Service == null)
                {
                    lines.Add($"  <environment name=\"{EscapeXml(ExportEnvironmentName(target.Environment, "XML"))}\">");
                    AppendXmlValues(lines, BuildOutputValues(null, target.Environment), 4);
                    lines.Add("  </environment>");
                }
                else if (target.Environment == null)
                {
                    lines.Add($"  <service name=\"{EscapeXml(ExportServiceName(target.Service, "XML", false))}\">");
                    lines.Add("    <global>");
                    AppendXmlValues(lines, BuildOutputValues(target.Service, null), 6);
                    lines.Add("    </global>");
                    lines.Add("  </service>");
                }
                else
                {
                    lines.Add($"  <service name=\"{EscapeXml(ExportServiceName(target.Service, "XML", false))}\" environment=\"{EscapeXml(ExportEnvironmentName(target.Environment, "XML"))}\">");
                    AppendXmlValues(lines, BuildOutputValues(target.Service, target.Environment), 4);
                    lines.Add("  </service>");
                }
            }
            lines.Add("</config>");
            return string.Join(Environment.NewLine, lines);
        }

        private static void AppendYamlValues(List<string> lines, Dictionary<string, string> values, int indent)
        {
            var prefix = new string(' ', indent);
            foreach (var pair in values)
            {
                lines.Add(prefix + YamlKey(pair.Key) + ": " + QuoteYaml(pair.Value));
            }
        }

        private static void AppendXmlValues(List<string> lines, Dictionary<string, string> values, int indent)
        {
            var prefix = new string(' ', indent);
            foreach (var pair in values)
            {
                lines.Add(prefix + $"<add key=\"{EscapeXml(pair.Key)}\" value=\"{EscapeXml(pair.Value)}\" />");
            }
        }

        private static string TomlKey(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static string TomlPathSegment(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static string YamlKey(string value)
        {
            if ((value ?? string.Empty).All(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-')) return value;
            return QuoteYaml(value);
        }

        private static string ExportServiceName(ServiceModel service, string format, bool pathName)
        {
            if (service == null) return string.Empty;
            format = NormalizeOutputFormat(format);
            if (format == "CONFIG")
            {
                return DefaultIfBlank(service.ConfigName, pathName ? OutputFolderPathSegment(service) : service.Name);
            }
            if (format == "TOML") return DefaultIfBlank(service.TomlName, service.Name);
            if (format == "YAML") return DefaultIfBlank(service.YamlName, service.Name);
            if (format == "XML") return DefaultIfBlank(service.XmlName, service.Name);
            if (format == "JSON") return DefaultIfBlank(service.JsonName, service.Name);
            return service.Name;
        }

        private static string OutputFolderPathSegment(ServiceModel service)
        {
            if (service == null) return string.Empty;
            return service.OutputFolder == null ? service.Name : service.OutputFolder.Trim();
        }

        private static string NormalizeOutputFolderKey(string value)
        {
            return (value ?? string.Empty).Trim().Trim('\\', '/');
        }

        private static string ExportEnvironmentName(EnvironmentModel environment, string format)
        {
            if (environment == null) return string.Empty;
            format = NormalizeOutputFormat(format);
            if (format == "CONFIG") return DefaultIfBlank(environment.ConfigName, environment.Name);
            if (format == "TOML") return DefaultIfBlank(environment.TomlName, environment.Name);
            if (format == "YAML") return DefaultIfBlank(environment.YamlName, environment.Name);
            if (format == "XML") return DefaultIfBlank(environment.XmlName, environment.Name);
            if (format == "JSON") return DefaultIfBlank(environment.JsonName, environment.Name);
            return environment.Name;
        }

        private static string FormatOutputValues(Dictionary<string, string> values, string format)
        {
            if (format == "JSON")
            {
                return new JavaScriptSerializer().Serialize(values);
            }
            if (format == "XML")
            {
                var lines = new List<string> { "<?xml version=\"1.0\" encoding=\"utf-8\"?>", "<config>" };
                lines.AddRange(values.Select(pair => $"  <add key=\"{EscapeXml(pair.Key)}\" value=\"{EscapeXml(pair.Value)}\" />"));
                lines.Add("</config>");
                return string.Join(Environment.NewLine, lines);
            }
            if (format == "YAML")
            {
                return string.Join(Environment.NewLine, values.Select(pair => $"{pair.Key}: {QuoteYaml(pair.Value)}"));
            }
            if (format == "TOML")
            {
                return string.Join(Environment.NewLine, values.Select(pair => $"{pair.Key} = {QuoteJson(pair.Value)}"));
            }

            return string.Join(Environment.NewLine, values.Select(pair => $"{pair.Key}={pair.Value}"));
        }

        private static string NormalizeOutputFormat(string format)
        {
            format = (format ?? "CONFIG").Trim().ToUpperInvariant();
            return new[] { "CONFIG", "TOML", "YAML", "XML", "JSON" }.Contains(format) ? format : "CONFIG";
        }

        private static string DefaultOutputExtension(string format)
        {
            format = NormalizeOutputFormat(format);
            if (format == "TOML") return ".toml";
            if (format == "YAML") return ".yaml";
            if (format == "XML") return ".xml";
            if (format == "JSON") return ".json";
            return ".env";
        }

        private static string NormalizeOutputExtension(string extension, string format)
        {
            if (string.IsNullOrWhiteSpace(extension)) return DefaultOutputExtension(format);
            extension = extension.Trim();
            return extension.StartsWith(".") ? extension : "." + extension;
        }

        private static string DefaultIfBlank(string value, string defaultValue)
        {
            return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
        }

        private static string EscapeXml(string value)
        {
            return System.Security.SecurityElement.Escape(value ?? string.Empty);
        }

        private static string QuoteYaml(string value)
        {
            return QuoteJson(value);
        }

        private static string QuoteJson(string value)
        {
            return new JavaScriptSerializer().Serialize(value ?? string.Empty);
        }
    }
}

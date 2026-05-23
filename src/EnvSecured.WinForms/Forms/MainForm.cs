using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using EnvSecured.Core.Models;
using EnvSecured.Core.Services;
using EnvSecured.Core.Validation;
using EnvSecured.Crypto;
using EnvSecured.WinForms;

namespace EnvSecured.WinForms.Forms
{
    public sealed class MainForm : Form
    {
        private readonly ProjectService projectService = new ProjectService();
        private readonly VaultFileService vaultFileService = new VaultFileService();
        private readonly EffectiveConfigService effectiveConfigService = new EffectiveConfigService();
        private readonly ValidationService validationService = new ValidationService();
        private readonly RecentProjectsService recentProjectsService = new RecentProjectsService();
        private readonly CryptoService cryptoService = new CryptoService();
        private readonly DpapiCacheService dpapiCacheService = new DpapiCacheService();

        private ProjectModel project;
        private string currentFilePath;
        private byte[] vaultKey;
        private bool modified;
        private bool recoveryBackupWarningShown;
        private bool suppressColumnWidthPersistence;
        private bool suppressImportComboAddNew;
        private bool suppressSplitterPersistence;
        private bool suppressVariableMatrixEvents;
        private string currentView = "Variables";
        private FlowLayoutPanel commandPanel;
        private Button saveButton;
        private Button validateButton;

        private TreeView navigation;
        private Panel contentPanel;
        private ComboBox environmentCombo;
        private ComboBox serviceCombo;
        private TextBox searchBox;
        private CheckBox showSecretsCheckBox;
        private CheckBox matrixLightColorsCheckBox;
        private CheckBox calculatedValuesCheckBox;
        private readonly Dictionary<string, string> variableColumnFilters = new Dictionary<string, string>();
        private DataGridView mainGrid;
        private DataGridView contractsMatrix;
        private DataGridView importFilesGrid;
        private DataGridView importPreviewGrid;
        private Button removeImportFilesButton;
        private Button setImportEnvironmentButton;
        private Button setImportServiceButton;
        private readonly List<ImportFileRow> importFiles = new List<ImportFileRow>();
        private readonly List<ImportPreviewRow> importPreviewRows = new List<ImportPreviewRow>();
        private const string AddNewOption = "<Add new...>";
        private const string GlobalOption = "Global";
        private const string AllServicesOption = "All services";
        private const string EncryptionOpen = "Open values";
        private const string EncryptionSecrets = "Secrets only";
        private const string EncryptionAllValues = "All values";
        private const string EncryptionWholeJson = "Whole JSON file";
        private const string PreferenceCalculatedValues = "Variables.CalculatedValues";
        private const string PreferenceLightMatrixColors = "Variables.LightMatrixColors";
        private static readonly Color[] MatrixPalette =
        {
            Color.FromArgb(220, 92, 52),
            Color.FromArgb(64, 126, 201),
            Color.FromArgb(58, 145, 89),
            Color.FromArgb(151, 94, 180),
            Color.FromArgb(196, 132, 38),
            Color.FromArgb(36, 150, 156),
            Color.FromArgb(190, 70, 120),
            Color.FromArgb(103, 122, 42),
            Color.FromArgb(86, 101, 214),
            Color.FromArgb(156, 83, 56)
        };
        private StatusStrip status;

        public MainForm()
        {
            Text = "EnvSecured Studio";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            MinimumSize = new Size(1050, 680);
            ApplyDefaultWindowSize();
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            BuildUi();
            RestoreWindowBounds();
            OpenLastProjectOrStart();
        }

        private void OpenLastProjectOrStart()
        {
            var lastProjectPath = recentProjectsService.LoadLastProject();
            if (!string.IsNullOrWhiteSpace(lastProjectPath))
            {
                try
                {
                    OpenProjectFile(lastProjectPath);
                    return;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Cannot open last project.\r\n\r\n{ex.Message}", "Open Project", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            RenderNoProjectView();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (project != null && modified)
            {
                var result = MessageBox.Show(
                    this,
                    "There are unsaved changes. Save the project before closing?",
                    "Unsaved Changes",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Warning);

                if (result == DialogResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }

                if (result == DialogResult.Yes)
                {
                    SaveProject();
                    if (modified)
                    {
                        e.Cancel = true;
                        return;
                    }
                }
            }

            SaveWindowBounds();
            base.OnFormClosing(e);
        }

        private void RestoreWindowBounds()
        {
            var bounds = recentProjectsService.LoadWindowBounds();
            if (bounds == null || bounds.Width < MinimumSize.Width || bounds.Height < MinimumSize.Height)
            {
                return;
            }

            var rectangle = new Rectangle(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
            if (!Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(rectangle)))
            {
                return;
            }

            StartPosition = FormStartPosition.Manual;
            Bounds = rectangle;
            if (bounds.Maximized)
            {
                WindowState = FormWindowState.Maximized;
            }
        }

        private void SaveWindowBounds()
        {
            var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            if (bounds.Width < MinimumSize.Width || bounds.Height < MinimumSize.Height)
            {
                return;
            }

            recentProjectsService.SaveWindowBounds(new WindowBounds
            {
                Left = bounds.Left,
                Top = bounds.Top,
                Width = bounds.Width,
                Height = bounds.Height,
                Maximized = WindowState == FormWindowState.Maximized
            });
        }

        private void ApplyDefaultWindowSize()
        {
            var workingArea = Screen.PrimaryScreen.WorkingArea;
            Width = Math.Min(1760, Math.Max(MinimumSize.Width, workingArea.Width - 40));
            Height = Math.Min(1180, Math.Max(MinimumSize.Height, workingArea.Height - 40));
        }

        private void ResetLayoutSettings()
        {
            if (MessageBox.Show(this, "Reset window size, splitters and table column widths to defaults?", "Reset Layout", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            recentProjectsService.ResetLayoutSettings();
            WindowState = FormWindowState.Normal;
            ApplyDefaultWindowSize();
            CenterToScreen();
            RenderCurrentView();
        }

        private void BuildUi()
        {
            var menu = new MenuStrip { Dock = DockStyle.Top };
            var file = new ToolStripMenuItem("File");
            file.DropDownItems.Add("New Project", null, (s, e) => NewProject());
            file.DropDownItems.Add("Open Project", null, (s, e) => OpenProject());
            file.DropDownItems.Add("Save", null, (s, e) => SaveProject());
            file.DropDownItems.Add("Save As", null, (s, e) => SaveProjectAs());
            file.DropDownItems.Add("Exit", null, (s, e) => Close());
            menu.Items.Add(file);
            menu.Items.Add(new ToolStripMenuItem("Help", null, new ToolStripMenuItem("About", null, (s, e) => MessageBox.Show(this, "EnvSecured Studio", "About"))));

            commandPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(4), WrapContents = false };
            AddCommand(commandPanel, "New Project", NewProject);
            AddCommand(commandPanel, "Open", OpenProject);
            saveButton = AddCommand(commandPanel, "Save", SaveProject);
            validateButton = AddCommand(commandPanel, "Validate", ValidateAll);
            AddCommand(commandPanel, "Render Files", RenderOutputFiles);
            AddCommand(commandPanel, "Reset Layout", ResetLayoutSettings);

            navigation = new TreeView { Dock = DockStyle.Fill, HideSelection = false };
            navigation.AfterSelect += (s, e) =>
            {
                if (e.Node.Level == 1)
                {
                    currentView = e.Node.Text;
                    RenderCurrentView();
                }
            };

            contentPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.Controls.Add(navigation, 0, 0);
            root.Controls.Add(contentPanel, 1, 0);

            status = new StatusStrip();
            Controls.Add(root);
            Controls.Add(commandPanel);
            Controls.Add(menu);
            Controls.Add(status);
            MainMenuStrip = menu;
        }

        private static Button AddCommand(FlowLayoutPanel panel, string text, Action action)
        {
            var button = new Button { Text = text, Width = 96, Height = 28, Margin = new Padding(2) };
            button.Click += (s, e) => action();
            panel.Controls.Add(button);
            return button;
        }

        private void RenderNoProjectView()
        {
            project = null;
            currentFilePath = null;
            vaultKey = null;
            modified = false;
            recoveryBackupWarningShown = false;
            Text = "EnvSecured Studio";
            navigation.Nodes.Clear();
            contentPanel.Controls.Clear();

            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 310));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

            var center = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(24)
            };
            center.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            center.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            center.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            center.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            center.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            center.Controls.Add(new Label { Text = "No project is open", Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 12, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            center.Controls.Add(new Label { Text = "Create a new EnvSecured Studio project or open an existing vault file.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 5, 0, 0) };
            var newButton = AddCommand(buttons, "New Project", NewProject);
            var openButton = AddCommand(buttons, "Open", OpenProject);
            newButton.Width = 120;
            openButton.Width = 120;
            newButton.Height = 30;
            openButton.Height = 30;
            center.Controls.Add(buttons, 0, 2);

            center.Controls.Add(new Label { Text = "Recent projects", Dock = DockStyle.Fill, Font = new Font(Font, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }, 0, 3);
            var recentList = new ListBox { Dock = DockStyle.Fill };
            foreach (var path in recentProjectsService.Load())
            {
                recentList.Items.Add(path);
            }
            recentList.DoubleClick += (s, e) =>
            {
                if (recentList.SelectedItem != null)
                {
                    OpenProjectFile(Convert.ToString(recentList.SelectedItem));
                }
            };
            center.Controls.Add(recentList, 0, 4);

            panel.Controls.Add(new Panel(), 0, 0);
            panel.Controls.Add(center, 0, 1);
            panel.Controls.Add(new Panel(), 0, 2);
            contentPanel.Controls.Add(panel);
            RefreshStatus();
        }

        private void NewProject()
        {
            var name = PromptDialog.Show(this, "New Project", "Project name:", "untitled-project");
            if (string.IsNullOrWhiteSpace(name)) name = "untitled-project";
            project = projectService.CreateProject(name, Slug(name));
            currentFilePath = null;
            vaultKey = null;
            modified = true;
            currentView = "Variables";
            RefreshNavigation();
            RenderCurrentView();
            RefreshStatus();
        }

        private void OpenProject()
        {
            using (var dialog = new OpenFileDialog { Filter = "EnvSecured Studio vault (*.json)|*.json|All files (*.*)|*.*" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                OpenProjectFile(dialog.FileName);
            }
        }

        private void OpenProjectFile(string path)
        {
            if (!System.IO.File.Exists(path))
            {
                MessageBox.Show(this, "Project file does not exist.", "Open Project", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                RenderNoProjectView();
                return;
            }

            var restoreBackup = false;
            if (vaultFileService.HasRecoveryBackup(path))
            {
                var backupPath = vaultFileService.GetRecoveryBackupPath(path);
                var restoreResult = MessageBox.Show(
                    this,
                    "Found an unsaved backup next to this project.\r\n\r\n" +
                    backupPath + "\r\n\r\n" +
                    "Restore data from this backup?",
                    "Restore Unsaved Backup",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                restoreBackup = restoreResult == DialogResult.Yes;
                if (!restoreBackup)
                {
                    DeleteRecoveryBackupAfterDecline(path, backupPath);
                }
            }

            project = LoadVaultFile(path, restoreBackup);
            if (project == null)
            {
                RenderNoProjectView();
                return;
            }

            currentFilePath = path;
            modified = restoreBackup;
            recoveryBackupWarningShown = false;
            recentProjectsService.Add(path);
            RefreshNavigation();
            RenderCurrentView();
            RefreshStatus();
        }

        private void DeleteRecoveryBackupAfterDecline(string projectPath, string backupPath)
        {
            var deleteResult = MessageBox.Show(
                this,
                "Delete this unsaved backup so it will not be offered again?\r\n\r\n" + backupPath,
                "Delete Unsaved Backup",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (deleteResult != DialogResult.Yes) return;

            try
            {
                vaultFileService.DeleteRecoveryBackup(projectPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not delete backup.\r\n\r\n" + ex.Message, "Delete Unsaved Backup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SaveProject()
        {
            if (currentFilePath == null)
            {
                SaveProjectAs();
                return;
            }

            if (!SaveVaultFile(currentFilePath, false))
            {
                return;
            }
            vaultFileService.DeleteRecoveryBackup(currentFilePath);
            modified = false;
            recoveryBackupWarningShown = false;
            RefreshStatus();
        }

        private void SaveProjectAs()
        {
            using (var dialog = new SaveFileDialog { Filter = "EnvSecured Studio vault (*.json)|*.json|All files (*.*)|*.*", FileName = "envsecured.vault.json" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                currentFilePath = dialog.FileName;
                SaveProject();
                recentProjectsService.Add(currentFilePath);
            }
        }

        private void RefreshNavigation()
        {
            navigation.Nodes.Clear();
            navigation.Nodes.Add("Project").Nodes.Add("Project");
            var config = navigation.Nodes.Add("Configuration");
            config.Nodes.Add("Variables");
            config.Nodes.Add("Services");
            config.Nodes.Add("Environments");
            config.Nodes.Add("Contracts");
            var io = navigation.Nodes.Add("Import / Export");
            io.Nodes.Add("Import");
            io.Nodes.Add("Export");
            navigation.Nodes.Add("Validation").Nodes.Add("Validation Results");
            navigation.ExpandAll();
        }

        private void RenderCurrentView()
        {
            contentPanel.Controls.Clear();
            if (currentView == "Services") RenderServicesView();
            else if (currentView == "Environments") RenderEnvironmentsView();
            else if (currentView == "Contracts") RenderContractsView();
            else if (currentView == "Import") RenderImportView();
            else if (currentView == "Export") RenderExportView();
            else if (currentView == "Project") RenderProjectView();
            else if (currentView == "Validation Results") RenderValidationView();
            else RenderVariablesView();
            RefreshStatus();
        }

        private void RenderProjectView()
        {
            project.Settings = project.Settings ?? new ProjectSettings();

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(8) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            AddCommand(buttons, "Apply", () =>
            {
                var nameBox = contentPanel.Controls.Find("ProjectNameBox", true).FirstOrDefault() as TextBox;
                var descriptionBox = contentPanel.Controls.Find("ProjectDescriptionBox", true).FirstOrDefault() as TextBox;
                var encryptionCombo = contentPanel.Controls.Find("ProjectEncryptionCombo", true).FirstOrDefault() as ComboBox;
                var cliExportPasswordBox = contentPanel.Controls.Find("CliExportPasswordRequiredBox", true).FirstOrDefault() as CheckBox;
                if (nameBox == null || encryptionCombo == null) return;

                project.ProjectName = string.IsNullOrWhiteSpace(nameBox.Text) ? project.ProjectName : nameBox.Text.Trim();
                project.Description = descriptionBox?.Text;
                SetProjectEncryptionMode(Convert.ToString(encryptionCombo.SelectedItem));
                project.Settings.CliExportPasswordRequired = cliExportPasswordBox?.Checked == true;
                Changed();
            });
            buttons.Controls.Add(new Label { Text = "Project Properties", AutoSize = true, Padding = new Padding(12, 7, 0, 0), Font = new Font(Font, FontStyle.Bold) });

            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var form = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, RowCount = 5, Padding = new Padding(0, 8, 16, 0), AutoSize = true };
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (var i = 0; i < 4; i++) form.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            form.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));

            form.Controls.Add(new Label { Text = "Name:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            form.Controls.Add(new TextBox { Name = "ProjectNameBox", Dock = DockStyle.Fill, Text = project.ProjectName ?? string.Empty }, 1, 0);
            form.Controls.Add(new Label { Text = "Storage:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
            form.Controls.Add(new ComboBox
            {
                Name = "ProjectEncryptionCombo",
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Items = { EncryptionOpen, EncryptionSecrets, EncryptionAllValues, EncryptionWholeJson },
                SelectedItem = GetProjectEncryptionModeLabel(project)
            }, 1, 1);
            form.Controls.Add(new Label { Text = "Current file:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
            form.Controls.Add(new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Text = currentFilePath ?? "not saved" }, 1, 2);
            form.Controls.Add(new Label { Text = "CLI export:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 3);
            form.Controls.Add(new CheckBox { Name = "CliExportPasswordRequiredBox", Dock = DockStyle.Left, AutoSize = true, Text = "Require --password or ENVSECURED_PASSWORD for CLI export", Checked = project.Settings.CliExportPasswordRequired }, 1, 3);
            form.Controls.Add(new Label { Text = "Description:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 4);
            form.Controls.Add(new TextBox { Name = "ProjectDescriptionBox", Dock = DockStyle.Fill, Multiline = true, Text = project.Description ?? string.Empty }, 1, 4);

            scroll.Controls.Add(form);
            root.Controls.Add(buttons, 0, 0);
            root.Controls.Add(scroll, 0, 1);
            contentPanel.Controls.Add(root);
        }

        private void RenderExportView()
        {
            project.Settings = project.Settings ?? new ProjectSettings();

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(8) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            AddCommand(buttons, "Apply", ApplyExportSettingsFromView);
            AddCommand(buttons, "Render Files", () =>
            {
                ApplyExportSettingsFromView();
                RenderOutputFiles();
            });
            buttons.Controls.Add(new Label { Text = "Export Settings", AutoSize = true, Padding = new Padding(12, 7, 0, 0), Font = new Font(Font, FontStyle.Bold) });

            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(0, 8, 16, 0) };
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 256));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var form = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 8, Margin = Padding.Empty };
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (var i = 0; i < 8; i++) form.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

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

            var targetHeader = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            targetHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            targetHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            targetHeader.Controls.Add(new Label { Text = "Render matrix:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            var targetButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            var selectAllTargets = new Button { Text = "Select All", Width = 90, Height = 26 };
            var selectNoTargets = new Button { Text = "Select None", Width = 90, Height = 26 };
            targetButtons.Controls.Add(selectAllTargets);
            targetButtons.Controls.Add(selectNoTargets);
            targetHeader.Controls.Add(targetButtons, 1, 0);

            var outputTargetGrid = BuildOutputTargetGrid();
            selectAllTargets.Click += (s, e) => SetOutputTargetGrid(outputTargetGrid, true, true);
            selectNoTargets.Click += (s, e) => SetOutputTargetGrid(outputTargetGrid, false, true);

            body.Controls.Add(form, 0, 0);
            body.Controls.Add(targetHeader, 0, 1);
            body.Controls.Add(outputTargetGrid, 0, 2);
            root.Controls.Add(buttons, 0, 0);
            root.Controls.Add(body, 0, 1);
            contentPanel.Controls.Add(root);
        }

        private void BrowseOutputRootFolder(TextBox outputRootBox)
        {
            using (var dialog = new FolderBrowserDialog
            {
                Description = "Select output folder",
                ShowNewFolderButton = true,
                SelectedPath = Directory.Exists(outputRootBox.Text) ? outputRootBox.Text : string.Empty
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                outputRootBox.Text = dialog.SelectedPath;
            }
        }

        private void ApplyExportSettingsFromView()
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
            SaveOutputTargetsFromView(false);
            Changed();
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
                MessageBox.Show(this, "Set Export -> Out folder before rendering files.", "Render Files", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                currentView = "Export";
                RenderCurrentView();
                return;
            }

            if (!EnsureOutputRootFolderExists(project.Settings.OutputRootFolder))
            {
                return;
            }

            var targets = GetConfiguredOutputTargets();
            if (targets == null || targets.Count == 0) return;

            var format = NormalizeOutputFormat(project.Settings.OutputFormat);
            if (project.Settings.OutputStructuredSingleFile && format != "CONFIG")
            {
                var path = BuildStructuredOutputPath();
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(path, FormatStructuredOutput(targets, format));
                MessageBox.Show(this, "Rendered 1 file.", "Render Files");
                return;
            }

            var rendered = 0;
            foreach (var target in targets)
            {
                var values = BuildOutputValues(target.Service, target.Environment);
                var path = BuildOutputPath(target);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, FormatOutputValues(values, format));
                rendered++;
            }

            MessageBox.Show(this, $"Rendered {rendered} file(s).", "Render Files");
        }

        private bool EnsureOutputRootFolderExists(string outputRootFolder)
        {
            if (Directory.Exists(outputRootFolder)) return true;

            var result = MessageBox.Show(
                this,
                "Output folder does not exist. Create it now?" + Environment.NewLine + outputRootFolder,
                "Render Files",
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
                MessageBox.Show(this, "Could not create output folder." + Environment.NewLine + ex.Message, "Render Files", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

            AddOutputTargetRow(grid, null);
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
            row.Cells["Service"].Value = service?.Name ?? "Global service";
            foreach (DataGridViewColumn column in grid.Columns)
            {
                if (column.Name != "Service") row.Cells[column.Name].Value = IsOutputTargetEnabled(service?.Id, (column.Tag as EnvironmentModel)?.Id);
            }
        }

        private void SetOutputTargetGrid(DataGridView grid, bool value, bool persist)
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                foreach (DataGridViewColumn column in grid.Columns)
                {
                    if (column.Name != "Service") row.Cells[column.Name].Value = value;
                }
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
            var services = new ServiceModel[] { null }.Concat(project.Services.OrderBy(s => s.SortOrder)).ToList();
            var environments = new EnvironmentModel[] { null }.Concat(project.Environments.OrderBy(e => e.SortOrder)).ToList();
            foreach (var service in services)
            {
                foreach (var environment in environments)
                {
                    if (IsOutputTargetEnabled(service?.Id, environment?.Id))
                    {
                        result.Add(new OutputTarget(service, environment));
                    }
                }
            }

            if (result.Count == 0)
            {
                MessageBox.Show(this, "No render targets selected.", "Render Files", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            return Path.Combine(project.Settings.OutputRootFolder, relative);
        }

        private string BuildStructuredOutputPath()
        {
            var ext = NormalizeOutputExtension(project.Settings.OutputExtension, project.Settings.OutputFormat);
            var relative = ApplyOutputMaskPlaceholders(DefaultIfBlank(project.Settings.OutputStructuredSingleFileMask, @"{project_name}{.ext}"), ext, string.Empty, string.Empty)
                .TrimStart('\\', '/');
            return Path.Combine(project.Settings.OutputRootFolder, relative);
        }

        private string ApplyOutputMaskPlaceholders(string mask, string extension, string serviceName, string environmentName)
        {
            var projectName = SafeOutputName(DefaultIfBlank(project.ProjectName, project.ProjectId));
            return (mask ?? string.Empty)
                .Replace("{project_name}", projectName)
                .Replace("{project}", projectName)
                .Replace("{service}", serviceName ?? string.Empty)
                .Replace("{env}", environmentName ?? string.Empty)
                .Replace("{.ext}", extension)
                .Replace("{ext}", extension.TrimStart('.'));
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
            if (format == "CONFIG") return DefaultIfBlank(service.ConfigName, pathName ? DefaultIfBlank(service.OutputFolder, service.Name) : service.Name);
            if (format == "TOML") return DefaultIfBlank(service.TomlName, service.Name);
            if (format == "YAML") return DefaultIfBlank(service.YamlName, service.Name);
            if (format == "XML") return DefaultIfBlank(service.XmlName, service.Name);
            if (format == "JSON") return DefaultIfBlank(service.JsonName, service.Name);
            return service.Name;
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

        private void RenderVariablesView()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            AddCommand(buttons, "Add Variable", AddVariable);
            AddCommand(buttons, "Edit Variable", EditVariable);
            AddCommand(buttons, "Delete Variable", DeleteVariable);
            AddCommand(buttons, "Set Value", SetValue);
            AddCommand(buttons, "Delete Value", DeleteValue);
            AddCommand(buttons, "Set Environment", SetVariableEnvironmentFilter);
            AddCommand(buttons, "Set Service", SetVariableServiceFilter);
            AddCommand(buttons, "Contracts Matrix", () => { currentView = "Contracts"; RenderCurrentView(); });
            showSecretsCheckBox = new CheckBox { Text = "Show Secrets", AutoSize = true, Padding = new Padding(12, 6, 0, 0) };
            showSecretsCheckBox.CheckedChanged += (s, e) => RefreshVariableDisplayPreservingSelection(root);
            buttons.Controls.Add(showSecretsCheckBox);
            calculatedValuesCheckBox = new CheckBox { Text = "Calculated Values", Checked = recentProjectsService.LoadBooleanPreference(PreferenceCalculatedValues, true), AutoSize = true, Padding = new Padding(8, 6, 0, 0) };
            calculatedValuesCheckBox.CheckedChanged += (s, e) =>
            {
                recentProjectsService.SaveBooleanPreference(PreferenceCalculatedValues, calculatedValuesCheckBox.Checked);
                RefreshVariableDisplayPreservingSelection(root);
            };
            buttons.Controls.Add(calculatedValuesCheckBox);
            matrixLightColorsCheckBox = new CheckBox { Text = "Light Matrix Colors", Checked = recentProjectsService.LoadBooleanPreference(PreferenceLightMatrixColors, false), AutoSize = true, Padding = new Padding(8, 6, 0, 0) };
            matrixLightColorsCheckBox.CheckedChanged += (s, e) =>
            {
                recentProjectsService.SaveBooleanPreference(PreferenceLightMatrixColors, matrixLightColorsCheckBox.Checked);
                RefreshVariableDetails(root);
            };
            buttons.Controls.Add(matrixLightColorsCheckBox);
            root.Controls.Add(buttons, 0, 0);

            root.Controls.Add(BuildVariableFilters(), 0, 1);
            mainGrid = BuildVariablesGrid(root);
            mainGrid.SelectionChanged += (s, e) => RefreshVariableDetails(root);

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                Panel1MinSize = 80,
                Panel2MinSize = 80
            };
            split.Panel1.Controls.Add(mainGrid);
            split.Panel2.Controls.Add(BuildVariableDetails());
            root.Controls.Add(split, 0, 2);
            contentPanel.Controls.Add(root);
            AttachSplitterRatioPersistence(split, "VariablesVertical");
            split.HandleCreated += (s, e) => BeginInvoke(new Action(() => RestoreSplitterRatio(split, "VariablesVertical", 0.72)));
            RefreshVariableGrid();
        }

        private void AttachSplitterRatioPersistence(SplitContainer split, string splitterKey)
        {
            split.SplitterMoved += (s, e) => SaveSplitterRatio(split, splitterKey);
        }

        private void RestoreSplitterRatio(SplitContainer split, string splitterKey, double defaultRatio)
        {
            var ratio = recentProjectsService.LoadSplitterRatio(splitterKey) ?? defaultRatio;
            SetSafeSplitterDistance(split, ratio);
        }

        private void SaveSplitterRatio(SplitContainer split, string splitterKey)
        {
            if (suppressSplitterPersistence || split.IsDisposed) return;
            var available = SplitterAvailableSize(split);
            if (available <= 0) return;
            var ratio = split.SplitterDistance / (double)available;
            ratio = Math.Max(0.05, Math.Min(0.95, ratio));
            recentProjectsService.SaveSplitterRatio(splitterKey, ratio);
        }

        private void SetSafeSplitterDistance(SplitContainer split, double ratio)
        {
            if (split.IsDisposed) return;
            var available = SplitterAvailableSize(split);
            if (available <= split.Panel1MinSize + split.Panel2MinSize) return;
            var min = split.Panel1MinSize;
            var max = available - split.Panel2MinSize;
            var desired = (int)(available * Math.Max(0.05, Math.Min(0.95, ratio)));
            suppressSplitterPersistence = true;
            try
            {
                split.SplitterDistance = Math.Max(min, Math.Min(max, desired));
            }
            finally
            {
                suppressSplitterPersistence = false;
            }
        }

        private static int SplitterAvailableSize(SplitContainer split)
        {
            return (split.Orientation == Orientation.Horizontal ? split.Height : split.Width) - split.SplitterWidth;
        }

        private Control BuildVariableFilters()
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, Padding = new Padding(0, 4, 0, 4) };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 65));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 55));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var environments = new[] { new ScopeSelectorItem(null, "Global") }
                .Concat(project.Environments.OrderBy(e => e.SortOrder).Select(e => new ScopeSelectorItem(e.Id, e.Name)))
                .ToList();
            var services = new[] { new ScopeSelectorItem(null, "All services") }
                .Concat(project.Services.OrderBy(s => s.SortOrder).Select(s => new ScopeSelectorItem(s.Id, s.Name)))
                .ToList();

            environmentCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, DataSource = environments, DisplayMember = "Name" };
            serviceCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, DataSource = services, DisplayMember = "Name" };
            searchBox = new TextBox { Dock = DockStyle.Fill };
            environmentCombo.SelectedIndexChanged += (s, e) => RefreshVariableGrid();
            serviceCombo.SelectedIndexChanged += (s, e) => RefreshVariableGrid();
            searchBox.TextChanged += (s, e) => RefreshVariableGrid();

            panel.Controls.Add(new Label { Text = "Environment:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            panel.Controls.Add(environmentCombo, 1, 0);
            panel.Controls.Add(new Label { Text = "Service:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 2, 0);
            panel.Controls.Add(serviceCombo, 3, 0);
            panel.Controls.Add(new Label { Text = "Search:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 4, 0);
            panel.Controls.Add(searchBox, 5, 0);
            return panel;
        }

        private Control BuildVariableDetails()
        {
            var panel = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                Panel1MinSize = 50,
                Panel2MinSize = 50
            };
            panel.Panel1.Controls.Add(new GroupBox { Dock = DockStyle.Fill, Text = "Selected Variable", Name = "variableInfo" });
            panel.Panel2.Controls.Add(new DataGridView
            {
                Dock = DockStyle.Fill,
                Name = "variableMatrix",
                ReadOnly = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.CellSelect
            });
            AttachSplitterRatioPersistence(panel, "VariableDetailsMatrix");
            panel.HandleCreated += (s, e) => BeginInvoke(new Action(() =>
            {
                ApplyVariableDetailsMinSizes(panel);
                RestoreSplitterRatio(panel, "VariableDetailsMatrix", 0.25);
            }));
            return panel;
        }

        private static void ApplyVariableDetailsMinSizes(SplitContainer panel)
        {
            if (panel.IsDisposed) return;
            var available = SplitterAvailableSize(panel);
            if (available <= 0) return;
            panel.Panel1MinSize = Math.Min(180, Math.Max(50, available / 4));
            panel.Panel2MinSize = Math.Min(360, Math.Max(50, available / 2));
        }

        private void RefreshVariableDetails(TableLayoutPanel root)
        {
            if (root.RowCount < 3) return;
            var outerSplit = root.GetControlFromPosition(0, 2) as SplitContainer;
            var details = outerSplit?.Panel2.Controls.OfType<SplitContainer>().FirstOrDefault();
            if (details == null) return;
            var info = details.Panel1.Controls.OfType<GroupBox>().FirstOrDefault();
            var matrix = details.Panel2.Controls.OfType<DataGridView>().FirstOrDefault(g => g.Name == "variableMatrix");
            if (info == null || matrix == null) return;

            info.Controls.Clear();
            var variable = GetSelectedVariable();
            if (variable == null)
            {
                info.Text = "Selected Variable";
                matrix.Columns.Clear();
                matrix.Rows.Clear();
                return;
            }

            var env = SelectedEnvironment();
            var svc = SelectedService();
            var effective = BuildDisplayEffective(svc?.Id, env?.Id).FirstOrDefault(x => x.Variable.Id == variable.Id);
            var selected = FindSelectedLayerValue(variable.Id);
            var effectiveValue = effective?.Value ?? selected.Value;
            var source = effective?.SourceScope.ToString() ?? selected.Source;

            info.Text = variable.Key;
            info.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = $"Display: {variable.DisplayName}\r\nType: {variable.Type}\r\nSecret: {(variable.IsSecret ? "Yes" : "No")}\r\nAllow null: {(variable.AllowNull ? "Yes" : "No")}\r\nAllow blank: {(variable.AllowBlank ? "Yes" : "No")}\r\nEffective: {DisplayValue(variable, effectiveValue)}\r\nSource: {source}",
                Padding = new Padding(12)
            });

            BuildVariableServiceEnvironmentMatrix(matrix, variable);
        }

        private void BuildVariableServiceEnvironmentMatrix(DataGridView matrix, VariableDefinitionModel variable)
        {
            suppressVariableMatrixEvents = true;
            try
            {
                matrix.Columns.Clear();
                matrix.Rows.Clear();
                matrix.Tag = variable.Id;
                matrix.CellDoubleClick -= VariableMatrixCellDoubleClick;
                matrix.KeyDown -= VariableMatrixKeyDown;
                matrix.CellValueChanged -= VariableMatrixCellValueChanged;
                matrix.CurrentCellDirtyStateChanged -= VariableMatrixCurrentCellDirtyStateChanged;
                matrix.CellDoubleClick += VariableMatrixCellDoubleClick;
                matrix.KeyDown += VariableMatrixKeyDown;
                matrix.CellValueChanged += VariableMatrixCellValueChanged;
                matrix.CurrentCellDirtyStateChanged += VariableMatrixCurrentCellDirtyStateChanged;
                var valueColors = BuildVariableMatrixValueColors(variable);
                matrix.Columns.Add(new DataGridViewTextBoxColumn { Name = "Service", HeaderText = "Service", ReadOnly = true, FillWeight = 120 });
                matrix.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Use", HeaderText = "Use", FillWeight = 45 });
                matrix.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = "env_global",
                    HeaderText = "Global environment",
                    Tag = null,
                    ReadOnly = true,
                    FillWeight = 120
                });
                foreach (var environment in project.Environments.OrderBy(e => e.SortOrder))
                {
                    matrix.Columns.Add(new DataGridViewTextBoxColumn
                    {
                        Name = "env_" + environment.Id,
                        HeaderText = environment.Name,
                        Tag = environment,
                        ReadOnly = true,
                        FillWeight = 120
                    });
                }

                AddVariableMatrixRow(matrix, variable, null, valueColors);
                foreach (var service in project.Services.OrderBy(s => s.SortOrder))
                {
                    AddVariableMatrixRow(matrix, variable, service, valueColors);
                }
            }
            finally
            {
                suppressVariableMatrixEvents = false;
            }
        }

        private Dictionary<string, Color> BuildVariableMatrixValueColors(VariableDefinitionModel variable)
        {
            var values = new List<string>();
            var services = new ServiceModel[] { null }.Concat(project.Services.OrderBy(s => s.SortOrder)).ToList();
            var environments = new EnvironmentModel[] { null }.Concat(project.Environments.OrderBy(e => e.SortOrder)).ToList();
            foreach (var service in services)
            {
                foreach (var environment in environments)
                {
                    var effective = BuildDisplayEffective(service?.Id, environment?.Id).FirstOrDefault(x => x.Variable.Id == variable.Id);
                    if (effective == null || effective.Missing || string.IsNullOrEmpty(effective.Value)) continue;
                    if (!values.Contains(effective.Value))
                    {
                        values.Add(effective.Value);
                    }
                }
            }

            var result = new Dictionary<string, Color>();
            for (var i = 0; i < values.Count; i++)
            {
                result[values[i]] = MatrixPalette[i % MatrixPalette.Length];
            }
            return result;
        }

        private void AddVariableMatrixRow(DataGridView matrix, VariableDefinitionModel variable, ServiceModel service, Dictionary<string, Color> valueColors)
        {
            var rowIndex = matrix.Rows.Add();
            var row = matrix.Rows[rowIndex];
            row.Cells["Service"].Value = service?.Name ?? "Global service";
            row.Tag = service?.Id;
            row.Cells["Service"].Style.BackColor = SystemColors.ControlLight;
            row.Cells["Use"].Value = service != null && IsVariableUsedByService(variable.Id, service.Id);
            row.Cells["Use"].ReadOnly = service == null;
            row.Cells["Use"].Style.BackColor = service == null ? SystemColors.ControlLight : Color.White;
            foreach (DataGridViewColumn column in matrix.Columns)
            {
                if (column.Name == "Service" || column.Name == "Use") continue;
                var environment = column.Tag as EnvironmentModel;
                var state = ResolveVariableMatrixCell(variable, service, environment, valueColors);
                var cell = row.Cells[column.Name];
                cell.ReadOnly = true;
                cell.Value = state.DisplayValue;
                cell.ToolTipText = state.ToolTip;
                cell.Style.BackColor = state.BackColor;
                cell.Style.ForeColor = state.ForeColor;
                cell.Style.Font = state.FontStyle == FontStyle.Bold
                    ? new Font(matrix.Font, FontStyle.Bold)
                    : matrix.Font;
            }
        }

        private MatrixCellState ResolveVariableMatrixCell(VariableDefinitionModel variable, ServiceModel service, EnvironmentModel environment, Dictionary<string, Color> valueColors)
        {
            var usedByService = service == null || IsVariableUsedByService(variable.Id, service.Id);
            if (!usedByService)
            {
                return MatrixCellState.Empty("-");
            }
            var effective = BuildDisplayEffective(service?.Id, environment?.Id).FirstOrDefault(x => x.Variable.Id == variable.Id);
            if (effective == null || effective.Missing)
            {
                return MatrixCellState.Empty("-");
            }

            var display = DisplayValue(variable, effective.Value);
            var color = valueColors.TryGetValue(effective.Value ?? string.Empty, out var mappedColor)
                ? mappedColor
                : Color.White;
            var direct =
                (service == null && environment == null && effective.SourceScope == ValueScope.Global) ||
                (service == null && environment != null && effective.SourceScope == ValueScope.Environment) ||
                (service != null && environment == null && effective.SourceScope == ValueScope.Service) ||
                (service != null && environment != null && effective.SourceScope == ValueScope.ServiceEnvironment);
            var inherited = effective.SourceScope == ValueScope.Global ||
                effective.SourceScope == ValueScope.Environment ||
                effective.SourceScope == ValueScope.Service;
            if (direct)
            {
                return MatrixCellState.Direct(display, color, MatrixLightColorMode(), "Defined at " + effective.SourceScope);
            }

            if (inherited)
            {
                return MatrixCellState.Inherited(display, color, MatrixLightColorMode(), "Inherited from " + effective.SourceScope);
            }

            return MatrixCellState.Empty("-");
        }

        private void VariableMatrixCellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex <= 1) return;
            var matrix = sender as DataGridView;
            if (matrix == null) return;
            SetVariableMatrixDirectValue(matrix, e.RowIndex, e.ColumnIndex);
        }

        private void VariableMatrixCurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            var matrix = sender as DataGridView;
            if (matrix?.IsCurrentCellDirty == true)
            {
                matrix.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        }

        private void VariableMatrixCellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (suppressVariableMatrixEvents) return;
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            var matrix = sender as DataGridView;
            if (matrix == null || matrix.Columns[e.ColumnIndex].Name != "Use") return;
            var variableId = Convert.ToString(matrix.Tag);
            var serviceId = Convert.ToString(matrix.Rows[e.RowIndex].Tag);
            if (string.IsNullOrWhiteSpace(variableId) || string.IsNullOrWhiteSpace(serviceId)) return;
            var variable = project.Variables.FirstOrDefault(v => v.Id == variableId);
            if (variable == null) return;

            var enabled = Convert.ToBoolean(matrix.Rows[e.RowIndex].Cells["Use"].Value ?? false);
            var existing = project.Contracts.FirstOrDefault(c => c.VariableId == variableId && c.ServiceId == serviceId);
            if (enabled)
            {
                if (existing == null && !HasGlobalValue(variableId))
                {
                    project.Contracts.Add(new VariableContractModel
                    {
                        Id = ProjectService.NewId(),
                        VariableId = variableId,
                        ServiceId = serviceId,
                        Required = true,
                        SortOrder = project.Contracts.Count * 10
                    });
                }
                else if (existing != null && existing.Excluded)
                {
                    project.Contracts.Remove(existing);
                }
            }
            else if (existing != null)
            {
                if (HasGlobalValue(variableId))
                {
                    existing.Excluded = true;
                    existing.Required = false;
                }
                else
                {
                    project.Contracts.Remove(existing);
                }
            }
            else if (HasGlobalValue(variableId))
            {
                project.Contracts.Add(new VariableContractModel
                {
                    Id = ProjectService.NewId(),
                    VariableId = variableId,
                    ServiceId = serviceId,
                    Excluded = true,
                    Required = false,
                    SortOrder = project.Contracts.Count * 10
                });
            }

            modified = true;
            SaveRecoveryBackupIfPossible();
            RefreshVariableGrid();
            BuildVariableServiceEnvironmentMatrix(matrix, variable);
            RefreshStatus();
        }

        private void VariableMatrixKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Delete) return;
            var matrix = sender as DataGridView;
            if (matrix?.CurrentCell == null || matrix.CurrentCell.RowIndex < 0 || matrix.CurrentCell.ColumnIndex <= 1) return;
            DeleteVariableMatrixDirectValue(matrix, matrix.CurrentCell.RowIndex, matrix.CurrentCell.ColumnIndex);
            e.Handled = true;
        }

        private void SetVariableMatrixDirectValue(DataGridView matrix, int rowIndex, int columnIndex)
        {
            var target = MatrixTargetFromCell(matrix, rowIndex, columnIndex);
            if (target == null) return;
            var existing = FindDirectValue(target.Variable.Id, target.Scope, target.ServiceId, target.EnvironmentId);
            var value = PromptDialog.Show(this, "Set Value", $"{target.Variable.Key} value for {target.Label}:", existing?.Value ?? string.Empty);
            if (value == null) return;

            if (existing == null)
            {
                project.Values.Add(new VariableValueModel
                {
                    Id = ProjectService.NewId(),
                    VariableId = target.Variable.Id,
                    Scope = target.Scope,
                    EnvironmentId = target.EnvironmentId,
                    ServiceId = target.ServiceId,
                    Value = value,
                    IsEncrypted = target.Variable.IsSecret,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            else
            {
                existing.Value = value;
                existing.IsEncrypted = target.Variable.IsSecret;
                existing.UpdatedAt = DateTime.UtcNow;
            }

            EnsureContractForMatrixTarget(target);
            Changed();
        }

        private void DeleteVariableMatrixDirectValue(DataGridView matrix, int rowIndex, int columnIndex)
        {
            var target = MatrixTargetFromCell(matrix, rowIndex, columnIndex);
            if (target == null) return;
            var existing = FindDirectValue(target.Variable.Id, target.Scope, target.ServiceId, target.EnvironmentId);
            if (existing == null) return;
            if (MessageBox.Show(this, $"Delete direct value for {target.Label}?", "Delete Value", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            project.Values.Remove(existing);
            Changed();
        }

        private MatrixValueTarget MatrixTargetFromCell(DataGridView matrix, int rowIndex, int columnIndex)
        {
            var variableId = Convert.ToString(matrix.Tag);
            var variable = project.Variables.FirstOrDefault(v => v.Id == variableId);
            if (variable == null || rowIndex < 0 || columnIndex <= 1) return null;

            var row = matrix.Rows[rowIndex];
            var column = matrix.Columns[columnIndex];
            var serviceId = Convert.ToString(row.Tag);
            var environment = column.Tag as EnvironmentModel;
            var environmentId = environment?.Id;
            ValueScope scope;
            if (serviceId == null && environmentId == null) scope = ValueScope.Global;
            else if (serviceId == null) scope = ValueScope.Environment;
            else if (environmentId == null) scope = ValueScope.Service;
            else scope = ValueScope.ServiceEnvironment;

            var service = serviceId == null ? null : project.Services.FirstOrDefault(s => s.Id == serviceId);
            var label = $"{service?.Name ?? "Global service"} / {environment?.Name ?? "Global environment"}";
            return new MatrixValueTarget(variable, scope, serviceId, environmentId, label);
        }

        private VariableValueModel FindDirectValue(string variableId, ValueScope scope, string serviceId, string environmentId)
        {
            return project.Values.LastOrDefault(v =>
                v.VariableId == variableId &&
                v.Scope == scope &&
                v.ServiceId == serviceId &&
                v.EnvironmentId == environmentId);
        }

        private void EnsureContractForMatrixTarget(MatrixValueTarget target)
        {
            if (target.ServiceId == null) return;
            var existing = project.Contracts.FirstOrDefault(c => c.VariableId == target.Variable.Id && c.ServiceId == target.ServiceId);
            if (existing != null)
            {
                existing.Excluded = false;
                return;
            }
            if (HasGlobalValue(target.Variable.Id)) return;
            project.Contracts.Add(new VariableContractModel
            {
                Id = ProjectService.NewId(),
                VariableId = target.Variable.Id,
                ServiceId = target.ServiceId,
                Required = true,
                SortOrder = project.Contracts.Count * 10
            });
        }

        private bool MatrixLightColorMode()
        {
            return matrixLightColorsCheckBox?.Checked == true;
        }

        private bool ShowCalculatedValues()
        {
            return calculatedValuesCheckBox == null || calculatedValuesCheckBox.Checked;
        }

        private IReadOnlyList<EffectiveValue> BuildDisplayEffective(string serviceId, string environmentId)
        {
            return ShowCalculatedValues()
                ? effectiveConfigService.Build(project, serviceId, environmentId)
                : BuildRawEffectiveValues(serviceId, environmentId);
        }

        private IReadOnlyList<EffectiveValue> BuildRawEffectiveValues(string serviceId, string environmentId)
        {
            return project.Variables
                .Where(v => v.IsActive)
                .OrderBy(v => v.SortOrder)
                .ThenBy(v => v.Key)
                .Select(v => BuildRawEffectiveValue(v, serviceId, environmentId))
                .ToList();
        }

        private EffectiveValue BuildRawEffectiveValue(VariableDefinitionModel variable, string serviceId, string environmentId)
        {
            VariableValueModel selected = null;
            foreach (var scope in new[] { ValueScope.Global, ValueScope.Environment, ValueScope.Service, ValueScope.ServiceEnvironment })
            {
                var candidate = project.Values.LastOrDefault(v =>
                    v.VariableId == variable.Id &&
                    v.Scope == scope &&
                    MatchesValueScope(v, scope, serviceId, environmentId));
                if (candidate != null)
                {
                    selected = candidate;
                }
            }

            return new EffectiveValue
            {
                Variable = variable,
                Value = selected?.Value,
                SourceScope = selected?.Scope
            };
        }

        private static bool MatchesValueScope(VariableValueModel value, ValueScope scope, string serviceId, string environmentId)
        {
            if (scope == ValueScope.Global) return value.ServiceId == null && value.EnvironmentId == null;
            if (scope == ValueScope.Environment) return value.ServiceId == null && value.EnvironmentId == environmentId;
            if (scope == ValueScope.Service) return value.ServiceId == serviceId && value.EnvironmentId == null;
            return value.ServiceId == serviceId && value.EnvironmentId == environmentId;
        }

        private void RenderServicesView()
        {
            var root = BuildCrudView("Services", AddService, EditService, DeleteService);
            mainGrid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex >= 0) EditService();
            };
            mainGrid.DataSource = project.Services.OrderBy(s => s.SortOrder).Select(s => new
            {
                s.Id,
                s.Name,
                s.DisplayName,
                s.OutputFolder,
                s.DefaultPrefix,
                s.ConfigName,
                s.TomlName,
                s.YamlName,
                s.XmlName,
                s.JsonName,
                Active = s.IsActive
            }).ToList();
            AttachColumnWidthPersistence(mainGrid, "Services");
            RestoreColumnWidths(mainGrid, "Services");
            contentPanel.Controls.Add(root);
        }

        private void RenderEnvironmentsView()
        {
            var root = BuildCrudView("Environments", AddEnvironment, EditEnvironment, DeleteEnvironment);
            mainGrid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex >= 0) EditEnvironment();
            };
            mainGrid.DataSource = project.Environments.OrderBy(e => e.SortOrder).Select(e => new
            {
                e.Id,
                e.Name,
                e.DisplayName,
                e.ConfigName,
                e.TomlName,
                e.YamlName,
                e.XmlName,
                e.JsonName,
                Active = e.IsActive
            }).ToList();
            AttachColumnWidthPersistence(mainGrid, "Environments");
            RestoreColumnWidths(mainGrid, "Environments");
            contentPanel.Controls.Add(root);
        }

        private void RenderContractsView()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            AddCommand(buttons, "Apply Changes", ApplyContractsMatrix);
            AddCommand(buttons, "Auto Assign Prefixes", AutoAssignAllPrefixes);
            buttons.Controls.Add(new Label
            {
                Text = "Contracts Matrix: variables down, services across",
                AutoSize = true,
                Padding = new Padding(12, 7, 0, 0),
                Font = new Font(Font, FontStyle.Bold)
            });

            contractsMatrix = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.CellSelect
            };
            BuildContractsMatrix();
            AttachColumnWidthPersistence(contractsMatrix, "Contracts");
            RestoreColumnWidths(contractsMatrix, "Contracts");
            root.Controls.Add(buttons, 0, 0);
            root.Controls.Add(contractsMatrix, 0, 1);
            contentPanel.Controls.Add(root);
        }

        private void RenderImportView()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 62));

            var fileButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            AddCommand(fileButtons, "Add Files", AddImportFiles);
            AddCommand(fileButtons, "Select All", () => SetImportFilesSelected(true));
            AddCommand(fileButtons, "Select None", () => SetImportFilesSelected(false));
            removeImportFilesButton = AddCommand(fileButtons, "Remove Files", RemoveImportFile);
            setImportEnvironmentButton = AddCommand(fileButtons, "Set Env For Selected", SetImportEnvironmentForSelected);
            setImportServiceButton = AddCommand(fileButtons, "Set Service For Selected", SetImportServiceForSelected);
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
            AddCommand(previewButtons, "Select All", () => SetImportPreviewIncluded(true));
            AddCommand(previewButtons, "Select None", () => SetImportPreviewIncluded(false));
            AddCommand(previewButtons, "Set Env For Included", SetImportPreviewEnvironmentForIncluded);
            AddCommand(previewButtons, "Set Service For Included", SetImportPreviewServiceForIncluded);
            AddCommand(previewButtons, "Secret On", () => SetImportPreviewSecretForIncluded(true));
            AddCommand(previewButtons, "Secret Off", () => SetImportPreviewSecretForIncluded(false));
            AddCommand(previewButtons, "Required On", () => SetImportPreviewRequiredForIncluded(true));
            AddCommand(previewButtons, "Required Off", () => SetImportPreviewRequiredForIncluded(false));
            AddCommand(previewButtons, "Apply Import", ApplyImportPreview);
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
            if (serviceCombo == null) return;
            var value = ChooseServiceOption();
            if (value == null) return;
            SelectComboByName(serviceCombo, NormalizeServiceOption(value));
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
                        : project.Contracts.FirstOrDefault(c => c.VariableId == variable.Id && c.ServiceId == target.ServiceId && !c.Excluded);
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
                    variable = new VariableDefinitionModel
                    {
                        Id = UniqueVariableId(row.Key),
                        Key = row.Key.Trim().ToUpperInvariant(),
                        DisplayName = row.Key.Trim(),
                        Type = row.Secret ? VariableType.Password : VariableType.String,
                        IsSecret = row.Secret,
                        SortOrder = project.Variables.Count * 10
                    };
                    project.Variables.Add(variable);
                    AutoAssignVariableToMatchingServices(variable);
                }
                else
                {
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
                        UpdatedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    existing.Value = row.NewValue;
                    existing.IsEncrypted = row.Secret;
                    existing.UpdatedAt = DateTime.UtcNow;
                }

                if (target.ServiceId != null)
                {
                    var contract = project.Contracts.FirstOrDefault(c => c.VariableId == variable.Id && c.ServiceId == target.ServiceId);
                    if (contract == null)
                    {
                        project.Contracts.Add(new VariableContractModel
                        {
                            Id = ProjectService.NewId(),
                            VariableId = variable.Id,
                            ServiceId = target.ServiceId,
                            Required = row.Required,
                            SortOrder = project.Contracts.Count * 10
                        });
                    }
                    else
                    {
                        contract.Excluded = false;
                        contract.Required = row.Required;
                    }
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

        private void BuildContractsMatrix()
        {
            contractsMatrix.Columns.Clear();
            contractsMatrix.Rows.Clear();
            contractsMatrix.Columns.Add(new DataGridViewTextBoxColumn { Name = "VariableId", HeaderText = "VariableId", Visible = false });
            contractsMatrix.Columns.Add(new DataGridViewTextBoxColumn { Name = "Group", HeaderText = "Group", ReadOnly = true, FillWeight = 80 });
            contractsMatrix.Columns.Add(new DataGridViewTextBoxColumn { Name = "Variable", HeaderText = "Variable", ReadOnly = true, FillWeight = 160 });

            foreach (var service in project.Services.OrderBy(s => s.SortOrder))
            {
                contractsMatrix.Columns.Add(new DataGridViewCheckBoxColumn
                {
                    Name = "svc_" + service.Id,
                    HeaderText = service.Name,
                    Tag = service.Id,
                    FillWeight = 70
                });
            }

            foreach (var variable in project.Variables.OrderBy(v => v.GroupName).ThenBy(v => v.SortOrder).ThenBy(v => v.Key))
            {
                var rowIndex = contractsMatrix.Rows.Add();
                var row = contractsMatrix.Rows[rowIndex];
                row.Cells["VariableId"].Value = variable.Id;
                row.Cells["Group"].Value = string.IsNullOrWhiteSpace(variable.GroupName) ? "(none)" : variable.GroupName;
                row.Cells["Variable"].Value = variable.Key;
                foreach (var service in project.Services)
                {
                    row.Cells["svc_" + service.Id].Value = IsVariableUsedByService(variable.Id, service.Id);
                }
            }
        }

        private void ApplyContractsMatrix()
        {
            if (contractsMatrix == null) return;
            var previousContracts = project.Contracts.ToList();
            project.Contracts.Clear();
            foreach (DataGridViewRow row in contractsMatrix.Rows)
            {
                var variableId = Convert.ToString(row.Cells["VariableId"].Value);
                foreach (DataGridViewColumn column in contractsMatrix.Columns)
                {
                    if (!(column is DataGridViewCheckBoxColumn)) continue;
                    var serviceId = Convert.ToString(column.Tag);
                    var enabled = Convert.ToBoolean(row.Cells[column.Name].Value ?? false);
                    if (enabled)
                    {
                        var previous = previousContracts.FirstOrDefault(c => c.VariableId == variableId && c.ServiceId == serviceId && !c.Excluded);
                        if (previous != null)
                        {
                            previous.Excluded = false;
                            project.Contracts.Add(previous);
                        }
                        else if (!HasGlobalValue(variableId))
                        {
                            project.Contracts.Add(new VariableContractModel
                            {
                                Id = ProjectService.NewId(),
                                VariableId = variableId,
                                ServiceId = serviceId,
                                Required = true,
                                SortOrder = project.Contracts.Count * 10
                            });
                        }
                    }
                    else if (HasGlobalValue(variableId))
                    {
                        project.Contracts.Add(new VariableContractModel
                        {
                            Id = ProjectService.NewId(),
                            VariableId = variableId,
                            ServiceId = serviceId,
                            Excluded = true,
                            Required = false,
                            SortOrder = project.Contracts.Count * 10
                        });
                    }
                }
            }
            Changed();
        }

        private void AutoAssignAllPrefixes()
        {
            foreach (var service in project.Services)
            {
                AutoAssignVariablesToService(service);
            }
            Changed();
        }

        private void RenderValidationView()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            var validate = AddCommand(buttons, "Validate All", () => RefreshValidationResults(null));
            buttons.Controls.Add(new Label
            {
                Text = "Checks duplicate names, broken references, required values and blank required values for every service/environment pair",
                AutoSize = true,
                Padding = new Padding(12, 7, 0, 0),
                Font = new Font(Font, FontStyle.Bold)
            });
            var summary = new Label { Name = "ValidationSummary", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            mainGrid = BuildGrid();

            root.Controls.Add(buttons, 0, 0);
            root.Controls.Add(summary, 0, 1);
            root.Controls.Add(mainGrid, 0, 2);
            contentPanel.Controls.Add(root);
            RefreshValidationResults(summary);
            AttachColumnWidthPersistence(mainGrid, "ValidationResults");
            RestoreColumnWidths(mainGrid, "ValidationResults");
        }

        private void RefreshValidationResults(Label summaryLabel)
        {
            var results = validationService.Validate(project).ToList();
            if (mainGrid != null)
            {
                mainGrid.DataSource = results.Select(r => new
                {
                    r.Severity,
                    Check = r.Code,
                    Service = ServiceName(r.ServiceId),
                    Environment = EnvironmentName(r.EnvironmentId),
                    Variable = VariableKey(r.VariableId),
                    r.Message
                }).ToList();
            }

            if (summaryLabel == null)
            {
                summaryLabel = contentPanel.Controls.Find("ValidationSummary", true).FirstOrDefault() as Label;
            }
            if (summaryLabel != null)
            {
                var errors = results.Count(r => r.Severity == ValidationSeverity.Error);
                var warnings = results.Count(r => r.Severity == ValidationSeverity.Warning);
                summaryLabel.Text = results.Count == 0
                    ? "Validation passed."
                    : $"Validation found {errors} error(s), {warnings} warning(s).";
                summaryLabel.ForeColor = errors > 0 ? Color.Red : SystemColors.ControlText;
            }
        }

        private Control BuildCrudView(string title, Action add, Action edit, Action delete)
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            AddCommand(buttons, "Add", add);
            if (edit != null) AddCommand(buttons, "Edit", edit);
            if (delete != null) AddCommand(buttons, "Delete", delete);
            buttons.Controls.Add(new Label { Text = title, AutoSize = true, Padding = new Padding(12, 7, 0, 0), Font = new Font(Font, FontStyle.Bold) });
            mainGrid = BuildGrid();
            root.Controls.Add(buttons, 0, 0);
            root.Controls.Add(mainGrid, 0, 1);
            return root;
        }

        private static DataGridView BuildGrid()
        {
            return new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false
            };
        }

        private void AttachColumnWidthPersistence(DataGridView grid, string gridKey)
        {
            grid.ColumnWidthChanged += (s, e) =>
            {
                if (suppressColumnWidthPersistence) return;
                SaveColumnWidths(grid, gridKey);
            };
        }

        private void RestoreColumnWidths(DataGridView grid, string gridKey)
        {
            var widths = recentProjectsService.LoadGridColumnWidths(gridKey);
            if (widths.Count == 0)
            {
                widths = DefaultColumnWidths(gridKey);
            }
            if (widths.Count == 0) return;

            suppressColumnWidthPersistence = true;
            try
            {
                grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
                foreach (DataGridViewColumn column in grid.Columns)
                {
                    if (widths.TryGetValue(column.Name, out var width) && width > 20)
                    {
                        column.Width = width;
                    }
                }
            }
            finally
            {
                suppressColumnWidthPersistence = false;
            }
        }

        private void SaveColumnWidths(DataGridView grid, string gridKey)
        {
            if (suppressColumnWidthPersistence || grid.Columns.Count == 0) return;
            recentProjectsService.SaveGridColumnWidths(
                gridKey,
                grid.Columns
                    .Cast<DataGridViewColumn>()
                    .Where(c => !string.IsNullOrWhiteSpace(c.Name) && c.Visible)
                    .ToDictionary(c => c.Name, c => c.Width));
        }

        private static Dictionary<string, int> DefaultColumnWidths(string gridKey)
        {
            if (gridKey != "Variables")
            {
                return new Dictionary<string, int>();
            }

            return new Dictionary<string, int>
            {
                { "Validation", 35 },
                { "Key", 300 },
                { "Secret", 56 },
                { "Required", 60 },
                { "AllowNull", 75 },
                { "Global", 220 },
                { "Environment", 170 },
                { "Service", 170 },
                { "ServiceEnvironment", 185 },
                { "Effective", 245 },
                { "Source", 100 }
            };
        }

        private DataGridView BuildVariablesGrid(TableLayoutPanel root)
        {
            var grid = BuildGrid();
            grid.ReadOnly = false;
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "Id", Visible = false, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Validation", HeaderText = "!", ReadOnly = true, FillWeight = 35 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Key", HeaderText = "Key", ReadOnly = true, FillWeight = 150 });
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Secret", HeaderText = "Secret", FillWeight = 55 });
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Required", HeaderText = "Required", FillWeight = 65 });
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "AllowNull", HeaderText = "Allow Null", FillWeight = 75 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Global", HeaderText = "Global", ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Environment", HeaderText = "Environment", ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Service", HeaderText = "Service", ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ServiceEnvironment", HeaderText = "ServiceEnvironment", ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Effective", HeaderText = "Effective", ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Source", HeaderText = "Source", ReadOnly = true, FillWeight = 70 });
            foreach (DataGridViewColumn column in grid.Columns)
            {
                if (column.Name != "Id")
                {
                    column.HeaderText = FilterHeaderText(column.Name, column.HeaderText);
                }
            }
            grid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (grid.IsCurrentCellDirty)
                {
                    grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }
            };
            grid.ColumnHeaderMouseClick += (s, e) => ShowVariableColumnFilterMenu(grid, e.ColumnIndex);
            grid.CellValueChanged += (s, e) => ApplyVariableGridCheckboxChange(grid, root, e.RowIndex, e.ColumnIndex);
            grid.DataError += (s, e) => { e.ThrowException = false; };
            AttachColumnWidthPersistence(grid, "Variables");
            RestoreColumnWidths(grid, "Variables");
            return grid;
        }

        private void RefreshVariableGrid()
        {
            if (mainGrid == null || project == null || currentView != "Variables") return;
            var selectedId = SelectedId();
            var firstDisplayedScrollingRowIndex = mainGrid.FirstDisplayedScrollingRowIndex >= 0 ? mainGrid.FirstDisplayedScrollingRowIndex : 0;
            var env = SelectedEnvironment();
            var svc = SelectedService();
            var search = searchBox?.Text?.Trim();
            var validationByVariable = validationService.Validate(project)
                .Where(r => !string.IsNullOrWhiteSpace(r.VariableId))
                .GroupBy(r => r.VariableId)
                .ToDictionary(g => g.Key, g => g.ToList());
            mainGrid.Rows.Clear();
            foreach (var v in project.Variables.OrderBy(v => v.SortOrder).Where(v => string.IsNullOrWhiteSpace(search) || v.Key.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                var effective = BuildDisplayEffective(svc?.Id, env?.Id).FirstOrDefault(x => x.Variable.Id == v.Id);
                var selected = FindSelectedLayerValue(v.Id);
                var required = svc != null && project.Contracts.Any(c => c.ServiceId == svc.Id && c.VariableId == v.Id && !c.Excluded && c.Required);
                var globalValue = FindValue(v.Id, ValueScope.Global, null, null);
                var environmentValue = FindValue(v.Id, ValueScope.Environment, null, env?.Id);
                var serviceValue = FindValue(v.Id, ValueScope.Service, svc?.Id, null);
                var serviceEnvironmentValue = FindValue(v.Id, ValueScope.ServiceEnvironment, svc?.Id, env?.Id);
                var effectiveValue = DisplayValue(v, effective?.Value ?? selected.Value);
                var source = effective?.SourceScope.ToString() ?? selected.Source;
                if (!PassesVariableColumnFilters(v, required, globalValue, environmentValue, serviceValue, serviceEnvironmentValue, effectiveValue, source))
                {
                    continue;
                }

                var rowIndex = mainGrid.Rows.Add(
                    v.Id,
                    ValidationIndicator(v.Id, validationByVariable),
                    v.Key,
                    v.IsSecret,
                    required,
                    v.AllowNull,
                    globalValue,
                    environmentValue,
                    serviceValue,
                    serviceEnvironmentValue,
                    effectiveValue,
                    source);
                var row = mainGrid.Rows[rowIndex];
                ApplyValidationCellStyle(row.Cells["Validation"], v.Id, validationByVariable);
                if (svc == null)
                {
                    row.Cells["Required"].ReadOnly = true;
                    row.Cells["Required"].Style.BackColor = SystemColors.ControlLight;
                }
            }
            if (mainGrid.Rows.Count == 0)
            {
                mainGrid.ClearSelection();
                mainGrid.CurrentCell = null;
            }
            else
            {
                RestoreVariableGridSelection(selectedId, firstDisplayedScrollingRowIndex);
            }
        }

        private void RefreshVariableDisplayPreservingSelection(TableLayoutPanel root)
        {
            var selectedId = SelectedId();
            RefreshVariableGrid();
            RestoreVariableGridSelection(selectedId, -1);
            RefreshVariableDetails(root);
        }

        private void RestoreVariableGridSelection(string variableId, int fallbackScrollRowIndex)
        {
            if (mainGrid == null || mainGrid.Rows.Count == 0) return;
            var targetRow = mainGrid.Rows.Cast<DataGridViewRow>().FirstOrDefault(row => Convert.ToString(row.Cells["Id"].Value) == variableId)
                ?? mainGrid.Rows[0];
            mainGrid.ClearSelection();
            targetRow.Selected = true;
            mainGrid.CurrentCell = targetRow.Cells["Key"];
            if (fallbackScrollRowIndex >= 0 && fallbackScrollRowIndex < mainGrid.Rows.Count)
            {
                mainGrid.FirstDisplayedScrollingRowIndex = fallbackScrollRowIndex;
            }
            else if (targetRow.Index >= 0)
            {
                mainGrid.FirstDisplayedScrollingRowIndex = Math.Max(0, Math.Min(targetRow.Index, mainGrid.Rows.Count - 1));
            }
        }

        private static string ValidationIndicator(string variableId, Dictionary<string, List<ValidationResult>> validationByVariable)
        {
            if (!validationByVariable.TryGetValue(variableId, out var results)) return string.Empty;
            return results.Any(r => r.Severity == ValidationSeverity.Error) ? "!" : results.Any(r => r.Severity == ValidationSeverity.Warning) ? "!" : string.Empty;
        }

        private static void ApplyValidationCellStyle(DataGridViewCell cell, string variableId, Dictionary<string, List<ValidationResult>> validationByVariable)
        {
            cell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            if (!validationByVariable.TryGetValue(variableId, out var results) || results.Count == 0)
            {
                cell.ToolTipText = string.Empty;
                return;
            }

            var hasError = results.Any(r => r.Severity == ValidationSeverity.Error);
            cell.Style.BackColor = hasError ? Color.Red : Color.Gold;
            cell.Style.ForeColor = hasError ? Color.White : Color.Black;
            cell.Style.Font = new Font(cell.DataGridView.Font, FontStyle.Bold);
            cell.ToolTipText = string.Join(Environment.NewLine, results.Select(r => $"{r.Severity}: {r.Code}: {r.Message}").Distinct());
        }

        private bool PassesVariableColumnFilters(
            VariableDefinitionModel variable,
            bool required,
            string globalValue,
            string environmentValue,
            string serviceValue,
            string serviceEnvironmentValue,
            string effectiveValue,
            string source)
        {
            return PassesTextFilter("Key", variable.Key) &&
                PassesBoolFilter("Secret", variable.IsSecret) &&
                PassesBoolFilter("Required", required) &&
                PassesTextFilter("Global", globalValue) &&
                PassesTextFilter("Environment", environmentValue) &&
                PassesTextFilter("Service", serviceValue) &&
                PassesTextFilter("ServiceEnvironment", serviceEnvironmentValue) &&
                PassesTextFilter("Effective", effectiveValue) &&
                PassesTextFilter("Source", source);
        }

        private bool PassesTextFilter(string columnName, string value)
        {
            if (!variableColumnFilters.TryGetValue(columnName, out var filter)) return true;
            filter = filter?.Trim();
            return string.IsNullOrWhiteSpace(filter) || (value ?? string.Empty).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool PassesBoolFilter(string columnName, bool value)
        {
            if (!variableColumnFilters.TryGetValue(columnName, out var filter)) return true;
            if (string.IsNullOrWhiteSpace(filter) || filter == "All") return true;
            return (filter == "Yes") == value;
        }

        private void ShowVariableColumnFilterMenu(DataGridView grid, int columnIndex)
        {
            if (columnIndex < 0 || columnIndex >= grid.Columns.Count) return;
            var column = grid.Columns[columnIndex];
            if (column.Name == "Id") return;

            var menu = new ContextMenuStrip();
            if (column.Name == "Secret" || column.Name == "Required")
            {
                AddBoolFilterMenuItem(menu, column.Name, "All");
                AddBoolFilterMenuItem(menu, column.Name, "Yes");
                AddBoolFilterMenuItem(menu, column.Name, "No");
            }
            else
            {
                var textBox = new ToolStripTextBox { Text = GetVariableColumnFilter(column.Name), Width = 220 };
                textBox.KeyDown += (s, e) =>
                {
                    if (e.KeyCode != Keys.Enter) return;
                    SetVariableColumnFilter(column.Name, textBox.Text);
                    menu.Close();
                };
                menu.Items.Add(new ToolStripLabel("Contains:"));
                menu.Items.Add(textBox);
                menu.Items.Add("Apply", null, (s, e) => SetVariableColumnFilter(column.Name, textBox.Text));
            }
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Clear Filter", null, (s, e) => SetVariableColumnFilter(column.Name, null));
            menu.Opening += (s, e) =>
            {
                if (menu.Items.OfType<ToolStripTextBox>().FirstOrDefault() is ToolStripTextBox tb)
                {
                    tb.Focus();
                    tb.SelectAll();
                }
            };
            var headerRectangle = grid.GetCellDisplayRectangle(columnIndex, -1, true);
            menu.Show(grid, headerRectangle.Left, headerRectangle.Bottom);
        }

        private void AddBoolFilterMenuItem(ContextMenuStrip menu, string columnName, string value)
        {
            var current = GetVariableColumnFilter(columnName);
            menu.Items.Add(new ToolStripMenuItem(value, null, (s, e) => SetVariableColumnFilter(columnName, value))
            {
                Checked = string.Equals(current, value, StringComparison.OrdinalIgnoreCase) ||
                    (string.IsNullOrWhiteSpace(current) && value == "All")
            });
        }

        private string GetVariableColumnFilter(string columnName)
        {
            return variableColumnFilters.TryGetValue(columnName, out var value) ? value : null;
        }

        private void SetVariableColumnFilter(string columnName, string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "All")
            {
                variableColumnFilters.Remove(columnName);
            }
            else
            {
                variableColumnFilters[columnName] = value;
            }
            UpdateVariableColumnHeaders();
            RefreshVariableGrid();
        }

        private void UpdateVariableColumnHeaders()
        {
            if (mainGrid == null) return;
            foreach (DataGridViewColumn column in mainGrid.Columns)
            {
                if (column.Name == "Id") continue;
                column.HeaderText = FilterHeaderText(column.Name, BaseVariableHeaderText(column.Name));
            }
        }

        private string FilterHeaderText(string columnName, string fallback)
        {
            var marker = variableColumnFilters.ContainsKey(columnName) ? "*" : string.Empty;
            return $"{BaseVariableHeaderText(columnName, fallback)} {marker}▼";
        }

        private static string BaseVariableHeaderText(string columnName, string fallback = null)
        {
            if (columnName == "ServiceEnvironment") return "ServiceEnvironment";
            return fallback ?? columnName;
        }

        private void ApplyVariableGridCheckboxChange(DataGridView grid, TableLayoutPanel root, int rowIndex, int columnIndex)
        {
            if (rowIndex < 0 || columnIndex < 0 || rowIndex >= grid.Rows.Count) return;
            var columnName = grid.Columns[columnIndex].Name;
            if (columnName != "Secret" && columnName != "Required" && columnName != "AllowNull") return;

            var variableId = Convert.ToString(grid.Rows[rowIndex].Cells["Id"].Value);
            var variable = project.Variables.FirstOrDefault(v => v.Id == variableId);
            if (variable == null) return;

            var enabled = Convert.ToBoolean(grid.Rows[rowIndex].Cells[columnName].Value ?? false);
            if (columnName == "Secret")
            {
                variable.IsSecret = enabled;
                variable.Type = enabled ? VariableType.Password : VariableType.String;
            }
            else if (columnName == "AllowNull")
            {
                variable.AllowNull = enabled;
            }
            else
            {
                var service = SelectedService();
                if (service == null)
                {
                    grid.Rows[rowIndex].Cells[columnName].Value = false;
                    return;
                }

                var contract = project.Contracts.FirstOrDefault(c => c.VariableId == variable.Id && c.ServiceId == service.Id);
                if (enabled)
                {
                    if (contract == null)
                    {
                        project.Contracts.Add(new VariableContractModel
                        {
                            Id = ProjectService.NewId(),
                            VariableId = variable.Id,
                            ServiceId = service.Id,
                            Required = true,
                            SortOrder = project.Contracts.Count * 10
                        });
                    }
                    else
                    {
                        contract.Excluded = false;
                        contract.Required = true;
                    }
                }
                else if (contract != null && !contract.Excluded)
                {
                    contract.Required = false;
                }
            }

            modified = true;
            SaveRecoveryBackupIfPossible();
            RefreshVariableDetails(root);
            RefreshStatus();
        }

        private void AddEnvironment()
        {
            var env = new EnvironmentModel
            {
                Id = "environment-" + (project.Environments.Count + 1),
                Name = "dev",
                DisplayName = "dev",
                SortOrder = project.Environments.Count * 10,
                IsActive = true
            };
            if (!ShowEnvironmentCard(env, true)) return;
            env.Id = UniqueEnvironmentId(Slug(env.Name));
            project.Environments.Add(env);
            Changed();
        }

        private void EditEnvironment()
        {
            var env = SelectedById(project.Environments, e => e.Id);
            if (env == null) return;
            if (ShowEnvironmentCard(env, false)) Changed();
        }

        private void DeleteEnvironment()
        {
            var env = SelectedById(project.Environments, e => e.Id);
            if (env == null || !ConfirmDelete(env.Name)) return;
            project.Environments.Remove(env);
            project.Values.RemoveAll(v => v.EnvironmentId == env.Id);
            Changed();
        }

        private void AddService()
        {
            var service = new ServiceModel
            {
                Id = "service-" + (project.Services.Count + 1),
                Name = "backend",
                DisplayName = "backend",
                OutputFolder = "backend",
                DefaultPrefix = "BACKEND_",
                SortOrder = project.Services.Count * 10,
                IsActive = true
            };
            if (!ShowServiceCard(service, true)) return;
            service.Id = UniqueServiceId(Slug(service.Name));
            project.Services.Add(service);
            AutoAssignVariablesToService(service);
            Changed();
        }

        private void EditService()
        {
            var service = SelectedById(project.Services, s => s.Id);
            if (service == null) return;
            if (!ShowServiceCard(service, false)) return;
            AutoAssignVariablesToService(service);
            Changed();
        }

        private void DeleteService()
        {
            var service = SelectedById(project.Services, s => s.Id);
            if (service == null || !ConfirmDelete(service.Name)) return;
            project.Services.Remove(service);
            project.Contracts.RemoveAll(c => c.ServiceId == service.Id);
            project.Values.RemoveAll(v => v.ServiceId == service.Id);
            Changed();
        }

        private bool ShowServiceCard(ServiceModel service, bool isNew)
        {
            using (var dialog = new Form
            {
                Text = isNew ? "Add Service" : "Service Card",
                Width = 560,
                Height = 520,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false,
                MaximizeBox = false
            })
            {
                var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Padding = new Padding(10) };
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
                var form = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 12 };
                form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
                form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                for (var i = 0; i < 12; i++) form.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

                var idBox = AddCardTextBox(form, "Id:", 0, service.Id, true);
                var nameBox = AddCardTextBox(form, "Name:", 1, service.Name, false);
                var displayBox = AddCardTextBox(form, "Display name:", 2, service.DisplayName, false);
                var outputFolderBox = AddCardTextBox(form, "Output folder:", 3, service.OutputFolder, false);
                var prefixBox = AddCardTextBox(form, "Default prefix:", 4, service.DefaultPrefix, false);
                var activeBox = AddCardCheckBox(form, "Active:", 5, service.IsActive);
                var configBox = AddCardTextBox(form, "CONFIG name:", 6, service.ConfigName, false);
                var tomlBox = AddCardTextBox(form, "TOML name:", 7, service.TomlName, false);
                var yamlBox = AddCardTextBox(form, "YAML name:", 8, service.YamlName, false);
                var xmlBox = AddCardTextBox(form, "XML name:", 9, service.XmlName, false);
                var jsonBox = AddCardTextBox(form, "JSON name:", 10, service.JsonName, false);
                var descriptionBox = AddCardTextBox(form, "Description:", 11, service.Description, false);

                nameBox.TextChanged += (s, e) =>
                {
                    if (string.IsNullOrWhiteSpace(displayBox.Text)) displayBox.Text = nameBox.Text;
                    if (string.IsNullOrWhiteSpace(outputFolderBox.Text)) outputFolderBox.Text = Slug(nameBox.Text);
                    if (string.IsNullOrWhiteSpace(prefixBox.Text)) prefixBox.Text = Slug(nameBox.Text).ToUpperInvariant() + "_";
                };

                var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
                var ok = new Button { Text = "OK", Width = 90, Height = 28, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Cancel", Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
                bottom.Controls.Add(cancel);
                bottom.Controls.Add(ok);
                root.Controls.Add(form, 0, 0);
                root.Controls.Add(bottom, 0, 1);
                dialog.Controls.Add(root);
                dialog.AcceptButton = ok;
                dialog.CancelButton = cancel;

                while (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    var name = nameBox.Text.Trim();
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        MessageBox.Show(this, "Name is required.", dialog.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        continue;
                    }
                    if (project.Services.Any(s => s != service && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        MessageBox.Show(this, "Service name already exists.", dialog.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        continue;
                    }

                    service.Name = name;
                    service.DisplayName = DefaultIfBlank(displayBox.Text, name);
                    service.OutputFolder = DefaultIfBlank(outputFolderBox.Text, Slug(name));
                    service.DefaultPrefix = prefixBox.Text;
                    service.IsActive = activeBox.Checked;
                    service.ConfigName = configBox.Text;
                    service.TomlName = tomlBox.Text;
                    service.YamlName = yamlBox.Text;
                    service.XmlName = xmlBox.Text;
                    service.JsonName = jsonBox.Text;
                    service.Description = descriptionBox.Text;
                    return true;
                }
            }

            return false;
        }

        private bool ShowEnvironmentCard(EnvironmentModel environment, bool isNew)
        {
            using (var dialog = new Form
            {
                Text = isNew ? "Add Environment" : "Environment Card",
                Width = 560,
                Height = 430,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false,
                MaximizeBox = false
            })
            {
                var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Padding = new Padding(10) };
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
                var form = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 9 };
                form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
                form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                for (var i = 0; i < 9; i++) form.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

                AddCardTextBox(form, "Id:", 0, environment.Id, true);
                var nameBox = AddCardTextBox(form, "Name:", 1, environment.Name, false);
                var displayBox = AddCardTextBox(form, "Display name:", 2, environment.DisplayName, false);
                var activeBox = AddCardCheckBox(form, "Active:", 3, environment.IsActive);
                var configBox = AddCardTextBox(form, "CONFIG name:", 4, environment.ConfigName, false);
                var tomlBox = AddCardTextBox(form, "TOML name:", 5, environment.TomlName, false);
                var yamlBox = AddCardTextBox(form, "YAML name:", 6, environment.YamlName, false);
                var xmlBox = AddCardTextBox(form, "XML name:", 7, environment.XmlName, false);
                var jsonBox = AddCardTextBox(form, "JSON name:", 8, environment.JsonName, false);

                nameBox.TextChanged += (s, e) =>
                {
                    if (string.IsNullOrWhiteSpace(displayBox.Text)) displayBox.Text = nameBox.Text;
                };

                var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
                var ok = new Button { Text = "OK", Width = 90, Height = 28, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Cancel", Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
                bottom.Controls.Add(cancel);
                bottom.Controls.Add(ok);
                root.Controls.Add(form, 0, 0);
                root.Controls.Add(bottom, 0, 1);
                dialog.Controls.Add(root);
                dialog.AcceptButton = ok;
                dialog.CancelButton = cancel;

                while (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    var name = nameBox.Text.Trim();
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        MessageBox.Show(this, "Name is required.", dialog.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        continue;
                    }
                    if (project.Environments.Any(e => e != environment && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        MessageBox.Show(this, "Environment name already exists.", dialog.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        continue;
                    }

                    environment.Name = name;
                    environment.DisplayName = DefaultIfBlank(displayBox.Text, name);
                    environment.IsActive = activeBox.Checked;
                    environment.ConfigName = configBox.Text;
                    environment.TomlName = tomlBox.Text;
                    environment.YamlName = yamlBox.Text;
                    environment.XmlName = xmlBox.Text;
                    environment.JsonName = jsonBox.Text;
                    return true;
                }
            }

            return false;
        }

        private static TextBox AddCardTextBox(TableLayoutPanel form, string label, int row, string value, bool readOnly)
        {
            form.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
            var box = new TextBox { Dock = DockStyle.Fill, Text = value ?? string.Empty, ReadOnly = readOnly };
            form.Controls.Add(box, 1, row);
            return box;
        }

        private static CheckBox AddCardCheckBox(TableLayoutPanel form, string label, int row, bool value)
        {
            form.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
            var box = new CheckBox { Dock = DockStyle.Left, Checked = value, AutoSize = true };
            form.Controls.Add(box, 1, row);
            return box;
        }

        private string UniqueServiceId(string baseId)
        {
            baseId = string.IsNullOrWhiteSpace(baseId) ? "service" : baseId;
            var id = baseId;
            var suffix = 2;
            while (project.Services.Any(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase))) id = baseId + "-" + suffix++;
            return id;
        }

        private string UniqueEnvironmentId(string baseId)
        {
            baseId = string.IsNullOrWhiteSpace(baseId) ? "environment" : baseId;
            var id = baseId;
            var suffix = 2;
            while (project.Environments.Any(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase))) id = baseId + "-" + suffix++;
            return id;
        }

        private void AddVariable()
        {
            var key = PromptDialog.Show(this, "Add Variable", "Key:", "DATABASE_HOST");
            if (string.IsNullOrWhiteSpace(key)) return;
            var isSecret = MessageBox.Show(this, "Secret variable?", "Add Variable", MessageBoxButtons.YesNo) == DialogResult.Yes;
            var variable = new VariableDefinitionModel
            {
                Id = Slug(key),
                Key = key.Trim().ToUpperInvariant(),
                DisplayName = key.Trim(),
                Type = isSecret ? VariableType.Password : VariableType.String,
                IsSecret = isSecret,
                SortOrder = project.Variables.Count * 10
            };
            project.Variables.Add(variable);
            AutoAssignVariableToMatchingServices(variable);
            Changed();
        }

        private void EditVariable()
        {
            var variable = GetSelectedVariable();
            if (variable == null) return;
            var key = PromptDialog.Show(this, "Edit Variable", "Key:", variable.Key);
            if (string.IsNullOrWhiteSpace(key)) return;
            variable.Key = key.Trim().ToUpperInvariant();
            variable.DisplayName = key.Trim();
            AutoAssignVariableToMatchingServices(variable);
            Changed();
        }

        private void DeleteVariable()
        {
            var variable = GetSelectedVariable();
            if (variable == null || !ConfirmDelete(variable.Key)) return;
            project.Variables.Remove(variable);
            project.Values.RemoveAll(v => v.VariableId == variable.Id);
            project.Contracts.RemoveAll(c => c.VariableId == variable.Id);
            Changed();
        }

        private void SetValue()
        {
            var variable = GetSelectedVariable();
            if (variable == null) return;
            var target = CurrentValueTarget();
            var existing = project.Values.LastOrDefault(v =>
                v.VariableId == variable.Id &&
                v.Scope == target.Scope &&
                v.EnvironmentId == target.EnvironmentId &&
                v.ServiceId == target.ServiceId);
            var value = PromptDialog.Show(this, "Set Value", $"{variable.Key} value for {target.Label}:", existing?.Value ?? string.Empty);
            if (value == null) return;
            if (existing == null)
            {
                project.Values.Add(new VariableValueModel
                {
                    Id = ProjectService.NewId(),
                    VariableId = variable.Id,
                    Scope = target.Scope,
                    EnvironmentId = target.EnvironmentId,
                    ServiceId = target.ServiceId,
                    Value = value,
                    IsEncrypted = variable.IsSecret,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            else
            {
                existing.Value = value;
                existing.UpdatedAt = DateTime.UtcNow;
            }
            Changed();
        }

        private void DeleteValue()
        {
            var variable = GetSelectedVariable();
            if (variable == null) return;
            var target = CurrentValueTarget();
            project.Values.RemoveAll(v =>
                v.VariableId == variable.Id &&
                v.Scope == target.Scope &&
                v.EnvironmentId == target.EnvironmentId &&
                v.ServiceId == target.ServiceId);
            Changed();
        }

        private void AssignServices()
        {
            var variable = GetSelectedVariable();
            if (variable == null)
            {
                MessageBox.Show(this, "Select a variable on Variables screen first.", "Contracts");
                return;
            }
            using (var dialog = new CheckedListForm("Used By Services", project.Services.Select(s => s.Name).ToArray(), project.Services.Select(s => IsVariableUsedByService(variable.Id, s.Id)).ToArray()))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                project.Contracts.RemoveAll(c => c.VariableId == variable.Id);
                for (var i = 0; i < project.Services.Count; i++)
                {
                    if (dialog.Checked[i])
                    {
                        if (!HasGlobalValue(variable.Id))
                        {
                            project.Contracts.Add(new VariableContractModel { Id = ProjectService.NewId(), VariableId = variable.Id, ServiceId = project.Services[i].Id, Required = true });
                        }
                    }
                    else if (HasGlobalValue(variable.Id))
                    {
                        project.Contracts.Add(new VariableContractModel { Id = ProjectService.NewId(), VariableId = variable.Id, ServiceId = project.Services[i].Id, Excluded = true, Required = false });
                    }
                }
                Changed();
            }
        }

        private void DeleteContract()
        {
            var id = SelectedId();
            var contract = project.Contracts.FirstOrDefault(c => c.Id == id);
            if (contract == null || !ConfirmDelete("contract")) return;
            project.Contracts.Remove(contract);
            Changed();
        }

        private void AutoAssignVariableToMatchingServices(VariableDefinitionModel variable)
        {
            foreach (var service in project.Services)
            {
                if (!MatchesServicePrefix(variable, service)) continue;
                var existing = project.Contracts.FirstOrDefault(c => c.VariableId == variable.Id && c.ServiceId == service.Id);
                if (existing != null)
                {
                    existing.Excluded = false;
                }
                else if (!HasGlobalValue(variable.Id))
                {
                    project.Contracts.Add(new VariableContractModel
                    {
                        Id = ProjectService.NewId(),
                        VariableId = variable.Id,
                        ServiceId = service.Id,
                        Required = true,
                        SortOrder = project.Contracts.Count * 10
                    });
                }
            }
        }

        private bool IsVariableUsedByService(string variableId, string serviceId)
        {
            var contract = project.Contracts.FirstOrDefault(c => c.VariableId == variableId && c.ServiceId == serviceId);
            if (contract != null) return !contract.Excluded;
            return HasGlobalValue(variableId);
        }

        private bool HasGlobalValue(string variableId)
        {
            return project.Values.Any(v => v.VariableId == variableId && v.Scope == ValueScope.Global && v.ServiceId == null && v.EnvironmentId == null);
        }

        private void AutoAssignVariablesToService(ServiceModel service)
        {
            foreach (var variable in project.Variables.Where(v => MatchesServicePrefix(v, service)))
            {
                var existing = project.Contracts.FirstOrDefault(c => c.VariableId == variable.Id && c.ServiceId == service.Id);
                if (existing != null)
                {
                    existing.Excluded = false;
                }
                else if (!HasGlobalValue(variable.Id))
                {
                    project.Contracts.Add(new VariableContractModel
                    {
                        Id = ProjectService.NewId(),
                        VariableId = variable.Id,
                        ServiceId = service.Id,
                        Required = true,
                        SortOrder = project.Contracts.Count * 10
                    });
                }
            }
        }

        private static bool MatchesServicePrefix(VariableDefinitionModel variable, ServiceModel service)
        {
            return !string.IsNullOrWhiteSpace(service.DefaultPrefix)
                && !string.IsNullOrWhiteSpace(variable.Key)
                && variable.Key.StartsWith(service.DefaultPrefix.Trim().ToUpperInvariant(), StringComparison.OrdinalIgnoreCase);
        }

        private ValueScope? ChooseScope()
        {
            using (var dialog = new ScopeDialog())
            {
                return dialog.ShowDialog(this) == DialogResult.OK ? dialog.SelectedScope : (ValueScope?)null;
            }
        }

        private void ValidateAll()
        {
            var results = validationService.Validate(project);
            MessageBox.Show(
                this,
                results.Any(r => r.Severity == ValidationSeverity.Error)
                    ? string.Join(Environment.NewLine, results.Select(r => $"{r.Severity}: {r.Code}: {VariableKey(r.VariableId)} {ServiceName(r.ServiceId)} {EnvironmentName(r.EnvironmentId)} - {r.Message}").Take(20))
                    : "Validation passed.",
                "Validation");
        }

        private VariableDefinitionModel GetSelectedVariable()
        {
            var id = SelectedId();
            return project.Variables.FirstOrDefault(v => v.Id == id);
        }

        private string VariableKey(string variableId)
        {
            if (string.IsNullOrWhiteSpace(variableId)) return string.Empty;
            return project.Variables.FirstOrDefault(v => v.Id == variableId)?.Key ?? variableId;
        }

        private string ServiceName(string serviceId)
        {
            if (string.IsNullOrWhiteSpace(serviceId)) return string.Empty;
            return project.Services.FirstOrDefault(s => s.Id == serviceId)?.Name ?? serviceId;
        }

        private string EnvironmentName(string environmentId)
        {
            if (string.IsNullOrWhiteSpace(environmentId)) return string.Empty;
            return project.Environments.FirstOrDefault(e => e.Id == environmentId)?.Name ?? environmentId;
        }

        private T SelectedById<T>(System.Collections.Generic.IEnumerable<T> items, Func<T, string> idSelector)
        {
            var id = SelectedId();
            return items.FirstOrDefault(x => idSelector(x) == id);
        }

        private string SelectedId()
        {
            if (mainGrid?.CurrentRow == null || !mainGrid.Columns.Contains("Id")) return null;
            return Convert.ToString(mainGrid.CurrentRow.Cells["Id"].Value);
        }

        private string FindValue(string variableId, ValueScope scope, string serviceId, string environmentId)
        {
            var value = project.Values.LastOrDefault(v => v.VariableId == variableId && v.Scope == scope && v.ServiceId == serviceId && v.EnvironmentId == environmentId);
            if (value == null) return "-";
            var variable = project.Variables.First(v => v.Id == variableId);
            return DisplayValue(variable, value.Value);
        }

        private string FindRawValue(string key, ValueScope scope, string environmentId, string serviceId)
        {
            var variable = project.Variables.FirstOrDefault(v => string.Equals(v.Key, key, StringComparison.OrdinalIgnoreCase));
            if (variable == null) return null;
            return project.Values.LastOrDefault(v =>
                v.VariableId == variable.Id &&
                v.Scope == scope &&
                v.EnvironmentId == environmentId &&
                v.ServiceId == serviceId)?.Value;
        }

        private IEnumerable<KeyValuePair<string, string>> ParseConfigFile(string path)
        {
            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                if (line.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
                {
                    line = line.Substring("export ".Length).TrimStart();
                }

                var equalsIndex = line.IndexOf('=');
                if (equalsIndex <= 0) continue;
                var key = line.Substring(0, equalsIndex).Trim();
                var value = line.Substring(equalsIndex + 1).Trim();
                if (key.Length == 0) continue;
                if ((value.StartsWith("\"") && value.EndsWith("\"")) || (value.StartsWith("'") && value.EndsWith("'")))
                {
                    value = value.Substring(1, value.Length - 2);
                }
                yield return new KeyValuePair<string, string>(key.ToUpperInvariant(), value);
            }
        }

        private string InferEnvironmentName(string path)
        {
            var name = Path.GetFileName(path).ToLowerInvariant();
            return project.Environments
                .OrderByDescending(e => e.Name.Length)
                .FirstOrDefault(e => name.Contains(e.Name.ToLowerInvariant()) || name.Contains(e.Id.ToLowerInvariant()))
                ?.Name ?? string.Empty;
        }

        private string InferServiceName(string path)
        {
            var name = Path.GetFileName(path).ToLowerInvariant();
            return project.Services
                .OrderByDescending(s => s.Name.Length)
                .FirstOrDefault(s => name.Contains(s.Name.ToLowerInvariant()) || name.Contains(s.Id.ToLowerInvariant()))
                ?.Name ?? string.Empty;
        }

        private ValueTarget ResolveImportTarget(string environmentName, string serviceName)
        {
            environmentName = NormalizeEnvironmentOption(environmentName);
            serviceName = NormalizeServiceOption(serviceName);
            var environment = project.Environments.FirstOrDefault(e =>
                string.Equals(e.Name, environmentName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(e.Id, environmentName, StringComparison.OrdinalIgnoreCase));
            var service = project.Services.FirstOrDefault(s =>
                string.Equals(s.Name, serviceName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.Id, serviceName, StringComparison.OrdinalIgnoreCase));

            if (environment == null && service == null)
            {
                return new ValueTarget(ValueScope.Global, null, null, "Global / All services");
            }

            if (environment != null && service == null)
            {
                return new ValueTarget(ValueScope.Environment, environment.Id, null, $"Environment: {environment.Name} / All services");
            }

            if (environment == null)
            {
                return new ValueTarget(ValueScope.Service, null, service.Id, $"Global / Service: {service.Name}");
            }

            return new ValueTarget(ValueScope.ServiceEnvironment, environment.Id, service.Id, $"Environment: {environment.Name} / Service: {service.Name}");
        }

        private string NormalizeEnvironmentOption(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || string.Equals(value, GlobalOption, StringComparison.OrdinalIgnoreCase))
            {
                return GlobalOption;
            }

            var env = project.Environments.FirstOrDefault(e =>
                string.Equals(e.Id, value, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(e.Name, value, StringComparison.OrdinalIgnoreCase));
            return env?.Name ?? value;
        }

        private string NormalizeServiceOption(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || string.Equals(value, AllServicesOption, StringComparison.OrdinalIgnoreCase))
            {
                return AllServicesOption;
            }

            var service = project.Services.FirstOrDefault(s =>
                string.Equals(s.Id, value, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.Name, value, StringComparison.OrdinalIgnoreCase));
            return service?.Name ?? value;
        }

        private string UniqueVariableId(string key)
        {
            var baseId = Slug(key);
            var id = baseId;
            var index = 2;
            while (project.Variables.Any(v => v.Id == id))
            {
                id = baseId + "-" + index++;
            }
            return id;
        }

        private static bool IsSecretImportKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;
            var normalized = key.ToUpperInvariant();
            return normalized.Contains("PASSWORD") ||
                normalized.Contains("PASS") ||
                normalized.Contains("SECRET") ||
                normalized.Contains("TOKEN") ||
                normalized.Contains("API_KEY") ||
                normalized.EndsWith("_KEY", StringComparison.OrdinalIgnoreCase);
        }

        private static string Mask(VariableDefinitionModel variable, string value)
        {
            if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
            return variable.IsSecret ? "********" : value;
        }

        private string DisplayValue(VariableDefinitionModel variable, string value)
        {
            if (showSecretsCheckBox?.Checked == true)
            {
                return value ?? string.Empty;
            }

            return Mask(variable, value);
        }

        private static string ScopeEnv(ValueScope scope, EnvironmentModel env)
        {
            return scope == ValueScope.Environment || scope == ValueScope.ServiceEnvironment ? env?.Id : null;
        }

        private static string ScopeSvc(ValueScope scope, ServiceModel svc)
        {
            return scope == ValueScope.Service || scope == ValueScope.ServiceEnvironment ? svc?.Id : null;
        }

        private EnvironmentModel SelectedEnvironment()
        {
            var selected = environmentCombo?.SelectedItem as ScopeSelectorItem;
            return selected?.Id == null ? null : project.Environments.FirstOrDefault(e => e.Id == selected.Id);
        }

        private ServiceModel SelectedService()
        {
            var selected = serviceCombo?.SelectedItem as ScopeSelectorItem;
            return selected?.Id == null ? null : project.Services.FirstOrDefault(s => s.Id == selected.Id);
        }

        private ValueTarget CurrentValueTarget()
        {
            var environment = SelectedEnvironment();
            var service = SelectedService();
            if (environment == null && service == null)
            {
                return new ValueTarget(ValueScope.Global, null, null, "Global / All services");
            }

            if (environment != null && service == null)
            {
                return new ValueTarget(ValueScope.Environment, environment.Id, null, $"Environment: {environment.Name} / All services");
            }

            if (environment == null)
            {
                return new ValueTarget(ValueScope.Service, null, service.Id, $"Global / Service: {service.Name}");
            }

            return new ValueTarget(ValueScope.ServiceEnvironment, environment.Id, service.Id, $"Environment: {environment.Name} / Service: {service.Name}");
        }

        private LayerValue FindSelectedLayerValue(string variableId)
        {
            var target = CurrentValueTarget();
            var value = project.Values.LastOrDefault(v =>
                v.VariableId == variableId &&
                v.Scope == target.Scope &&
                v.EnvironmentId == target.EnvironmentId &&
                v.ServiceId == target.ServiceId);
            return value == null
                ? new LayerValue(null, "Missing")
                : new LayerValue(value.Value, target.Scope.ToString());
        }

        private void Changed()
        {
            modified = true;
            SaveRecoveryBackupIfPossible();
            RenderCurrentView();
        }

        private void SaveRecoveryBackupIfPossible()
        {
            if (project == null || string.IsNullOrWhiteSpace(currentFilePath)) return;

            try
            {
                SaveVaultFile(currentFilePath, true);
            }
            catch (Exception ex)
            {
                if (recoveryBackupWarningShown) return;
                recoveryBackupWarningShown = true;
                MessageBox.Show(
                    this,
                    "Could not write the unsaved backup file.\r\n\r\n" + ex.Message,
                    "Autosave Backup",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private bool SaveVaultFile(string path, bool recoveryBackup)
        {
            var storageProject = PrepareProjectForStorage();
            if (storageProject == null)
            {
                return false;
            }

            if (IsWholeJsonEncryption(storageProject))
            {
                if (!SaveWholeJsonVaultFile(storageProject, path, recoveryBackup))
                {
                    return false;
                }
                return true;
            }

            if (recoveryBackup)
            {
                vaultFileService.SaveRecoveryBackup(storageProject, path);
            }
            else
            {
                vaultFileService.Save(storageProject, path);
            }

            return true;
        }

        private ProjectModel LoadVaultFile(string path, bool recoveryBackup)
        {
            var actualPath = recoveryBackup ? vaultFileService.GetRecoveryBackupPath(path) : path;
            var json = File.ReadAllText(actualPath);
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

            if (json.IndexOf("\"EnvSecured.EncryptedProject.v1\"", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var envelope = serializer.Deserialize<EncryptedProjectFile>(json);
                if (envelope?.Payload == null || envelope.Crypto == null)
                {
                    MessageBox.Show(this, "Encrypted project file is invalid.", "Open Project", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return null;
                }

                var key = UnlockCryptoMetadata(envelope.Crypto);
                if (key == null) return null;
                vaultKey = key;
                var projectJson = cryptoService.DecryptString(envelope.Payload, key);
                var decryptedProject = serializer.Deserialize<ProjectModel>(projectJson);
                decryptedProject.Crypto = envelope.Crypto;
                NormalizeLoadedProjectSettings(decryptedProject);
                DecryptCliExportPolicy(decryptedProject, key);
                return decryptedProject;
            }

            var loadedProject = serializer.Deserialize<ProjectModel>(json);
            NormalizeLoadedProjectSettings(loadedProject);
            if (!UnlockProjectIfNeeded(loadedProject))
            {
                return null;
            }
            return loadedProject;
        }

        private bool SaveWholeJsonVaultFile(ProjectModel storageProject, string path, bool recoveryBackup)
        {
            if (!EnsureVaultKey(storageProject))
            {
                return false;
            }
            project.Crypto = storageProject.Crypto;

            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var projectJson = serializer.Serialize(storageProject);
            var envelope = new EncryptedProjectFile
            {
                Crypto = storageProject.Crypto,
                Payload = cryptoService.EncryptString(projectJson, vaultKey)
            };
            var envelopeJson = serializer.Serialize(envelope);
            var actualPath = recoveryBackup ? vaultFileService.GetRecoveryBackupPath(path) : path;
            SaveTextAtomic(envelopeJson, actualPath, !recoveryBackup);
            return true;
        }

        private static void SaveTextAtomic(string text, string path, bool keepBackup)
        {
            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, text);
            if (File.Exists(path))
            {
                if (keepBackup)
                {
                    File.Copy(path, path + ".bak", true);
                }
                File.Delete(path);
            }
            File.Move(tempPath, path);
        }

        private ProjectModel PrepareProjectForStorage()
        {
            var storageProject = CloneProject(project);
            storageProject.Settings = storageProject.Settings ?? new ProjectSettings();
            storageProject.Settings.CliExportPasswordRequired = project.Settings?.CliExportPasswordRequired == true;
            NormalizeLoadedProjectSettings(storageProject);
            if (IsWholeJsonEncryption(storageProject))
            {
                if (!PrepareCliExportPolicyForStorage(storageProject))
                {
                    return null;
                }
                foreach (var value in storageProject.Values)
                {
                    value.EncryptedValue = null;
                    value.IsEncrypted = false;
                }
                return storageProject;
            }

            if (!PrepareCliExportPolicyForStorage(storageProject))
            {
                return null;
            }

            if (!ProjectRequiresValueEncryption(storageProject))
            {
                foreach (var value in storageProject.Values)
                {
                    value.EncryptedValue = null;
                    value.IsEncrypted = false;
                }
                return storageProject;
            }

            if (!EnsureVaultKey(storageProject))
            {
                return null;
            }
            project.Crypto = storageProject.Crypto;

            foreach (var value in storageProject.Values)
            {
                var variable = storageProject.Variables.FirstOrDefault(v => v.Id == value.VariableId);
                if (!ShouldEncryptValue(storageProject, variable)) 
                {
                    value.EncryptedValue = null;
                    value.IsEncrypted = false;
                    continue;
                }
                value.EncryptedValue = cryptoService.EncryptString(value.Value ?? string.Empty, vaultKey);
                value.Value = null;
                value.IsEncrypted = true;
            }

            return storageProject;
        }

        private bool UnlockProjectIfNeeded(ProjectModel loadedProject)
        {
            vaultKey = null;
            if (!ProjectHasEncryptedValues(loadedProject))
            {
                return true;
            }

            if (!EnsureVaultKey(loadedProject))
            {
                return false;
            }

            foreach (var value in loadedProject.Values.Where(v => v.IsEncrypted && v.EncryptedValue != null))
            {
                value.Value = cryptoService.DecryptString(value.EncryptedValue, vaultKey);
                value.EncryptedValue = null;
            }

            DecryptCliExportPolicy(loadedProject, vaultKey);

            return true;
        }

        private bool PrepareCliExportPolicyForStorage(ProjectModel storageProject)
        {
            storageProject.Settings = storageProject.Settings ?? new ProjectSettings();
            if (!EnsureVaultKey(storageProject))
            {
                return false;
            }

            storageProject.Settings.CliExportPasswordRequiredEncrypted = cryptoService.EncryptString(storageProject.Settings.CliExportPasswordRequired ? "required:true:v1" : "required:false:v1", vaultKey);
            project.Crypto = storageProject.Crypto;
            return true;
        }

        private void DecryptCliExportPolicy(ProjectModel targetProject, byte[] key)
        {
            targetProject.Settings = targetProject.Settings ?? new ProjectSettings();
            if (targetProject.Settings.CliExportPasswordRequiredEncrypted == null)
            {
                targetProject.Settings.CliExportPasswordRequired = true;
                return;
            }

            var value = cryptoService.DecryptString(targetProject.Settings.CliExportPasswordRequiredEncrypted, key);
            targetProject.Settings.CliExportPasswordRequired = string.Equals(value, "required:true:v1", StringComparison.Ordinal);
        }

        private byte[] UnlockCryptoMetadata(VaultCryptoMetadata crypto)
        {
            var cachedKey = TryLoadCachedVaultKey(crypto);
            if (cachedKey != null)
            {
                return cachedKey;
            }

            while (true)
            {
                var password = PromptDialog.ShowPassword(this, "Unlock Vault", "Master password:");
                if (password == null) return null;
                try
                {
                    var key = cryptoService.DeriveKey(password, Convert.FromBase64String(crypto.Salt), crypto.Iterations);
                    cryptoService.DecryptString(crypto.KeyCheck, key);
                    SaveCachedVaultKey(crypto, key);
                    return key;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Cannot unlock vault.\r\n\r\n" + ex.Message, "Unlock Vault", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private bool EnsureVaultKey(ProjectModel targetProject)
        {
            if (vaultKey != null) return true;

            targetProject.Crypto = targetProject.Crypto ?? new VaultCryptoMetadata();
            if (string.IsNullOrWhiteSpace(targetProject.Crypto.Salt) || targetProject.Crypto.KeyCheck == null)
            {
                return CreateVaultKey(targetProject);
            }

            vaultKey = UnlockCryptoMetadata(targetProject.Crypto);
            return vaultKey != null;
        }

        private bool CreateVaultKey(ProjectModel targetProject)
        {
            var password = PromptDialog.ShowPassword(this, "Create Vault Password", "Master password for encrypted values:");
            if (password == null) return false;
            if (password.Length == 0)
            {
                MessageBox.Show(this, "Master password cannot be empty.", "Create Vault Password", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            var confirm = PromptDialog.ShowPassword(this, "Create Vault Password", "Repeat master password:");
            if (confirm == null) return false;
            if (password != confirm)
            {
                MessageBox.Show(this, "Passwords do not match.", "Create Vault Password", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            var salt = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }

            targetProject.Crypto.Salt = Convert.ToBase64String(salt);
            targetProject.Crypto.Iterations = targetProject.Crypto.Iterations <= 0 ? 300000 : targetProject.Crypto.Iterations;
            vaultKey = cryptoService.DeriveKey(password, salt, targetProject.Crypto.Iterations);
            targetProject.Crypto.KeyCheck = cryptoService.EncryptString("EnvSecuredVaultKeyCheck:v1", vaultKey);
            SaveCachedVaultKey(targetProject.Crypto, vaultKey);
            return true;
        }

        private byte[] TryLoadCachedVaultKey(VaultCryptoMetadata crypto)
        {
            var cachePath = GetVaultKeyCachePath(crypto);
            if (cachePath == null) return null;

            try
            {
                var key = dpapiCacheService.TryLoad(cachePath);
                if (key == null) return null;
                cryptoService.DecryptString(crypto.KeyCheck, key);
                return key;
            }
            catch
            {
                return null;
            }
        }

        private void SaveCachedVaultKey(VaultCryptoMetadata crypto, byte[] key)
        {
            var cachePath = GetVaultKeyCachePath(crypto);
            if (cachePath == null || key == null) return;

            try
            {
                dpapiCacheService.Save(cachePath, key);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save local vault unlock cache.\r\n\r\n" + ex.Message, "Unlock Cache", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static string GetVaultKeyCachePath(VaultCryptoMetadata crypto)
        {
            if (crypto == null || string.IsNullOrWhiteSpace(crypto.Salt) || crypto.KeyCheck == null)
            {
                return null;
            }

            var material = crypto.Salt + "|" + crypto.Iterations + "|" + crypto.KeyCheck.Nonce + "|" + crypto.KeyCheck.Tag;
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(material));
                var name = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant() + ".key";
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "EnvSecured",
                    "VaultKeyCache",
                    name);
            }
        }

        private static bool ProjectRequiresValueEncryption(ProjectModel targetProject)
        {
            var mode = GetProjectEncryptionMode(targetProject);
            return string.Equals(mode, "AllValues", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mode, "SecretsOnly", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWholeJsonEncryption(ProjectModel targetProject)
        {
            return string.Equals(GetProjectEncryptionMode(targetProject), "WholeJson", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ProjectHasEncryptedValues(ProjectModel targetProject)
        {
            return targetProject.Values.Any(v => v.IsEncrypted && v.EncryptedValue != null) ||
                targetProject.Settings?.CliExportPasswordRequiredEncrypted != null;
        }

        private static bool ShouldEncryptValue(ProjectModel targetProject, VariableDefinitionModel variable)
        {
            var mode = GetProjectEncryptionMode(targetProject);
            if (string.Equals(mode, "AllValues", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(mode, "SecretsOnly", StringComparison.OrdinalIgnoreCase)) return variable?.IsSecret == true;
            return false;
        }

        private static void NormalizeLoadedProjectSettings(ProjectModel targetProject)
        {
            if (targetProject == null) return;
            targetProject.Settings = targetProject.Settings ?? new ProjectSettings();
            if (string.IsNullOrWhiteSpace(targetProject.Settings.EncryptionMode))
            {
                targetProject.Settings.EncryptionMode = targetProject.Settings.EncryptAllValues ? "AllValues" : "Open";
            }
            targetProject.Settings.EncryptAllValues = string.Equals(targetProject.Settings.EncryptionMode, "AllValues", StringComparison.OrdinalIgnoreCase);
        }

        private static ProjectModel CloneProject(ProjectModel source)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            return serializer.Deserialize<ProjectModel>(serializer.Serialize(source));
        }

        private void RefreshStatus()
        {
            if (project == null)
            {
                Text = "EnvSecured Studio";
                if (saveButton != null) saveButton.Enabled = false;
                if (validateButton != null) validateButton.Enabled = false;
                if (status == null) return;
                status.Items.Clear();
                status.Items.Add("No project open");
                status.Items.Add("Use New Project or Open");
                return;
            }

            Text = modified
                ? $"EnvSecured Studio - {project.ProjectName} [UNSAVED]"
                : $"EnvSecured Studio - {project.ProjectName}";
            if (saveButton != null) saveButton.Enabled = true;
            if (validateButton != null) validateButton.Enabled = true;
            if (status == null) return;
            status.Items.Clear();
            if (modified)
            {
                status.Items.Add(new ToolStripStatusLabel("UNSAVED CHANGES")
                {
                    ForeColor = Color.Red,
                    Font = new Font(status.Font, FontStyle.Bold)
                });
            }
            else
            {
                status.Items.Add("Ready");
            }
            status.Items.Add($"Project: {project.ProjectName}");
            status.Items.Add($"File: {(currentFilePath ?? "not saved")}");
            status.Items.Add($"Services: {project.Services.Count}");
            status.Items.Add($"Environments: {project.Environments.Count}");
            status.Items.Add($"Variables: {project.Variables.Count}");
            status.Items.Add($"Secrets: {project.Variables.Count(v => v.IsSecret)}");
            status.Items.Add($"v{project.Version}");
        }

        private static bool ConfirmDelete(string name)
        {
            return MessageBox.Show($"Delete {name}?", "Confirm delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
        }

        private static string Slug(string value)
        {
            return new string((value ?? string.Empty).Trim().ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
        }

        private sealed class ScopeSelectorItem
        {
            public ScopeSelectorItem(string id, string name)
            {
                Id = id;
                Name = name;
            }

            public string Id { get; }
            public string Name { get; }
        }

        private sealed class ValueTarget
        {
            public ValueTarget(ValueScope scope, string environmentId, string serviceId, string label)
            {
                Scope = scope;
                EnvironmentId = environmentId;
                ServiceId = serviceId;
                Label = label;
            }

            public ValueScope Scope { get; }
            public string EnvironmentId { get; }
            public string ServiceId { get; }
            public string Label { get; }
        }

        private sealed class LayerValue
        {
            public LayerValue(string value, string source)
            {
                Value = value;
                Source = source;
            }

            public string Value { get; }
            public string Source { get; }
        }

        private sealed class MatrixCellState
        {
            private MatrixCellState(string displayValue, Color backColor, Color foreColor, FontStyle fontStyle, string toolTip)
            {
                DisplayValue = displayValue;
                BackColor = backColor;
                ForeColor = foreColor;
                FontStyle = fontStyle;
                ToolTip = toolTip;
            }

            public string DisplayValue { get; }
            public Color BackColor { get; }
            public Color ForeColor { get; }
            public FontStyle FontStyle { get; }
            public string ToolTip { get; }

            public static MatrixCellState Direct(string value, Color color, bool lightMode, string toolTip)
            {
                return new MatrixCellState(value, Color.White, color, FontStyle.Bold, toolTip);
            }

            public static MatrixCellState Inherited(string value, Color color, bool lightMode, string toolTip)
            {
                return lightMode
                    ? new MatrixCellState(value, VeryLighten(color), Darken(color), FontStyle.Regular, toolTip)
                    : new MatrixCellState(value, Darken(color), Color.White, FontStyle.Regular, toolTip);
            }

            public static MatrixCellState Empty(string value)
            {
                return new MatrixCellState(value, Color.FromArgb(80, 80, 80), Color.White, FontStyle.Regular, "No value");
            }

            private static Color Darken(Color color)
            {
                return Color.FromArgb(
                    Math.Max(0, (int)(color.R * 0.48)),
                    Math.Max(0, (int)(color.G * 0.48)),
                    Math.Max(0, (int)(color.B * 0.48)));
            }

            private static Color Lighten(Color color)
            {
                return Blend(color, Color.White, 0.78);
            }

            private static Color VeryLighten(Color color)
            {
                return Blend(color, Color.White, 0.88);
            }

            private static Color Blend(Color color, Color target, double amount)
            {
                return Color.FromArgb(
                    (int)(color.R + (target.R - color.R) * amount),
                    (int)(color.G + (target.G - color.G) * amount),
                    (int)(color.B + (target.B - color.B) * amount));
            }
        }

        private sealed class MatrixValueTarget
        {
            public MatrixValueTarget(VariableDefinitionModel variable, ValueScope scope, string serviceId, string environmentId, string label)
            {
                Variable = variable;
                Scope = scope;
                ServiceId = serviceId;
                EnvironmentId = environmentId;
                Label = label;
            }

            public VariableDefinitionModel Variable { get; }
            public ValueScope Scope { get; }
            public string ServiceId { get; }
            public string EnvironmentId { get; }
            public string Label { get; }
        }

        private sealed class OutputTarget
        {
            public OutputTarget(ServiceModel service, EnvironmentModel environment)
            {
                Service = service;
                Environment = environment;
            }

            public ServiceModel Service { get; }
            public EnvironmentModel Environment { get; }
        }

        private sealed class ImportFileRow
        {
            public bool Selected { get; set; }
            public string FilePath { get; set; }
            public string Environment { get; set; }
            public string Service { get; set; }
        }

        private sealed class ImportPreviewRow
        {
            public bool Include { get; set; }
            public string Action { get; set; }
            public string Key { get; set; }
            public bool Secret { get; set; }
            public bool Required { get; set; }
            public string Environment { get; set; }
            public string Service { get; set; }
            public string Scope { get; set; }
            public string OldValue { get; set; }
            public string NewValue { get; set; }
            public string FilePath { get; set; }
        }
    }
}

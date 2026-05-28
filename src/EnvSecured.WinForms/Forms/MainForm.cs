using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
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
        private readonly GeneratedValueService generatedValueService = new GeneratedValueService();
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
        private bool suppressOutputTargetGridEvents;
        private readonly HashSet<SplitContainer> splittersRestoring = new HashSet<SplitContainer>();
        private readonly HashSet<DataGridView> gridsRestoringColumns = new HashSet<DataGridView>();
        private string currentView = "Variables";
        private FlowLayoutPanel commandPanel;
        private Button saveButton;
        private Button validateButton;
        private Dictionary<string, Image> uiImages = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private ImageList navigationImages;
        private GroupBox quickStatsGroup;
        private TableLayoutPanel quickStatsTable;
        private PictureBox vaultStatusIcon;
        private Label vaultStatusLabel;

        private TreeView navigation;
        private Panel contentPanel;
        private ComboBox environmentCombo;
        private ComboBox ownerFilterCombo;
        private ComboBox scopeFilterCombo;
        private TextBox searchBox;
        private CheckBox showSecretsCheckBox;
        private CheckBox matrixLightColorsCheckBox;
        private CheckBox calculatedValuesCheckBox;
        private readonly Dictionary<string, string> variableColumnFilters = new Dictionary<string, string>();
        private DataGridView mainGrid;
        private DataGridView scopeMatrix;
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
        private const string ScopeModeNone = "NONE";
        private const string ScopeModeRead = "Read";
        private const string ScopeModeOverride = "Override";
        private const string ScopeModeExport = "Export";
        private const string ScopeModeFull = "Full";
        private static readonly string[] ScopeModes =
        {
            ScopeModeNone,
            ScopeModeRead,
            ScopeModeOverride,
            ScopeModeExport,
            ScopeModeFull
        };
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

        public MainForm(string initialFilePath = null)
        {
            Text = "EnvSecured Studio";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            MinimumSize = new Size(1050, 680);
            ApplyDefaultWindowSize();
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            BuildUi();
            RestoreWindowBounds();
            OpenInitialProjectOrStart(initialFilePath);
        }

        private void OpenInitialProjectOrStart(string initialFilePath)
        {
            if (!string.IsNullOrWhiteSpace(initialFilePath))
            {
                try
                {
                    OpenProjectFile(initialFilePath);
                    return;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Cannot open project.\r\n\r\n{ex.Message}", "Open Project", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

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
                else if (result == DialogResult.No)
                {
                    DeleteRecoveryBackupQuietly();
                }
            }

            SaveWindowBounds();
            ClearVaultKey();
            base.OnFormClosing(e);
        }

        private void DeleteRecoveryBackupQuietly()
        {
            if (string.IsNullOrWhiteSpace(currentFilePath)) return;
            try
            {
                vaultFileService.DeleteRecoveryBackup(currentFilePath);
            }
            catch
            {
            }
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
            LoadUiImages();

            var menu = new MenuStrip { Dock = DockStyle.Top, BackColor = Color.White };
            var file = new ToolStripMenuItem("File");
            file.DropDownItems.Add("New Project", null, (s, e) => NewProject());
            file.DropDownItems.Add("Open Project", null, (s, e) => OpenProject());
            file.DropDownItems.Add("Save", null, (s, e) => SaveProject());
            file.DropDownItems.Add("Save As", null, (s, e) => SaveProjectAs());
            file.DropDownItems.Add("Exit", null, (s, e) => Close());
            menu.Items.Add(file);
            menu.Items.Add(new ToolStripMenuItem("Help", null, new ToolStripMenuItem("About", null, (s, e) => ShowAboutDialog())));

            commandPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(8, 6, 8, 6),
                WrapContents = false,
                BackColor = Color.White
            };
            AddCommand(commandPanel, "New Project", NewProject, "New", 118);
            AddCommand(commandPanel, "Open", OpenProject, "Open", 96);
            saveButton = AddCommand(commandPanel, "Save", SaveProject, "Save", 96);
            validateButton = AddCommand(commandPanel, "Validate", ValidateAll, "Validate", 110);
            AddCommand(commandPanel, "Export", RenderOutputFiles, "Export", 96);
            AddCommand(commandPanel, "Reset Layout", ResetLayoutSettings, "Reset", 124);
            var commandBar = new TableLayoutPanel { Dock = DockStyle.Top, Height = 46, ColumnCount = 2, RowCount = 1, BackColor = Color.White };
            commandBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            commandBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            commandBar.Controls.Add(commandPanel, 0, 0);
            commandBar.Controls.Add(BuildVaultStatusPanel(), 1, 0);

            navigation = new TreeView
            {
                Dock = DockStyle.Fill,
                HideSelection = false,
                BorderStyle = BorderStyle.None,
                FullRowSelect = true,
                ShowLines = false,
                ItemHeight = 24,
                BackColor = Color.White
            };
            if (navigationImages != null) navigation.ImageList = navigationImages;
            navigation.AfterSelect += (s, e) =>
            {
                if (e.Node.Level == 1)
                {
                    currentView = Convert.ToString(e.Node.Tag ?? e.Node.Text);
                    RenderCurrentView();
                }
            };

            contentPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };

            var leftPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, BackColor = Color.White };
            leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            leftPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 128));
            leftPanel.Controls.Add(navigation, 0, 0);
            quickStatsTable = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 5, Padding = new Padding(6, 6, 6, 12) };
            quickStatsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 16));
            quickStatsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
            quickStatsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (var i = 0; i < 5; i++) quickStatsTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 17));
            quickStatsGroup = new GroupBox { Dock = DockStyle.Fill, Text = "Quick Stats", Padding = new Padding(4) };
            quickStatsGroup.Controls.Add(quickStatsTable);
            leftPanel.Controls.Add(quickStatsGroup, 0, 1);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Color.FromArgb(248, 249, 251) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.Controls.Add(leftPanel, 0, 0);
            root.Controls.Add(contentPanel, 1, 0);

            status = new StatusStrip();
            Controls.Add(root);
            Controls.Add(commandBar);
            Controls.Add(menu);
            Controls.Add(status);
            MainMenuStrip = menu;
        }

        private Button AddCommand(FlowLayoutPanel panel, string text, Action action, string imageKey = null, int width = 96)
        {
            var button = new Button
            {
                Text = text,
                Width = width,
                Height = 30,
                Margin = new Padding(3, 2, 3, 2),
                FlatStyle = FlatStyle.Flat,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                ImageAlign = ContentAlignment.MiddleLeft,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.White
            };
            button.FlatAppearance.BorderColor = Color.FromArgb(214, 219, 226);
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(244, 248, 255);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(231, 241, 255);
            if (!string.IsNullOrWhiteSpace(imageKey) && uiImages.TryGetValue(imageKey, out var image))
            {
                button.Image = image;
                button.Padding = new Padding(4, 0, 4, 0);
            }
            button.Click += (s, e) => action();
            panel.Controls.Add(button);
            return button;
        }

        private Control BuildVaultStatusPanel()
        {
            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(4, 10, 10, 0),
                BackColor = Color.White
            };
            vaultStatusIcon = new PictureBox
            {
                Width = 18,
                Height = 18,
                SizeMode = PictureBoxSizeMode.CenterImage,
                Margin = new Padding(0, 0, 6, 0)
            };
            vaultStatusLabel = new Label
            {
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 2, 0, 0),
                Text = "Vault: -"
            };
            panel.Controls.Add(vaultStatusIcon);
            panel.Controls.Add(vaultStatusLabel);
            return panel;
        }

        private void LoadUiImages()
        {
            uiImages = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
            navigationImages = null;
            AddIconResource("New", "new.ico");
            AddIconResource("Add", "add.ico");
            AddIconResource("Open", "open.ico");
            AddIconResource("Save", "save.ico");
            AddIconResource("SaveAs", "save_as.ico");
            AddIconResource("Validate", "validate.ico");
            AddIconResource("Export", "export.ico");
            AddIconResource("Import", "import.ico");
            AddIconResource("ImportExport", "imp-exp.ico");
            AddIconResource("Reset", "reset_view.ico");
            AddIconResource("Apply", "apply.ico");
            AddIconResource("Edit", "edit.ico");
            AddIconResource("Delete", "delete.ico");
            AddIconResource("FilesAdd", "files-add.ico");
            AddIconResource("FilesRemove", "files-remove.ico");
            AddIconResource("SelectAll", "select-all.ico");
            AddIconResource("SelectNone", "select-none.ico");
            AddIconResource("Project", "home.ico");
            AddIconResource("Settings", "settings.ico");
            AddIconResource("Variables", "vars.ico");
            AddIconResource("Services", "service.ico");
            AddIconResource("Environments", "env.ico");
            AddIconResource("Scope", "scope.ico");
            AddIconResource("Key", "key.ico");
            AddIconResource("Lock", "lock.ico");
            AddIconResource("Unlock", "unlocked.ico");
            AddIconResource("Locked", "locked.ico");
            AddIconResource("VaultOpen", "vault-decrypted.ico");
            AddIconResource("VaultEncrypted", "vault-encrypted.ico");
            AddIconResource("VaultSecrets", "vault-secrets-only.ico");
            AddIconResource("VaultMasked", "vault-masked.ico");
            AddIconResource("Info", "info.ico");
            AddIconResource("Warning", "warning.ico");
            AddIconResource("Validation", "report.ico");

            if (uiImages.Count == 0) return;
            navigationImages = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(16, 16) };
            foreach (var pair in uiImages)
            {
                navigationImages.Images.Add(pair.Key, pair.Value);
            }
        }

        private void AddIconResource(string key, string fileName)
        {
            using (var stream = OpenEmbeddedIcon(fileName))
            {
                if (stream != null)
                {
                    uiImages[key] = IconToBitmap(stream);
                    return;
                }
            }

            var filePath = ResolveIconFilePath(fileName);
            if (filePath == null) return;
            using (var icon = new Icon(filePath, new Size(18, 18)))
            {
                uiImages[key] = icon.ToBitmap();
            }
        }

        private static Stream OpenEmbeddedIcon(string fileName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(".Assets.icons." + fileName, StringComparison.OrdinalIgnoreCase));
            return resourceName == null ? null : assembly.GetManifestResourceStream(resourceName);
        }

        private static Bitmap IconToBitmap(Stream stream)
        {
            using (var icon = new Icon(stream, new Size(18, 18)))
            {
                return icon.ToBitmap();
            }
        }

        private static string ResolveIconFilePath(string fileName)
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "Assets", "icons", fileName),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Assets", "icons", fileName)),
                Path.Combine(Environment.CurrentDirectory, "src", "EnvSecured.WinForms", "Assets", "icons", fileName)
            };
            return candidates.FirstOrDefault(File.Exists);
        }

        private void ClearVaultKey()
        {
            if (vaultKey == null) return;
            ClearKey(vaultKey);
            vaultKey = null;
        }

        private static void ClearKey(byte[] key)
        {
            if (key != null)
            {
                Array.Clear(key, 0, key.Length);
            }
        }

        private void SetVaultKey(byte[] key)
        {
            if (!ReferenceEquals(vaultKey, key))
            {
                ClearVaultKey();
            }

            vaultKey = key;
        }

        private void RenderNoProjectView()
        {
            project = null;
            currentFilePath = null;
            ClearVaultKey();
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
            center.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            center.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            center.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            center.Controls.Add(new Label { Text = "No project is open", Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 12, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            center.Controls.Add(new Label { Text = "Create a new EnvSecured Studio project or open an existing vault file.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 6, 0, 6) };
            var newButton = AddCommand(buttons, "New Project", NewProject, "New", 118);
            var openButton = AddCommand(buttons, "Open", OpenProject, "Open", 96);
            newButton.Width = 120;
            openButton.Width = 120;
            newButton.Height = 28;
            openButton.Height = 28;
            newButton.Margin = new Padding(0, 0, 6, 0);
            openButton.Margin = new Padding(0);
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
            ClearVaultKey();
            modified = true;
            currentView = "Variables";
            RefreshNavigation();
            RenderCurrentView();
            RefreshStatus();
        }

        private void OpenProject()
        {
            using (var dialog = new OpenFileDialog { Filter = "EnvSecured Studio vault (*.envs)|*.envs|All files (*.*)|*.*" })
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
            ApplyCurrentViewEditsBeforeSave();

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
            using (var dialog = new SaveFileDialog { Filter = "EnvSecured Studio vault (*.envs)|*.envs|All files (*.*)|*.*", FileName = "envsecured.envs", DefaultExt = "envs", AddExtension = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                currentFilePath = dialog.FileName;
                SaveProject();
                recentProjectsService.Add(currentFilePath);
            }
        }

        private void ApplyCurrentViewEditsBeforeSave()
        {
            if (project == null) return;
            if (currentView == "Project")
            {
                ApplyProjectSettingsFromView();
            }
            else if (currentView == "Export")
            {
                ApplyExportSettingsFromView(false);
            }
        }

        private void RefreshNavigation()
        {
            navigation.Nodes.Clear();
            var projectGroup = AddNavigationGroup("Project", "Project");
            AddNavigationNode(projectGroup, "Project", "Project", "Project");
            var config = AddNavigationGroup("Configuration", "Settings");
            AddNavigationNode(config, "Variables", "Variables", "Variables");
            AddNavigationNode(config, "Services", "Services", "Services");
            AddNavigationNode(config, "Environments", "Environments", "Environments");
            AddNavigationNode(config, "Scope", "Scope", "Scope");
            var io = AddNavigationGroup("Import / Export", "ImportExport");
            AddNavigationNode(io, "Import", "Import", "Import");
            AddNavigationNode(io, "Export", "Export", "Export");
            var validationGroup = AddNavigationGroup("Validation", "Validation");
            AddNavigationNode(validationGroup, "Validation Results", "Validation Results", "Validation");
            navigation.ExpandAll();
        }

        private TreeNode AddNavigationGroup(string text, string imageKey, string view = null)
        {
            var node = navigation.Nodes.Add(text);
            node.Tag = view;
            SetNavigationNodeImage(node, imageKey);
            return node;
        }

        private void AddNavigationNode(TreeNode parent, string text, string view, string imageKey)
        {
            var node = parent.Nodes.Add(text);
            node.Tag = view;
            SetNavigationNodeImage(node, imageKey);
        }

        private static void SetNavigationNodeImage(TreeNode node, string imageKey)
        {
            node.ImageKey = imageKey;
            node.SelectedImageKey = imageKey;
        }

        private void RenderCurrentView()
        {
            contentPanel.Controls.Clear();
            if (currentView == "Services") RenderServicesView();
            else if (currentView == "Environments") RenderEnvironmentsView();
            else if (currentView == "Scope" || currentView == "Contracts") RenderScopeView();
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
                if (ApplyProjectSettingsFromView())
                {
                    Changed();
                }
            }, "Apply", 96);
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

        private bool ApplyProjectSettingsFromView()
        {
            var nameBox = contentPanel.Controls.Find("ProjectNameBox", true).FirstOrDefault() as TextBox;
            var descriptionBox = contentPanel.Controls.Find("ProjectDescriptionBox", true).FirstOrDefault() as TextBox;
            var encryptionCombo = contentPanel.Controls.Find("ProjectEncryptionCombo", true).FirstOrDefault() as ComboBox;
            var cliExportPasswordBox = contentPanel.Controls.Find("CliExportPasswordRequiredBox", true).FirstOrDefault() as CheckBox;
            if (nameBox == null || encryptionCombo == null) return false;

            project.Settings = project.Settings ?? new ProjectSettings();
            project.ProjectName = string.IsNullOrWhiteSpace(nameBox.Text) ? project.ProjectName : nameBox.Text.Trim();
            project.Description = descriptionBox?.Text;
            SetProjectEncryptionMode(Convert.ToString(encryptionCombo.SelectedItem));
            project.Settings.CliExportPasswordRequired = cliExportPasswordBox?.Checked == true;
            project.Settings.CliExportPasswordRequiredPolicy = project.Settings.CliExportPasswordRequired;
            return true;
        }

        private void ShowAboutDialog()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var product = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product
                ?? "EnvSecured Studio";
            var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? assembly.GetName().Version?.ToString()
                ?? "unknown";
            var copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright
                ?? "Maxim Hegel © 2026";
            var description = assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description
                ?? "Windows desktop and CLI tool for managing encrypted service and environment configuration vaults.";
            const string repositoryUrl = "https://github.com/hegelmax/env-secured-studio";

            using (var dialog = new Form
            {
                Text = "About EnvSecured Studio",
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                ClientSize = new Size(640, 430)
            })
            {
                var root = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 2,
                    RowCount = 5,
                    Padding = new Padding(10)
                };
                root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
                root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

                var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                root.Controls.Add(new PictureBox
                {
                    Dock = DockStyle.Top,
                    Width = 32,
                    Height = 32,
                    Margin = new Padding(0, 12, 8, 0),
                    SizeMode = PictureBoxSizeMode.CenterImage,
                    Image = icon?.ToBitmap()
                }, 0, 0);

                var productBox = new GroupBox { Text = "Product Information", Dock = DockStyle.Fill };
                var productGrid = BuildAboutGrid(2);
                productGrid.Controls.Add(new Label { Text = "Product:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
                productGrid.Controls.Add(new Label { Text = product, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 1, 0);
                productGrid.Controls.Add(new Label { Text = "Version:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
                productGrid.Controls.Add(new Label { Text = version, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 1, 1);
                productBox.Controls.Add(productGrid);
                root.Controls.Add(productBox, 1, 0);

                var supportBox = new GroupBox { Text = "Support Information", Dock = DockStyle.Fill };
                var supportLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(8, 6, 8, 6) };
                supportLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                supportLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
                supportLayout.Controls.Add(new Label
                {
                    Text = description,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft
                }, 0, 0);
                var link = new LinkLabel { Text = repositoryUrl, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
                link.LinkClicked += (s, e) =>
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(repositoryUrl) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(dialog, "Could not open link.\r\n\r\n" + ex.Message, "About", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                };
                supportLayout.Controls.Add(link, 0, 1);
                supportBox.Controls.Add(supportLayout);
                root.Controls.Add(supportBox, 1, 1);

                var additionalBox = new GroupBox { Text = "Additional Information", Dock = DockStyle.Fill };
                var additionalGrid = BuildAboutGrid(4);
                additionalGrid.Controls.Add(new Label { Text = "Executable:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
                additionalGrid.Controls.Add(new Label { Text = Application.ExecutablePath, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 1, 0);
                additionalGrid.Controls.Add(new Label { Text = "Host name:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
                additionalGrid.Controls.Add(new Label { Text = Environment.MachineName, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 1, 1);
                additionalGrid.Controls.Add(new Label { Text = "OS version:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
                additionalGrid.Controls.Add(new Label { Text = Environment.OSVersion.VersionString, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 1, 2);
                additionalGrid.Controls.Add(new Label { Text = ".NET runtime:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 3);
                additionalGrid.Controls.Add(new Label { Text = Environment.Version.ToString(), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 1, 3);
                additionalBox.Controls.Add(additionalGrid);
                root.Controls.Add(additionalBox, 1, 2);

                root.Controls.Add(new Label
                {
                    Text = copyright + Environment.NewLine + "EnvSecured Studio manages encrypted service and environment configuration vaults for desktop and CLI workflows.",
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft
                }, 1, 3);

                var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty };
                var okButton = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 90, Height = 26 };
                buttons.Controls.Add(okButton);
                root.Controls.Add(buttons, 1, 4);

                dialog.Controls.Add(root);
                dialog.AcceptButton = okButton;
                dialog.CancelButton = okButton;
                dialog.ShowDialog(this);
            }
        }

        private TableLayoutPanel BuildAboutGrid(int rows)
        {
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = rows, Padding = new Padding(8, 8, 8, 6) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (var i = 0; i < rows; i++)
            {
                grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            }
            return grid;
        }

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

        private Control BuildViewHeader(string title, string description)
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Color.White, Padding = new Padding(0, 2, 0, 0) };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.Controls.Add(new Label
            {
                Text = title,
                AutoSize = true,
                Font = new Font(Font.FontFamily, 15f, FontStyle.Bold),
                Padding = new Padding(0, 4, 12, 0)
            }, 0, 0);
            panel.Controls.Add(new Label
            {
                Text = description,
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(91, 99, 112),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            }, 1, 0);
            return panel;
        }

        private void RenderVariablesView()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, Padding = new Padding(8, 8, 8, 0), BackColor = Color.White };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.Controls.Add(BuildViewHeader("Variables", "Manage configuration variables and their usage across services and environments."), 0, 0);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 2, 0, 2) };
            AddCommand(buttons, "Add Variable", AddVariable, "Add", 118);
            AddCommand(buttons, "Edit Variable", EditVariable, "Edit", 118);
            AddCommand(buttons, "Delete Variable", DeleteVariable, "Delete", 132);
            AddCommand(buttons, "Regenerate Generated", RegenerateAllGeneratedVariables, "Reset", 158);
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
            root.Controls.Add(buttons, 0, 1);

            root.Controls.Add(BuildVariableFilters(), 0, 2);
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
            root.Controls.Add(split, 0, 3);
            contentPanel.Controls.Add(root);
            AttachSplitterRatioPersistence(split, "VariablesVertical");
            RestoreSplitterRatioWhenReady(split, "VariablesVertical", 0.72);
            RefreshVariableGrid();
            RefreshVariableDetails(root);
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

        private void RestoreSplitterRatioWhenReady(SplitContainer split, string splitterKey, double defaultRatio)
        {
            splittersRestoring.Add(split);
            EventHandler handler = null;
            handler = (s, e) =>
            {
                if (split.IsDisposed || !split.IsHandleCreated) return;
                split.BeginInvoke(new Action(() =>
                {
                    if (split.IsDisposed || !split.IsHandleCreated) return;
                    if (!TryRestoreSplitterRatio(split, splitterKey, defaultRatio)) return;
                    split.HandleCreated -= handler;
                    split.SizeChanged -= handler;
                    splittersRestoring.Remove(split);
                }));
            };
            split.Disposed += (s, e) => splittersRestoring.Remove(split);
            split.HandleCreated += handler;
            split.SizeChanged += handler;
            if (split.IsHandleCreated) handler(split, EventArgs.Empty);
        }

        private bool TryRestoreSplitterRatio(SplitContainer split, string splitterKey, double defaultRatio)
        {
            var available = SplitterAvailableSize(split);
            if (available <= split.Panel1MinSize + split.Panel2MinSize) return false;
            var ratio = recentProjectsService.LoadSplitterRatio(splitterKey) ?? defaultRatio;
            return SetSafeSplitterDistance(split, ratio);
        }

        private void SaveSplitterRatio(SplitContainer split, string splitterKey)
        {
            if (suppressSplitterPersistence || splittersRestoring.Contains(split) || split.IsDisposed) return;
            var available = SplitterAvailableSize(split);
            if (available <= 0) return;
            var ratio = split.SplitterDistance / (double)available;
            ratio = Math.Max(0.05, Math.Min(0.95, ratio));
            recentProjectsService.SaveSplitterRatio(splitterKey, ratio);
        }

        private bool SetSafeSplitterDistance(SplitContainer split, double ratio)
        {
            if (split.IsDisposed) return false;
            var available = SplitterAvailableSize(split);
            if (available <= split.Panel1MinSize + split.Panel2MinSize) return false;
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
            return true;
        }

        private static int SplitterAvailableSize(SplitContainer split)
        {
            return (split.Orientation == Orientation.Horizontal ? split.Height : split.Width) - split.SplitterWidth;
        }

        private Control BuildVariableFilters()
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 8, Padding = new Padding(0, 4, 0, 4) };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 55));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var environments = new[] { new ScopeSelectorItem(null, "Global") }
                .Concat(project.Environments.OrderBy(e => e.SortOrder).Select(e => new ScopeSelectorItem(e.Id, e.Name)))
                .ToList();
            var owners = new[] { new ScopeSelectorItem(null, "All owners") }
                .Concat(project.Services.OrderBy(s => s.SortOrder).Select(s => new ScopeSelectorItem(s.Id, s.Name)))
                .ToList();
            var scopes = new[] { new ScopeSelectorItem(null, "All scopes") }
                .Concat(project.Services.OrderBy(s => s.SortOrder).Select(s => new ScopeSelectorItem(s.Id, s.Name)))
                .ToList();

            environmentCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, DataSource = environments, DisplayMember = "Name" };
            ownerFilterCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, DataSource = owners, DisplayMember = "Name" };
            scopeFilterCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, DataSource = scopes, DisplayMember = "Name" };
            searchBox = new TextBox { Dock = DockStyle.Fill };
            environmentCombo.SelectedIndexChanged += (s, e) => RefreshVariableGrid();
            ownerFilterCombo.SelectedIndexChanged += (s, e) => RefreshVariableGrid();
            scopeFilterCombo.SelectedIndexChanged += (s, e) => RefreshVariableGrid();
            searchBox.TextChanged += (s, e) => RefreshVariableGrid();

            panel.Controls.Add(new Label { Text = "Environment:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            panel.Controls.Add(environmentCombo, 1, 0);
            panel.Controls.Add(new Label { Text = "Owner:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 2, 0);
            panel.Controls.Add(ownerFilterCombo, 3, 0);
            panel.Controls.Add(new Label { Text = "Scope:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 4, 0);
            panel.Controls.Add(scopeFilterCombo, 5, 0);
            panel.Controls.Add(new Label { Text = "Search:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 6, 0);
            panel.Controls.Add(searchBox, 7, 0);
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
            var matrixPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            matrixPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            matrixPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var matrixHeader = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            matrixHeader.Controls.Add(new Label { Text = "Service Values", AutoSize = true, Padding = new Padding(0, 7, 10, 0), Font = new Font(Font, FontStyle.Bold) });
            matrixLightColorsCheckBox = new CheckBox { Text = "Light matrix colors", Checked = recentProjectsService.LoadBooleanPreference(PreferenceLightMatrixColors, false), AutoSize = true, Padding = new Padding(0, 5, 0, 0) };
            var matrix = new DataGridView
            {
                Dock = DockStyle.Fill,
                Name = "variableMatrix",
                ReadOnly = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.CellSelect
            };
            matrixLightColorsCheckBox.CheckedChanged += (s, e) =>
            {
                recentProjectsService.SaveBooleanPreference(PreferenceLightMatrixColors, matrixLightColorsCheckBox.Checked);
                var variable = GetSelectedVariable();
                if (variable != null) BuildVariableServiceEnvironmentMatrix(matrix, variable);
            };
            AttachColumnWidthPersistence(matrix, "VariableMatrix");
            matrixHeader.Controls.Add(matrixLightColorsCheckBox);
            matrixPanel.Controls.Add(matrixHeader, 0, 0);
            matrixPanel.Controls.Add(matrix, 0, 1);
            panel.Panel2.Controls.Add(matrixPanel);
            AttachSplitterRatioPersistence(panel, "VariableDetailsMatrix");
            panel.HandleCreated += (s, e) => panel.BeginInvoke(new Action(() =>
            {
                ApplyVariableDetailsMinSizes(panel);
            }));
            panel.SizeChanged += (s, e) => ApplyVariableDetailsMinSizes(panel);
            RestoreSplitterRatioWhenReady(panel, "VariableDetailsMatrix", 0.25);
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
            var outerSplit = root.Controls.OfType<SplitContainer>().FirstOrDefault();
            var details = outerSplit?.Panel2.Controls.OfType<SplitContainer>().FirstOrDefault();
            if (details == null) return;
            var info = details.Panel1.Controls.OfType<GroupBox>().FirstOrDefault();
            var matrix = details.Panel2.Controls.Find("variableMatrix", true).OfType<DataGridView>().FirstOrDefault();
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
            var updatedAt = effective?.SourceUpdatedAt ?? selected.UpdatedAt;

            info.Text = variable.Key;
            info.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = $"Owner: {OwnerDisplayName(variable)}\r\nType: {variable.Type}\r\nGroup: {DisplayInfoValue(variable.GroupName)}\r\nDemo value: {DisplayInfoValue(variable.DemoValue)}\r\nDemo comment: {DisplayInfoValue(variable.DemoComment)}\r\nDescription: {DisplayInfoValue(variable.Description)}\r\nSecret: {(variable.IsSecret ? "Yes" : "No")}\r\nAllow shared secret: {(variable.AllowSharedSecret ? "Yes" : "No")}\r\nAllow null: {(variable.AllowNull ? "Yes" : "No")}\r\nAllow blank: {(variable.AllowBlank ? "Yes" : "No")}\r\nEffective: {DisplayValue(variable, effectiveValue)}\r\nSource: {source}\r\nUpdatedAt: {FormatUpdatedAtExact(updatedAt)}",
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
                matrix.CellMouseDown -= VariableMatrixCellMouseDown;
                matrix.CellDoubleClick += VariableMatrixCellDoubleClick;
                matrix.KeyDown += VariableMatrixKeyDown;
                matrix.CellValueChanged += VariableMatrixCellValueChanged;
                matrix.CurrentCellDirtyStateChanged += VariableMatrixCurrentCellDirtyStateChanged;
                matrix.CellMouseDown += VariableMatrixCellMouseDown;
                var valueColors = BuildVariableMatrixValueColors(variable);
                matrix.Columns.Add(new DataGridViewTextBoxColumn { Name = "Service", HeaderText = "Service", ReadOnly = true, FillWeight = 120 });
                matrix.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Owner", HeaderText = "Owner", FillWeight = 45 });
                matrix.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Visible", HeaderText = "Scope", FillWeight = 45 });
                matrix.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Override", HeaderText = "Override", FillWeight = 55 });
                matrix.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Use", HeaderText = "Export", FillWeight = 45 });
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

                foreach (var service in project.Services.OrderBy(s => s.SortOrder))
                {
                    AddVariableMatrixRow(matrix, variable, service, valueColors);
                }
                RestoreColumnWidths(matrix, "VariableMatrix");
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
            var isOwner = SameNullable(variable.OwnerServiceId, service?.Id);
            var visible = service == null || ProjectService.IsVariableVisibleToService(project, variable, service.Id);
            row.Cells["Owner"].Value = isOwner;
            row.Cells["Owner"].ReadOnly = false;
            row.Cells["Owner"].Style.BackColor = Color.White;
            row.Cells["Visible"].Value = service == null ? isOwner : ProjectService.IsVariableVisibleToService(project, variable, service.Id);
            row.Cells["Visible"].ReadOnly = service == null || isOwner;
            row.Cells["Visible"].Style.BackColor = row.Cells["Visible"].ReadOnly ? SystemColors.ControlLight : Color.White;
            row.Cells["Override"].Value = service == null ? isOwner : ProjectService.CanOverrideVariableForService(project, variable, service.Id);
            row.Cells["Override"].ReadOnly = service == null || isOwner || !visible;
            row.Cells["Override"].Style.BackColor = row.Cells["Override"].ReadOnly ? SystemColors.ControlLight : Color.White;
            row.Cells["Use"].Value = service != null && visible && IsVariableUsedByService(variable.Id, service.Id);
            row.Cells["Use"].ReadOnly = service == null || !visible;
            row.Cells["Use"].Style.BackColor = row.Cells["Use"].ReadOnly ? SystemColors.ControlLight : Color.White;
            foreach (DataGridViewColumn column in matrix.Columns)
            {
                if (column.Name == "Service" || column.Name == "Owner" || column.Name == "Use" || column.Name == "Visible" || column.Name == "Override") continue;
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
            if (service != null && !ProjectService.IsVariableVisibleToService(project, variable, service.Id))
            {
                return MatrixCellState.Empty("-");
            }
            var scope = MatrixScope(service?.Id, environment?.Id);
            var directValue = FindDirectValue(variable.Id, scope, service?.Id, environment?.Id);
            var effective = BuildDisplayEffective(service?.Id, environment?.Id).FirstOrDefault(x => x.Variable.Id == variable.Id);
            if (effective == null || effective.Missing)
            {
                return MatrixCellState.Empty("-");
            }

            var displayValue = !ShowCalculatedValues() && directValue != null ? directValue.Value : effective.Value;
            var display = DisplayValue(variable, displayValue);
            var color = valueColors.TryGetValue(displayValue ?? string.Empty, out var mappedColor)
                ? mappedColor
                : Color.White;
            var direct =
                effective.SourceScope == scope &&
                SameNullable(effective.SourceServiceId, service?.Id) &&
                SameNullable(effective.SourceEnvironmentId, environment?.Id);
            var inherited = !direct;
            var contractNote = usedByService ? string.Empty : "Not in service contract. ";
            var updatedNote = string.IsNullOrWhiteSpace(effective.SourceUpdatedAt)
                ? string.Empty
                : "\r\nUpdatedAt: " + FormatUpdatedAtExact(effective.SourceUpdatedAt);
            if (direct)
            {
                return MatrixCellState.Direct(display, color, MatrixLightColorMode(), contractNote + "Defined at " + MatrixSourceLabel(effective) + updatedNote);
            }

            if (inherited)
            {
                return MatrixCellState.Inherited(display, color, MatrixLightColorMode(), contractNote + "Inherited from " + MatrixSourceLabel(effective) + updatedNote);
            }

            return MatrixCellState.Empty("-");
        }

        private string MatrixSourceLabel(EffectiveValue effective)
        {
            if (effective == null || effective.SourceScope == null) return string.Empty;
            var service = string.IsNullOrWhiteSpace(effective.SourceServiceId)
                ? null
                : project.Services.FirstOrDefault(s => s.Id == effective.SourceServiceId)?.Name ?? effective.SourceServiceId;
            var environment = string.IsNullOrWhiteSpace(effective.SourceEnvironmentId)
                ? null
                : project.Environments.FirstOrDefault(e => e.Id == effective.SourceEnvironmentId)?.Name ?? effective.SourceEnvironmentId;

            if (service == null && environment == null) return "Global";
            if (service == null) return "Environment " + environment;
            if (environment == null) return "Service " + service;
            return "Service " + service + " / Environment " + environment;
        }

        private static ValueScope MatrixScope(string serviceId, string environmentId)
        {
            if (serviceId == null && environmentId == null) return ValueScope.Global;
            if (serviceId == null) return ValueScope.Environment;
            if (environmentId == null) return ValueScope.Service;
            return ValueScope.ServiceEnvironment;
        }

        private void VariableMatrixCellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex <= 4) return;
            var matrix = sender as DataGridView;
            if (matrix == null) return;
            SetVariableMatrixDirectValue(matrix, e.RowIndex, e.ColumnIndex);
        }

        private void VariableMatrixCellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right || e.RowIndex < 0 || e.ColumnIndex < 0) return;
            var matrix = sender as DataGridView;
            if (matrix == null) return;
            SelectGridCell(matrix, e.RowIndex, e.ColumnIndex);
            var target = e.ColumnIndex > 4 ? MatrixTargetFromCell(matrix, e.RowIndex, e.ColumnIndex) : null;
            var variable = target?.Variable ?? project?.Variables.FirstOrDefault(v => v.Id == Convert.ToString(matrix.Tag));
            var effective = target == null
                ? null
                : BuildDisplayEffective(target.ServiceId, target.EnvironmentId).FirstOrDefault(x => x.Variable.Id == target.Variable.Id && !x.Missing);

            var menu = new ContextMenuStrip();
            menu.Items.Add("Copy Key", null, (s, args) => CopyText(variable?.Key));
            menu.Items.Add("Copy {{KEY}}", null, (s, args) => CopyText(VariablePlaceholder(variable?.Key)));
            menu.Items.Add("Copy Value", null, (s, args) => CopyText(Convert.ToString(matrix.Rows[e.RowIndex].Cells[e.ColumnIndex].Value)));
            menu.Items.Add("Copy Effective Value", null, (s, args) => CopyText(variable == null ? string.Empty : DisplayValue(variable, effective?.Value)));
            if (target != null)
            {
                menu.Items.Add(new ToolStripSeparator());
                if (variable?.IsGenerated == true)
                {
                    var generateItem = new ToolStripMenuItem("Generate / Regenerate Generated Value", null, (s, args) => GenerateVariableMatrixValue(matrix, e.RowIndex, e.ColumnIndex));
                    if (GeneratedValueService.NormalizeScope(variable.GeneratorScope) == GeneratedValueService.ScopeOwnerEnvironment && target.EnvironmentId == null)
                    {
                        generateItem.Enabled = false;
                        generateItem.ToolTipText = "Select a concrete environment column for owner-environment generated values.";
                    }
                    menu.Items.Add(generateItem);
                }
                menu.Items.Add("Delete Direct Value", null, (s, args) => DeleteVariableMatrixDirectValue(matrix, e.RowIndex, e.ColumnIndex));
            }
            menu.Show(matrix, matrix.PointToClient(Cursor.Position));
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
            if (matrix == null) return;
            var changedColumnName = matrix.Columns[e.ColumnIndex].Name;
            if (changedColumnName != "Owner" && changedColumnName != "Use" && changedColumnName != "Visible" && changedColumnName != "Override") return;
            var variableId = Convert.ToString(matrix.Tag);
            var serviceId = Convert.ToString(matrix.Rows[e.RowIndex].Tag);
            if (string.IsNullOrWhiteSpace(variableId)) return;
            var variable = project.Variables.FirstOrDefault(v => v.Id == variableId);
            if (variable == null) return;

            if (changedColumnName == "Owner")
            {
                var ownerChecked = Convert.ToBoolean(matrix.Rows[e.RowIndex].Cells["Owner"].Value ?? false);
                if (!ownerChecked)
                {
                    RefreshAfterVariableMatrixChange(matrix, variable, e.RowIndex, e.ColumnIndex);
                    return;
                }
                var oldOwnerServiceId = variable.OwnerServiceId;
                if (!SameNullable(oldOwnerServiceId, serviceId))
                {
                    var movable = CountMovableOwnerValues(variable.Id, oldOwnerServiceId, serviceId);
                    if (movable > 0)
                    {
                        var answer = MessageBox.Show(
                            this,
                            $"Move {movable} owner value(s) to the new owner?",
                            "Change Owner",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question);
                        if (answer == DialogResult.Yes)
                        {
                            ProjectService.MoveOwnerValues(project, variable.Id, oldOwnerServiceId, serviceId);
                        }
                    }
                }
                variable.OwnerServiceId = serviceId;
                if (!string.IsNullOrWhiteSpace(serviceId))
                {
                    var existing = project.Contracts.FirstOrDefault(c => c.VariableId == variableId && c.ServiceId == serviceId);
                    SetVariableServiceContractFlags(variableId, serviceId, existing != null && !existing.Excluded, true, true);
                }
                RefreshAfterVariableMatrixChange(matrix, variable, e.RowIndex, e.ColumnIndex);
                return;
            }

            if (string.IsNullOrWhiteSpace(serviceId)) return;
            var export = Convert.ToBoolean(matrix.Rows[e.RowIndex].Cells["Use"].Value ?? false);
            var visible = Convert.ToBoolean(matrix.Rows[e.RowIndex].Cells["Visible"].Value ?? false);
            var allowOverride = Convert.ToBoolean(matrix.Rows[e.RowIndex].Cells["Override"].Value ?? false);
            var wasVisible = ProjectService.IsVariableVisibleToService(project, variable, serviceId);
            if (wasVisible && !visible)
            {
                var references = CountScopeInterpolationReferences(variable, serviceId);
                if (references > 0)
                {
                    var answer = MessageBox.Show(
                        this,
                        $"{variable.Key} is referenced by {references} value(s) in this service scope. Remove it from scope anyway?",
                        "Scope",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);
                    if (answer != DialogResult.Yes)
                    {
                        RefreshAfterVariableMatrixChange(matrix, variable, e.RowIndex, e.ColumnIndex);
                        return;
                    }
                }
            }
            if (!visible)
            {
                export = false;
                allowOverride = false;
            }
            SetVariableServiceContractFlags(variableId, serviceId, export, visible, allowOverride);

            RefreshAfterVariableMatrixChange(matrix, variable, e.RowIndex, e.ColumnIndex);
        }

        private int CountScopeInterpolationReferences(VariableDefinitionModel variable, string serviceId)
        {
            if (variable == null || string.IsNullOrWhiteSpace(serviceId)) return 0;
            var token = VariablePlaceholder(variable.Key);
            if (string.IsNullOrWhiteSpace(token)) return 0;

            return project.Environments.Select(e => e.Id).Concat(new string[] { null })
                .SelectMany(environmentId => BuildRawEffectiveValues(serviceId, environmentId))
                .Where(value => value.Variable.Id != variable.Id && !value.Missing && !string.IsNullOrEmpty(value.Value))
                .Select(value => value.Value)
                .Distinct()
                .Count(value => value.Contains(token));
        }

        private int CountMovableOwnerValues(string variableId, string oldOwnerServiceId, string newOwnerServiceId)
        {
            var before = CloneProject(project);
            return ProjectService.MoveOwnerValues(before, variableId, oldOwnerServiceId, newOwnerServiceId);
        }

        private void SetVariableServiceContractFlags(string variableId, string serviceId, bool export, bool visible, bool allowOverride)
        {
            var existing = project.Contracts.FirstOrDefault(c => c.VariableId == variableId && c.ServiceId == serviceId);
            if (existing == null)
            {
                existing = new VariableContractModel
                {
                    Id = ProjectService.NewId(),
                    VariableId = variableId,
                    ServiceId = serviceId,
                    SortOrder = project.Contracts.Count * 10
                };
                project.Contracts.Add(existing);
            }

            existing.Excluded = !export;
            existing.Required = export;
            existing.VisibleToService = visible;
            existing.AllowOverride = visible && allowOverride;
            existing.ShareWithOtherServices = visible;
        }

        private void VariableMatrixKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Delete) return;
            var matrix = sender as DataGridView;
            if (matrix?.CurrentCell == null || matrix.CurrentCell.RowIndex < 0 || matrix.CurrentCell.ColumnIndex <= 4) return;
            DeleteVariableMatrixDirectValue(matrix, matrix.CurrentCell.RowIndex, matrix.CurrentCell.ColumnIndex);
            e.Handled = true;
        }

        private void SetVariableMatrixDirectValue(DataGridView matrix, int rowIndex, int columnIndex)
        {
            var target = MatrixTargetFromCell(matrix, rowIndex, columnIndex);
            if (target == null) return;
            if (!ProjectService.CanOverrideVariableForService(project, target.Variable, target.ServiceId))
            {
                MessageBox.Show(this, "This variable is visible in the selected service, but overriding it is not allowed.", "Set Value", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var existing = FindDirectValue(target.Variable.Id, target.Scope, target.ServiceId, target.EnvironmentId);
            var orphanedBlankServiceValue = FindOrphanedBlankServiceValue(target);
            var currentValue = NewerValue(orphanedBlankServiceValue, existing) ?? BuildDisplayEffective(target.ServiceId, target.EnvironmentId)
                .FirstOrDefault(x => x.Variable.Id == target.Variable.Id && !x.Missing)
                ?.Value ?? string.Empty;
            var value = PromptDialog.Show(this, "Set Value", $"{target.Variable.Key} value for {target.Label}:", currentValue);
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
                    UpdatedAtUtc = DateTime.UtcNow
                });
            }
            else
            {
                existing.Value = value;
                existing.IsEncrypted = target.Variable.IsSecret;
                existing.UpdatedAtUtc = DateTime.UtcNow;
            }
            if (orphanedBlankServiceValue != null)
            {
                project.Values.Remove(orphanedBlankServiceValue);
            }

            EnsureContractForMatrixTarget(target);
            RefreshAfterVariableMatrixChange(matrix, target.Variable, rowIndex, columnIndex);
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
            RefreshAfterVariableMatrixChange(matrix, target.Variable, rowIndex, columnIndex);
        }

        private void GenerateVariableMatrixValue(DataGridView matrix, int rowIndex, int columnIndex)
        {
            var target = MatrixTargetFromCell(matrix, rowIndex, columnIndex);
            if (target == null || !target.Variable.IsGenerated) return;
            var environmentId = GeneratedValueService.NormalizeScope(target.Variable.GeneratorScope) == GeneratedValueService.ScopeOwnerEnvironment
                ? target.EnvironmentId
                : null;
            if (GeneratedValueService.NormalizeScope(target.Variable.GeneratorScope) == GeneratedValueService.ScopeOwnerEnvironment && string.IsNullOrWhiteSpace(environmentId))
            {
                MessageBox.Show(this, "Select a concrete environment column for owner-environment generated values.", "Generate Value", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var canonical = generatedValueService.BuildCanonicalTarget(target.Variable, environmentId);
            var service = string.IsNullOrWhiteSpace(canonical.ServiceId)
                ? "Global service"
                : project.Services.FirstOrDefault(s => s.Id == canonical.ServiceId)?.Name ?? canonical.ServiceId;
            var environment = string.IsNullOrWhiteSpace(canonical.EnvironmentId)
                ? "Global environment"
                : project.Environments.FirstOrDefault(e => e.Id == canonical.EnvironmentId)?.Name ?? canonical.EnvironmentId;
            if (MessageBox.Show(this, $"Regenerate {target.Variable.Key} for {service} / {environment}?", "Generate Value", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            generatedValueService.Generate(project, target.Variable, environmentId, true);
            RefreshAfterVariableMatrixChange(matrix, target.Variable, rowIndex, columnIndex);
        }

        private void RefreshAfterVariableMatrixChange(DataGridView matrix, VariableDefinitionModel variable, int rowIndex, int columnIndex)
        {
            var selectedVariableId = variable?.Id ?? SelectedId();
            var firstDisplayedScrollingRowIndex = mainGrid?.FirstDisplayedScrollingRowIndex >= 0 ? mainGrid.FirstDisplayedScrollingRowIndex : -1;
            modified = true;
            SaveRecoveryBackupIfPossible();
            RefreshVariableGrid();
            RestoreVariableGridSelection(selectedVariableId, firstDisplayedScrollingRowIndex);
            BuildVariableServiceEnvironmentMatrix(matrix, variable);
            RestoreVariableMatrixCell(matrix, rowIndex, columnIndex);
            RefreshStatus();
        }

        private static void RestoreVariableMatrixCell(DataGridView matrix, int rowIndex, int columnIndex)
        {
            if (matrix == null || matrix.Rows.Count == 0 || matrix.Columns.Count == 0) return;
            rowIndex = Math.Max(0, Math.Min(rowIndex, matrix.Rows.Count - 1));
            columnIndex = Math.Max(0, Math.Min(columnIndex, matrix.Columns.Count - 1));
            matrix.CurrentCell = matrix.Rows[rowIndex].Cells[columnIndex];
            matrix.Rows[rowIndex].Selected = true;
        }

        private static void SelectGridCell(DataGridView grid, int rowIndex, int columnIndex)
        {
            grid.ClearSelection();
            grid.CurrentCell = grid.Rows[rowIndex].Cells[columnIndex];
            grid.Rows[rowIndex].Selected = true;
            grid.Rows[rowIndex].Cells[columnIndex].Selected = true;
        }

        private static void CopyText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                Clipboard.Clear();
                return;
            }
            Clipboard.SetText(text);
        }

        private static string VariablePlaceholder(string key)
        {
            return string.IsNullOrWhiteSpace(key) ? string.Empty : "{{" + key + "}}";
        }

        private MatrixValueTarget MatrixTargetFromCell(DataGridView matrix, int rowIndex, int columnIndex)
        {
            var variableId = Convert.ToString(matrix.Tag);
            var variable = project.Variables.FirstOrDefault(v => v.Id == variableId);
            if (variable == null || rowIndex < 0 || columnIndex <= 1) return null;

            var row = matrix.Rows[rowIndex];
            var column = matrix.Columns[columnIndex];
            var serviceId = row.Tag as string;
            if (string.IsNullOrWhiteSpace(serviceId)) serviceId = null;
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

        private VariableValueModel FindOrphanedBlankServiceValue(MatrixValueTarget target)
        {
            if (target.Scope != ValueScope.Global && target.Scope != ValueScope.Environment) return null;
            return project.Values.LastOrDefault(v =>
                v.VariableId == target.Variable.Id &&
                (v.Scope == ValueScope.Service || v.Scope == ValueScope.ServiceEnvironment) &&
                string.IsNullOrWhiteSpace(v.ServiceId) &&
                v.EnvironmentId == target.EnvironmentId);
        }

        private static string NewerValue(VariableValueModel left, VariableValueModel right)
        {
            if (left == null) return right?.Value;
            if (right == null) return left.Value;
            return left.UpdatedAtUtc >= right.UpdatedAtUtc ? left.Value : right.Value;
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
                .Select(v => EffectiveConfigService.BuildRawValue(project, v, serviceId, environmentId))
                .ToList();
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
                SharedWithoutContract = s.AllowSharedVariablesWithoutContract,
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

        private void RenderScopeView()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            AddCommand(buttons, "Apply Changes", ApplyScopeMatrix, "Apply", 132);
            buttons.Controls.Add(new Label
            {
                Text = "Scope Matrix: NONE, Read, Override, Export, Full",
                AutoSize = true,
                Padding = new Padding(12, 7, 0, 0),
                Font = new Font(Font, FontStyle.Bold)
            });

            scopeMatrix = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.CellSelect
            };
            scopeMatrix.DataError += (s, e) => { e.ThrowException = false; };
            scopeMatrix.CellValueChanged += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
                var cell = scopeMatrix.Rows[e.RowIndex].Cells[e.ColumnIndex];
                ApplyScopeModeCellStyle(cell, Convert.ToString(cell.Value));
            };
            scopeMatrix.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (scopeMatrix.IsCurrentCellDirty)
                {
                    scopeMatrix.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }
            };
            BuildScopeMatrix();
            AttachColumnWidthPersistence(scopeMatrix, "Scope");
            RestoreColumnWidths(scopeMatrix, "Scope");
            root.Controls.Add(buttons, 0, 0);
            root.Controls.Add(scopeMatrix, 0, 1);
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

        private void BuildScopeMatrix()
        {
            scopeMatrix.Columns.Clear();
            scopeMatrix.Rows.Clear();
            scopeMatrix.Columns.Add(new DataGridViewTextBoxColumn { Name = "VariableId", HeaderText = "VariableId", Visible = false });
            scopeMatrix.Columns.Add(new DataGridViewTextBoxColumn { Name = "Owner", HeaderText = "Owner", ReadOnly = true, FillWeight = 80 });
            scopeMatrix.Columns.Add(new DataGridViewTextBoxColumn { Name = "Variable", HeaderText = "Variable", ReadOnly = true, FillWeight = 160 });

            var services = project.Services.OrderBy(s => s.SortOrder).ThenBy(s => s.Name).ToList();
            foreach (var service in services)
            {
                var column = new DataGridViewComboBoxColumn
                {
                    Name = "svc_" + service.Id,
                    HeaderText = service.Name,
                    Tag = service.Id,
                    FillWeight = 80,
                    FlatStyle = FlatStyle.Flat
                };
                column.Items.AddRange(ScopeModes.Cast<object>().ToArray());
                scopeMatrix.Columns.Add(column);
            }

            var serviceById = services.ToDictionary(s => s.Id, s => s.Name);
            var variables = project.Variables
                .OrderBy(v => OwnerSortOrder(v.OwnerServiceId))
                .ThenBy(v => OwnerDisplayName(v.OwnerServiceId, serviceById))
                .ThenBy(v => v.GroupName)
                .ThenBy(v => v.SortOrder)
                .ThenBy(v => v.Key);

            foreach (var variable in variables)
            {
                var rowIndex = scopeMatrix.Rows.Add();
                var row = scopeMatrix.Rows[rowIndex];
                row.Cells["VariableId"].Value = variable.Id;
                row.Cells["Owner"].Value = OwnerDisplayName(variable.OwnerServiceId, serviceById);
                row.Cells["Variable"].Value = variable.Key;
                foreach (var service in services)
                {
                    var isOwner = string.Equals(variable.OwnerServiceId, service.Id, StringComparison.Ordinal);
                    var mode = isOwner ? "---" : ScopeModeFromState(variable, service.Id);
                    var cell = row.Cells["svc_" + service.Id];
                    if (isOwner)
                    {
                        cell = new DataGridViewTextBoxCell();
                        row.Cells["svc_" + service.Id] = cell;
                        cell.ReadOnly = true;
                    }
                    cell.Value = mode;
                    ApplyScopeModeCellStyle(cell, mode);
                }
            }
        }

        private void ApplyScopeMatrix()
        {
            if (scopeMatrix == null) return;
            foreach (DataGridViewRow row in scopeMatrix.Rows)
            {
                var variableId = Convert.ToString(row.Cells["VariableId"].Value);
                var variable = project.Variables.FirstOrDefault(v => v.Id == variableId);
                if (variable == null) continue;

                foreach (DataGridViewColumn column in scopeMatrix.Columns)
                {
                    if (!(column is DataGridViewComboBoxColumn)) continue;
                    var serviceId = Convert.ToString(column.Tag);
                    var mode = Convert.ToString(row.Cells[column.Name].Value);
                    if (string.Equals(variable.OwnerServiceId, serviceId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    ParseScopeMode(mode, out var visible, out var allowOverride, out var export);

                    if (!visible && !export && !allowOverride)
                    {
                        project.Contracts.RemoveAll(c => c.VariableId == variable.Id && c.ServiceId == serviceId);
                    }
                    else
                    {
                        ProjectService.EnsureServiceScopeContract(project, variable.Id, serviceId, export, visible, allowOverride);
                    }
                }
            }

            Changed();
            BuildScopeMatrix();
        }

        private string ScopeModeFromState(VariableDefinitionModel variable, string serviceId)
        {
            var visible = ProjectService.IsVariableVisibleToService(project, variable, serviceId);
            if (!visible) return ScopeModeNone;

            var allowOverride = ProjectService.CanOverrideVariableForService(project, variable, serviceId);
            var export = IsVariableUsedByService(variable.Id, serviceId);
            if (allowOverride && export) return ScopeModeFull;
            if (allowOverride) return ScopeModeOverride;
            if (export) return ScopeModeExport;
            return ScopeModeRead;
        }

        private static void ParseScopeMode(string mode, out bool visible, out bool allowOverride, out bool export)
        {
            visible = false;
            allowOverride = false;
            export = false;

            if (string.Equals(mode, ScopeModeRead, StringComparison.OrdinalIgnoreCase))
            {
                visible = true;
            }
            else if (string.Equals(mode, ScopeModeOverride, StringComparison.OrdinalIgnoreCase))
            {
                visible = true;
                allowOverride = true;
            }
            else if (string.Equals(mode, ScopeModeExport, StringComparison.OrdinalIgnoreCase))
            {
                visible = true;
                export = true;
            }
            else if (string.Equals(mode, ScopeModeFull, StringComparison.OrdinalIgnoreCase))
            {
                visible = true;
                allowOverride = true;
                export = true;
            }
        }

        private static void ApplyScopeModeCellStyle(DataGridViewCell cell, string mode)
        {
            if (cell == null) return;
            cell.Style.BackColor = ScopeModeBackColor(mode);
            cell.Style.ForeColor = ScopeModeForeColor(mode);
            cell.Style.SelectionBackColor = ControlPaint.Dark(ScopeModeBackColor(mode));
            cell.Style.SelectionForeColor = ScopeModeForeColor(mode);
        }

        private static Color ScopeModeBackColor(string mode)
        {
            if (string.Equals(mode, "---", StringComparison.OrdinalIgnoreCase)) return SystemColors.ControlLight;
            if (string.Equals(mode, ScopeModeRead, StringComparison.OrdinalIgnoreCase)) return Color.FromArgb(226, 239, 218);
            if (string.Equals(mode, ScopeModeOverride, StringComparison.OrdinalIgnoreCase)) return Color.FromArgb(255, 242, 204);
            if (string.Equals(mode, ScopeModeExport, StringComparison.OrdinalIgnoreCase)) return Color.FromArgb(221, 235, 247);
            if (string.Equals(mode, ScopeModeFull, StringComparison.OrdinalIgnoreCase)) return Color.FromArgb(248, 203, 173);
            return Color.FromArgb(224, 224, 224);
        }

        private static Color ScopeModeForeColor(string mode)
        {
            return string.Equals(mode, ScopeModeNone, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mode, "---", StringComparison.OrdinalIgnoreCase)
                ? SystemColors.GrayText
                : SystemColors.ControlText;
        }

        private int OwnerSortOrder(string ownerServiceId)
        {
            return project.Services.FirstOrDefault(s => string.Equals(s.Id, ownerServiceId, StringComparison.Ordinal))?.SortOrder ?? int.MaxValue;
        }

        private static string OwnerDisplayName(string ownerServiceId, IDictionary<string, string> serviceById)
        {
            if (string.IsNullOrWhiteSpace(ownerServiceId)) return "(none)";
            return serviceById.TryGetValue(ownerServiceId, out var name) ? name : ownerServiceId;
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
            var validate = AddCommand(buttons, "Validate All", () => RefreshValidationResults(null), "Validate", 116);
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
            AddCommand(buttons, "Add", add, "Add", 86);
            if (edit != null) AddCommand(buttons, "Edit", edit, "Edit", 86);
            if (delete != null) AddCommand(buttons, "Delete", delete, "Delete", 96);
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
                if (suppressColumnWidthPersistence || gridsRestoringColumns.Contains(grid)) return;
                SaveColumnWidths(grid, gridKey);
            };
            grid.Disposed += (s, e) => gridsRestoringColumns.Remove(grid);
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
            gridsRestoringColumns.Add(grid);
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
                gridsRestoringColumns.Remove(grid);
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
                { "Owner", 120 },
                { "Export", 60 },
                { "Secret", 56 },
                { "AllowSharedSecret", 92 },
                { "Required", 60 },
                { "AllowNull", 75 },
                { "Group", 140 },
                { "DemoValue", 180 },
                { "DemoComment", 180 },
                { "Description", 240 },
                { "Service", 170 },
                { "ServiceEnvironment", 185 },
                { "Effective", 245 },
                { "Source", 100 },
                { "Updated", 75 }
            };
        }

        private DataGridView BuildVariablesGrid(TableLayoutPanel root)
        {
            var grid = BuildGrid();
            grid.ReadOnly = false;
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "Id", Visible = false, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Validation", HeaderText = "!", ReadOnly = true, FillWeight = 35 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Key", HeaderText = "Key", ReadOnly = true, FillWeight = 150 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Owner", HeaderText = "Owner", ReadOnly = true, FillWeight = 75 });
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Export", HeaderText = "Export", FillWeight = 60 });
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Secret", HeaderText = "Secret", FillWeight = 55 });
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "AllowSharedSecret", HeaderText = "Shared Secret", FillWeight = 80 });
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Required", HeaderText = "Required", FillWeight = 65 });
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "AllowNull", HeaderText = "Allow Null", FillWeight = 75 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Group", HeaderText = "Group" });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "DemoValue", HeaderText = "Demo value" });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "DemoComment", HeaderText = "Demo comment" });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Description", HeaderText = "Description" });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Service", HeaderText = "Service", ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ServiceEnvironment", HeaderText = "ServiceEnvironment", ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Effective", HeaderText = "Calculated", ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Source", HeaderText = "Source", ReadOnly = true, FillWeight = 70 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Updated", HeaderText = "Updated", ReadOnly = true, FillWeight = 70 });
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
            grid.ColumnHeaderMouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    ShowVariableColumnFilterMenu(grid, e.ColumnIndex);
                }
            };
            grid.CellMouseDown += VariableGridCellMouseDown;
            grid.CellDoubleClick += VariableGridCellDoubleClick;
            grid.CellValueChanged += (s, e) => ApplyVariableGridChange(grid, root, e.RowIndex, e.ColumnIndex);
            grid.DataError += (s, e) => { e.ThrowException = false; };
            AttachColumnWidthPersistence(grid, "Variables");
            RestoreColumnWidths(grid, "Variables");
            return grid;
        }

        private void VariableGridCellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            var grid = sender as DataGridView;
            if (grid == null) return;
            if (grid.Columns[e.ColumnIndex] is DataGridViewCheckBoxColumn) return;
            SelectGridCell(grid, e.RowIndex, e.ColumnIndex);
            EditVariable();
        }

        private void VariableGridCellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right || e.RowIndex < 0 || e.ColumnIndex < 0) return;
            var grid = sender as DataGridView;
            if (grid == null) return;
            SelectGridCell(grid, e.RowIndex, e.ColumnIndex);
            var row = grid.Rows[e.RowIndex];
            var key = Convert.ToString(row.Cells["Key"].Value);
            var menu = new ContextMenuStrip();
            menu.Items.Add("Copy Key", null, (s, args) => CopyText(key));
            menu.Items.Add("Copy {{KEY}}", null, (s, args) => CopyText(VariablePlaceholder(key)));
            menu.Items.Add("Copy Value", null, (s, args) => CopyText(Convert.ToString(row.Cells[e.ColumnIndex].Value)));
            menu.Items.Add("Copy Effective Value", null, (s, args) => CopyText(Convert.ToString(row.Cells["Effective"].Value)));
            menu.Show(grid, grid.PointToClient(Cursor.Position));
        }

        private void RefreshVariableGrid()
        {
            if (mainGrid == null || project == null || currentView != "Variables") return;
            var selectedId = SelectedId();
            var firstDisplayedScrollingRowIndex = mainGrid.FirstDisplayedScrollingRowIndex >= 0 ? mainGrid.FirstDisplayedScrollingRowIndex : 0;
            var env = SelectedEnvironment();
            var svc = SelectedService();
            var ownerFilter = SelectedOwnerFilter();
            var scopeFilter = SelectedScopeFilter();
            var exportScopeServiceId = SelectedExportScopeServiceId();
            var search = searchBox?.Text?.Trim();
            var validationByVariable = validationService.Validate(project)
                .Where(r => !string.IsNullOrWhiteSpace(r.VariableId))
                .GroupBy(r => r.VariableId)
                .ToDictionary(g => g.Key, g => g.ToList());
            if (mainGrid.Columns.Contains("Export"))
            {
                mainGrid.Columns["Export"].Visible = exportScopeServiceId != null;
                mainGrid.Columns["Export"].ToolTipText = "Export this variable for the selected Scope service.";
            }
            var hasScope = svc != null;
            SetVariableGridScopeMode(hasScope);
            mainGrid.Rows.Clear();
            foreach (var v in project.Variables.OrderBy(v => v.SortOrder).Where(v => string.IsNullOrWhiteSpace(search) || v.Key.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                if (!PassesOwnerFilter(v, ownerFilter) || !PassesScopeFilter(v, scopeFilter))
                {
                    continue;
                }

                var effective = BuildDisplayEffective(svc?.Id, env?.Id).FirstOrDefault(x => x.Variable.Id == v.Id);
                var selected = FindSelectedLayerValue(v.Id);
                var required = svc != null && project.Contracts.Any(c => c.ServiceId == svc.Id && c.VariableId == v.Id && !c.Excluded && c.Required);
                var serviceValue = FindValue(v.Id, ValueScope.Service, svc?.Id, null);
                var serviceEnvironmentValue = FindValue(v.Id, ValueScope.ServiceEnvironment, svc?.Id, env?.Id);
                var effectiveValue = DisplayValue(v, effective?.Value ?? selected.Value);
                var source = effective?.SourceScope.ToString() ?? selected.Source;
                var updatedAt = effective?.SourceUpdatedAt ?? selected.UpdatedAt;
                var updated = FormatUpdatedAtAge(updatedAt);
                var export = exportScopeServiceId != null && IsVariableUsedByService(v.Id, exportScopeServiceId);
                if (!PassesVariableColumnFilters(v, export, required, serviceValue, serviceEnvironmentValue, effectiveValue, source, updated, hasScope))
                {
                    continue;
                }

                var rowIndex = mainGrid.Rows.Add(
                    v.Id,
                    ValidationIndicator(v.Id, validationByVariable),
                    v.Key,
                    OwnerDisplayName(v),
                    export,
                    v.IsSecret,
                    v.AllowSharedSecret,
                    required,
                    v.AllowNull,
                    v.GroupName,
                    v.DemoValue,
                    v.DemoComment,
                    v.Description,
                    serviceValue,
                    serviceEnvironmentValue,
                    effectiveValue,
                    source,
                    updated);
                var row = mainGrid.Rows[rowIndex];
                row.Cells["Updated"].ToolTipText = FormatUpdatedAtExact(updatedAt);
                ApplyValidationCellStyle(row.Cells["Validation"], v.Id, validationByVariable);
                var exportEditable = exportScopeServiceId != null && ProjectService.IsVariableVisibleToService(project, v, exportScopeServiceId);
                row.Cells["Export"].ReadOnly = !exportEditable;
                row.Cells["Export"].Style.BackColor = exportEditable ? Color.White : SystemColors.ControlLight;
                row.Cells["Export"].ToolTipText = exportEditable
                        ? "Export this variable for the selected scope service."
                        : "The variable is not in scope for the selected service.";
                if (svc == null)
                {
                    row.Cells["Required"].ReadOnly = true;
                    row.Cells["Required"].Style.BackColor = SystemColors.ControlLight;
                }
                else
                {
                    row.Cells["Required"].ReadOnly = !ProjectService.IsVariableVisibleToService(project, v, svc.Id);
                    row.Cells["Required"].Style.BackColor = row.Cells["Required"].ReadOnly ? SystemColors.ControlLight : Color.White;
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

        private void SetVariableGridScopeMode(bool hasScope)
        {
            SetColumnVisible("Group", !hasScope);
            SetColumnVisible("DemoValue", !hasScope);
            SetColumnVisible("DemoComment", !hasScope);
            SetColumnVisible("Description", !hasScope);
            SetColumnVisible("Service", hasScope);
            SetColumnVisible("ServiceEnvironment", hasScope);
            SetColumnVisible("Effective", hasScope);
            SetColumnVisible("Source", hasScope);
            SetColumnVisible("Updated", hasScope);
            SetColumnVisible("Required", hasScope);
        }

        private void SetColumnVisible(string columnName, bool visible)
        {
            if (mainGrid?.Columns.Contains(columnName) == true)
            {
                mainGrid.Columns[columnName].Visible = visible;
            }
        }

        private string SelectedOwnerFilter()
        {
            return (ownerFilterCombo?.SelectedItem as ScopeSelectorItem)?.Id;
        }

        private string SelectedScopeFilter()
        {
            return (scopeFilterCombo?.SelectedItem as ScopeSelectorItem)?.Id;
        }

        private string SelectedExportScopeServiceId()
        {
            var scope = SelectedScopeFilter();
            return string.IsNullOrWhiteSpace(scope) ? null : scope;
        }

        private string DefaultVariableOwnerServiceId()
        {
            var owner = SelectedOwnerFilter();
            if (!string.IsNullOrWhiteSpace(owner)) return owner;
            var scope = SelectedScopeFilter();
            if (!string.IsNullOrWhiteSpace(scope)) return scope;
            return project.Services.OrderBy(s => s.SortOrder).ThenBy(s => s.Name).FirstOrDefault()?.Id;
        }

        private bool PassesOwnerFilter(VariableDefinitionModel variable, string ownerFilter)
        {
            if (string.IsNullOrWhiteSpace(ownerFilter)) return true;
            return string.Equals(variable.OwnerServiceId, ownerFilter, StringComparison.Ordinal);
        }

        private bool PassesScopeFilter(VariableDefinitionModel variable, string scopeFilter)
        {
            if (string.IsNullOrWhiteSpace(scopeFilter)) return true;
            return ProjectService.IsVariableVisibleToService(project, variable, scopeFilter);
        }

        private string OwnerDisplayName(VariableDefinitionModel variable)
        {
            return project.Services.FirstOrDefault(s => s.Id == variable.OwnerServiceId)?.Name ?? variable.OwnerServiceId;
        }

        private static string DisplayInfoValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value;
        }

        private static string FormatUpdatedAtAge(string value)
        {
            if (!TryParseUpdatedAt(value, out var updatedAt))
            {
                return "-";
            }

            var elapsed = DateTime.UtcNow - updatedAt;
            if (elapsed < TimeSpan.Zero)
            {
                elapsed = TimeSpan.Zero;
            }

            if (elapsed.TotalMinutes < 1) return "now";
            if (elapsed.TotalHours < 1) return Math.Max(1, (int)elapsed.TotalMinutes) + "min";
            if (elapsed.TotalDays < 1) return Math.Max(1, (int)elapsed.TotalHours) + "h";
            if (elapsed.TotalDays < 365) return Math.Max(1, (int)elapsed.TotalDays) + "d";
            return Math.Max(1, (int)(elapsed.TotalDays / 365)) + "y";
        }

        private static string FormatUpdatedAtExact(string value)
        {
            return TryParseUpdatedAt(value, out var updatedAt)
                ? updatedAt.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)
                : DisplayInfoValue(value);
        }

        private static bool TryParseUpdatedAt(string value, out DateTime updatedAt)
        {
            updatedAt = DateTime.MinValue;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (value.StartsWith("/Date(", StringComparison.Ordinal) && value.EndsWith(")/", StringComparison.Ordinal))
            {
                var milliseconds = value.Substring(6, value.Length - 8);
                var offsetIndex = milliseconds.IndexOfAny(new[] { '+', '-' }, 1);
                if (offsetIndex > 0)
                {
                    milliseconds = milliseconds.Substring(0, offsetIndex);
                }

                if (long.TryParse(milliseconds, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixMilliseconds))
                {
                    updatedAt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(unixMilliseconds);
                    return true;
                }
            }

            if (DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
            {
                updatedAt = parsed.ToUniversalTime();
                return true;
            }

            return false;
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
            bool export,
            bool required,
            string serviceValue,
            string serviceEnvironmentValue,
            string effectiveValue,
            string source,
            string updated,
            bool hasScope)
        {
            return PassesTextFilter("Key", variable.Key) &&
                PassesBoolFilter("Export", export) &&
                PassesBoolFilter("Secret", variable.IsSecret) &&
                PassesBoolFilter("AllowSharedSecret", variable.AllowSharedSecret) &&
                PassesTextFilter("Group", variable.GroupName) &&
                PassesTextFilter("DemoValue", variable.DemoValue) &&
                PassesTextFilter("DemoComment", variable.DemoComment) &&
                PassesTextFilter("Description", variable.Description) &&
                (!hasScope ||
                    (PassesBoolFilter("Required", required) &&
                    PassesTextFilter("Service", serviceValue) &&
                    PassesTextFilter("ServiceEnvironment", serviceEnvironmentValue) &&
                    PassesTextFilter("Effective", effectiveValue) &&
                    PassesTextFilter("Source", source) &&
                    PassesTextFilter("Updated", updated)));
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
            if (column.Name == "Export" || column.Name == "Secret" || column.Name == "AllowSharedSecret" || column.Name == "Required" || column.Name == "AllowNull")
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
            if (columnName == "AllowSharedSecret") return "Shared Secret";
            return fallback ?? columnName;
        }

        private void ApplyVariableGridChange(DataGridView grid, TableLayoutPanel root, int rowIndex, int columnIndex)
        {
            if (rowIndex < 0 || columnIndex < 0 || rowIndex >= grid.Rows.Count) return;
            var columnName = grid.Columns[columnIndex].Name;
            if (columnName != "Export" &&
                columnName != "Secret" &&
                columnName != "AllowSharedSecret" &&
                columnName != "Required" &&
                columnName != "AllowNull" &&
                columnName != "Group" &&
                columnName != "DemoValue" &&
                columnName != "DemoComment" &&
                columnName != "Description") return;

            var variableId = Convert.ToString(grid.Rows[rowIndex].Cells["Id"].Value);
            var variable = project.Variables.FirstOrDefault(v => v.Id == variableId);
            if (variable == null) return;

            if (columnName == "Group" || columnName == "DemoValue" || columnName == "DemoComment" || columnName == "Description")
            {
                var value = Convert.ToString(grid.Rows[rowIndex].Cells[columnName].Value);
                if (columnName == "Group") variable.GroupName = value;
                else if (columnName == "DemoValue") variable.DemoValue = value;
                else if (columnName == "DemoComment") variable.DemoComment = value;
                else variable.Description = value;

                modified = true;
                SaveRecoveryBackupIfPossible();
                RefreshVariableDetails(root);
                RefreshStatus();
                return;
            }

            var enabled = Convert.ToBoolean(grid.Rows[rowIndex].Cells[columnName].Value ?? false);
            if (columnName == "Export")
            {
                var serviceId = SelectedExportScopeServiceId();
                if (serviceId == null || !ProjectService.IsVariableVisibleToService(project, variable, serviceId))
                {
                    grid.Rows[rowIndex].Cells[columnName].Value = false;
                    return;
                }

                var visible = ProjectService.IsVariableVisibleToService(project, variable, serviceId);
                var allowOverride = ProjectService.CanOverrideVariableForService(project, variable, serviceId);
                SetVariableServiceContractFlags(variable.Id, serviceId, enabled, visible, allowOverride);
            }
            else if (columnName == "Secret")
            {
                variable.IsSecret = enabled;
                variable.Type = enabled ? VariableType.Password : VariableType.String;
                if (!enabled)
                {
                    variable.AllowSharedSecret = false;
                    grid.Rows[rowIndex].Cells["AllowSharedSecret"].Value = false;
                }
            }
            else if (columnName == "AllowSharedSecret")
            {
                variable.AllowSharedSecret = enabled;
                if (enabled)
                {
                    variable.IsSecret = true;
                    variable.Type = VariableType.Password;
                    grid.Rows[rowIndex].Cells["Secret"].Value = true;
                }
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
            RefreshVariableDisplayPreservingSelection(root);
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
                Height = 552,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false,
                MaximizeBox = false
            })
            {
                var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Padding = new Padding(10) };
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
                var form = new TableLayoutPanel { Dock = DockStyle.Top, Height = 13 * 32, ColumnCount = 2, RowCount = 13 };
                form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
                form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                for (var i = 0; i < 13; i++) form.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

                var idBox = AddCardTextBox(form, "Id:", 0, service.Id, true);
                var nameBox = AddCardTextBox(form, "Name:", 1, service.Name, false);
                var displayBox = AddCardTextBox(form, "Display name:", 2, service.DisplayName, false);
                var outputFolderBox = AddCardTextBox(form, "Output folder:", 3, service.OutputFolder, false);
                var prefixBox = AddCardTextBox(form, "Default prefix:", 4, service.DefaultPrefix, false);
                var activeBox = AddCardCheckBox(form, "Active:", 5, service.IsActive);
                var sharedWithoutContractBox = AddCardCheckBox(form, "Shared vars:", 6, service.AllowSharedVariablesWithoutContract);
                sharedWithoutContractBox.Text = "Allow without explicit contract";
                var configBox = AddCardTextBox(form, "CONFIG name:", 7, service.ConfigName, false);
                var tomlBox = AddCardTextBox(form, "TOML name:", 8, service.TomlName, false);
                var yamlBox = AddCardTextBox(form, "YAML name:", 9, service.YamlName, false);
                var xmlBox = AddCardTextBox(form, "XML name:", 10, service.XmlName, false);
                var jsonBox = AddCardTextBox(form, "JSON name:", 11, service.JsonName, false);
                var descriptionBox = AddCardTextBox(form, "Description:", 12, service.Description, false);

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
                    var outputFolder = outputFolderBox.Text.Trim();
                    var duplicateOutputFolder = project.Services.FirstOrDefault(s =>
                        s != service &&
                        string.Equals(NormalizeOutputFolderKey(s.OutputFolder), NormalizeOutputFolderKey(outputFolder), StringComparison.OrdinalIgnoreCase));
                    if (duplicateOutputFolder != null)
                    {
                        MessageBox.Show(this, "Service output folder must be unique. Empty output folder can be used by only one service.", dialog.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        continue;
                    }

                    service.Name = name;
                    service.DisplayName = DefaultIfBlank(displayBox.Text, name);
                    service.OutputFolder = outputFolder;
                    service.DefaultPrefix = prefixBox.Text;
                    service.IsActive = activeBox.Checked;
                    service.AllowSharedVariablesWithoutContract = sharedWithoutContractBox.Checked;
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
                var form = new TableLayoutPanel { Dock = DockStyle.Top, Height = 9 * 32, ColumnCount = 2, RowCount = 9 };
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
            return AddCardTextBox(form, label, row, value, readOnly, 0, 1);
        }

        private static TextBox AddCardTextBox(TableLayoutPanel form, string label, int row, string value, bool readOnly, int labelColumn, int inputColumn)
        {
            form.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, labelColumn, row);
            var box = new TextBox
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = new Padding(0, 4, 0, 0),
                Text = value ?? string.Empty,
                ReadOnly = readOnly
            };
            form.Controls.Add(box, inputColumn, row);
            return box;
        }

        private static CheckBox AddCardCheckBox(TableLayoutPanel form, string label, int row, bool value)
        {
            form.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
            var box = new CheckBox { Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 0, 0), Checked = value, AutoSize = true };
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
            var ownerServiceId = DefaultVariableOwnerServiceId();
            if (string.IsNullOrWhiteSpace(ownerServiceId))
            {
                MessageBox.Show(this, "Create at least one service before adding variables.", "Add Variable", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var variable = new VariableDefinitionModel
            {
                Id = UniqueVariableId("DATABASE_HOST"),
                Key = "DATABASE_HOST",
                DisplayName = "DATABASE_HOST",
                Type = VariableType.String,
                OwnerServiceId = ownerServiceId,
                IsActive = true,
                SortOrder = project.Variables.Count * 10
            };
            if (!ShowVariableCard(variable, true)) return;
            project.Variables.Add(variable);
            Changed();
        }

        private void EditVariable()
        {
            var variable = GetSelectedVariable();
            if (variable == null) return;
            if (!ShowVariableCard(variable, false)) return;
            var selectedId = variable.Id;
            var firstDisplayedScrollingRowIndex = mainGrid?.FirstDisplayedScrollingRowIndex >= 0 ? mainGrid.FirstDisplayedScrollingRowIndex : -1;
            modified = true;
            SaveRecoveryBackupIfPossible();
            RefreshVariableGrid();
            RestoreVariableGridSelection(selectedId, firstDisplayedScrollingRowIndex);
            RefreshVariableDetails(contentPanel.Controls.OfType<TableLayoutPanel>().FirstOrDefault());
            RefreshStatus();
        }

        private void RegenerateAllGeneratedVariables()
        {
            var variables = project?.Variables?.Where(v => v.IsActive && v.IsGenerated).OrderBy(v => v.SortOrder).ThenBy(v => v.Key).ToList();
            if (variables == null || variables.Count == 0)
            {
                MessageBox.Show(this, "There are no generated variables.", "Regenerate Generated", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var targets = variables.Sum(v =>
                GeneratedValueService.NormalizeScope(v.GeneratorScope) == GeneratedValueService.ScopeOwnerEnvironment
                    ? project.Environments.Count(e => e.IsActive)
                    : 1);
            if (MessageBox.Show(this, $"Regenerate {targets} generated value(s) for {variables.Count} variable(s)?", "Regenerate Generated", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            var selectedId = SelectedId();
            var firstDisplayedScrollingRowIndex = mainGrid?.FirstDisplayedScrollingRowIndex >= 0 ? mainGrid.FirstDisplayedScrollingRowIndex : -1;
            var count = 0;
            foreach (var variable in variables)
            {
                count += GenerateVariableValues(variable, null, true);
            }

            modified = true;
            SaveRecoveryBackupIfPossible();
            RefreshVariableGrid();
            RestoreVariableGridSelection(selectedId, firstDisplayedScrollingRowIndex);
            RefreshVariableDetails(contentPanel.Controls.OfType<TableLayoutPanel>().FirstOrDefault());
            RefreshStatus();
            MessageBox.Show(this, $"Regenerated {count} generated value(s).", "Regenerate Generated", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private bool ShowVariableCard(VariableDefinitionModel variable, bool isNew)
        {
            if (project.Services.Count == 0)
            {
                MessageBox.Show(this, "Create at least one service before editing variables.", isNew ? "Add Variable" : "Variable Card", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            using (var dialog = new Form
            {
                Text = isNew ? "Add Variable" : "Variable Card",
                Width = 860,
                Height = 720,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false,
                MaximizeBox = false
            })
            {
                var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, Padding = new Padding(10) };
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 360));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

                var form = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 8 };
                form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
                form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
                form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                for (var i = 0; i < 7; i++) form.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
                form.RowStyles.Add(new RowStyle(SizeType.Absolute, 128));

                var keyBox = AddCardTextBox(form, "Key:", 0, variable.Key, false);
                form.SetColumnSpan(keyBox, 3);
                var keyError = new ErrorProvider { ContainerControl = dialog, BlinkStyle = ErrorBlinkStyle.NeverBlink };

                form.Controls.Add(new Label { Text = "Owner:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
                var ownerCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Name" };
                var ownerItems = project.Services.OrderBy(s => s.SortOrder).ThenBy(s => s.Name).Select(s => new ScopeSelectorItem(s.Id, s.Name)).ToList();
                ownerCombo.Items.AddRange(ownerItems.Cast<object>().ToArray());
                var ownerIndex = ownerItems.FindIndex(i => string.Equals(i.Id, variable.OwnerServiceId, StringComparison.Ordinal));
                ownerCombo.SelectedIndex = ownerIndex >= 0 ? ownerIndex : 0;
                form.Controls.Add(ownerCombo, 1, 1);

                form.Controls.Add(new Label { Text = "Type:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 2, 1);
                var typeCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
                typeCombo.Items.AddRange(Enum.GetNames(typeof(VariableType)).Cast<object>().ToArray());
                typeCombo.SelectedItem = variable.Type.ToString();
                form.Controls.Add(typeCombo, 3, 1);

                var activeBox = AddCardCheckBox(form, "Active:", 2, variable.IsActive);
                var secretBox = AddCardCheckBox(form, "Secret:", 3, variable.IsSecret);
                var sharedSecretBox = AddCardCheckBox(form, "Shared secret:", 4, variable.AllowSharedSecret);
                var allowNullBox = AddCardCheckBox(form, "Allow null:", 5, variable.AllowNull);
                var allowBlankBox = AddCardCheckBox(form, "Allow blank:", 6, variable.AllowBlank);
                var groupBox = AddCardTextBox(form, "Group:", 2, variable.GroupName, false, 2, 3);
                var exampleBox = AddCardTextBox(form, "Demo value:", 3, variable.DemoValue, false, 2, 3);
                var placeholderBox = AddCardTextBox(form, "Demo comment:", 4, variable.DemoComment, false, 2, 3);
                var descriptionBox = AddCardTextBox(form, "Description:", 5, variable.Description, false, 2, 3);

                var summary = new Label
                {
                    Text = $"Values: {project.Values.Count(v => v.VariableId == variable.Id)}    References: {CountInterpolationReferences(variable.Key)}",
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                form.Controls.Add(new Label { Text = "Summary:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 2, 6);
                form.Controls.Add(summary, 3, 6);

                var generationGroup = new GroupBox { Text = "Generation", Dock = DockStyle.Fill };
                var generationForm = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 3, Padding = new Padding(8, 4, 8, 4) };
                generationForm.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
                generationForm.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                generationForm.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
                generationForm.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                generationForm.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
                generationForm.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
                generationForm.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
                generationGroup.Controls.Add(generationForm);
                form.Controls.Add(generationGroup, 0, 7);
                form.SetColumnSpan(generationGroup, 4);

                var generatedBox = AddCardCheckBox(generationForm, "Generated:", 0, variable.IsGenerated);
                var generatorTypeLabel = new Label { Text = "Generator:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
                generationForm.Controls.Add(generatorTypeLabel, 2, 0);
                var generatorTypeCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
                generatorTypeCombo.Items.AddRange(new object[] { GeneratedValueService.TypePassword, GeneratedValueService.TypeTokenHex, GeneratedValueService.TypeTokenBase62, GeneratedValueService.TypeGuid });
                generatorTypeCombo.SelectedItem = GeneratedValueService.NormalizeType(variable.GeneratorType);
                generationForm.Controls.Add(generatorTypeCombo, 3, 0);

                var generatorLengthLabel = new Label { Text = "Length:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
                generationForm.Controls.Add(generatorLengthLabel, 0, 1);
                var generatorLengthBox = new NumericUpDown { Dock = DockStyle.Left, Minimum = 8, Maximum = 4096, Width = 90, Value = GeneratedValueService.NormalizeLength(variable.GeneratorLength, variable.GeneratorType) };
                generationForm.Controls.Add(generatorLengthBox, 1, 1);
                var generatorScopeLabel = new Label { Text = "Gen scope:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
                generationForm.Controls.Add(generatorScopeLabel, 2, 1);
                var generatorScopeCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
                generatorScopeCombo.Items.AddRange(new object[] { GeneratedValueService.ScopeOwnerGlobal, GeneratedValueService.ScopeOwnerEnvironment });
                generatorScopeCombo.SelectedItem = GeneratedValueService.NormalizeScope(variable.GeneratorScope);
                generationForm.Controls.Add(generatorScopeCombo, 3, 1);

                var generatorModeLabel = new Label { Text = "Gen mode:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
                generationForm.Controls.Add(generatorModeLabel, 0, 2);
                var generatorModeCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
                generatorModeCombo.Items.AddRange(new object[] { GeneratedValueService.ModeManual, GeneratedValueService.ModeRotateOnSync });
                generatorModeCombo.SelectedItem = GeneratedValueService.NormalizeMode(variable.GeneratorMode);
                generationForm.Controls.Add(generatorModeCombo, 1, 2);
                var generateButton = new Button { Text = "Generate now", Width = 120, Height = 26, Enabled = !isNew };
                generationForm.Controls.Add(generateButton, 3, 2);
                Action updateGeneratorVisibility = () =>
                {
                    var visible = generatedBox.Checked;
                    generatorTypeLabel.Visible = visible;
                    generatorTypeCombo.Visible = visible;
                    generatorLengthLabel.Visible = visible;
                    generatorLengthBox.Visible = visible;
                    generatorScopeLabel.Visible = visible;
                    generatorScopeCombo.Visible = visible;
                    generatorModeLabel.Visible = visible;
                    generatorModeCombo.Visible = visible;
                    generateButton.Visible = visible;
                    generateButton.Enabled = visible && !isNew;
                };
                generatedBox.CheckedChanged += (s, e) => updateGeneratorVisibility();
                updateGeneratorVisibility();

                Action applyGeneratorFields = () =>
                {
                    variable.IsGenerated = generatedBox.Checked;
                    variable.GeneratorType = GeneratedValueService.NormalizeType(Convert.ToString(generatorTypeCombo.SelectedItem));
                    variable.GeneratorLength = GeneratedValueService.NormalizeLength((int)generatorLengthBox.Value, variable.GeneratorType);
                    variable.GeneratorScope = GeneratedValueService.NormalizeScope(Convert.ToString(generatorScopeCombo.SelectedItem));
                    variable.GeneratorMode = GeneratedValueService.NormalizeMode(Convert.ToString(generatorModeCombo.SelectedItem));
                    if (variable.IsGenerated)
                    {
                        variable.IsSecret = true;
                        variable.Type = VariableType.Password;
                        secretBox.Checked = true;
                        typeCombo.SelectedItem = VariableType.Password.ToString();
                    }
                };
                generateButton.Click += (s, e) =>
                {
                    if (!generatedBox.Checked)
                    {
                        MessageBox.Show(this, "Enable Generated first.", dialog.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    applyGeneratorFields();
                    var count = GenerateVariableValues(variable, null, true);
                    modified = true;
                    SaveRecoveryBackupIfPossible();
                    summary.Text = $"Values: {project.Values.Count(v => v.VariableId == variable.Id)}    References: {CountInterpolationReferences(variable.Key)}";
                    MessageBox.Show(this, $"Generated {count} value(s).", dialog.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                };

                secretBox.CheckedChanged += (s, e) =>
                {
                    if (secretBox.Checked) typeCombo.SelectedItem = VariableType.Password.ToString();
                    else if (string.Equals(Convert.ToString(typeCombo.SelectedItem), VariableType.Password.ToString(), StringComparison.Ordinal))
                    {
                        typeCombo.SelectedItem = VariableType.String.ToString();
                    }
                };
                sharedSecretBox.CheckedChanged += (s, e) =>
                {
                    if (sharedSecretBox.Checked)
                    {
                        secretBox.Checked = true;
                        typeCombo.SelectedItem = VariableType.Password.ToString();
                    }
                };

                var scopeHeader = new Label
                {
                    Text = "Service Scope: NONE / Read / Override / Export / Full",
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Font = new Font(Font, FontStyle.Bold)
                };
                var scopeGrid = BuildVariableCardScopeGrid(variable, SelectedScopeOwnerId(ownerCombo));
                ownerCombo.SelectedIndexChanged += (s, e) => PopulateVariableCardScopeGrid(scopeGrid, variable, SelectedScopeOwnerId(ownerCombo));

                var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
                var ok = new Button { Text = "OK", Width = 90, Height = 28, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Cancel", Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
                var validationMessage = new Label
                {
                    AutoSize = true,
                    ForeColor = Color.Firebrick,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(0, 7, 12, 0)
                };
                bottom.Controls.Add(cancel);
                bottom.Controls.Add(ok);
                bottom.Controls.Add(validationMessage);
                Action validateKey = () =>
                {
                    var error = ValidateVariableKey(variable, keyBox.Text);
                    keyError.SetError(keyBox, error);
                    validationMessage.Text = error;
                    keyBox.BackColor = string.IsNullOrEmpty(error) ? SystemColors.Window : Color.MistyRose;
                    ok.Enabled = string.IsNullOrEmpty(error);
                };
                keyBox.TextChanged += (s, e) => validateKey();
                keyBox.Leave += (s, e) => validateKey();
                validateKey();

                root.Controls.Add(form, 0, 0);
                root.Controls.Add(scopeHeader, 0, 1);
                root.Controls.Add(scopeGrid, 0, 2);
                root.Controls.Add(bottom, 0, 3);
                dialog.Controls.Add(root);
                dialog.AcceptButton = ok;
                dialog.CancelButton = cancel;

                while (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    var key = keyBox.Text.Trim().ToUpperInvariant();
                    var keyValidation = ValidateVariableKey(variable, key);
                    if (!string.IsNullOrEmpty(keyValidation))
                    {
                        keyError.SetError(keyBox, keyValidation);
                        validationMessage.Text = keyValidation;
                        keyBox.BackColor = Color.MistyRose;
                        ok.Enabled = false;
                        keyBox.Focus();
                        continue;
                    }

                    var oldKey = variable.Key;
                    var newOwnerServiceId = SelectedScopeOwnerId(ownerCombo);
                    var oldOwnerServiceId = variable.OwnerServiceId;
                    if (!isNew && !string.Equals(oldKey, key, StringComparison.OrdinalIgnoreCase))
                    {
                        var referenceCount = CountInterpolationReferences(oldKey);
                        if (referenceCount > 0)
                        {
                            var answer = MessageBox.Show(
                                this,
                                $"Replace {referenceCount} interpolation reference(s) from {{{{{oldKey}}}}} to {{{{{key}}}}}?",
                                dialog.Text,
                                MessageBoxButtons.YesNo,
                                MessageBoxIcon.Question);
                            if (answer == DialogResult.Yes)
                            {
                                ProjectService.ReplaceInterpolationReferences(project, oldKey, key);
                            }
                        }
                    }

                    if (!SameNullable(oldOwnerServiceId, newOwnerServiceId))
                    {
                        var movable = CountMovableOwnerValues(variable.Id, oldOwnerServiceId, newOwnerServiceId);
                        if (movable > 0)
                        {
                            var answer = MessageBox.Show(
                                this,
                                $"Move {movable} owner value(s) to the new owner?",
                                dialog.Text,
                                MessageBoxButtons.YesNo,
                                MessageBoxIcon.Question);
                            if (answer == DialogResult.Yes)
                            {
                                ProjectService.MoveOwnerValues(project, variable.Id, oldOwnerServiceId, newOwnerServiceId);
                            }
                        }
                    }

                    variable.Key = key;
                    variable.DisplayName = key;
                    variable.OwnerServiceId = newOwnerServiceId;
                    variable.Type = (VariableType)Enum.Parse(typeof(VariableType), Convert.ToString(typeCombo.SelectedItem));
                    variable.IsSecret = secretBox.Checked || variable.Type == VariableType.Password;
                    variable.AllowSharedSecret = sharedSecretBox.Checked && variable.IsSecret;
                    variable.AllowNull = allowNullBox.Checked;
                    variable.AllowBlank = allowBlankBox.Checked;
                    variable.IsActive = activeBox.Checked;
                    variable.GroupName = groupBox.Text;
                    variable.DemoValue = exampleBox.Text;
                    variable.DemoComment = placeholderBox.Text;
                    variable.Description = descriptionBox.Text;
                    applyGeneratorFields();
                    ApplyVariableCardScopeGrid(scopeGrid, variable);
                    return true;
                }
            }

            return false;
        }

        private string ValidateVariableKey(VariableDefinitionModel variable, string key)
        {
            key = (key ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                return "Key is required.";
            }

            return project.Variables.Any(v => v != variable && string.Equals(v.Key, key, StringComparison.OrdinalIgnoreCase))
                ? "Variable key already exists."
                : string.Empty;
        }

        private int GenerateVariableValues(VariableDefinitionModel variable, string environmentId, bool overwrite)
        {
            if (variable == null || !variable.IsGenerated) return 0;
            var scope = GeneratedValueService.NormalizeScope(variable.GeneratorScope);
            var environments = scope == GeneratedValueService.ScopeOwnerEnvironment
                ? (string.IsNullOrWhiteSpace(environmentId)
                    ? project.Environments.Where(e => e.IsActive).Select(e => e.Id).ToList()
                    : new List<string> { environmentId })
                : new List<string> { null };

            var count = 0;
            foreach (var env in environments)
            {
                generatedValueService.Generate(project, variable, env, overwrite);
                count++;
            }

            return count;
        }

        private DataGridView BuildVariableCardScopeGrid(VariableDefinitionModel variable, string ownerServiceId)
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                RowHeadersVisible = false
            };
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ServiceId", HeaderText = "ServiceId", Visible = false });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Service", HeaderText = "Service", ReadOnly = true, FillWeight = 140 });
            var modeColumn = new DataGridViewComboBoxColumn
            {
                Name = "Mode",
                HeaderText = "Mode",
                FillWeight = 120,
                FlatStyle = FlatStyle.Flat
            };
            modeColumn.Items.AddRange(ScopeModes.Cast<object>().ToArray());
            grid.Columns.Add(modeColumn);
            grid.DataError += (s, e) => { e.ThrowException = false; };
            grid.CellValueChanged += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0 || grid.Columns[e.ColumnIndex].Name != "Mode") return;
                var cell = grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
                ApplyScopeModeCellStyle(cell, Convert.ToString(cell.Value));
            };
            grid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (grid.IsCurrentCellDirty)
                {
                    grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }
            };
            PopulateVariableCardScopeGrid(grid, variable, ownerServiceId);
            return grid;
        }

        private void PopulateVariableCardScopeGrid(DataGridView grid, VariableDefinitionModel variable, string ownerServiceId)
        {
            grid.Rows.Clear();
            foreach (var service in project.Services.OrderBy(s => s.SortOrder).ThenBy(s => s.Name))
            {
                var rowIndex = grid.Rows.Add();
                var row = grid.Rows[rowIndex];
                row.Cells["ServiceId"].Value = service.Id;
                row.Cells["Service"].Value = service.Name;
                row.Cells["Service"].Style.BackColor = SystemColors.ControlLight;
                var isOwner = string.Equals(ownerServiceId, service.Id, StringComparison.Ordinal);
                var mode = isOwner ? "---" : ScopeModeFromContract(variable.Id, service.Id);
                if (isOwner)
                {
                    row.Cells["Mode"] = new DataGridViewTextBoxCell();
                    row.Cells["Mode"].ReadOnly = true;
                }
                else if (!(row.Cells["Mode"] is DataGridViewComboBoxCell))
                {
                    var comboCell = new DataGridViewComboBoxCell { FlatStyle = FlatStyle.Flat };
                    comboCell.Items.AddRange(ScopeModes.Cast<object>().ToArray());
                    row.Cells["Mode"] = comboCell;
                }
                row.Cells["Mode"].Value = mode;
                ApplyScopeModeCellStyle(row.Cells["Mode"], mode);
            }
        }

        private void ApplyVariableCardScopeGrid(DataGridView grid, VariableDefinitionModel variable)
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                var serviceId = Convert.ToString(row.Cells["ServiceId"].Value);
                if (string.IsNullOrWhiteSpace(serviceId) || string.Equals(variable.OwnerServiceId, serviceId, StringComparison.Ordinal))
                {
                    continue;
                }

                ParseScopeMode(Convert.ToString(row.Cells["Mode"].Value), out var visible, out var allowOverride, out var export);
                if (!visible && !allowOverride && !export)
                {
                    project.Contracts.RemoveAll(c => c.VariableId == variable.Id && c.ServiceId == serviceId);
                }
                else
                {
                    ProjectService.EnsureServiceScopeContract(project, variable.Id, serviceId, export, visible, allowOverride);
                }
            }
        }

        private string ScopeModeFromContract(string variableId, string serviceId)
        {
            var contract = project.Contracts.FirstOrDefault(c => c.VariableId == variableId && c.ServiceId == serviceId);
            if (contract == null) return ScopeModeNone;
            var visible = contract.VisibleToService;
            var export = !contract.Excluded;
            var allowOverride = visible && contract.AllowOverride;
            if (!visible && !export) return ScopeModeNone;
            if (allowOverride && export) return ScopeModeFull;
            if (allowOverride) return ScopeModeOverride;
            if (export) return ScopeModeExport;
            return ScopeModeRead;
        }

        private static string SelectedScopeOwnerId(ComboBox ownerCombo)
        {
            return (ownerCombo?.SelectedItem as ScopeSelectorItem)?.Id;
        }

        private int CountInterpolationReferences(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return 0;
            var token = "{{" + key.Trim().ToUpperInvariant() + "}}";
            return project.Values.Count(v => !string.IsNullOrEmpty(v.Value) && v.Value.Contains(token));
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
            if (!ProjectService.CanOverrideVariableForService(project, variable, target.ServiceId))
            {
                MessageBox.Show(this, "This variable cannot be set in the selected service scope.", "Set Value", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
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
                    UpdatedAtUtc = DateTime.UtcNow
                });
            }
            else
            {
                existing.Value = value;
                existing.UpdatedAtUtc = DateTime.UtcNow;
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
            return ProjectService.IsVariableUsedByService(project, variableId, serviceId);
        }

        private bool HasGlobalValue(string variableId)
        {
            return ProjectService.HasGlobalValue(project, variableId);
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
                if (value.Length >= 2 && ((value.StartsWith("\"") && value.EndsWith("\"")) || (value.StartsWith("'") && value.EndsWith("'"))))
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
            var selected = scopeFilterCombo?.SelectedItem as ScopeSelectorItem;
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
                ? new LayerValue(null, "Missing", null)
                : new LayerValue(value.Value, target.Scope.ToString(), value.UpdatedAt);
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

            if (TryReadEncryptedEnvelope(json, serializer, out var envelope))
            {
                if (envelope?.Payload == null || envelope.Crypto == null)
                {
                    MessageBox.Show(this, "Encrypted project file is invalid.", "Open Project", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return null;
                }

                var key = UnlockCryptoMetadata(envelope.Crypto);
                if (key == null) return null;
                SetVaultKey(key);
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

        private static bool TryReadEncryptedEnvelope(string json, JavaScriptSerializer serializer, out EncryptedProjectFile envelope)
        {
            return EncryptedEnvelopeDetector.TryRead(json, serializer, out envelope);
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
            SaveTextAtomic(envelopeJson, actualPath, false);
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
            var backupPath = path + ".bak";
            if (!keepBackup && File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }
        }

        private ProjectModel PrepareProjectForStorage()
        {
            var storageProject = CloneProject(project);
            storageProject.Settings = storageProject.Settings ?? new ProjectSettings();
            storageProject.Settings.CliExportPasswordRequired = project.Settings?.CliExportPasswordRequired == true;
            storageProject.Settings.CliExportPasswordRequiredPolicy = storageProject.Settings.CliExportPasswordRequired;
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
            ClearVaultKey();
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
            storageProject.Settings.CliExportPasswordRequiredPolicy = storageProject.Settings.CliExportPasswordRequired;
            project.Crypto = storageProject.Crypto;
            return true;
        }

        private void DecryptCliExportPolicy(ProjectModel targetProject, byte[] key)
        {
            targetProject.Settings = targetProject.Settings ?? new ProjectSettings();
            if (targetProject.Settings.CliExportPasswordRequiredEncrypted == null)
            {
                targetProject.Settings.CliExportPasswordRequired = true;
                targetProject.Settings.CliExportPasswordRequiredPolicy = true;
                return;
            }

            var value = cryptoService.DecryptString(targetProject.Settings.CliExportPasswordRequiredEncrypted, key);
            targetProject.Settings.CliExportPasswordRequired = string.Equals(value, "required:true:v1", StringComparison.Ordinal);
            targetProject.Settings.CliExportPasswordRequiredPolicy = targetProject.Settings.CliExportPasswordRequired;
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
                byte[] key = null;
                try
                {
                    key = cryptoService.DeriveKey(password, Convert.FromBase64String(crypto.Salt), crypto.Iterations);
                    cryptoService.DecryptString(crypto.KeyCheck, key);
                    SaveCachedVaultKey(crypto, key);
                    return key;
                }
                catch (Exception ex)
                {
                    ClearKey(key);
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

            SetVaultKey(UnlockCryptoMetadata(targetProject.Crypto));
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
            SetVaultKey(cryptoService.DeriveKey(password, salt, targetProject.Crypto.Iterations));
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
                try
                {
                    cryptoService.DecryptString(crypto.KeyCheck, key);
                    return key;
                }
                catch
                {
                    ClearKey(key);
                    return null;
                }
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
            ProjectService.EnsureProjectCollections(targetProject);
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
                UpdateQuickStats();
                UpdateVaultStatus();
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
            UpdateQuickStats();
            UpdateVaultStatus();
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

        private void UpdateVaultStatus()
        {
            if (vaultStatusLabel == null || vaultStatusIcon == null) return;
            if (project == null)
            {
                vaultStatusLabel.Text = "Vault: No project";
                vaultStatusIcon.Image = TryGetUiImage("Info");
                return;
            }

            var mode = GetProjectEncryptionMode(project);
            vaultStatusLabel.Text = "Vault: " + VaultProtectionLabel(mode);
            vaultStatusIcon.Image = VaultModeIcon(mode) ?? TryGetUiImage("Info");
        }

        private static string VaultProtectionLabel(string mode)
        {
            if (string.Equals(mode, "SecretsOnly", StringComparison.OrdinalIgnoreCase)) return "Secrets only";
            if (string.Equals(mode, "AllValues", StringComparison.OrdinalIgnoreCase)) return "All values";
            if (string.Equals(mode, "WholeJson", StringComparison.OrdinalIgnoreCase)) return "Whole vault";
            return "Open";
        }

        private Image VaultModeIcon(string mode)
        {
            if (string.Equals(mode, "Open", StringComparison.OrdinalIgnoreCase)) return TryGetUiImage("VaultOpen");
            if (string.Equals(mode, "SecretsOnly", StringComparison.OrdinalIgnoreCase)) return TryGetUiImage("VaultSecrets");
            if (string.Equals(mode, "AllValues", StringComparison.OrdinalIgnoreCase)) return TryGetUiImage("VaultEncrypted");
            if (string.Equals(mode, "WholeJson", StringComparison.OrdinalIgnoreCase)) return TryGetUiImage("VaultMasked");
            return TryGetUiImage("Unlock");
        }

        private Image TryGetUiImage(string key)
        {
            return !string.IsNullOrWhiteSpace(key) && uiImages.TryGetValue(key, out var image) ? image : null;
        }

        private void UpdateQuickStats()
        {
            if (quickStatsTable == null) return;
            quickStatsTable.SuspendLayout();
            quickStatsTable.Controls.Clear();
            if (project == null)
            {
                AddQuickStatRow(0, "Services:", "-");
                AddQuickStatRow(1, "Environments:", "-");
                AddQuickStatRow(2, "Variables:", "-");
                AddQuickStatRow(3, "Secrets:", "-");
                AddQuickStatRow(4, "File:", "none");
                quickStatsTable.ResumeLayout();
                return;
            }

            AddQuickStatRow(0, "Services:", project.Services.Count.ToString());
            AddQuickStatRow(1, "Environments:", project.Environments.Count.ToString());
            AddQuickStatRow(2, "Variables:", project.Variables.Count.ToString());
            AddQuickStatRow(3, "Secrets:", project.Variables.Count(v => v.IsSecret).ToString());
            AddQuickStatRow(4, "File:", modified ? "modified" : "saved");
            quickStatsTable.ResumeLayout();
        }

        private void AddQuickStatRow(int row, string label, string value)
        {
            var icon = new Label
            {
                Text = "●",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(25, 172, 52),
                Font = new Font(Font.FontFamily, 7f, FontStyle.Bold),
                Margin = Padding.Empty
            };
            var name = new Label
            {
                Text = label,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = Padding.Empty
            };
            var val = new Label
            {
                Text = value,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Margin = Padding.Empty
            };
            quickStatsTable.Controls.Add(icon, 0, row);
            quickStatsTable.Controls.Add(name, 1, row);
            quickStatsTable.Controls.Add(val, 2, row);
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
            public LayerValue(string value, string source, string updatedAt)
            {
                Value = value;
                Source = source;
                UpdatedAt = updatedAt;
            }

            public string Value { get; }
            public string Source { get; }
            public string UpdatedAt { get; }
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

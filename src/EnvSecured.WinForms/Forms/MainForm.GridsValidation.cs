using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using EnvSecured.Core.Models;
using EnvSecured.Core.Validation;

namespace EnvSecured.WinForms.Forms
{
    public sealed partial class MainForm
    {
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
                { "Sync", 55 },
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
    }
}

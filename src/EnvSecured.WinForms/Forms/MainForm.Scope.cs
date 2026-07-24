using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using EnvSecured.Core.Models;
using EnvSecured.Core.Services;

namespace EnvSecured.WinForms.Forms
{
    public sealed partial class MainForm
    {
        private void RenderScopeView()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            AddCommand(buttons, "Apply Changes", ApplyScopeMatrix, "Apply", 132);
            buttons.Controls.Add(new Label
            {
                Text = "Mode controls scope/override/file export; Sync controls external integrations",
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
                if (scopeMatrix.Columns[e.ColumnIndex] is DataGridViewComboBoxColumn)
                {
                    var mode = Convert.ToString(cell.Value);
                    ApplyScopeModeCellStyle(cell, mode);
                    var serviceId = Convert.ToString(scopeMatrix.Columns[e.ColumnIndex].Tag);
                    var syncCell = scopeMatrix.Rows[e.RowIndex].Cells["sync_" + serviceId];
                    var isOwner = string.Equals(Convert.ToString(scopeMatrix.Rows[e.RowIndex].Cells["OwnerId"].Value), serviceId, StringComparison.Ordinal);
                    var disabled = !isOwner && string.Equals(mode, ScopeModeNone, StringComparison.OrdinalIgnoreCase);
                    syncCell.ReadOnly = disabled;
                    syncCell.Style.BackColor = disabled ? SystemColors.ControlLight : Color.White;
                    if (disabled) syncCell.Value = false;
                }
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

        private void BuildScopeMatrix()
        {
            scopeMatrix.Columns.Clear();
            scopeMatrix.Rows.Clear();
            scopeMatrix.Columns.Add(new DataGridViewTextBoxColumn { Name = "VariableId", HeaderText = "VariableId", Visible = false });
            scopeMatrix.Columns.Add(new DataGridViewTextBoxColumn { Name = "OwnerId", HeaderText = "OwnerId", Visible = false });
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
                scopeMatrix.Columns.Add(new DataGridViewCheckBoxColumn
                {
                    Name = "sync_" + service.Id,
                    HeaderText = service.Name + " Sync",
                    Tag = service.Id,
                    FillWeight = 45
                });
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
                row.Cells["OwnerId"].Value = variable.OwnerServiceId;
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
                    var contract = project.Contracts.FirstOrDefault(c => c.VariableId == variable.Id && c.ServiceId == service.Id);
                    var syncCell = row.Cells["sync_" + service.Id];
                    syncCell.Value = ProjectService.IsContractSyncEnabled(contract);
                    syncCell.ReadOnly = !isOwner && string.Equals(mode, ScopeModeNone, StringComparison.OrdinalIgnoreCase);
                    syncCell.Style.BackColor = syncCell.ReadOnly ? SystemColors.ControlLight : Color.White;
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

                if (!string.IsNullOrWhiteSpace(variable.OwnerServiceId))
                {
                    var ownerSync = Convert.ToBoolean(row.Cells["sync_" + variable.OwnerServiceId].Value ?? false);
                    var ownerContract = project.Contracts.FirstOrDefault(c => c.VariableId == variable.Id && c.ServiceId == variable.OwnerServiceId);
                    if (ownerContract == null)
                    {
                        ownerContract = ProjectService.EnsureServiceScopeContract(project, variable.Id, variable.OwnerServiceId, false, true, true, ownerSync);
                    }
                    ownerContract.Sync = ownerSync;
                }

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
                    var sync = Convert.ToBoolean(row.Cells["sync_" + serviceId].Value ?? false);

                    if (!visible && !export && !allowOverride && !sync)
                    {
                        project.Contracts.RemoveAll(c => c.VariableId == variable.Id && c.ServiceId == serviceId);
                    }
                    else
                    {
                        ProjectService.EnsureServiceScopeContract(project, variable.Id, serviceId, export, visible, allowOverride, sync);
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
    }
}

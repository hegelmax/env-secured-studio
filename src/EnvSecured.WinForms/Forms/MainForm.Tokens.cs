using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using EnvSecured.Core.Models;

namespace EnvSecured.WinForms.Forms
{
    public sealed partial class MainForm
    {
        private DataGridView accessTokenGrid;

        private void RenderAccessTokensView()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(8), BackColor = System.Drawing.Color.White };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.Controls.Add(BuildViewHeader("Access Tokens", "Issue scoped read and batch-export credentials without sharing the administrator password."), 0, 0);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            AddCommand(buttons, "Create", CreateAccessToken, "Add", 92);
            AddCommand(buttons, "Edit Grants", EditAccessToken, "Edit", 108);
            AddCommand(buttons, "Reveal / Copy", RevealAccessToken, "Key", 118);
            AddCommand(buttons, "Rotate", RotateAccessToken, "Reset", 92);
            AddCommand(buttons, "Revoke", RevokeAccessToken, "Lock", 92);
            AddCommand(buttons, "Delete", DeleteAccessToken, "Delete", 92);
            AddCommand(buttons, "Generate Defaults", GenerateDefaultAccessTokens, "Key", 142);
            root.Controls.Add(buttons, 0, 1);

            accessTokenGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                RowHeadersVisible = false,
                BackgroundColor = System.Drawing.Color.White
            };
            accessTokenGrid.Columns.Add("Id", "Id");
            accessTokenGrid.Columns.Add("Name", "Name");
            accessTokenGrid.Columns.Add("Type", "Type");
            accessTokenGrid.Columns.Add("Service", "Service");
            accessTokenGrid.Columns.Add("Environment", "Environment");
            accessTokenGrid.Columns.Add("Status", "Status");
            accessTokenGrid.Columns.Add("Version", "Version");
            accessTokenGrid.Columns.Add("Expires", "Expires");
            accessTokenGrid.Columns.Add("Grants", "Grants");
            root.Controls.Add(accessTokenGrid, 0, 2);
            contentPanel.Controls.Add(root);
            RefreshAccessTokenGrid();
        }

        private void RefreshAccessTokenGrid()
        {
            if (accessTokenGrid == null) return;
            accessTokenGrid.Rows.Clear();
            var tokenScopes = new EnvSecured.Core.Services.TokenScopePolicyService();
            foreach (var token in project.AccessTokens.OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                var index = accessTokenGrid.Rows.Add(token.Id, token.Name, token.Type,
                    tokenScopes.ResolveServiceId(token) ?? string.Empty, tokenScopes.ResolveEnvironmentId(token) ?? string.Empty,
                    token.Status, token.Version, token.ExpiresAt, string.Join("; ", token.Grants));
                accessTokenGrid.Rows[index].Tag = token.Id;
            }
        }

        private AccessTokenModel SelectedAccessToken()
        {
            var id = accessTokenGrid?.CurrentRow?.Tag as string;
            return project.AccessTokens.FirstOrDefault(x => x.Id == id);
        }

        private void CreateAccessToken()
        {
            var edit = TokenEditorDialog.Show(this, project);
            if (edit == null || !EnsureVaultKey(project)) return;
            project.Settings.EncryptionMode = "AllValues";
            project.Settings.EncryptAllValues = true;
            var result = vaultProtectionService.CreateToken(project, vaultKey, edit.Id, edit.Name, edit.Type, edit.Grants,
                edit.ExpiresAt, edit.ServiceId, edit.EnvironmentId);
            Changed();
            RefreshAccessTokenGrid();
            TokenEditorDialog.ShowSecret(this, "Access Token Created", result.Secret);
        }

        private void EditAccessToken()
        {
            var token = SelectedAccessToken();
            if (token == null) return;
            var edit = TokenEditorDialog.Show(this, project, token);
            if (edit == null) return;
            token.Name = edit.Name;
            token.Type = edit.Type;
            token.ServiceId = edit.ServiceId;
            token.EnvironmentId = edit.EnvironmentId;
            token.ExpiresAt = edit.ExpiresAt;
            token.Grants = edit.Grants;
            Changed();
            RefreshAccessTokenGrid();
        }

        private void RotateAccessToken()
        {
            var token = SelectedAccessToken();
            if (token == null || !EnsureVaultKey(project)) return;
            var secret = vaultProtectionService.RotateToken(project, vaultKey, token);
            Changed();
            RefreshAccessTokenGrid();
            TokenEditorDialog.ShowSecret(this, "Access Token Rotated", secret);
        }

        private void RevealAccessToken()
        {
            var token = SelectedAccessToken();
            if (token == null) return;
            if (MessageBox.Show(
                    this,
                    "Reveal the current bearer token for " + token.Id + "?\r\n\r\n" +
                    "Anyone who copies it receives the token's granted access.",
                    "Reveal Access Token",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes) return;
            if (!EnsureVaultKey(project)) return;

            try
            {
                var addedLookupId = string.IsNullOrWhiteSpace(token.LookupId);
                var secret = vaultProtectionService.RevealToken(project, vaultKey, token);
                if (addedLookupId)
                {
                    Changed();
                    RefreshAccessTokenGrid();
                }
                TokenEditorDialog.ShowSecret(this, "Access Token", token.Id + "=" + secret);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Reveal Access Token", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void RevokeAccessToken()
        {
            var token = SelectedAccessToken();
            if (token == null || MessageBox.Show(this, "Revoke " + token.Id + "?", "Access Token", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            token.Status = "Revoked";
            Changed();
            RefreshAccessTokenGrid();
        }

        private void DeleteAccessToken()
        {
            var token = SelectedAccessToken();
            if (token == null || !ConfirmDelete(token.Id)) return;
            project.AccessTokens.Remove(token);
            Changed();
            RefreshAccessTokenGrid();
        }

        private void GenerateDefaultAccessTokens()
        {
            if (!EnsureVaultKey(project)) return;
            var secrets = new List<string>();
            foreach (var service in project.Services.Where(x => x.IsActive))
            {
                var secret = CreateDefaultServiceAccessToken(service);
                if (secret != null) secrets.Add(secret);
            }
            foreach (var environment in project.Environments.Where(x => x.IsActive))
            {
                var secret = CreateDefaultEnvironmentAccessToken(environment);
                if (secret != null) secrets.Add(secret);
            }
            if (secrets.Count == 0)
            {
                MessageBox.Show(this, "All default service/environment tokens already exist.", "Access Tokens");
                return;
            }
            Changed();
            RefreshAccessTokenGrid();
            TokenEditorDialog.ShowSecret(this, "Default Access Tokens Created", string.Join(Environment.NewLine, secrets));
        }

        private string CreateDefaultServiceAccessToken(ServiceModel service)
        {
            var id = "service-" + service.Id;
            if (project.AccessTokens.Any(x => x.Id == id)) return null;
            project.Settings.EncryptionMode = "AllValues";
            project.Settings.EncryptAllValues = true;
            var result = vaultProtectionService.CreateToken(project, vaultKey, id,
                "Service: " + service.Name, "Service", new[] { service.Id + ".*.*.Read|BatchExport" }, null, service.Id, null);
            return id + "=" + result.Secret;
        }

        private string CreateDefaultEnvironmentAccessToken(EnvironmentModel environment)
        {
            var id = "environment-" + environment.Id;
            if (project.AccessTokens.Any(x => x.Id == id)) return null;
            project.Settings.EncryptionMode = "AllValues";
            project.Settings.EncryptAllValues = true;
            var result = vaultProtectionService.CreateToken(project, vaultKey, id,
                "Environment: " + environment.Name, "Environment", new[] { "*." + environment.Id + ".*.Read|BatchExport" }, null, null, environment.Id);
            return id + "=" + result.Secret;
        }
    }
}

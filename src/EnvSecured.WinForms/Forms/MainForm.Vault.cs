using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using EnvSecured.Core.Models;
using EnvSecured.Core.Services;

namespace EnvSecured.WinForms.Forms
{
    public sealed partial class MainForm
    {
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

        private bool SaveSplitVaultFile(ProjectModel splitProject, string path, string password)
        {
            var previousProject = project;
            var previousKey = vaultKey == null ? null : (byte[])vaultKey.Clone();
            try
            {
                project = splitProject;
                ClearVaultKey();
                if (!string.Equals(GetProjectEncryptionMode(splitProject), "Open", StringComparison.OrdinalIgnoreCase))
                {
                    if (!CreateVaultKey(splitProject, password, password))
                    {
                        return false;
                    }
                }

                var directory = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                return SaveVaultFile(path, false);
            }
            finally
            {
                project = previousProject;
                ClearVaultKey();
                if (previousKey != null)
                {
                    SetVaultKey(previousKey);
                }
            }
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
                var storedDocument = vaultFileService.IsV2(projectJson)
                    ? serializer.Deserialize<VaultV2Document>(projectJson)
                    : null;
                var decryptedProject = vaultFileService.Deserialize(projectJson);
                if (storedDocument != null)
                {
                    vaultProtectionService.VerifyIntegrity(storedDocument, key);
                }
                decryptedProject.Crypto = envelope.Crypto;
                NormalizeLoadedProjectSettings(decryptedProject);
                if (storedDocument == null)
                {
                    vaultProtectionService.VerifyIntegrity(decryptedProject, key);
                }
                DecryptCliExportPolicy(decryptedProject, key);
                DecryptSyncProviderSettings(decryptedProject, key);
                return decryptedProject;
            }

            var sourceDocument = vaultFileService.IsV2(json)
                ? serializer.Deserialize<VaultV2Document>(json)
                : null;
            var loadedProject = vaultFileService.Deserialize(json);
            NormalizeLoadedProjectSettings(loadedProject);
            if (!UnlockProjectIfNeeded(loadedProject, sourceDocument))
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
            var projectJson = vaultFileService.Serialize(storageProject);
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
            CopyRuntimeSyncProviderSettings(project, storageProject);
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
                if (!EncryptSyncProviderSettings(storageProject))
                {
                    return null;
                }
                foreach (var value in storageProject.Values)
                {
                    value.EncryptedValue = null;
                    value.IsEncrypted = false;
                }
                vaultProtectionService.ProtectIntegrity(storageProject, vaultKey);
                return storageProject;
            }

            if (!PrepareCliExportPolicyForStorage(storageProject))
            {
                return null;
            }
            if (!EncryptSyncProviderSettings(storageProject))
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
                vaultProtectionService.ProtectIntegrity(storageProject, vaultKey);
                return storageProject;
            }

            if (!EnsureVaultKey(storageProject))
            {
                return null;
            }
            project.Crypto = storageProject.Crypto;

            vaultProtectionService.EncryptValuesForStorage(
                storageProject,
                vaultKey,
                variable => storageProject.AccessTokens.Count > 0 || ShouldEncryptValue(storageProject, variable));
            vaultProtectionService.ProtectIntegrity(storageProject, vaultKey);

            return storageProject;
        }

        private bool UnlockProjectIfNeeded(ProjectModel loadedProject, VaultV2Document sourceDocument = null)
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

            if (sourceDocument != null)
            {
                vaultProtectionService.VerifyIntegrity(sourceDocument, vaultKey);
            }
            else
            {
                vaultProtectionService.VerifyIntegrity(loadedProject, vaultKey);
            }
            vaultProtectionService.DecryptValuesForAdmin(loadedProject, vaultKey);

            DecryptCliExportPolicy(loadedProject, vaultKey);
            DecryptSyncProviderSettings(loadedProject, vaultKey);

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

        private bool EncryptSyncProviderSettings(ProjectModel storageProject)
        {
            if (storageProject?.Sync?.Providers == null) return true;
            if (!EnsureVaultKey(storageProject))
            {
                return false;
            }

            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            foreach (var provider in storageProject.Sync.Providers)
            {
                var settings = provider.Settings ?? new Dictionary<string, string>();
                provider.EncryptedSettings = cryptoService.EncryptString(serializer.Serialize(settings), vaultKey);
            }

            project.Crypto = storageProject.Crypto;
            return true;
        }

        private static void CopyRuntimeSyncProviderSettings(ProjectModel source, ProjectModel target)
        {
            if (source?.Sync?.Providers == null || target?.Sync?.Providers == null) return;
            foreach (var targetProvider in target.Sync.Providers)
            {
                var sourceProvider = source.Sync.Providers.FirstOrDefault(p => string.Equals(p.Id, targetProvider.Id, StringComparison.Ordinal));
                if (sourceProvider?.Settings == null) continue;
                targetProvider.Settings = new Dictionary<string, string>(sourceProvider.Settings, StringComparer.OrdinalIgnoreCase);
            }
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

        private void DecryptSyncProviderSettings(ProjectModel targetProject, byte[] key)
        {
            if (targetProject?.Sync?.Providers == null) return;
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            foreach (var provider in targetProject.Sync.Providers)
            {
                provider.Settings = provider.Settings ?? new Dictionary<string, string>();
                if (provider.EncryptedSettings == null) continue;
                var json = cryptoService.DecryptString(provider.EncryptedSettings, key);
                provider.Settings = serializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
            }
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
            var confirm = PromptDialog.ShowPassword(this, "Create Vault Password", "Repeat master password:");
            return CreateVaultKey(targetProject, password, confirm);
        }

        private bool CreateVaultKey(ProjectModel targetProject, string password, string confirm)
        {
            if (password == null || confirm == null) return false;
            if (password.Length == 0)
            {
                MessageBox.Show(this, "Master password cannot be empty.", "Create Vault Password", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
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
                targetProject.Settings?.CliExportPasswordRequiredEncrypted != null ||
                targetProject.Sync?.Providers?.Any(p => p.EncryptedSettings != null) == true;
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
    }
}

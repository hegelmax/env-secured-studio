using System.IO;
using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using EnvSecured.Core.Models;

namespace EnvSecured.Core.Services
{
    public sealed class VaultFileService
    {
        private const string RecoveryBackupSuffix = ".autosave.json";
        private const string LegacyBackupSuffix = ".v1.bak";
        private readonly VaultFormatConverter converter = new VaultFormatConverter();

        public ProjectModel Load(string path)
        {
            return Deserialize(File.ReadAllText(path));
        }

        public ProjectModel Deserialize(string json)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            if (IsV2Json(json, serializer))
            {
                return converter.FromV2(serializer.Deserialize<VaultV2Document>(json));
            }
            var legacy = serializer.Deserialize<ProjectModel>(json);
            ProjectService.EnsureProjectCollections(legacy);
            return legacy;
        }

        public string Serialize(ProjectModel project)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            return serializer.Serialize(converter.ToV2(project));
        }

        public bool IsV2(string json)
        {
            return IsV2Json(json, new JavaScriptSerializer { MaxJsonLength = int.MaxValue });
        }

        public void Save(ProjectModel project, string path)
        {
            var tempPath = path + ".tmp";
            var backupPath = path + ".bak";
            var json = Serialize(project);
            File.WriteAllText(tempPath, json);
            if (File.Exists(path))
            {
                var previous = File.ReadAllText(path);
                if (!IsV2(previous) && !File.Exists(path + LegacyBackupSuffix))
                {
                    File.Copy(path, path + LegacyBackupSuffix, false);
                }
                File.Copy(path, backupPath, true);
                File.Delete(path);
            }
            File.Move(tempPath, path);
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }
        }

        public string GetRecoveryBackupPath(string path)
        {
            return path + RecoveryBackupSuffix;
        }

        public bool HasRecoveryBackup(string path)
        {
            return File.Exists(GetRecoveryBackupPath(path));
        }

        public ProjectModel LoadRecoveryBackup(string path)
        {
            return Load(GetRecoveryBackupPath(path));
        }

        public void SaveRecoveryBackup(ProjectModel project, string path)
        {
            var backupPath = GetRecoveryBackupPath(path);
            var tempPath = backupPath + ".tmp";
            var json = Serialize(project);

            File.WriteAllText(tempPath, json);
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }
            File.Move(tempPath, backupPath);
        }

        public void DeleteRecoveryBackup(string path)
        {
            var backupPath = GetRecoveryBackupPath(path);
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }
        }

        private static bool IsV2Json(string json, JavaScriptSerializer serializer)
        {
            if (string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                var root = serializer.Deserialize<Dictionary<string, object>>(json);
                return root != null && root.TryGetValue("Format", out var format) &&
                    string.Equals(Convert.ToString(format), "EnvSecured.Vault", StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }
    }
}

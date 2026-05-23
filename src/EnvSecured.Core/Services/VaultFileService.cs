using System.IO;
using System.Web.Script.Serialization;
using EnvSecured.Core.Models;

namespace EnvSecured.Core.Services
{
    public sealed class VaultFileService
    {
        private const string RecoveryBackupSuffix = ".autosave.json";

        public ProjectModel Load(string path)
        {
            return new JavaScriptSerializer().Deserialize<ProjectModel>(File.ReadAllText(path));
        }

        public void Save(ProjectModel project, string path)
        {
            var tempPath = path + ".tmp";
            var backupPath = path + ".bak";
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var json = serializer.Serialize(project);
            File.WriteAllText(tempPath, json);
            if (File.Exists(path))
            {
                File.Copy(path, backupPath, true);
                File.Delete(path);
            }
            File.Move(tempPath, path);
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
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var json = serializer.Serialize(project);

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
    }
}

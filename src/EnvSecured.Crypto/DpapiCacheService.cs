using System.IO;
using System.Security.Cryptography;

namespace EnvSecured.Crypto
{
    public sealed class DpapiCacheService
    {
        public void Save(string path, byte[] key)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var protectedBytes = ProtectedData.Protect(key, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(path, protectedBytes);
        }

        public byte[] TryLoad(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                return ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
            }
            catch (CryptographicException)
            {
                return null;
            }
        }
    }
}

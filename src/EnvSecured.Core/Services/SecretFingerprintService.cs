using System;
using System.Security.Cryptography;
using System.Text;

namespace EnvSecured.Core.Services
{
    public static class SecretFingerprintService
    {
        public const string ProviderSettingKey = "secretFingerprintKey";
        public const string VariablePrefix = "ENVSECURED_SECRET_FP_";
        private const string Prefix = "hmac-sha256:v1:";

        public static string GenerateKey()
        {
            var bytes = new byte[32];
            try
            {
                using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
                return Convert.ToBase64String(bytes);
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
            }
        }

        public static string VariableName(string secretVariableName)
        {
            if (string.IsNullOrWhiteSpace(secretVariableName))
                throw new ArgumentException("Secret variable name is required.", nameof(secretVariableName));
            var normalized = secretVariableName.Trim().ToUpperInvariant();
            var bytes = Encoding.UTF8.GetBytes(normalized);
            byte[] digest = null;
            try
            {
                using (var sha = SHA256.Create()) digest = sha.ComputeHash(bytes);
                var suffix = new StringBuilder(24);
                for (var i = 0; i < 12; i++) suffix.Append(digest[i].ToString("X2"));
                return VariablePrefix + suffix;
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
                if (digest != null) Array.Clear(digest, 0, digest.Length);
            }
        }

        public static string Compute(string base64Key, string providerId, string environment, string secretVariableName, string secretValue)
        {
            byte[] key;
            try
            {
                key = Convert.FromBase64String(base64Key ?? string.Empty);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("The provider secret fingerprint key is invalid.", ex);
            }
            if (key.Length != 32)
            {
                Array.Clear(key, 0, key.Length);
                throw new InvalidOperationException("The provider secret fingerprint key must contain 32 bytes.");
            }

            var context = "EnvSecured.SecretFingerprint:v1\0" +
                (providerId ?? string.Empty) + "\0" +
                (environment ?? string.Empty) + "\0" +
                (secretVariableName ?? string.Empty) + "\0" +
                (secretValue ?? string.Empty);
            var data = Encoding.UTF8.GetBytes(context);
            byte[] digest = null;
            try
            {
                using (var hmac = new HMACSHA256(key)) digest = hmac.ComputeHash(data);
                return Prefix + Base64Url(digest);
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
                Array.Clear(data, 0, data.Length);
                if (digest != null) Array.Clear(digest, 0, digest.Length);
            }
        }

        private static string Base64Url(byte[] value)
        {
            return Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}

using System;
using System.Security.Cryptography;
using EnvSecured.Core.Models;

namespace EnvSecured.Crypto
{
    public sealed class CryptoService
    {
        public byte[] DeriveKey(string masterPassword, byte[] salt, int iterations = 300000)
        {
            using (var pbkdf2 = new Rfc2898DeriveBytes(masterPassword, salt, iterations, HashAlgorithmName.SHA256))
            {
                return pbkdf2.GetBytes(64);
            }
        }

        public EncryptedPayload EncryptString(string plainText, byte[] key)
        {
            if (key == null || key.Length < 64)
            {
                throw new ArgumentException("AES-CBC-HMAC encryption requires a 64-byte derived key.", nameof(key));
            }

            var iv = RandomBytes(16);
            var input = System.Text.Encoding.UTF8.GetBytes(plainText ?? string.Empty);
            byte[] ciphertext;
            using (var aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = Slice(key, 0, 32);
                aes.IV = iv;
                using (var encryptor = aes.CreateEncryptor())
                {
                    ciphertext = encryptor.TransformFinalBlock(input, 0, input.Length);
                }
            }

            return new EncryptedPayload
            {
                Alg = "AES-256-CBC-HMAC-SHA256",
                Nonce = Convert.ToBase64String(iv),
                Ciphertext = Convert.ToBase64String(ciphertext),
                Tag = Convert.ToBase64String(ComputeTag(key, iv, ciphertext))
            };
        }

        public string DecryptString(EncryptedPayload payload, byte[] key)
        {
            if (key == null || key.Length < 64)
            {
                throw new ArgumentException("AES-CBC-HMAC decryption requires a 64-byte derived key.", nameof(key));
            }

            var iv = Convert.FromBase64String(payload.Nonce);
            var ciphertext = Convert.FromBase64String(payload.Ciphertext);
            var tag = Convert.FromBase64String(payload.Tag);
            var expectedTag = ComputeTag(key, iv, ciphertext);
            if (!FixedTimeEquals(tag, expectedTag))
            {
                throw new CryptographicException("Encrypted payload authentication failed.");
            }

            byte[] output;
            using (var aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = Slice(key, 0, 32);
                aes.IV = iv;
                using (var decryptor = aes.CreateDecryptor())
                {
                    output = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
                }
            }

            return System.Text.Encoding.UTF8.GetString(output);
        }

        private static byte[] ComputeTag(byte[] key, byte[] iv, byte[] ciphertext)
        {
            var data = new byte[iv.Length + ciphertext.Length];
            Buffer.BlockCopy(iv, 0, data, 0, iv.Length);
            Buffer.BlockCopy(ciphertext, 0, data, iv.Length, ciphertext.Length);
            using (var hmac = new HMACSHA256(Slice(key, 32, 32)))
            {
                return hmac.ComputeHash(data);
            }
        }

        private static byte[] Slice(byte[] source, int offset, int length)
        {
            var result = new byte[length];
            Buffer.BlockCopy(source, offset, result, 0, length);
            return result;
        }

        private static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            var diff = 0;
            for (var i = 0; i < left.Length; i++)
            {
                diff |= left[i] ^ right[i];
            }
            return diff == 0;
        }

        private static byte[] RandomBytes(int length)
        {
            var bytes = new byte[length];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return bytes;
        }
    }
}

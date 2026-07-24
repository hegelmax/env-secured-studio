using System;
using System.Security.Cryptography;
using System.Text;
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

        public EncryptedPayload EncryptStringWithAad(string plainText, byte[] key, string aad)
        {
            if (key == null || key.Length < 64)
            {
                throw new ArgumentException("AES-CBC-HMAC encryption requires a 64-byte key.", nameof(key));
            }

            var iv = RandomBytes(16);
            var input = Encoding.UTF8.GetBytes(plainText ?? string.Empty);
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
                Alg = "AES-256-CBC-HMAC-SHA256-AAD",
                AadVersion = 1,
                Nonce = Convert.ToBase64String(iv),
                Ciphertext = Convert.ToBase64String(ciphertext),
                Tag = Convert.ToBase64String(ComputeAadTag(key, iv, ciphertext, aad))
            };
        }

        public string DecryptString(EncryptedPayload payload, byte[] key)
        {
            if (key == null || key.Length < 64)
            {
                throw new ArgumentException("AES-CBC-HMAC decryption requires a 64-byte derived key.", nameof(key));
            }

            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (payload.AadVersion > 0)
                throw new CryptographicException("Authenticated payload requires its associated data.");

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

        public string DecryptStringWithAad(EncryptedPayload payload, byte[] key, string aad)
        {
            if (key == null || key.Length < 64)
                throw new ArgumentException("AES-CBC-HMAC decryption requires a 64-byte key.", nameof(key));
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (payload.AadVersion != 1)
                throw new CryptographicException("Unsupported authenticated-data version.");

            var iv = Convert.FromBase64String(payload.Nonce);
            var ciphertext = Convert.FromBase64String(payload.Ciphertext);
            var tag = Convert.FromBase64String(payload.Tag);
            var expectedTag = ComputeAadTag(key, iv, ciphertext, aad);
            if (!FixedTimeEquals(tag, expectedTag))
                throw new CryptographicException("Encrypted payload authentication failed.");

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
            return Encoding.UTF8.GetString(output);
        }

        public byte[] DeriveSubkey(byte[] rootKey, string purpose, int length = 64)
        {
            if (rootKey == null || rootKey.Length == 0) throw new ArgumentException("Root key is required.", nameof(rootKey));
            if (length <= 0 || length > 255 * 32) throw new ArgumentOutOfRangeException(nameof(length));
            var info = Encoding.UTF8.GetBytes(purpose ?? string.Empty);
            var output = new byte[length];
            byte[] previous = new byte[0];
            var offset = 0;
            using (var hmac = new HMACSHA256(rootKey))
            {
                for (byte counter = 1; offset < length; counter++)
                {
                    var input = new byte[previous.Length + info.Length + 1];
                    Buffer.BlockCopy(previous, 0, input, 0, previous.Length);
                    Buffer.BlockCopy(info, 0, input, previous.Length, info.Length);
                    input[input.Length - 1] = counter;
                    previous = hmac.ComputeHash(input);
                    var count = Math.Min(previous.Length, length - offset);
                    Buffer.BlockCopy(previous, 0, output, offset, count);
                    offset += count;
                    Array.Clear(input, 0, input.Length);
                }
            }
            Array.Clear(previous, 0, previous.Length);
            return output;
        }

        public byte[] ComputeHmac(byte[] key, string text)
        {
            using (var hmac = new HMACSHA256(key))
            {
                return hmac.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty));
            }
        }

        public bool VerifyHmac(byte[] key, string text, string expectedBase64)
        {
            if (string.IsNullOrWhiteSpace(expectedBase64)) return false;
            return FixedTimeEquals(ComputeHmac(key, text), Convert.FromBase64String(expectedBase64));
        }

        public byte[] GenerateRandomKey(int length = 64)
        {
            return RandomBytes(length);
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

        private static byte[] ComputeAadTag(byte[] key, byte[] iv, byte[] ciphertext, string aad)
        {
            var domain = Encoding.UTF8.GetBytes("EnvSecured.Aead.v1");
            var aadBytes = Encoding.UTF8.GetBytes(aad ?? string.Empty);
            var data = new byte[domain.Length + 4 + aadBytes.Length + iv.Length + ciphertext.Length];
            Buffer.BlockCopy(domain, 0, data, 0, domain.Length);
            var length = BitConverter.GetBytes(aadBytes.Length);
            if (BitConverter.IsLittleEndian) Array.Reverse(length);
            Buffer.BlockCopy(length, 0, data, domain.Length, 4);
            Buffer.BlockCopy(aadBytes, 0, data, domain.Length + 4, aadBytes.Length);
            Buffer.BlockCopy(iv, 0, data, domain.Length + 4 + aadBytes.Length, iv.Length);
            Buffer.BlockCopy(ciphertext, 0, data, domain.Length + 4 + aadBytes.Length + iv.Length, ciphertext.Length);
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

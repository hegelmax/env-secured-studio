using System;
using System.Linq;
using System.Security.Cryptography;
using EnvSecured.Core.Models;
using EnvSecured.Crypto;
using Xunit;

namespace EnvSecured.Tests
{
    public sealed class CryptoServiceTests
    {
        [Fact]
        public void EncryptString_RoundTripsPlainText()
        {
            var service = new CryptoService();
            var key = service.DeriveKey("password", Enumerable.Range(1, 32).Select(i => (byte)i).ToArray(), 1000);

            var payload = service.EncryptString("secret-value", key);
            var decrypted = service.DecryptString(payload, key);

            Assert.Equal("secret-value", decrypted);
        }

        [Fact]
        public void DecryptString_RejectsTamperedCiphertext()
        {
            var service = new CryptoService();
            var key = service.DeriveKey("password", Enumerable.Range(1, 32).Select(i => (byte)i).ToArray(), 1000);
            var payload = service.EncryptString("secret-value", key);
            var ciphertext = Convert.FromBase64String(payload.Ciphertext);
            ciphertext[0] ^= 0x01;

            var tampered = new EncryptedPayload
            {
                Alg = payload.Alg,
                Nonce = payload.Nonce,
                Ciphertext = Convert.ToBase64String(ciphertext),
                Tag = payload.Tag
            };

            Assert.Throws<CryptographicException>(() => service.DecryptString(tampered, key));
        }

        [Fact]
        public void DecryptString_RejectsTamperedNonce()
        {
            var service = new CryptoService();
            var key = service.DeriveKey("password", Enumerable.Range(1, 32).Select(i => (byte)i).ToArray(), 1000);
            var payload = service.EncryptString("secret-value", key);
            var nonce = Convert.FromBase64String(payload.Nonce);
            nonce[0] ^= 0x01;

            var tampered = new EncryptedPayload
            {
                Alg = payload.Alg,
                Nonce = Convert.ToBase64String(nonce),
                Ciphertext = payload.Ciphertext,
                Tag = payload.Tag
            };

            Assert.Throws<CryptographicException>(() => service.DecryptString(tampered, key));
        }

        [Fact]
        public void DecryptString_RejectsTamperedTag()
        {
            var service = new CryptoService();
            var key = service.DeriveKey("password", Enumerable.Range(1, 32).Select(i => (byte)i).ToArray(), 1000);
            var payload = service.EncryptString("secret-value", key);
            var tag = Convert.FromBase64String(payload.Tag);
            tag[0] ^= 0x01;

            var tampered = new EncryptedPayload
            {
                Alg = payload.Alg,
                Nonce = payload.Nonce,
                Ciphertext = payload.Ciphertext,
                Tag = Convert.ToBase64String(tag)
            };

            Assert.Throws<CryptographicException>(() => service.DecryptString(tampered, key));
        }

        [Fact]
        public void DecryptString_RejectsWrongKey()
        {
            var service = new CryptoService();
            var key = service.DeriveKey("password", Enumerable.Range(1, 32).Select(i => (byte)i).ToArray(), 1000);
            var wrongKey = service.DeriveKey("other-password", Enumerable.Range(1, 32).Select(i => (byte)i).ToArray(), 1000);
            var payload = service.EncryptString("secret-value", key);

            Assert.Throws<CryptographicException>(() => service.DecryptString(payload, wrongKey));
        }
    }
}

namespace EnvSecured.Core.Models
{
    public sealed class VaultCryptoMetadata
    {
        public string Kdf { get; set; } = "PBKDF2-HMAC-SHA256";
        public int Iterations { get; set; } = 300000;
        public string Salt { get; set; }
        public EncryptedPayload KeyCheck { get; set; }
    }

    public sealed class EncryptedPayload
    {
        public string Alg { get; set; } = "AES-256-CBC-HMAC-SHA256";
        public string Nonce { get; set; }
        public string Ciphertext { get; set; }
        public string Tag { get; set; }
    }

    public sealed class EncryptedProjectFile
    {
        public string Format { get; set; } = "EnvSecured.EncryptedProject.v1";
        public VaultCryptoMetadata Crypto { get; set; } = new VaultCryptoMetadata();
        public EncryptedPayload Payload { get; set; }
    }
}

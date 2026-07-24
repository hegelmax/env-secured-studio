using System.Collections.Generic;

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
        public int AadVersion { get; set; }
        public string Nonce { get; set; }
        public string Ciphertext { get; set; }
        public string Tag { get; set; }
        public List<WrappedKeySlot> KeySlots { get; set; } = new List<WrappedKeySlot>();
    }

    public sealed class WrappedKeySlot
    {
        public string PrincipalType { get; set; }
        public string PrincipalId { get; set; }
        public int KeyVersion { get; set; } = 1;
        public EncryptedPayload WrappedDek { get; set; }
    }

    public sealed class EncryptedProjectFile
    {
        public string Format { get; set; } = "EnvSecured.EncryptedVault";
        public int Version { get; set; } = 2;
        public VaultCryptoMetadata Crypto { get; set; } = new VaultCryptoMetadata();
        public EncryptedPayload Payload { get; set; }
    }
}

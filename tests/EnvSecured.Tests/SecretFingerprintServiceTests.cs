using EnvSecured.Core.Services;
using Xunit;

namespace EnvSecured.Tests
{
    public sealed class SecretFingerprintServiceTests
    {
        [Fact]
        public void SameKeyAndContextProduceSameFingerprint()
        {
            var key = SecretFingerprintService.GenerateKey();
            var first = SecretFingerprintService.Compute(key, "github-main", "dev", "DATABASE_PASSWORD", "secret");
            var second = SecretFingerprintService.Compute(key, "github-main", "dev", "DATABASE_PASSWORD", "secret");

            Assert.Equal(first, second);
            Assert.StartsWith("hmac-sha256:v1:", first);
        }

        [Fact]
        public void DifferentProviderKeyDoesNotMatchAcrossIndependentVaults()
        {
            var first = SecretFingerprintService.Compute(SecretFingerprintService.GenerateKey(), "github-main", "dev", "DATABASE_PASSWORD", "secret");
            var second = SecretFingerprintService.Compute(SecretFingerprintService.GenerateKey(), "github-main", "dev", "DATABASE_PASSWORD", "secret");

            Assert.NotEqual(first, second);
        }

        [Fact]
        public void ContextPreventsEqualityAcrossSecretNamesOrEnvironments()
        {
            var key = SecretFingerprintService.GenerateKey();
            var dev = SecretFingerprintService.Compute(key, "github-main", "dev", "DATABASE_PASSWORD", "secret");
            var prod = SecretFingerprintService.Compute(key, "github-main", "prod", "DATABASE_PASSWORD", "secret");
            var otherName = SecretFingerprintService.Compute(key, "github-main", "dev", "API_PASSWORD", "secret");

            Assert.NotEqual(dev, prod);
            Assert.NotEqual(dev, otherName);
        }

        [Fact]
        public void InternalVariableNameDoesNotOccupyApplicationHashSuffix()
        {
            var name = SecretFingerprintService.VariableName("DB_PASSWORD");

            Assert.StartsWith("ENVSECURED_SECRET_FP_", name);
            Assert.Equal(45, name.Length);
            Assert.NotEqual("DB_PASSWORD_HASH", name);
            Assert.Equal(name, SecretFingerprintService.VariableName("db_password"));
        }
    }
}

using System.Web.Script.Serialization;
using EnvSecured.Core.Models;
using EnvSecured.Core.Services;
using Xunit;

namespace EnvSecured.Tests
{
    public sealed class EncryptedEnvelopeDetectorTests
    {
        [Fact]
        public void TryRead_DoesNotTreatPlainProjectAsEncryptedEnvelope()
        {
            var json = new JavaScriptSerializer().Serialize(new ProjectModel { ProjectName = "cpa" });

            var detected = EncryptedEnvelopeDetector.TryRead(json, new JavaScriptSerializer(), out var envelope);

            Assert.False(detected);
            Assert.Null(envelope);
        }

        [Fact]
        public void TryRead_ReadsValidEncryptedEnvelope()
        {
            var serializer = new JavaScriptSerializer();
            var json = serializer.Serialize(new EncryptedProjectFile
            {
                Crypto = new VaultCryptoMetadata { Salt = "salt" },
                Payload = new EncryptedPayload { Nonce = "nonce", Ciphertext = "ciphertext", Tag = "tag" }
            });

            var detected = EncryptedEnvelopeDetector.TryRead(json, serializer, out var envelope);

            Assert.True(detected);
            Assert.NotNull(envelope);
            Assert.Equal(EncryptedEnvelopeDetector.Format, envelope.Format);
            Assert.Equal("salt", envelope.Crypto.Salt);
            Assert.Equal("ciphertext", envelope.Payload.Ciphertext);
        }

        [Theory]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("{")]
        [InlineData("{}")]
        public void TryRead_ReturnsFalseForInvalidOrEmptyJson(string json)
        {
            var detected = EncryptedEnvelopeDetector.TryRead(json, new JavaScriptSerializer(), out var envelope);

            Assert.False(detected);
            Assert.Null(envelope);
        }
    }
}

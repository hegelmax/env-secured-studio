using System;
using EnvSecured.Core.Services;
using Xunit;

namespace EnvSecured.Tests
{
    public sealed class GrantServiceTests
    {
        private readonly GrantService service = new GrantService();

        [Fact]
        public void GrantMatchesServiceEnvironmentVariableMaskAndPermission()
        {
            var rules = new[] { "database|backend.dev.PREFECT_*|*_S3_*.Read|BatchExport" };

            Assert.True(service.Allows(rules, "backend", "dev", "BACKUP_S3_REGION", "Read"));
            Assert.True(service.Allows(rules, "database", "dev", "PREFECT_API_URL", "BatchExport"));
            Assert.False(service.Allows(rules, "backend", "prod", "BACKUP_S3_REGION", "Read"));
            Assert.False(service.Allows(rules, "backend", "dev", "DATABASE_URL", "Read"));
        }

        [Fact]
        public void NormalizePreservesSelectorOrderAndCollapsesStars()
        {
            Assert.Equal("database|backend.dev.*_S3_*.Read|BatchExport",
                service.Normalize("database|backend.dev.**_S3_**.Read|BatchExport"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("database.dev.Read")]
        [InlineData("database.dev.*.Unknown")]
        [InlineData("database..*.Read")]
        public void InvalidGrantFailsClosed(string rule)
        {
            Assert.ThrowsAny<Exception>(() => service.Parse(rule));
        }
    }
}

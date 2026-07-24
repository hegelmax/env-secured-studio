using System;
using System.Collections.Generic;
using EnvSecured.Core.Models;
using EnvSecured.Core.Services;
using Xunit;

namespace EnvSecured.Tests
{
    public sealed class TokenScopePolicyServiceTests
    {
        private readonly TokenScopePolicyService policy = new TokenScopePolicyService();

        [Fact]
        public void ServiceTokenCannotGrantAnotherOrAllServices()
        {
            var project = CreateProject();
            var token = new AccessTokenModel
            {
                Type = "Service",
                ServiceId = "database",
                Grants = new List<string> { "database.*.*.Read|BatchExport" }
            };
            policy.Validate(project, token);

            token.Grants = new List<string> { "database|backend.*.*.Read" };
            Assert.Throws<InvalidOperationException>(() => policy.Validate(project, token));
            token.Grants = new List<string> { "*.*.*.Read" };
            Assert.Throws<InvalidOperationException>(() => policy.Validate(project, token));
        }

        [Fact]
        public void EnvironmentAndPairTokensAreRestrictedToTheirTargets()
        {
            var project = CreateProject();
            var environment = new AccessTokenModel
            {
                Type = "Environment",
                EnvironmentId = "dev",
                Grants = new List<string> { "*.dev.*.Read" }
            };
            policy.Validate(project, environment);
            environment.Grants = new List<string> { "*.dev|prod.*.Read" };
            Assert.Throws<InvalidOperationException>(() => policy.Validate(project, environment));

            var pair = new AccessTokenModel
            {
                Type = "ServiceEnvironment",
                ServiceId = "backend",
                EnvironmentId = "prod",
                Grants = new List<string> { "backend.prod.*.Read|BatchExport" }
            };
            policy.Validate(project, pair);
            Assert.True(policy.AllowsTarget(pair, "backend", "prod"));
            Assert.False(policy.AllowsTarget(pair, "backend", "dev"));
        }

        [Fact]
        public void LegacyTypedTokenScopeCanBeInferredFromGrant()
        {
            var token = new AccessTokenModel
            {
                Type = "ServiceEnvironment",
                Grants = new List<string> { "backend.dev.*.Read" }
            };

            policy.NormalizeMetadata(CreateProject(), token);

            Assert.Equal("backend", token.ServiceId);
            Assert.Equal("dev", token.EnvironmentId);
        }

        private static ProjectModel CreateProject()
        {
            var project = new ProjectModel();
            project.Services.Add(new ServiceModel { Id = "database", Name = "database" });
            project.Services.Add(new ServiceModel { Id = "backend", Name = "backend" });
            project.Environments.Add(new EnvironmentModel { Id = "dev", Name = "dev" });
            project.Environments.Add(new EnvironmentModel { Id = "prod", Name = "prod" });
            return project;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using EnvSecured.Core.Models;

namespace EnvSecured.Core.Services
{
    public sealed class TokenScopePolicyService
    {
        private readonly GrantService grants = new GrantService();

        public string NormalizeType(string type)
        {
            if (string.Equals(type, "Service", StringComparison.OrdinalIgnoreCase)) return "Service";
            if (string.Equals(type, "Environment", StringComparison.OrdinalIgnoreCase)) return "Environment";
            if (string.Equals(type, "ServiceEnvironment", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "Service-Environment", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "Service_Environment", StringComparison.OrdinalIgnoreCase)) return "ServiceEnvironment";
            if (string.Equals(type, "Custom", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(type)) return "Custom";
            throw new InvalidOperationException("Unknown access token type: " + type);
        }

        public void NormalizeMetadata(ProjectModel project, AccessTokenModel token)
        {
            token.Type = NormalizeType(token.Type);
            if (token.Type == "Custom")
            {
                token.ServiceId = null;
                token.EnvironmentId = null;
                return;
            }
            if ((token.Type == "Service" || token.Type == "ServiceEnvironment") && string.IsNullOrWhiteSpace(token.ServiceId))
                token.ServiceId = InferSingleSelector(token.Grants, true);
            if ((token.Type == "Environment" || token.Type == "ServiceEnvironment") && string.IsNullOrWhiteSpace(token.EnvironmentId))
                token.EnvironmentId = InferSingleSelector(token.Grants, false);
            if (token.Type == "Service") token.EnvironmentId = null;
            if (token.Type == "Environment") token.ServiceId = null;
        }

        public void Validate(ProjectModel project, AccessTokenModel token)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            if (token == null) throw new ArgumentNullException(nameof(token));
            var type = NormalizeType(token.Type);
            if (token.Grants == null || token.Grants.Count == 0)
                throw new InvalidOperationException("At least one token grant is required.");
            var serviceId = ResolveServiceId(token);
            var environmentId = ResolveEnvironmentId(token);
            if (type == "Service" || type == "ServiceEnvironment")
            {
                if (string.IsNullOrWhiteSpace(serviceId)) throw new InvalidOperationException(type + " token requires a service.");
                if (!project.Services.Any(x => string.Equals(x.Id, serviceId, StringComparison.Ordinal)))
                    throw new InvalidOperationException("Token service does not exist: " + serviceId);
            }
            if (type == "Environment" || type == "ServiceEnvironment")
            {
                if (string.IsNullOrWhiteSpace(environmentId)) throw new InvalidOperationException(type + " token requires an environment.");
                if (!project.Environments.Any(x => string.Equals(x.Id, environmentId, StringComparison.Ordinal)))
                    throw new InvalidOperationException("Token environment does not exist: " + environmentId);
            }

            foreach (var text in token.Grants ?? new List<string>())
            {
                var rule = grants.Parse(text);
                if ((type == "Service" || type == "ServiceEnvironment") &&
                    (rule.Services.Count != 1 || !string.Equals(rule.Services[0], serviceId, StringComparison.Ordinal)))
                    throw new InvalidOperationException(type + " token grants must be limited to service '" + serviceId + "'.");
                if ((type == "Environment" || type == "ServiceEnvironment") &&
                    (rule.Environments.Count != 1 || !string.Equals(rule.Environments[0], environmentId, StringComparison.Ordinal)))
                    throw new InvalidOperationException(type + " token grants must be limited to environment '" + environmentId + "'.");
            }
        }

        public bool AllowsTarget(AccessTokenModel token, string serviceId, string environmentId)
        {
            var type = NormalizeType(token.Type);
            if ((type == "Service" || type == "ServiceEnvironment") &&
                !string.Equals(ResolveServiceId(token), serviceId, StringComparison.Ordinal)) return false;
            if ((type == "Environment" || type == "ServiceEnvironment") &&
                !string.Equals(ResolveEnvironmentId(token), environmentId, StringComparison.Ordinal)) return false;
            return true;
        }

        public string ResolveServiceId(AccessTokenModel token) =>
            !string.IsNullOrWhiteSpace(token?.ServiceId) ? token.ServiceId : InferSingleSelector(token?.Grants, true);

        public string ResolveEnvironmentId(AccessTokenModel token) =>
            !string.IsNullOrWhiteSpace(token?.EnvironmentId) ? token.EnvironmentId : InferSingleSelector(token?.Grants, false);

        public string DefaultId(string type, string serviceId, string environmentId)
        {
            type = NormalizeType(type);
            if (type == "Service") return string.IsNullOrWhiteSpace(serviceId) ? null : "service-" + serviceId;
            if (type == "Environment") return string.IsNullOrWhiteSpace(environmentId) ? null : "environment-" + environmentId;
            if (type == "ServiceEnvironment") return string.IsNullOrWhiteSpace(serviceId) || string.IsNullOrWhiteSpace(environmentId) ? null : serviceId + "-" + environmentId;
            return null;
        }

        public string DefaultGrant(string type, string serviceId, string environmentId)
        {
            type = NormalizeType(type);
            if (type == "Service") return string.IsNullOrWhiteSpace(serviceId) ? null : serviceId + ".*.*.Read|BatchExport";
            if (type == "Environment") return string.IsNullOrWhiteSpace(environmentId) ? null : "*." + environmentId + ".*.Read|BatchExport";
            if (type == "ServiceEnvironment") return string.IsNullOrWhiteSpace(serviceId) || string.IsNullOrWhiteSpace(environmentId) ? null : serviceId + "." + environmentId + ".*.Read|BatchExport";
            return null;
        }

        private string InferSingleSelector(IEnumerable<string> rules, bool service)
        {
            var values = new HashSet<string>(StringComparer.Ordinal);
            foreach (var text in rules ?? Enumerable.Empty<string>())
            {
                var parsed = grants.Parse(text);
                var selector = service ? parsed.Services : parsed.Environments;
                if (selector.Count != 1 || selector[0] == "*") return null;
                values.Add(selector[0]);
            }
            return values.Count == 1 ? values.Single() : null;
        }
    }
}

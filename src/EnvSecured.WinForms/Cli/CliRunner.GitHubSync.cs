using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using EnvSecured.Core.Models;
using EnvSecured.Core.Services;

namespace EnvSecured.WinForms.Cli
{
    internal static partial class CliRunner
    {
        private static readonly ExternalCommandService ExternalCommandService = new ExternalCommandService();

        private static int ListSyncProviders(ProjectModel project, Dictionary<string, string> options)
        {
            var providers = (project.Sync?.Providers ?? new List<SyncProviderModel>())
                .OrderBy(p => p.Name)
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.Type,
                    p.IsEnabled,
                    Organization = SyncSetting(p, "organization"),
                    Repository = SyncSetting(p, "repository"),
                    Mode = DefaultIfBlank(SyncSetting(p, "syncMode"), "AddUpdate"),
                    EnvironmentMap = SyncEnvironmentMappings(p).ToDictionary(m => m.LocalEnvironment, m => m.RemoteEnvironment),
                    Services = SyncStringSet(p, "syncServices").OrderBy(v => v).ToArray(),
                    Environments = SyncStringSet(p, "syncEnvironments").OrderBy(v => v).ToArray(),
                    Secrets = SyncBool(p, "syncSecrets", true),
                    Variables = SyncBool(p, "syncVariables", true),
                    RequiredOnly = SyncBool(p, "syncRequiredOnly", false),
                    p.LastCheckedAt,
                    p.LastStatus
                })
                .ToList();

            if (WantsJson(options))
            {
                Console.WriteLine(new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(providers));
                return 0;
            }

            foreach (var provider in providers)
            {
                Console.WriteLine($"{provider.Id}\t{provider.Name}\t{provider.Type}\tenabled={provider.IsEnabled}\t{provider.Organization}/{provider.Repository}\tmode={provider.Mode}");
            }
            return 0;
        }

        private static int AddSyncProvider(ProjectModel project, Dictionary<string, string> options)
        {
            RejectStoredGitHubToken(options);
            project.Sync = project.Sync ?? new ProjectSyncSettings();
            project.Sync.Providers = project.Sync.Providers ?? new List<SyncProviderModel>();
            var name = Get(options, "name") ?? "GitHub Actions";
            var requestedId = Get(options, "id");
            var baseId = string.IsNullOrWhiteSpace(requestedId) ? Slug(name) : requestedId.Trim();
            if (string.IsNullOrWhiteSpace(baseId)) baseId = "github-actions";
            if (project.Sync.Providers.Any(p => Same(p.Id, baseId)))
                throw new InvalidOperationException("Sync provider id already exists: " + baseId);

            var provider = new SyncProviderModel
            {
                Id = baseId,
                Name = name.Trim(),
                Type = "GitHubActions",
                IsEnabled = !options.ContainsKey("enabled") || ParseBool(options["enabled"]),
                Settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            };
            ApplySyncProviderOptions(project, provider, options, true);
            EnsureCliSecretFingerprintKey(provider);
            ValidateCliSyncProvider(provider);
            project.Sync.Providers.Add(provider);
            Console.WriteLine("Created sync provider " + provider.Id + ".");
            return 0;
        }

        private static int EditSyncProvider(ProjectModel project, Dictionary<string, string> options)
        {
            RejectStoredGitHubToken(options);
            var provider = ResolveSyncProvider(project, Required(options, "provider"));
            ApplySyncProviderOptions(project, provider, options, false);
            EnsureCliSecretFingerprintKey(provider);
            ValidateCliSyncProvider(provider);
            Console.WriteLine("Updated sync provider " + provider.Id + ".");
            return 0;
        }

        private static int DeleteSyncProvider(ProjectModel project, Dictionary<string, string> options)
        {
            var provider = ResolveSyncProvider(project, Required(options, "provider"));
            project.Sync.Providers.Remove(provider);
            Console.WriteLine("Deleted sync provider " + provider.Id + ".");
            return 0;
        }

        private static int InitializeSyncProvider(ProjectModel project, Dictionary<string, string> options)
        {
            var provider = ResolveSyncProvider(project, Required(options, "provider"));
            ValidateCliSyncProvider(provider);
            if (!string.IsNullOrWhiteSpace(SyncSetting(provider, SecretFingerprintService.ProviderSettingKey)))
            {
                Console.WriteLine("Sync provider " + provider.Id + " is already initialized.");
                return 0;
            }
            EnsureCliSecretFingerprintKey(provider);
            Console.WriteLine("Initialized fingerprint key for sync provider " + provider.Id + ".");
            return 0;
        }

        private static int RotateSyncProviderKey(ProjectModel project, Dictionary<string, string> options)
        {
            var provider = ResolveSyncProvider(project, Required(options, "provider"));
            ValidateCliSyncProvider(provider);
            SetSyncSetting(provider, SecretFingerprintService.ProviderSettingKey, SecretFingerprintService.GenerateKey());
            Console.WriteLine("Rotated fingerprint key for sync provider " + provider.Id + ". The next AddUpdate, Mirror, or ForceReplace sync will refresh its secrets and fingerprints.");
            return 0;
        }

        private static void ApplySyncProviderOptions(ProjectModel project, SyncProviderModel provider, Dictionary<string, string> options, bool isNew)
        {
            if (options.ContainsKey("name")) provider.Name = options["name"].Trim();
            if (options.ContainsKey("enabled")) provider.IsEnabled = ParseBool(options["enabled"]);
            if (options.ContainsKey("organization")) SetSyncSetting(provider, "organization", options["organization"].Trim());
            if (options.ContainsKey("repository")) SetSyncSetting(provider, "repository", options["repository"].Trim());
            if (options.ContainsKey("mode")) SetSyncSetting(provider, "syncMode", NormalizeSyncMode(options["mode"]));
            else if (isNew) SetSyncSetting(provider, "syncMode", "AddUpdate");
            ApplySyncBoolOption(provider, options, "secrets", "syncSecrets", true, isNew);
            ApplySyncBoolOption(provider, options, "variables", "syncVariables", true, isNew);
            ApplySyncBoolOption(provider, options, "required-only", "syncRequiredOnly", false, isNew);

            if (options.ContainsKey("services"))
            {
                var ids = ResolveServiceFilter(project, options["services"]).OrderBy(v => v).ToArray();
                SetSyncJson(provider, "syncServices", ids);
            }
            else if (isNew)
            {
                SetSyncJson(provider, "syncServices", new string[0]);
            }

            Dictionary<string, string> environmentMap;
            if (options.ContainsKey("env-map"))
            {
                environmentMap = ParseCliEnvironmentMap(project, options["env-map"]);
                SetSyncJson(provider, "environmentMap", environmentMap);
                SetSyncJson(provider, "environmentInclude", environmentMap.Keys.OrderBy(v => v).ToArray());
            }
            else if (isNew)
            {
                environmentMap = project.Environments.Where(e => e.IsActive)
                    .OrderBy(e => e.SortOrder).ThenBy(e => e.Name)
                    .ToDictionary(e => e.Name, e => e.Name, StringComparer.OrdinalIgnoreCase);
                SetSyncJson(provider, "environmentMap", environmentMap);
                SetSyncJson(provider, "environmentInclude", environmentMap.Keys.ToArray());
            }

            if (options.ContainsKey("environments"))
            {
                var names = ResolveCliEnvironmentNames(project, options["environments"]);
                SetSyncJson(provider, "syncEnvironments", names);
            }
            else if (isNew)
            {
                SetSyncJson(provider, "syncEnvironments", new string[0]);
            }
            SetSyncSetting(provider, "authMode", "GitHub CLI (stored auth or GH_TOKEN/GITHUB_TOKEN)");
        }

        private static void ApplySyncBoolOption(SyncProviderModel provider, Dictionary<string, string> options, string option, string setting, bool defaultValue, bool isNew)
        {
            if (options.ContainsKey(option)) SetSyncSetting(provider, setting, ParseBool(options[option]) ? "true" : "false");
            else if (isNew) SetSyncSetting(provider, setting, defaultValue ? "true" : "false");
        }

        private static Dictionary<string, string> ParseCliEnvironmentMap(ProjectModel project, string value)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in SplitSyncFilter(value))
            {
                var separator = entry.IndexOf('=');
                var localValue = separator < 0 ? entry.Trim() : entry.Substring(0, separator).Trim();
                var remoteValue = separator < 0 ? localValue : entry.Substring(separator + 1).Trim();
                if (string.IsNullOrWhiteSpace(localValue) || string.IsNullOrWhiteSpace(remoteValue))
                    throw new InvalidOperationException("Invalid --env-map entry: " + entry + ". Expected local=github.");
                var environment = project.Environments.FirstOrDefault(e => Same(e.Id, localValue) || Same(e.Name, localValue));
                if (environment == null) throw new InvalidOperationException("Mapped vault environment was not found: " + localValue);
                if (result.Values.Contains(remoteValue, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException("GitHub environment is mapped more than once: " + remoteValue);
                result[environment.Name] = remoteValue;
            }
            if (result.Count == 0) throw new InvalidOperationException("--env-map must contain at least one mapping.");
            return result;
        }

        private static string[] ResolveCliEnvironmentNames(ProjectModel project, string value)
        {
            return SplitSyncFilter(value).Select(item =>
            {
                var environment = project.Environments.FirstOrDefault(e => Same(e.Id, item) || Same(e.Name, item));
                if (environment == null) throw new InvalidOperationException("Environment was not found: " + item);
                return environment.Name;
            }).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(v => v).ToArray();
        }

        private static void ValidateCliSyncProvider(SyncProviderModel provider)
        {
            if (!string.Equals(provider.Type, "GitHubActions", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Only GitHubActions providers are supported.");
            if (string.IsNullOrWhiteSpace(provider.Name)) throw new InvalidOperationException("Sync provider name is required.");
            RequiredSyncSetting(provider, "organization");
            RequiredSyncSetting(provider, "repository");
            NormalizeSyncMode(SyncSetting(provider, "syncMode"));
        }

        private static void EnsureCliSecretFingerprintKey(SyncProviderModel provider)
        {
            if (string.IsNullOrWhiteSpace(SyncSetting(provider, SecretFingerprintService.ProviderSettingKey)))
                SetSyncSetting(provider, SecretFingerprintService.ProviderSettingKey, SecretFingerprintService.GenerateKey());
        }

        private static void RejectStoredGitHubToken(Dictionary<string, string> options)
        {
            if (options.ContainsKey("token") || options.ContainsKey("pat"))
                throw new InvalidOperationException("GitHub tokens are not stored in the vault. Set GH_TOKEN or GITHUB_TOKEN for this process.");
        }

        private static void SetSyncSetting(SyncProviderModel provider, string key, string value)
        {
            provider.Settings = provider.Settings ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var existingKey = provider.Settings.Keys.FirstOrDefault(k => Same(k, key));
            if (existingKey != null && !string.Equals(existingKey, key, StringComparison.Ordinal)) provider.Settings.Remove(existingKey);
            provider.Settings[key] = value ?? string.Empty;
        }

        private static void SetSyncJson(SyncProviderModel provider, string key, object value)
        {
            SetSyncSetting(provider, key, new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(value));
        }

        private static int SyncGitHub(ProjectModel project, Dictionary<string, string> options, bool pushCommand)
        {
            var provider = ResolveSyncProvider(project, Get(options, "provider"));
            if (!provider.IsEnabled)
                throw new InvalidOperationException("Sync provider is disabled: " + provider.Id);
            if (!string.Equals(provider.Type, "GitHubActions", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Only GitHubActions providers are supported.");

            var organization = RequiredSyncSetting(provider, "organization");
            var repository = RequiredSyncSetting(provider, "repository");
            var mappings = FilterSyncMappings(project, provider, options);
            if (mappings.Count == 0)
                throw new InvalidOperationException("No environment mappings match the provider and CLI filters.");

            var ghPath = ExternalCommandService.TryFindCommand("gh");
            if (ghPath == null)
                throw new InvalidOperationException("GitHub CLI (`gh`) was not found. Install it from https://cli.github.com/ and run `gh auth login`.");
            RequireGitHubCommand(ghPath, "auth status", null, "GitHub CLI is not authenticated. Run `gh auth login` or set GH_TOKEN/GITHUB_TOKEN.");

            var repositoryRef = organization.Trim() + "/" + repository.Trim();
            var remoteEnvironmentNames = LoadGitHubEnvironmentNames(ghPath, repositoryRef);
            var plan = new List<CliGitHubSyncRow>();
            var serviceFilterSpecified = SplitSyncFilter(Get(options, "service")).Count > 0;
            var mode = NormalizeSyncMode(SyncSetting(provider, "syncMode"));
            var secretFingerprintKey = SyncSetting(provider, SecretFingerprintService.ProviderSettingKey);
            if (serviceFilterSpecified && (mode == "Mirror" || mode == "ForceReplace"))
            {
                Console.Error.WriteLine("Warning: remote-only deletions are suppressed because --service creates a partial sync view.");
            }

            foreach (var mapping in mappings)
            {
                var localRows = BuildCliLocalSyncRows(project, provider, mapping, options, plan);
                if (localRows.Any(v => v.IsSecret) && string.IsNullOrWhiteSpace(secretFingerprintKey))
                {
                    throw new InvalidOperationException("The provider has no secret fingerprint key. Open its settings in EnvSecured Studio and save it once.");
                }
                var environmentExists = remoteEnvironmentNames.Contains(mapping.RemoteEnvironment);
                if (!environmentExists)
                {
                    plan.Add(CliGitHubSyncRow.EnvironmentCreate(mapping));
                }
                var remote = environmentExists
                    ? LoadGitHubRemoteValues(ghPath, repositoryRef, mapping.RemoteEnvironment)
                    : new CliGitHubRemoteValues();
                AddCliGitHubPlan(
                    plan, localRows, remote, mapping, mode, !serviceFilterSpecified,
                    provider.Id, secretFingerprintKey,
                    SyncBool(provider, "syncSecrets", true), SyncBool(provider, "syncVariables", true));
            }

            var apply = pushCommand && options.ContainsKey("apply") && ParseBool(options["apply"]);
            var allowDelete = options.ContainsKey("allow-delete") && ParseBool(options["allow-delete"]);
            var conflicts = plan.Where(p => p.Action == "Conflict").ToList();
            var deletes = plan.Where(p => p.Action == "Delete").ToList();

            if (apply && conflicts.Count > 0)
            {
                PrintCliGitHubPlan(plan, options, false);
                Error("Sync was not applied because the plan contains local value conflicts.");
                return 2;
            }
            if (apply && deletes.Count > 0 && !allowDelete)
            {
                PrintCliGitHubPlan(plan, options, false);
                Error("Sync was not applied because the plan contains deletions. Review the plan, then pass --allow-delete true.");
                return 1;
            }

            if (apply)
            {
                ApplyCliGitHubPlan(ghPath, repositoryRef, plan);
            }

            PrintCliGitHubPlan(plan, options, apply);
            if (pushCommand && !apply)
            {
                Console.Error.WriteLine("Dry run only. Pass --apply true to write to GitHub.");
            }
            return DetermineSyncExitCode(plan, apply, options.ContainsKey("check") && ParseBool(options["check"]));
        }

        private static int DetermineSyncExitCode(List<CliGitHubSyncRow> plan, bool applied, bool check)
        {
            return DetermineSyncExitCodeFromActions(plan.Select(p => p.Action), applied, check);
        }

        private static int DetermineSyncExitCodeFromActions(IEnumerable<string> actions, bool applied, bool check)
        {
            var values = (actions ?? Enumerable.Empty<string>()).ToList();
            if (values.Any(p => p == "Conflict")) return 2;
            if (applied || !check) return 0;
            return values.Any(p => p == "Create" || p == "Update" || p == "Replace" || p == "Delete") ? 2 : 0;
        }

        private static SyncProviderModel ResolveSyncProvider(ProjectModel project, string value)
        {
            var providers = project.Sync?.Providers ?? new List<SyncProviderModel>();
            if (string.IsNullOrWhiteSpace(value))
            {
                if (providers.Count == 1) return providers[0];
                throw new InvalidOperationException("Specify --provider <id|name>. Use sync-list to see providers.");
            }
            var provider = providers.FirstOrDefault(p =>
                string.Equals(p.Id, value, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Name, value, StringComparison.OrdinalIgnoreCase));
            if (provider == null) throw new InvalidOperationException("Sync provider was not found: " + value);
            return provider;
        }

        private static List<CliGitHubEnvironmentMapping> FilterSyncMappings(ProjectModel project, SyncProviderModel provider, Dictionary<string, string> options)
        {
            var providerEnvironments = SyncStringSet(provider, "syncEnvironments");
            var cliEnvironments = ResolveEnvironmentFilter(project, Get(options, "env"));
            return SyncEnvironmentMappings(provider)
                .Where(m => providerEnvironments.Count == 0 || providerEnvironments.Contains(m.LocalEnvironment))
                .Where(m => cliEnvironments.Count == 0 || cliEnvironments.Contains(m.LocalEnvironment))
                .OrderBy(m => m.LocalEnvironment)
                .ToList();
        }

        private static HashSet<string> ResolveEnvironmentFilter(ProjectModel project, string value)
        {
            var requested = SplitSyncFilter(value);
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in requested)
            {
                var environment = project.Environments.FirstOrDefault(e =>
                    string.Equals(e.Id, item, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(e.Name, item, StringComparison.OrdinalIgnoreCase));
                if (environment == null) throw new InvalidOperationException("Environment was not found: " + item);
                result.Add(environment.Id);
                result.Add(environment.Name);
            }
            return result;
        }

        private static List<CliGitHubLocalValue> BuildCliLocalSyncRows(
            ProjectModel project,
            SyncProviderModel provider,
            CliGitHubEnvironmentMapping mapping,
            Dictionary<string, string> options,
            List<CliGitHubSyncRow> plan)
        {
            var environment = project.Environments.FirstOrDefault(e =>
                string.Equals(e.Id, mapping.LocalEnvironment, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(e.Name, mapping.LocalEnvironment, StringComparison.OrdinalIgnoreCase));
            if (environment == null)
                throw new InvalidOperationException("Mapped vault environment was not found: " + mapping.LocalEnvironment);

            var providerServices = SyncStringSet(provider, "syncServices");
            var cliServices = ResolveServiceFilter(project, Get(options, "service"));
            var includeSecrets = SyncBool(provider, "syncSecrets", true);
            var includeVariables = SyncBool(provider, "syncVariables", true);
            var requiredOnly = SyncBool(provider, "syncRequiredOnly", false);
            var byKey = new Dictionary<string, CliGitHubLocalValue>(StringComparer.OrdinalIgnoreCase);

            foreach (var service in project.Services.Where(s => s.IsActive).OrderBy(s => s.SortOrder).ThenBy(s => s.Name))
            {
                if (providerServices.Count > 0 && !providerServices.Contains(service.Id)) continue;
                if (cliServices.Count > 0 && !cliServices.Contains(service.Id)) continue;

                var effectiveValues = EffectiveConfigService.Build(project, service.Id, environment.Id)
                    .Where(x => !x.Missing)
                    .Where(x => ProjectService.IsVariableVisibleToService(project, x.Variable, service.Id))
                    .Where(x => ProjectService.IsVariableSyncEnabledForService(project, x.Variable.Id, service.Id));

                foreach (var effective in effectiveValues)
                {
                    var variable = effective.Variable;
                    if (variable == null) continue;
                    if (variable.IsSecret && !includeSecrets) continue;
                    if (!variable.IsSecret && !includeVariables) continue;
                    if (requiredOnly && !IsCliRequiredForService(project, variable.Id, service.Id)) continue;

                    var value = effective.Value ?? string.Empty;
                    if (byKey.TryGetValue(variable.Key, out var existing))
                    {
                        if (!string.Equals(existing.Value, value, StringComparison.Ordinal) || existing.IsSecret != variable.IsSecret)
                        {
                            plan.Add(CliGitHubSyncRow.Conflict(mapping, variable.Key, existing, value, variable.IsSecret, service.Name));
                        }
                        continue;
                    }
                    byKey[variable.Key] = new CliGitHubLocalValue(variable.Key, value, variable.IsSecret, service.Name);
                }
            }
            return byKey.Values.OrderBy(v => v.Key).ToList();
        }

        private static HashSet<string> ResolveServiceFilter(ProjectModel project, string value)
        {
            var requested = SplitSyncFilter(value);
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in requested)
            {
                var service = project.Services.FirstOrDefault(s =>
                    string.Equals(s.Id, item, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(s.Name, item, StringComparison.OrdinalIgnoreCase));
                if (service == null) throw new InvalidOperationException("Service was not found: " + item);
                result.Add(service.Id);
            }
            return result;
        }

        private static bool IsCliRequiredForService(ProjectModel project, string variableId, string serviceId)
        {
            return project.Contracts.Any(c =>
                c.VariableId == variableId && c.ServiceId == serviceId &&
                ProjectService.IsContractSyncEnabled(c) && c.Required);
        }

        private static void AddCliGitHubPlan(
            List<CliGitHubSyncRow> plan,
            List<CliGitHubLocalValue> localRows,
            CliGitHubRemoteValues remote,
            CliGitHubEnvironmentMapping mapping,
            string mode,
            bool allowPlanDeletes,
            string providerId = null,
            string secretFingerprintKey = null,
            bool manageSecrets = true,
            bool manageVariables = true)
        {
            var localKeys = new HashSet<string>(localRows.Select(v => v.Key), StringComparer.OrdinalIgnoreCase);
            var fingerprintKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var local in localRows)
            {
                if (local.IsSecret)
                {
                    var fingerprintKey = SecretFingerprintService.VariableName(local.Key);
                    fingerprintKeys.Add(fingerprintKey);
                    if (localKeys.Contains(fingerprintKey))
                    {
                        plan.Add(CliGitHubSyncRow.FingerprintConflict(mapping, local.Key, fingerprintKey, local.ServiceName));
                        continue;
                    }
                    var fingerprint = SecretFingerprintService.Compute(secretFingerprintKey, providerId, mapping.RemoteEnvironment, local.Key, local.Value);
                    var exists = remote.Secrets.Contains(local.Key);
                    var hasFingerprint = remote.Variables.TryGetValue(fingerprintKey, out var remoteFingerprint);
                    var fingerprintMatches = hasFingerprint && string.Equals(fingerprint, remoteFingerprint, StringComparison.Ordinal);
                    var action = !exists ? "Create" :
                        mode == "AddMissing" ? "Unchanged" :
                        mode == "ForceReplace" ? "Replace" :
                        fingerprintMatches ? "Unchanged" : "Update";
                    var status = !exists ? "Missing" :
                        mode == "AddMissing" && !fingerprintMatches ? "Exists; fingerprint unavailable or different (AddMissing leaves it unchanged)" :
                        fingerprintMatches ? "Fingerprint matches" : "Fingerprint missing or changed";
                    plan.Add(CliGitHubSyncRow.Value(mapping, action, local, exists ? "********" : "-", status));

                    if (!exists || mode != "AddMissing" || fingerprintMatches)
                    {
                        var fingerprintAction = !hasFingerprint ? "Create" :
                            mode == "ForceReplace" ? "Replace" :
                            fingerprintMatches ? "Unchanged" : "Update";
                        plan.Add(CliGitHubSyncRow.Fingerprint(mapping, fingerprintAction, fingerprintKey, fingerprint, hasFingerprint ? remoteFingerprint : "-", local.ServiceName));
                    }
                }
                else
                {
                    var exists = remote.Variables.TryGetValue(local.Key, out var remoteValue);
                    var same = exists && string.Equals(local.Value, remoteValue, StringComparison.Ordinal);
                    var action = !exists ? "Create" :
                        mode == "AddMissing" ? "Unchanged" :
                        mode == "ForceReplace" ? "Replace" :
                        same ? "Unchanged" : "Update";
                    plan.Add(CliGitHubSyncRow.Value(mapping, action, local, exists ? remoteValue : "-", !exists ? "Missing" : same ? "Same" : "Changed"));
                }
            }

            if ((mode != "Mirror" && mode != "ForceReplace") || !allowPlanDeletes) return;
            var localSecrets = new HashSet<string>(localRows.Where(v => v.IsSecret).Select(v => v.Key), StringComparer.OrdinalIgnoreCase);
            var localVariables = new HashSet<string>(localRows.Where(v => !v.IsSecret).Select(v => v.Key).Concat(fingerprintKeys), StringComparer.OrdinalIgnoreCase);
            if (manageSecrets)
            {
                foreach (var key in remote.Secrets.Where(k => !localSecrets.Contains(k)).OrderBy(k => k))
                    plan.Add(CliGitHubSyncRow.Delete(mapping, key, true));
            }
            if (manageVariables)
            {
                foreach (var pair in remote.Variables.Where(p => !localVariables.Contains(p.Key)).OrderBy(p => p.Key))
                    plan.Add(CliGitHubSyncRow.Delete(mapping, pair.Key, false, pair.Value));
            }
            else if (manageSecrets)
            {
                foreach (var pair in remote.Variables.Where(p =>
                    !localVariables.Contains(p.Key) &&
                    remote.Secrets.Any(secret => string.Equals(SecretFingerprintService.VariableName(secret), p.Key, StringComparison.OrdinalIgnoreCase))).OrderBy(p => p.Key))
                    plan.Add(CliGitHubSyncRow.Delete(mapping, pair.Key, false, pair.Value));
            }
        }

        private static void ApplyCliGitHubPlan(string ghPath, string repositoryRef, List<CliGitHubSyncRow> plan)
        {
            foreach (var row in plan.Where(p => p.Action != "Unchanged" && p.Action != "Conflict"))
            {
                if (row.Type == "Environment")
                {
                    var endpoint = "repos/" + repositoryRef + "/environments/" + Uri.EscapeDataString(row.GitHubEnvironment);
                    RequireGitHubCommand(ghPath, "api -X PUT " + QuoteArgument(endpoint), null, "Cannot create GitHub environment '" + row.GitHubEnvironment + "'.");
                    continue;
                }

                var typeCommand = row.IsSecret ? "secret" : "variable";
                if (row.Action == "Delete")
                {
                    RequireGitHubCommand(ghPath,
                        typeCommand + " delete " + QuoteArgument(row.Key) + " --env " + QuoteArgument(row.GitHubEnvironment) + " --repo " + QuoteArgument(repositoryRef),
                        null,
                        "Cannot delete GitHub " + typeCommand + " '" + row.Key + "'.");
                    continue;
                }

                RequireGitHubCommand(ghPath,
                    typeCommand + " set " + QuoteArgument(row.Key) + " --env " + QuoteArgument(row.GitHubEnvironment) + " --repo " + QuoteArgument(repositoryRef),
                    row.RawLocalValue ?? string.Empty,
                    "Cannot set GitHub " + typeCommand + " '" + row.Key + "'.");
            }
        }

        private static void PrintCliGitHubPlan(List<CliGitHubSyncRow> plan, Dictionary<string, string> options, bool applied)
        {
            if (WantsJson(options))
            {
                var output = new
                {
                    Applied = applied,
                    Summary = plan.GroupBy(p => p.Action).ToDictionary(g => g.Key, g => g.Count()),
                    Changes = plan.Select(p => new
                    {
                        p.Action,
                        p.Type,
                        p.Key,
                        LocalValue = p.IsSecret ? "********" : p.LocalValue,
                        p.RemoteValue,
                        VaultEnvironment = p.LocalEnvironment,
                        p.GitHubEnvironment,
                        p.Service,
                        p.Status
                    }).ToArray()
                };
                Console.WriteLine(new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(output));
                return;
            }

            Console.WriteLine("ACTION\tTYPE\tKEY\tVAULT_ENV\tGITHUB_ENV\tSERVICE\tSTATUS");
            foreach (var row in plan)
                Console.WriteLine($"{row.Action}\t{row.Type}\t{row.Key}\t{row.LocalEnvironment}\t{row.GitHubEnvironment}\t{row.Service}\t{row.Status}");
            Console.WriteLine("Summary: " + string.Join(", ", plan.GroupBy(p => p.Action).OrderBy(g => g.Key).Select(g => g.Key + "=" + g.Count())));
        }

        private static HashSet<string> LoadGitHubEnvironmentNames(string ghPath, string repositoryRef)
        {
            var output = RequireGitHubCommand(ghPath, "api " + QuoteArgument("repos/" + repositoryRef + "/environments") + " --paginate", null,
                "Cannot load GitHub environments for " + repositoryRef + ".");
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in ReadCliGitHubJsonPages(output))
            {
                if (!root.TryGetValue("environments", out var raw) || !(raw is IEnumerable values)) continue;
                foreach (var item in values.Cast<object>().OfType<Dictionary<string, object>>())
                {
                    var name = item.TryGetValue("name", out var rawName) ? Convert.ToString(rawName) : null;
                    if (!string.IsNullOrWhiteSpace(name)) result.Add(name);
                }
            }
            return result;
        }

        private static CliGitHubRemoteValues LoadGitHubRemoteValues(string ghPath, string repositoryRef, string environment)
        {
            var result = new CliGitHubRemoteValues();
            var basePath = "repos/" + repositoryRef + "/environments/" + Uri.EscapeDataString(environment);
            var variableJson = RequireGitHubCommand(ghPath, "api " + QuoteArgument(basePath + "/variables") + " --paginate", null,
                "Cannot load variables from GitHub environment '" + environment + "'.");
            foreach (var root in ReadCliGitHubJsonPages(variableJson))
            {
                if (!root.TryGetValue("variables", out var raw) || !(raw is IEnumerable values)) continue;
                foreach (var item in values.Cast<object>().OfType<Dictionary<string, object>>())
                {
                    var name = item.TryGetValue("name", out var rawName) ? Convert.ToString(rawName) : null;
                    var value = item.TryGetValue("value", out var rawValue) ? Convert.ToString(rawValue) : string.Empty;
                    if (!string.IsNullOrWhiteSpace(name)) result.Variables[name] = value ?? string.Empty;
                }
            }

            var secretJson = RequireGitHubCommand(ghPath, "api " + QuoteArgument(basePath + "/secrets") + " --paginate", null,
                "Cannot load secrets from GitHub environment '" + environment + "'.");
            foreach (var root in ReadCliGitHubJsonPages(secretJson))
            {
                if (!root.TryGetValue("secrets", out var raw) || !(raw is IEnumerable values)) continue;
                foreach (var item in values.Cast<object>().OfType<Dictionary<string, object>>())
                {
                    var name = item.TryGetValue("name", out var rawName) ? Convert.ToString(rawName) : null;
                    if (!string.IsNullOrWhiteSpace(name)) result.Secrets.Add(name);
                }
            }
            return result;
        }

        private static string RequireGitHubCommand(string ghPath, string arguments, string standardInput, string errorPrefix)
        {
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = ghPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardInput = standardInput != null,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                process.StartInfo.EnvironmentVariables["GH_PROMPT_DISABLED"] = "1";
                process.OutputDataReceived += (s, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                if (standardInput != null)
                {
                    process.StandardInput.Write(standardInput);
                    process.StandardInput.Close();
                }
                if (!process.WaitForExit(60000))
                {
                    try { process.Kill(); } catch { }
                    throw new InvalidOperationException(errorPrefix + " Command timed out.");
                }
                if (process.ExitCode != 0)
                {
                    var detail = FirstCliErrorLine(stderr.ToString());
                    throw new InvalidOperationException(errorPrefix + (string.IsNullOrWhiteSpace(detail) ? string.Empty : " " + detail));
                }
                return stdout.ToString();
            }
        }

        private static IEnumerable<Dictionary<string, object>> ReadCliGitHubJsonPages(string json)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            foreach (var page in SplitCliGitHubJsonObjects(json))
                yield return serializer.Deserialize<Dictionary<string, object>>(page);
        }

        private static IEnumerable<string> SplitCliGitHubJsonObjects(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) yield break;
            var start = -1;
            var depth = 0;
            var inString = false;
            var escaped = false;
            for (var i = 0; i < json.Length; i++)
            {
                var ch = json[i];
                if (inString)
                {
                    if (escaped) { escaped = false; continue; }
                    if (ch == '\\') { escaped = true; continue; }
                    if (ch == '"') inString = false;
                    continue;
                }
                if (ch == '"') { inString = true; continue; }
                if (ch == '{') { if (depth == 0) start = i; depth++; continue; }
                if (ch != '}') continue;
                depth--;
                if (depth == 0 && start >= 0)
                {
                    yield return json.Substring(start, i - start + 1);
                    start = -1;
                }
            }
        }

        private static List<CliGitHubEnvironmentMapping> SyncEnvironmentMappings(SyncProviderModel provider)
        {
            var result = new List<CliGitHubEnvironmentMapping>();
            var json = SyncSetting(provider, "environmentMap");
            if (string.IsNullOrWhiteSpace(json)) return result;
            try
            {
                var map = new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(json);
                var included = SyncStringSet(provider, "environmentInclude");
                foreach (var pair in map ?? new Dictionary<string, string>())
                {
                    if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value)) continue;
                    if (included.Count > 0 && !included.Contains(pair.Key)) continue;
                    result.Add(new CliGitHubEnvironmentMapping(pair.Key.Trim(), pair.Value.Trim()));
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Provider environmentMap is invalid: " + ex.Message);
            }
            return result;
        }

        private static HashSet<string> SyncStringSet(SyncProviderModel provider, string key)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var json = SyncSetting(provider, key);
            if (string.IsNullOrWhiteSpace(json)) return result;
            try
            {
                foreach (var value in new JavaScriptSerializer().Deserialize<string[]>(json) ?? new string[0])
                    if (!string.IsNullOrWhiteSpace(value)) result.Add(value.Trim());
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Provider setting '" + key + "' is invalid: " + ex.Message);
            }
            return result;
        }

        private static List<string> SplitSyncFilter(string value)
        {
            return (value ?? string.Empty)
                .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(v => v.Trim()).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string SyncSetting(SyncProviderModel provider, string key)
        {
            if (provider?.Settings == null) return null;
            var pair = provider.Settings.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
            return pair.Key == null ? null : pair.Value;
        }

        private static string RequiredSyncSetting(SyncProviderModel provider, string key)
        {
            var value = SyncSetting(provider, key);
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("Provider setting '" + key + "' is required.");
            return value.Trim();
        }

        private static bool SyncBool(SyncProviderModel provider, string key, bool defaultValue)
        {
            var value = SyncSetting(provider, key);
            return string.IsNullOrWhiteSpace(value) ? defaultValue : ParseBool(value);
        }

        private static string NormalizeSyncMode(string value)
        {
            value = DefaultIfBlank(value, "AddUpdate").Trim().Replace("-", string.Empty).Replace("_", string.Empty).Replace(" ", string.Empty);
            if (string.Equals(value, "AddMissing", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "AddMissingOnly", StringComparison.OrdinalIgnoreCase)) return "AddMissing";
            if (string.Equals(value, "AddUpdate", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "AddAndUpdate", StringComparison.OrdinalIgnoreCase)) return "AddUpdate";
            if (string.Equals(value, "Mirror", StringComparison.OrdinalIgnoreCase)) return "Mirror";
            if (string.Equals(value, "ForceReplace", StringComparison.OrdinalIgnoreCase)) return "ForceReplace";
            throw new InvalidOperationException("Unknown provider syncMode: " + value);
        }

        private static bool WantsJson(Dictionary<string, string> options)
        {
            return options.ContainsKey("json") || string.Equals(Get(options, "format"), "json", StringComparison.OrdinalIgnoreCase);
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static string FirstCliErrorLine(string value)
        {
            return (value ?? string.Empty).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        }

        private static void PrintSyncHelp()
        {
            Console.WriteLine("EnvSecured GitHub synchronization");
            Console.WriteLine("Commands:");
            Console.WriteLine("  sync-list              List configured providers");
            Console.WriteLine("  sync-provider-add      Create a GitHub Actions provider");
            Console.WriteLine("  sync-provider-edit     Update a provider");
            Console.WriteLine("  sync-provider-delete   Delete a provider");
            Console.WriteLine("  sync-provider-init     Initialize a missing fingerprint key");
            Console.WriteLine("  sync-key-rotate        Rotate a provider fingerprint key");
            Console.WriteLine("  sync-preview           Compare vault values with GitHub");
            Console.WriteLine("  sync-push              Preview or apply changes");
            Console.WriteLine();
            Console.WriteLine("Run '<command> --help' for command-specific options.");
            Console.WriteLine("Authentication: install gh and use stored 'gh auth login' credentials, GH_TOKEN, or GITHUB_TOKEN.");
        }

        private static void PrintSyncListHelp()
        {
            Console.WriteLine("Usage: EnvSecured.exe sync-list --file <vault.envs> [--format json]");
            PrintSyncAdminAuthenticationHelp();
            Console.WriteLine("Exit codes: 0 success; 1 vault/configuration error.");
        }

        private static void PrintSyncPreviewHelp()
        {
            Console.WriteLine("Usage: EnvSecured.exe sync-preview --file <vault.envs> --provider <id|name> [options]");
            Console.WriteLine("Options:");
            Console.WriteLine("  --service <id[;id2]>   Narrow configured services; remote deletions are suppressed");
            Console.WriteLine("  --env <id[;id2]>       Narrow configured environment mappings");
            Console.WriteLine("  --format json           Emit a machine-readable plan");
            Console.WriteLine("  --check true            Exit 2 when Create/Update/Replace/Delete drift exists");
            Console.WriteLine("Defaults: all provider-selected services/environments; text output; check=false; no writes.");
            PrintSyncAdminAuthenticationHelp();
            PrintSyncGitHubAuthenticationHelp();
            Console.WriteLine("Exit codes: 0 preview completed (or no drift with --check); 1 execution/configuration error; 2 drift or local conflict.");
        }

        private static void PrintSyncPushHelp()
        {
            Console.WriteLine("Usage: EnvSecured.exe sync-push --file <vault.envs> --provider <id|name> [options]");
            Console.WriteLine("Options:");
            Console.WriteLine("  --apply true            Write the displayed plan to GitHub; default false (dry run)");
            Console.WriteLine("  --allow-delete true     Permit Delete actions in Mirror/ForceReplace; default false");
            Console.WriteLine("  --service <id[;id2]>   Narrow services and suppress remote-only deletion");
            Console.WriteLine("  --env <id[;id2]>       Narrow environment mappings");
            Console.WriteLine("  --format json           Emit a machine-readable plan/result");
            Console.WriteLine("  --check true            In dry-run mode, exit 2 when drift exists");
            Console.WriteLine("Without --apply true this command never changes GitHub.");
            PrintSyncAdminAuthenticationHelp();
            PrintSyncGitHubAuthenticationHelp();
            Console.WriteLine("Exit codes: 0 success/no checked drift; 1 execution/configuration error (including unconfirmed deletion); 2 drift with --check or local conflict.");
        }

        private static void PrintSyncProviderAddHelp()
        {
            Console.WriteLine("Usage: EnvSecured.exe sync-provider-add --file <vault.envs> --organization <owner> --repository <repo> [options]");
            Console.WriteLine("Options:");
            Console.WriteLine("  --id <id>               Stable provider id; default slug of --name");
            Console.WriteLine("  --name <name>           Display name; default 'GitHub Actions'");
            Console.WriteLine("  --env-map <a=b;c=d>     Vault-to-GitHub environment mapping; default identity map for active envs");
            Console.WriteLine("  --services <a;b>        Selected service ids/names; default all active services");
            Console.WriteLine("  --environments <a;b>   Selected mapped vault environments; default all mappings");
            Console.WriteLine("  --mode <mode>           AddMissing|AddUpdate|Mirror|ForceReplace; default AddUpdate");
            Console.WriteLine("  --secrets <bool>        Synchronize secrets; default true");
            Console.WriteLine("  --variables <bool>      Synchronize variables; default true");
            Console.WriteLine("  --required-only <bool>  Include only required scope rows; default false");
            Console.WriteLine("  --enabled <bool>        Provider enabled state; default true");
            Console.WriteLine("A fingerprint key is generated automatically and stored only in EncryptedSettings.");
            PrintSyncAdminAuthenticationHelp();
            Console.WriteLine("GitHub tokens are not stored; use GH_TOKEN/GITHUB_TOKEN when previewing or pushing.");
            Console.WriteLine("Exit codes: 0 created; 1 validation/save error.");
        }

        private static void PrintSyncProviderEditHelp()
        {
            Console.WriteLine("Usage: EnvSecured.exe sync-provider-edit --file <vault.envs> --provider <id|name> [options]");
            Console.WriteLine("Editable options: --name, --organization, --repository, --env-map, --services, --environments,");
            Console.WriteLine("                  --mode, --secrets, --variables, --required-only, --enabled.");
            Console.WriteLine("Only supplied settings are changed. A missing fingerprint key is initialized automatically.");
            PrintSyncAdminAuthenticationHelp();
            Console.WriteLine("Exit codes: 0 updated; 1 validation/save error.");
        }

        private static void PrintSyncProviderDeleteHelp()
        {
            Console.WriteLine("Usage: EnvSecured.exe sync-provider-delete --file <vault.envs> --provider <id|name>");
            Console.WriteLine("Deletes only the provider definition from the vault; it does not delete GitHub values.");
            PrintSyncAdminAuthenticationHelp();
            Console.WriteLine("Exit codes: 0 deleted; 1 provider/save error.");
        }

        private static void PrintSyncProviderInitHelp()
        {
            Console.WriteLine("Usage: EnvSecured.exe sync-provider-init --file <vault.envs> --provider <id|name>");
            Console.WriteLine("Initializes a missing secret-fingerprint HMAC key. Existing keys are left unchanged.");
            PrintSyncAdminAuthenticationHelp();
            Console.WriteLine("Exit codes: 0 initialized/already initialized; 1 provider/save error.");
        }

        private static void PrintSyncKeyRotateHelp()
        {
            Console.WriteLine("Usage: EnvSecured.exe sync-key-rotate --file <vault.envs> --provider <id|name>");
            Console.WriteLine("Generates a new fingerprint HMAC key. The next non-AddMissing push refreshes every managed secret and fingerprint.");
            PrintSyncAdminAuthenticationHelp();
            Console.WriteLine("Exit codes: 0 rotated; 1 provider/save error.");
        }

        private static void PrintSyncAdminAuthenticationHelp()
        {
            Console.WriteLine("Vault authentication: --password <value> or ENVSECURED_PASSWORD.");
        }

        private static void PrintSyncGitHubAuthenticationHelp()
        {
            Console.WriteLine("GitHub authentication: installed gh with 'gh auth login', or GH_TOKEN/GITHUB_TOKEN in headless CI.");
        }

        private sealed class CliGitHubEnvironmentMapping
        {
            public CliGitHubEnvironmentMapping(string localEnvironment, string remoteEnvironment)
            {
                LocalEnvironment = localEnvironment;
                RemoteEnvironment = remoteEnvironment;
            }
            public string LocalEnvironment { get; }
            public string RemoteEnvironment { get; }
        }

        private sealed class CliGitHubLocalValue
        {
            public CliGitHubLocalValue(string key, string value, bool isSecret, string serviceName)
            {
                Key = key; Value = value; IsSecret = isSecret; ServiceName = serviceName;
            }
            public string Key { get; }
            public string Value { get; }
            public bool IsSecret { get; }
            public string ServiceName { get; }
        }

        private sealed class CliGitHubRemoteValues
        {
            public Dictionary<string, string> Variables { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> Secrets { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class CliGitHubSyncRow
        {
            public string Action { get; set; }
            public string Type { get; set; }
            public string Key { get; set; }
            public string LocalValue { get; set; }
            public string RawLocalValue { get; set; }
            public string RemoteValue { get; set; }
            public string LocalEnvironment { get; set; }
            public string GitHubEnvironment { get; set; }
            public string Service { get; set; }
            public string Status { get; set; }
            public bool IsSecret { get; set; }

            public static CliGitHubSyncRow EnvironmentCreate(CliGitHubEnvironmentMapping mapping) => new CliGitHubSyncRow
            {
                Action = "Create", Type = "Environment", Key = mapping.RemoteEnvironment,
                LocalEnvironment = mapping.LocalEnvironment, GitHubEnvironment = mapping.RemoteEnvironment,
                LocalValue = "-", RemoteValue = "-", Service = "-", Status = "GitHub environment is missing"
            };

            public static CliGitHubSyncRow Value(CliGitHubEnvironmentMapping mapping, string action, CliGitHubLocalValue local, string remoteValue, string status) => new CliGitHubSyncRow
            {
                Action = action, Type = local.IsSecret ? "Secret" : "Variable", Key = local.Key,
                LocalValue = local.IsSecret ? "********" : local.Value, RawLocalValue = local.Value,
                RemoteValue = remoteValue, LocalEnvironment = mapping.LocalEnvironment,
                GitHubEnvironment = mapping.RemoteEnvironment, Service = local.ServiceName,
                Status = status, IsSecret = local.IsSecret
            };

            public static CliGitHubSyncRow Delete(CliGitHubEnvironmentMapping mapping, string key, bool secret, string remoteValue = "********") => new CliGitHubSyncRow
            {
                Action = "Delete", Type = secret ? "Secret" : "Variable", Key = key,
                LocalValue = "-", RemoteValue = remoteValue, LocalEnvironment = mapping.LocalEnvironment,
                GitHubEnvironment = mapping.RemoteEnvironment, Service = "-", Status = "Remote-only",
                IsSecret = secret
            };

            public static CliGitHubSyncRow Fingerprint(CliGitHubEnvironmentMapping mapping, string action, string key, string fingerprint, string remoteValue, string serviceName) => new CliGitHubSyncRow
            {
                Action = action, Type = "Fingerprint", Key = key, LocalValue = fingerprint,
                RawLocalValue = fingerprint, RemoteValue = remoteValue,
                LocalEnvironment = mapping.LocalEnvironment, GitHubEnvironment = mapping.RemoteEnvironment,
                Service = serviceName, Status = action == "Unchanged" ? "Same" : remoteValue == "-" ? "Missing" : "Changed"
            };

            public static CliGitHubSyncRow FingerprintConflict(CliGitHubEnvironmentMapping mapping, string secretKey, string fingerprintKey, string serviceName) => new CliGitHubSyncRow
            {
                Action = "Conflict", Type = "Fingerprint", Key = fingerprintKey,
                LocalValue = "-", RemoteValue = "-", LocalEnvironment = mapping.LocalEnvironment,
                GitHubEnvironment = mapping.RemoteEnvironment, Service = serviceName,
                Status = "Secret '" + secretKey + "' requires this reserved fingerprint name, but it is already a vault variable"
            };

            public static CliGitHubSyncRow Conflict(CliGitHubEnvironmentMapping mapping, string key, CliGitHubLocalValue existing, string value, bool isSecret, string serviceName) => new CliGitHubSyncRow
            {
                Action = "Conflict", Type = existing.IsSecret == isSecret ? (isSecret ? "Secret" : "Variable") : "Type",
                Key = key, LocalValue = isSecret || existing.IsSecret ? "********" : existing.Value + " / " + value,
                RemoteValue = "-", LocalEnvironment = mapping.LocalEnvironment, GitHubEnvironment = mapping.RemoteEnvironment,
                Service = existing.ServiceName + " / " + serviceName, Status = "Different local values or types", IsSecret = isSecret || existing.IsSecret
            };
        }
    }
}

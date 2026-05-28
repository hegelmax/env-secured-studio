using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using EnvSecured.Core.Models;
using EnvSecured.Core.Services;
using EnvSecured.Core.Validation;
using EnvSecured.Crypto;

namespace EnvSecured.WinForms.Cli
{
    internal static class CliRunner
    {
        private static readonly VaultFileService VaultFileService = new VaultFileService();
        private static readonly ProjectService ProjectService = new ProjectService();
        private static readonly EffectiveConfigService EffectiveConfigService = new EffectiveConfigService();
        private static readonly ValidationService ValidationService = new ValidationService();
        private static readonly CryptoService CryptoService = new CryptoService();

        public static bool IsCliRequest(string[] args)
        {
            if (args == null || args.Length == 0) return false;
            if (Same(args[0], "cli")) return true;
            return IsKnownCommand(args[0]) || Same(args[0], "--help") || Same(args[0], "-h");
        }

        public static int Run(string[] args)
        {
            try
            {
                if (args == null) args = new string[0];
                if (args.Length > 0 && Same(args[0], "cli")) args = args.Skip(1).ToArray();

                if (args.Length == 0 || Has(args, "--help") || Has(args, "-h"))
                {
                    PrintHelp();
                    return 0;
                }

                var command = args[0].ToLowerInvariant();
                var options = ParseOptions(args.Skip(1).ToArray());
                switch (command)
                {
                    case "new": return NewProject(options);
                    case "save-as": return SaveAs(options);
                    case "info": return WithProject(options, p => PrintInfo(p));
                    case "validate": return WithProject(options, p => Validate(p));
                    case "list": return WithProject(options, p => List(p, options));
                    case "get": return WithProject(options, p => GetValue(p, options), true);
                    case "add-service": return WithProjectSave(options, p => AddService(p, options));
                    case "edit-service": return WithProjectSave(options, p => EditService(p, options));
                    case "delete-service": return WithProjectSave(options, p => DeleteService(p, options));
                    case "add-env": return WithProjectSave(options, p => AddEnvironment(p, options));
                    case "edit-env": return WithProjectSave(options, p => EditEnvironment(p, options));
                    case "delete-env": return WithProjectSave(options, p => DeleteEnvironment(p, options));
                    case "add-var": return WithProjectSave(options, p => AddVariable(p, options));
                    case "edit-var": return WithProjectSave(options, p => EditVariable(p, options));
                    case "delete-var": return WithProjectSave(options, p => DeleteVariable(p, options));
                    case "set": return WithProjectSave(options, p => SetValue(p, options));
                    case "delete-value": return WithProjectSave(options, p => DeleteValue(p, options));
                    case "generate": return WithProjectSave(options, p => GenerateValue(p, options));
                    case "use": return WithProjectSave(options, p => SetContract(p, options, true));
                    case "unuse": return WithProjectSave(options, p => SetContract(p, options, false));
                    case "import": return WithProjectSave(options, p => ImportConfig(p, options));
                    case "settings": return WithProjectSave(options, p => UpdateSettings(p, options));
                    case "project": return WithProjectSave(options, p => UpdateProject(p, options));
                    case "export-target": return WithProjectSave(options, p => UpdateExportTarget(p, options));
                    case "auto-assign": return WithProjectSave(options, p => AutoAssignContracts(p, options));
                    case "compact-values": return WithProjectSave(options, p => CompactValues(p));
                    case "export":
                    case "render": return WithProjectPath(options, (p, f) => ExportFiles(p, options, f), true);
                    default:
                        Error("Unknown command: " + command);
                        PrintHelp();
                        return 2;
                }
            }
            catch (Exception ex)
            {
                Error(ex.Message);
                return 1;
            }
        }

        private static bool IsKnownCommand(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            switch (value.ToLowerInvariant())
            {
                case "new":
                case "save-as":
                case "info":
                case "validate":
                case "list":
                case "get":
                case "add-service":
                case "edit-service":
                case "delete-service":
                case "add-env":
                case "edit-env":
                case "delete-env":
                case "add-var":
                case "edit-var":
                case "delete-var":
                case "set":
                case "delete-value":
                case "generate":
                case "use":
                case "unuse":
                case "import":
                case "settings":
                case "project":
                case "export-target":
                case "auto-assign":
                case "compact-values":
                case "export":
                case "render":
                    return true;
                default:
                    return false;
            }
        }

        private static int NewProject(Dictionary<string, string> options)
        {
            var file = Required(options, "file");
            var name = Get(options, "name") ?? Path.GetFileNameWithoutExtension(file);
            var project = ProjectService.CreateProject(name, Slug(name));
            SaveProject(project, file, options);
            Console.WriteLine("Created " + file);
            return 0;
        }

        private static int SaveAs(Dictionary<string, string> options)
        {
            var source = Required(options, "file");
            var target = Required(options, "to");
            var sourceFullPath = Path.GetFullPath(source);
            var targetFullPath = Path.GetFullPath(target);
            if (!File.Exists(sourceFullPath))
            {
                throw new InvalidOperationException("Source vault file does not exist.");
            }

            if (string.Equals(sourceFullPath, targetFullPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Source and target vault file paths are the same.");
            }

            var targetDirectory = Path.GetDirectoryName(targetFullPath);
            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            var overwrite = options.ContainsKey("overwrite") && ParseBool(options["overwrite"]);
            File.Copy(sourceFullPath, targetFullPath, overwrite);
            if (options.ContainsKey("delete-source") && ParseBool(options["delete-source"]))
            {
                File.Delete(sourceFullPath);
                VaultFileService.DeleteRecoveryBackup(sourceFullPath);
                Console.WriteLine("Moved " + sourceFullPath + " -> " + targetFullPath);
            }
            else
            {
                Console.WriteLine("Saved copy " + targetFullPath);
            }

            return 0;
        }

        private static int WithProject(Dictionary<string, string> options, Func<ProjectModel, int> action, bool cliExport = false)
        {
            var file = Required(options, "file");
            if (cliExport)
            {
                RequireExplicitPasswordForCliExport(file, options);
            }
            var project = LoadProject(file, options);
            return action(project);
        }

        private static int WithProjectPath(Dictionary<string, string> options, Func<ProjectModel, string, int> action, bool cliExport = false)
        {
            var file = Required(options, "file");
            if (cliExport)
            {
                RequireExplicitPasswordForCliExport(file, options);
            }
            var project = LoadProject(file, options);
            return action(project, file);
        }

        private static int WithProjectSave(Dictionary<string, string> options, Func<ProjectModel, int> action)
        {
            var file = Required(options, "file");
            var project = LoadProject(file, options);
            var code = action(project);
            if (code == 0)
            {
                SaveProject(project, file, options);
                VaultFileService.DeleteRecoveryBackup(file);
            }
            return code;
        }

        private static int PrintInfo(ProjectModel project)
        {
            Console.WriteLine("Project: " + project.ProjectName);
            Console.WriteLine("Services: " + project.Services.Count);
            Console.WriteLine("Environments: " + project.Environments.Count);
            Console.WriteLine("Variables: " + project.Variables.Count);
            Console.WriteLine("Values: " + project.Values.Count);
            Console.WriteLine("Encryption: " + EncryptionMode(project));
            return 0;
        }

        private static int Validate(ProjectModel project)
        {
            var results = ValidationService.Validate(project);
            foreach (var r in results)
            {
                Console.WriteLine($"{r.Severity}\t{r.Code}\t{VariableKey(project, r.VariableId)}\t{ServiceName(project, r.ServiceId)}\t{EnvironmentName(project, r.EnvironmentId)}\t{r.Message}");
            }
            return results.Any(r => r.Severity == ValidationSeverity.Error) ? 1 : 0;
        }

        private static int List(ProjectModel project, Dictionary<string, string> options)
        {
            var what = (Get(options, "what") ?? "variables").ToLowerInvariant();
            if (what == "services")
            {
                foreach (var s in project.Services.OrderBy(s => s.SortOrder)) Console.WriteLine($"{s.Id}\t{s.Name}\t{s.DefaultPrefix}\tsharedWithoutContract={s.AllowSharedVariablesWithoutContract}");
                return 0;
            }
            if (what == "envs" || what == "environments")
            {
                foreach (var e in project.Environments.OrderBy(e => e.SortOrder)) Console.WriteLine($"{e.Id}\t{e.Name}");
                return 0;
            }
            if (what == "values")
            {
                var showSecrets = Flag(options, "show-secrets");
                foreach (var v in project.Values.OrderBy(v => VariableKey(project, v.VariableId)))
                {
                    var variable = project.Variables.FirstOrDefault(x => x.Id == v.VariableId);
                    Console.WriteLine($"{VariableKey(project, v.VariableId)}\t{v.Scope}\t{ServiceName(project, v.ServiceId)}\t{EnvironmentName(project, v.EnvironmentId)}\t{CliDisplayValue(variable, v.Value, showSecrets)}");
                }
                return 0;
            }

            foreach (var v in project.Variables.OrderBy(v => v.SortOrder).ThenBy(v => v.Key))
            {
                Console.WriteLine($"{v.Key}\tsecret={v.IsSecret}\tsharedSecret={v.AllowSharedSecret}\trequired={IsRequired(project, v.Id)}\tallowNull={v.AllowNull}\tallowBlank={v.AllowBlank}");
            }
            return 0;
        }

        private static int GetValue(ProjectModel project, Dictionary<string, string> options)
        {
            var key = Required(options, "key");
            var variable = project.Variables.FirstOrDefault(v => Same(v.Key, key) || Same(v.Id, key));
            if (variable == null)
            {
                Error("Variable not found: " + key);
                return 1;
            }
            if (!variable.IsActive)
            {
                Error(variable.Key + " is inactive.");
                return 1;
            }

            var service = ResolveOptionalService(project, Get(options, "service"));
            var environment = ResolveOptionalEnvironment(project, Get(options, "env"));
            var calculated = GetValueCalculatedMode(options);
            var effective = calculated
                ? EffectiveConfigService.Build(project, service?.Id, environment?.Id).FirstOrDefault(x => x.Variable.Id == variable.Id)
                : EffectiveConfigService.BuildRawValue(project, variable, service?.Id, environment?.Id);

            if (effective == null || effective.Missing)
            {
                Error(variable.Key + " has no effective value for " + TargetLabel(service, environment) + ".");
                return 1;
            }

            var showSecrets = Flag(options, "show-secrets");
            var displayValue = CliDisplayValue(variable, effective.Value, showSecrets);
            if (GetValueJsonOutput(options))
            {
                Console.WriteLine(new JavaScriptSerializer().Serialize(new
                {
                    key = variable.Key,
                    variableId = variable.Id,
                    value = displayValue,
                    calculated,
                    service = service?.Name,
                    environment = environment?.Name,
                    sourceScope = effective.SourceScope?.ToString(),
                    sourceServiceId = effective.SourceServiceId,
                    sourceEnvironmentId = effective.SourceEnvironmentId,
                    updatedAt = effective.SourceUpdatedAt,
                    isSecret = variable.IsSecret,
                    masked = variable.IsSecret && !showSecrets
                }));
                return 0;
            }

            Console.WriteLine(displayValue);
            return 0;
        }

        private static bool GetValueJsonOutput(Dictionary<string, string> options)
        {
            if (Flag(options, "json")) return true;
            var format = Get(options, "format");
            return !string.IsNullOrWhiteSpace(format) && Same(format, "json");
        }

        private static bool GetValueCalculatedMode(Dictionary<string, string> options)
        {
            var mode = Get(options, "value");
            if (!string.IsNullOrWhiteSpace(mode))
            {
                if (Same(mode, "raw") || Same(mode, "direct")) return false;
                if (Same(mode, "calculated") || Same(mode, "effective")) return true;
                throw new InvalidOperationException("--value must be calculated or raw.");
            }
            if (options.ContainsKey("calculated"))
            {
                return ParseBool(options["calculated"]);
            }
            return true;
        }

        private static int AddService(ProjectModel project, Dictionary<string, string> options)
        {
            var name = Required(options, "name");
            var id = Get(options, "id") ?? Slug(name);
            if (project.Services.Any(s => s.Id == id || Same(s.Name, name))) throw new InvalidOperationException("Service already exists.");
            var outputFolder = options.ContainsKey("folder") ? options["folder"].Trim() : id;
            EnsureUniqueOutputFolder(project, null, outputFolder);
            project.Services.Add(new ServiceModel
            {
                Id = id,
                Name = id,
                DisplayName = name,
                OutputFolder = outputFolder,
                DefaultPrefix = Get(options, "prefix") ?? id.ToUpperInvariant() + "_",
                AllowSharedVariablesWithoutContract = !Flag(options, "strict-contracts"),
                SortOrder = project.Services.Count * 10,
                IsActive = true
            });
            Console.WriteLine("Added service " + id);
            return 0;
        }

        private static int EditService(ProjectModel project, Dictionary<string, string> options)
        {
            var service = FindService(project, Required(options, "service")) ?? throw new InvalidOperationException("Service not found.");
            if (options.ContainsKey("name"))
            {
                var name = options["name"].Trim();
                if (project.Services.Any(s => s != service && Same(s.Name, name))) throw new InvalidOperationException("Service name already exists.");
                service.Name = name;
            }
            if (options.ContainsKey("display")) service.DisplayName = options["display"];
            if (options.ContainsKey("description")) service.Description = options["description"];
            if (options.ContainsKey("folder"))
            {
                var outputFolder = options["folder"].Trim();
                EnsureUniqueOutputFolder(project, service, outputFolder);
                service.OutputFolder = outputFolder;
            }
            if (options.ContainsKey("prefix")) service.DefaultPrefix = options["prefix"];
            if (options.ContainsKey("active")) service.IsActive = ParseBool(options["active"]);
            if (options.ContainsKey("shared-without-contract")) service.AllowSharedVariablesWithoutContract = ParseBool(options["shared-without-contract"]);
            ApplyServiceExportNames(service, options);
            Console.WriteLine("Updated service " + service.Id);
            return 0;
        }

        private static int DeleteService(ProjectModel project, Dictionary<string, string> options)
        {
            var service = FindService(project, Required(options, "service")) ?? throw new InvalidOperationException("Service not found.");
            project.Services.Remove(service);
            project.Contracts.RemoveAll(c => c.ServiceId == service.Id);
            project.Values.RemoveAll(v => v.ServiceId == service.Id);
            project.Settings?.OutputTargets?.RemoveAll(t => t.ServiceId == service.Id);
            Console.WriteLine("Deleted service " + service.Id);
            return 0;
        }

        private static int AddEnvironment(ProjectModel project, Dictionary<string, string> options)
        {
            var name = Required(options, "name");
            var id = Get(options, "id") ?? Slug(name);
            if (project.Environments.Any(e => e.Id == id || Same(e.Name, name))) throw new InvalidOperationException("Environment already exists.");
            project.Environments.Add(new EnvironmentModel { Id = id, Name = id, DisplayName = name, SortOrder = project.Environments.Count * 10, IsActive = true });
            Console.WriteLine("Added environment " + id);
            return 0;
        }

        private static int EditEnvironment(ProjectModel project, Dictionary<string, string> options)
        {
            var env = FindEnvironment(project, Required(options, "env")) ?? throw new InvalidOperationException("Environment not found.");
            if (options.ContainsKey("name"))
            {
                var name = options["name"].Trim();
                if (project.Environments.Any(e => e != env && Same(e.Name, name))) throw new InvalidOperationException("Environment name already exists.");
                env.Name = name;
            }
            if (options.ContainsKey("display")) env.DisplayName = options["display"];
            if (options.ContainsKey("active")) env.IsActive = ParseBool(options["active"]);
            ApplyEnvironmentExportNames(env, options);
            Console.WriteLine("Updated environment " + env.Id);
            return 0;
        }

        private static int DeleteEnvironment(ProjectModel project, Dictionary<string, string> options)
        {
            var env = FindEnvironment(project, Required(options, "env")) ?? throw new InvalidOperationException("Environment not found.");
            project.Environments.Remove(env);
            project.Values.RemoveAll(v => v.EnvironmentId == env.Id);
            project.Settings?.OutputTargets?.RemoveAll(t => t.EnvironmentId == env.Id);
            Console.WriteLine("Deleted environment " + env.Id);
            return 0;
        }

        private static int AddVariable(ProjectModel project, Dictionary<string, string> options)
        {
            var key = Required(options, "key").Trim().ToUpperInvariant();
            if (project.Variables.Any(v => Same(v.Key, key))) throw new InvalidOperationException("Variable already exists.");
            var secret = Flag(options, "secret");
            var variable = new VariableDefinitionModel
            {
                Id = UniqueVariableId(project, key),
                Key = key,
                DisplayName = key,
                IsSecret = secret,
                Type = secret ? VariableType.Password : VariableType.String,
                AllowSharedSecret = secret && Flag(options, "allow-shared-secret"),
                AllowNull = Flag(options, "allow-null"),
                AllowBlank = Flag(options, "allow-blank"),
                SortOrder = project.Variables.Count * 10,
                IsActive = true
            };
            project.Variables.Add(variable);
            AutoAssignVariableToMatchingServices(project, variable);
            Console.WriteLine("Added variable " + key);
            return 0;
        }

        private static int EditVariable(ProjectModel project, Dictionary<string, string> options)
        {
            var variable = FindVariable(project, Required(options, "key")) ?? throw new InvalidOperationException("Variable not found.");
            if (options.ContainsKey("new-key"))
            {
                var oldKey = variable.Key;
                var key = options["new-key"].Trim().ToUpperInvariant();
                if (project.Variables.Any(v => v != variable && Same(v.Key, key))) throw new InvalidOperationException("Variable already exists.");
                variable.Key = key;
                if (!options.ContainsKey("update-refs") || ParseBool(options["update-refs"]))
                {
                    var updated = ProjectService.ReplaceInterpolationReferences(project, oldKey, key);
                    if (updated > 0) Console.WriteLine("Updated " + updated + " interpolation reference(s).");
                }
            }
            if (options.ContainsKey("description")) variable.Description = options["description"];
            if (options.ContainsKey("group")) variable.GroupName = options["group"];
            if (options.ContainsKey("type")) variable.Type = ParseVariableType(options["type"]);
            if (options.ContainsKey("secret"))
            {
                variable.IsSecret = ParseBool(options["secret"]);
                if (variable.IsSecret) variable.Type = VariableType.Password;
                else variable.AllowSharedSecret = false;
            }
            if (options.ContainsKey("allow-shared-secret"))
            {
                variable.AllowSharedSecret = ParseBool(options["allow-shared-secret"]);
                if (variable.AllowSharedSecret)
                {
                    variable.IsSecret = true;
                    variable.Type = VariableType.Password;
                }
            }
            if (options.ContainsKey("allow-null")) variable.AllowNull = ParseBool(options["allow-null"]);
            if (options.ContainsKey("allow-blank")) variable.AllowBlank = ParseBool(options["allow-blank"]);
            if (options.ContainsKey("active")) variable.IsActive = ParseBool(options["active"]);
            if (options.ContainsKey("demo-value")) variable.DemoValue = options["demo-value"];
            if (options.ContainsKey("demo-comment")) variable.DemoComment = options["demo-comment"];
            if (options.ContainsKey("generated")) variable.IsGenerated = ParseBool(options["generated"]);
            if (options.ContainsKey("generator")) variable.GeneratorType = GeneratedValueService.NormalizeType(options["generator"]);
            if (options.ContainsKey("generator-type")) variable.GeneratorType = GeneratedValueService.NormalizeType(options["generator-type"]);
            if (options.ContainsKey("generator-length")) variable.GeneratorLength = GeneratedValueService.NormalizeLength(ParseInt(options["generator-length"], "generator-length"), variable.GeneratorType);
            if (options.ContainsKey("generator-scope")) variable.GeneratorScope = GeneratedValueService.NormalizeScope(options["generator-scope"]);
            if (options.ContainsKey("generator-mode")) variable.GeneratorMode = GeneratedValueService.NormalizeMode(options["generator-mode"]);
            if (variable.IsGenerated)
            {
                variable.IsSecret = true;
                variable.Type = VariableType.Password;
                variable.GeneratorType = GeneratedValueService.NormalizeType(variable.GeneratorType);
                variable.GeneratorLength = GeneratedValueService.NormalizeLength(variable.GeneratorLength, variable.GeneratorType);
                variable.GeneratorScope = GeneratedValueService.NormalizeScope(variable.GeneratorScope);
                variable.GeneratorMode = GeneratedValueService.NormalizeMode(variable.GeneratorMode);
            }
            if (options.ContainsKey("owner-service"))
            {
                var owner = options["owner-service"];
                var oldOwnerServiceId = variable.OwnerServiceId;
                var newOwnerServiceId = (FindService(project, owner) ?? throw new InvalidOperationException("Owner service not found.")).Id;
                if (!SameNullable(oldOwnerServiceId, newOwnerServiceId) && (!options.ContainsKey("move-owner-values") || ParseBool(options["move-owner-values"])))
                {
                    var moved = ProjectService.MoveOwnerValues(project, variable.Id, oldOwnerServiceId, newOwnerServiceId);
                    if (moved > 0) Console.WriteLine("Moved " + moved + " owner value(s).");
                }
                variable.OwnerServiceId = newOwnerServiceId;
            }
            AutoAssignVariableToMatchingServices(project, variable);
            Console.WriteLine("Updated variable " + variable.Key);
            return 0;
        }

        private static int GenerateValue(ProjectModel project, Dictionary<string, string> options)
        {
            var variable = FindVariable(project, Required(options, "key")) ?? throw new InvalidOperationException("Variable not found.");
            if (!variable.IsGenerated)
            {
                throw new InvalidOperationException("Variable is not configured as generated.");
            }

            var generator = new GeneratedValueService();
            var overwrite = !options.ContainsKey("overwrite") || ParseBool(options["overwrite"]);
            var scope = GeneratedValueService.NormalizeScope(variable.GeneratorScope);
            var environmentIds = scope == GeneratedValueService.ScopeOwnerEnvironment
                ? ResolveGenerateEnvironments(project, options)
                : new string[] { null };

            var count = 0;
            foreach (var environmentId in environmentIds)
            {
                generator.Generate(project, variable, environmentId, overwrite);
                count++;
            }

            Console.WriteLine("Generated " + count + " value(s) for " + variable.Key);
            return 0;
        }

        private static int DeleteVariable(ProjectModel project, Dictionary<string, string> options)
        {
            var variable = FindVariable(project, Required(options, "key")) ?? throw new InvalidOperationException("Variable not found.");
            project.Variables.Remove(variable);
            project.Values.RemoveAll(v => v.VariableId == variable.Id);
            project.Contracts.RemoveAll(c => c.VariableId == variable.Id);
            Console.WriteLine("Deleted variable " + variable.Key);
            return 0;
        }

        private static int SetValue(ProjectModel project, Dictionary<string, string> options)
        {
            var variable = EnsureVariable(project, Required(options, "key"), Flag(options, "secret"));
            var target = ResolveTarget(project, options);
            if (!ProjectService.CanOverrideVariableForService(project, variable, target.ServiceId))
            {
                throw new InvalidOperationException("Variable is not in scope for this service or overriding is disabled.");
            }
            var value = Required(options, "value");
            var existing = FindDirectValue(project, variable.Id, target.Scope, target.ServiceId, target.EnvironmentId);
            if (existing == null)
            {
                project.Values.Add(new VariableValueModel
                {
                    Id = ProjectService.NewId(),
                    VariableId = variable.Id,
                    Scope = target.Scope,
                    ServiceId = target.ServiceId,
                    EnvironmentId = target.EnvironmentId,
                    Value = value,
                    IsEncrypted = variable.IsSecret,
                    UpdatedAtUtc = DateTime.UtcNow
                });
            }
            else
            {
                existing.Value = value;
                existing.IsEncrypted = variable.IsSecret;
                existing.UpdatedAtUtc = DateTime.UtcNow;
            }

            if (target.ServiceId != null) EnsureContract(project, variable.Id, target.ServiceId, true);
            Console.WriteLine("Set " + variable.Key);
            return 0;
        }

        private static int DeleteValue(ProjectModel project, Dictionary<string, string> options)
        {
            var variable = FindVariable(project, Required(options, "key")) ?? throw new InvalidOperationException("Variable not found.");
            var target = ResolveTarget(project, options);
            project.Values.RemoveAll(v => v.VariableId == variable.Id && v.Scope == target.Scope && v.ServiceId == target.ServiceId && v.EnvironmentId == target.EnvironmentId);
            Console.WriteLine("Deleted value " + variable.Key);
            return 0;
        }

        private static int SetContract(ProjectModel project, Dictionary<string, string> options, bool enabled)
        {
            var variable = FindVariable(project, Required(options, "key")) ?? throw new InvalidOperationException("Variable not found.");
            var service = FindService(project, Required(options, "service")) ?? throw new InvalidOperationException("Service not found.");
            var visible = options.ContainsKey("visible") ? ParseBool(options["visible"]) : options.ContainsKey("share") ? ParseBool(options["share"]) : true;
            var allowOverride = options.ContainsKey("override") ? ParseBool(options["override"]) : true;
            if (enabled && !visible) throw new InvalidOperationException("A service cannot export a variable that is not in its scope.");
            if (!visible && ProjectService.IsVariableVisibleToService(project, variable, service.Id))
            {
                var references = CountScopeInterpolationReferences(project, variable, service.Id);
                if (references > 0 && !Flag(options, "allow-broken-scope"))
                {
                    throw new InvalidOperationException(variable.Key + " is referenced by " + references + " value(s) in this service scope. Add --allow-broken-scope true to remove it anyway.");
                }
            }
            if (enabled)
            {
                EnsureContract(project, variable.Id, service.Id, !Flag(options, "optional"));
                var contract = project.Contracts.FirstOrDefault(c => c.VariableId == variable.Id && c.ServiceId == service.Id);
                if (contract != null)
                {
                    contract.VisibleToService = visible;
                    contract.AllowOverride = visible && allowOverride;
                    contract.ShareWithOtherServices = visible;
                }
            }
            else
            {
                var contract = project.Contracts.FirstOrDefault(c => c.VariableId == variable.Id && c.ServiceId == service.Id);
                if (ProjectService.HasGlobalValue(project, variable.Id) || !visible || !allowOverride)
                {
                    if (contract == null)
                    {
                        project.Contracts.Add(new VariableContractModel { Id = ProjectService.NewId(), VariableId = variable.Id, ServiceId = service.Id, Excluded = true, Required = false, VisibleToService = visible, AllowOverride = visible && allowOverride, ShareWithOtherServices = visible, SortOrder = project.Contracts.Count * 10 });
                    }
                    else
                    {
                        contract.Excluded = true;
                        contract.Required = false;
                        contract.VisibleToService = visible;
                        contract.AllowOverride = visible && allowOverride;
                        contract.ShareWithOtherServices = visible;
                    }
                }
                else
                {
                    project.Contracts.RemoveAll(c => c.VariableId == variable.Id && c.ServiceId == service.Id);
                }
            }
            Console.WriteLine((enabled ? "Enabled " : "Disabled ") + variable.Key + " for " + service.Id);
            return 0;
        }

        private static int ImportConfig(ProjectModel project, Dictionary<string, string> options)
        {
            var inputs = Required(options, "input").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            var total = 0;
            foreach (var input in inputs)
            {
                var importOptions = new Dictionary<string, string>(options, StringComparer.OrdinalIgnoreCase);
                if (!importOptions.ContainsKey("service") && !importOptions.ContainsKey("env")) InferTarget(project, input, importOptions);

                foreach (var pair in ParseConfigFile(input))
                {
                    importOptions["key"] = pair.Key;
                    importOptions["value"] = pair.Value;
                    SetValue(project, importOptions);
                    var variable = FindVariable(project, pair.Key);
                    if (variable != null && Flag(options, "secret")) variable.IsSecret = true;
                    var target = ResolveTarget(project, importOptions);
                    if (target.ServiceId != null && variable != null) EnsureContract(project, variable.Id, target.ServiceId, !Flag(options, "optional"));
                    total++;
                }
            }
            Console.WriteLine("Imported " + total + " value(s).");
            return 0;
        }

        private static int UpdateSettings(ProjectModel project, Dictionary<string, string> options)
        {
            project.Settings = project.Settings ?? new ProjectSettings();
            if (options.ContainsKey("output-root")) project.Settings.OutputRootFolder = options["output-root"];
            if (options.ContainsKey("format")) project.Settings.OutputFormat = NormalizeOutputFormat(options["format"]);
            if (options.ContainsKey("ext")) project.Settings.OutputExtension = NormalizeOutputExtension(options["ext"], project.Settings.OutputFormat);
            if (options.ContainsKey("global-mask")) project.Settings.OutputGlobalMask = options["global-mask"];
            if (options.ContainsKey("env-mask")) project.Settings.OutputEnvironmentMask = options["env-mask"];
            if (options.ContainsKey("service-mask")) project.Settings.OutputServiceMask = options["service-mask"];
            if (options.ContainsKey("service-env-mask")) project.Settings.OutputServiceEnvironmentMask = options["service-env-mask"];
            if (options.ContainsKey("single-file")) project.Settings.OutputStructuredSingleFile = ParseBool(options["single-file"]);
            if (options.ContainsKey("single-file-mask")) project.Settings.OutputStructuredSingleFileMask = options["single-file-mask"];
            if (options.ContainsKey("data")) project.Settings.OutputDataFiles = ParseBool(options["data"]);
            if (options.ContainsKey("manifest")) project.Settings.OutputServiceManifest = ParseBool(options["manifest"]);
            if (options.ContainsKey("render-mode")) SetOutputRenderMode(project.Settings, options["render-mode"]);
            if (options.ContainsKey("manifest-mask")) project.Settings.OutputServiceManifestMask = options["manifest-mask"];
            if (options.ContainsKey("manifest-values")) project.Settings.OutputServiceManifestValueMode = NormalizeManifestValueMode(options["manifest-values"]);
            if (options.ContainsKey("cli-export-password"))
            {
                var required = ParseBool(options["cli-export-password"]);
                var key = EnsureCrypto(project, options);
                try
                {
                    project.Settings.CliExportPasswordRequired = required;
                    project.Settings.CliExportPasswordRequiredPolicy = required;
                    project.Settings.CliExportPasswordRequiredEncrypted = CryptoService.EncryptString(required ? "required:true:v1" : "required:false:v1", key);
                }
                finally
                {
                    ClearKey(key);
                }
            }
            if (options.ContainsKey("encryption"))
            {
                var currentMode = EncryptionMode(project);
                var nextMode = NormalizeEncryptionMode(options["encryption"]);
                if (IsSecurityDowngrade(currentMode, nextMode) && !Flag(options, "allow-security-downgrade"))
                {
                    throw new InvalidOperationException("Changing encryption from " + currentMode + " to " + nextMode + " lowers vault security. Add --allow-security-downgrade true to confirm.");
                }
                project.Settings.EncryptionMode = nextMode;
            }
            project.Settings.EncryptAllValues = project.Settings.EncryptionMode == "AllValues";
            Console.WriteLine("Updated settings.");
            return 0;
        }

        private static int UpdateProject(ProjectModel project, Dictionary<string, string> options)
        {
            if (options.ContainsKey("name")) project.ProjectName = options["name"];
            if (options.ContainsKey("id")) project.ProjectId = options["id"];
            if (options.ContainsKey("description")) project.Description = options["description"];
            Console.WriteLine("Updated project.");
            return 0;
        }

        private static int UpdateExportTarget(ProjectModel project, Dictionary<string, string> options)
        {
            project.Settings = project.Settings ?? new ProjectSettings();
            project.Settings.OutputTargets = project.Settings.OutputTargets ?? new List<OutputTargetSetting>();
            if (Flag(options, "all"))
            {
                var value = !options.ContainsKey("enabled") || ParseBool(options["enabled"]);
                project.Settings.OutputTargets.Clear();
                foreach (var service in new ServiceModel[] { null }.Concat(project.Services.OrderBy(s => s.SortOrder)))
                {
                    foreach (var env in new EnvironmentModel[] { null }.Concat(project.Environments.OrderBy(e => e.SortOrder)))
                    {
                        project.Settings.OutputTargets.Add(new OutputTargetSetting { ServiceId = service?.Id, EnvironmentId = env?.Id, Enabled = value });
                    }
                }
                Console.WriteLine("Updated all export targets.");
                return 0;
            }

            var serviceName = Get(options, "service");
            var envName = Get(options, "env");
            var serviceId = string.IsNullOrWhiteSpace(serviceName) || Same(serviceName, "global") ? null : (FindService(project, serviceName) ?? throw new InvalidOperationException("Service not found.")).Id;
            var envId = string.IsNullOrWhiteSpace(envName) || Same(envName, "global") ? null : (FindEnvironment(project, envName) ?? throw new InvalidOperationException("Environment not found.")).Id;
            var enabled = !options.ContainsKey("enabled") || ParseBool(options["enabled"]);
            var target = project.Settings.OutputTargets.FirstOrDefault(t => SameNullable(t.ServiceId, serviceId) && SameNullable(t.EnvironmentId, envId));
            if (target == null)
            {
                project.Settings.OutputTargets.Add(new OutputTargetSetting { ServiceId = serviceId, EnvironmentId = envId, Enabled = enabled });
            }
            else
            {
                target.Enabled = enabled;
            }
            Console.WriteLine("Updated export target.");
            return 0;
        }

        private static int AutoAssignContracts(ProjectModel project, Dictionary<string, string> options)
        {
            var key = Get(options, "key");
            if (!string.IsNullOrWhiteSpace(key))
            {
                var variable = FindVariable(project, key) ?? throw new InvalidOperationException("Variable not found.");
                AutoAssignVariableToMatchingServices(project, variable);
                Console.WriteLine("Auto-assigned " + variable.Key);
                return 0;
            }

            foreach (var variable in project.Variables)
            {
                AutoAssignVariableToMatchingServices(project, variable);
            }
            Console.WriteLine("Auto-assigned variables by service prefixes.");
            return 0;
        }

        private static int CompactValues(ProjectModel project)
        {
            var compacted = project.Values
                .Select((value, index) => new { value, index })
                .GroupBy(x => new { x.value.VariableId, x.value.Scope, x.value.ServiceId, x.value.EnvironmentId })
                .Select(g => g.OrderBy(x => x.index).Last().value)
                .ToList();
            var removed = project.Values.Count - compacted.Count;
            project.Values = compacted;
            Console.WriteLine("Removed " + removed + " duplicate value(s).");
            return 0;
        }

        private static int ExportFiles(ProjectModel project, Dictionary<string, string> options, string projectFilePath)
        {
            project.Settings = project.Settings ?? new ProjectSettings();
            var root = ResolveOutputRootFolder(Get(options, "output-root") ?? project.Settings.OutputRootFolder, projectFilePath);
            if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("Set --output-root or project output settings.");
            var format = NormalizeOutputFormat(Get(options, "format") ?? project.Settings.OutputFormat);
            var extension = Get(options, "ext") ?? (options.ContainsKey("format") ? DefaultOutputExtension(format) : GetProjectExtension(project, format));
            var targets = ResolveExportTargets(project, options);
            var singleFile = format != "CONFIG" && (Flag(options, "single-file") || (!options.ContainsKey("single-file") && project.Settings.OutputStructuredSingleFile));
            var data = options.ContainsKey("data") ? ParseBool(options["data"]) : project.Settings.OutputDataFiles;
            var manifest = options.ContainsKey("manifest") ? ParseBool(options["manifest"]) : project.Settings.OutputServiceManifest;
            var manifestValueMode = NormalizeManifestValueMode(Get(options, "manifest-values") ?? project.Settings.OutputServiceManifestValueMode);
            if (options.ContainsKey("render-mode"))
            {
                ParseRenderMode(options["render-mode"], out data, out manifest);
            }
            var rendered = 0;
            if (data && singleFile)
            {
                var path = BuildStructuredOutputPath(project, root, format, extension, Get(options, "single-file-mask"));
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(path, FormatStructuredOutput(project, targets, format));
                Console.WriteLine(path);
                rendered++;
            }
            else if (data)
            {
                foreach (var target in targets)
                {
                    var values = BuildOutputValues(project, target.Service, target.Environment);
                    var path = BuildOutputPath(project, target, root, format, extension, options);
                    var directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                    File.WriteAllText(path, FormatOutputValues(values, format));
                    Console.WriteLine(path);
                    rendered++;
                }
            }
            if (manifest)
            {
                rendered += ExportServiceManifestFiles(project, targets, root, Get(options, "manifest-mask"), manifestValueMode);
            }
            Console.WriteLine("Rendered " + rendered + " file(s).");
            return 0;
        }

        private static void SetOutputRenderMode(ProjectSettings settings, string value)
        {
            ParseRenderMode(value, out var data, out var manifest);
            settings.OutputDataFiles = data;
            settings.OutputServiceManifest = manifest;
        }

        private static string NormalizeManifestValueMode(string value)
        {
            if (string.Equals(value, "demo", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "example", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "placeholder", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "pattern", StringComparison.OrdinalIgnoreCase))
            {
                return "Demo";
            }
            return "Empty";
        }

        private static void ParseRenderMode(string value, out bool data, out bool manifest)
        {
            var mode = (value ?? "data").Trim().ToLowerInvariant();
            if (mode == "manifest" || mode == "manifest-only")
            {
                data = false;
                manifest = true;
            }
            else if (mode == "both" || mode == "data+manifest" || mode == "all")
            {
                data = true;
                manifest = true;
            }
            else
            {
                data = true;
                manifest = false;
            }
        }

        private static int ExportServiceManifestFiles(ProjectModel project, List<OutputTarget> targets, string outputRoot, string maskOverride, string valueMode)
        {
            var rendered = 0;
            foreach (var service in targets
                .Select(t => t.Service)
                .Where(s => s != null)
                .GroupBy(s => s.Id)
                .Select(g => g.First())
                .OrderBy(s => s.SortOrder)
                .ThenBy(s => s.Name))
            {
                var content = BuildServiceManifestContent(project, service, valueMode);
                var path = BuildServiceManifestPath(project, service, outputRoot, maskOverride);
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(path, content);
                Console.WriteLine(path);
                rendered++;
            }
            return rendered;
        }

        private static string ResolveOutputRootFolder(string outputRootFolder, string projectFilePath)
        {
            if (string.IsNullOrWhiteSpace(outputRootFolder) || Path.IsPathRooted(outputRootFolder))
            {
                return outputRootFolder;
            }

            var baseFolder = Path.GetDirectoryName(Path.GetFullPath(projectFilePath));
            return Path.GetFullPath(Path.Combine(baseFolder ?? Environment.CurrentDirectory, outputRootFolder));
        }

        private static List<OutputTarget> ResolveExportTargets(ProjectModel project, Dictionary<string, string> options)
        {
            var all = Flag(options, "all");
            var serviceName = Get(options, "service");
            var envName = Get(options, "env");
            if (!all && serviceName == null && envName == null)
            {
                return ResolveConfiguredExportTargets(project);
            }

            var services = all || serviceName == "*"
                ? new ServiceModel[] { null }.Concat(project.Services.OrderBy(s => s.SortOrder)).ToList()
                : new List<ServiceModel> { serviceName == null || serviceName.Equals("global", StringComparison.OrdinalIgnoreCase) ? null : FindService(project, serviceName) ?? throw new InvalidOperationException("Service not found.") };
            var envs = all || envName == "*"
                ? new EnvironmentModel[] { null }.Concat(project.Environments.OrderBy(e => e.SortOrder)).ToList()
                : new List<EnvironmentModel> { envName == null || envName.Equals("global", StringComparison.OrdinalIgnoreCase) ? null : FindEnvironment(project, envName) ?? throw new InvalidOperationException("Environment not found.") };
            return services.SelectMany(s => envs.Select(e => new OutputTarget(s, e))).ToList();
        }

        private static List<OutputTarget> ResolveConfiguredExportTargets(ProjectModel project)
        {
            var services = new ServiceModel[] { null }.Concat(project.Services.OrderBy(s => s.SortOrder)).ToList();
            var environments = new EnvironmentModel[] { null }.Concat(project.Environments.OrderBy(e => e.SortOrder)).ToList();
            var saved = project.Settings?.OutputTargets;
            if (saved == null || saved.Count == 0)
            {
                return services.SelectMany(s => environments.Select(e => new OutputTarget(s, e))).ToList();
            }

            return services
                .SelectMany(s => environments.Select(e => new OutputTarget(s, e)))
                .Where(t => saved.Any(x => SameNullable(x.ServiceId, t.Service?.Id) && SameNullable(x.EnvironmentId, t.Environment?.Id) && x.Enabled))
                .ToList();
        }

        private static Dictionary<string, string> BuildOutputValues(ProjectModel project, ServiceModel service, EnvironmentModel environment)
        {
            var effective = EffectiveConfigService.Build(project, service?.Id, environment?.Id).Where(x => !x.Missing);
            if (service != null)
            {
                effective = effective.Where(x => ProjectService.IsVariableUsedByService(project, x.Variable.Id, service.Id));
            }
            return effective.OrderBy(x => x.Variable.SortOrder).ThenBy(x => x.Variable.Key).ToDictionary(x => x.Variable.Key, x => x.Value ?? string.Empty);
        }

        private static string BuildServiceManifestContent(ProjectModel project, ServiceModel service, string valueMode)
        {
            return string.Join(Environment.NewLine, ProjectService.GetVariablesUsedByService(project, service.Id)
                .Select(v => ManifestLine(v, valueMode)));
        }

        private static string ManifestLine(VariableDefinitionModel variable, string valueMode)
        {
            if (!string.Equals(valueMode, "Demo", StringComparison.OrdinalIgnoreCase))
            {
                return variable.Key + "=";
            }

            var line = variable.Key + "=" + (variable.DemoValue ?? string.Empty);
            var comment = SingleLineComment(variable.DemoComment);
            return string.IsNullOrWhiteSpace(comment) ? line : line + " # " + comment;
        }

        private static string SingleLineComment(string value)
        {
            return (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        }

        private static int CountScopeInterpolationReferences(ProjectModel project, VariableDefinitionModel variable, string serviceId)
        {
            if (project == null || variable == null || string.IsNullOrWhiteSpace(serviceId)) return 0;
            var token = "{{" + variable.Key + "}}";
            return project.Environments.Select(e => e.Id).Concat(new string[] { null })
                .SelectMany(environmentId => project.Variables
                    .Where(v => v.IsActive && v.Id != variable.Id)
                    .Select(v => EffectiveConfigService.BuildRawValue(project, v, serviceId, environmentId)))
                .Where(value => !value.Missing && !string.IsNullOrEmpty(value.Value))
                .Select(value => value.Value)
                .Distinct()
                .Count(value => value.Contains(token));
        }

        private static string BuildOutputPath(ProjectModel project, OutputTarget target, string outputRoot, string format, string extensionOverride, Dictionary<string, string> options)
        {
            var mask = target.Service == null && target.Environment == null
                ? DefaultIfBlank(Get(options, "global-mask") ?? project.Settings.OutputGlobalMask, @"apps\.env{.ext}")
                : target.Service == null
                    ? DefaultIfBlank(Get(options, "env-mask") ?? project.Settings.OutputEnvironmentMask, @"apps\.env.{env}{.ext}")
                    : target.Environment == null
                        ? DefaultIfBlank(Get(options, "service-mask") ?? project.Settings.OutputServiceMask, @"apps\{service}\.env{.ext}")
                        : DefaultIfBlank(Get(options, "service-env-mask") ?? project.Settings.OutputServiceEnvironmentMask, @"apps\{service}\.env.{env}{.ext}");
            var ext = NormalizeOutputExtension(extensionOverride ?? GetProjectExtension(project, format), format);
            var relative = ApplyOutputMaskPlaceholders(project, mask, ext, ExportServiceName(target.Service, "CONFIG", true), ExportEnvironmentName(target.Environment, "CONFIG"))
                .TrimStart('\\', '/');
            return Path.Combine(outputRoot, relative);
        }

        private static string BuildStructuredOutputPath(ProjectModel project, string outputRoot, string format, string extension, string maskOverride)
        {
            var ext = NormalizeOutputExtension(extension, format);
            var relative = ApplyOutputMaskPlaceholders(project, DefaultIfBlank(maskOverride ?? project.Settings.OutputStructuredSingleFileMask, @"{project_name}{.ext}"), ext, string.Empty, string.Empty)
                .TrimStart('\\', '/');
            return Path.Combine(outputRoot, relative);
        }

        private static string BuildServiceManifestPath(ProjectModel project, ServiceModel service, string outputRoot, string maskOverride)
        {
            var relative = ApplyOutputMaskPlaceholders(project, DefaultIfBlank(maskOverride ?? project.Settings.OutputServiceManifestMask, @"apps\{service}\.env.example"), string.Empty, ExportServiceName(service, "CONFIG", true), string.Empty)
                .TrimStart('\\', '/');
            return Path.Combine(outputRoot, relative);
        }

        private static string ApplyOutputMaskPlaceholders(ProjectModel project, string mask, string extension, string serviceName, string environmentName)
        {
            var projectName = SafeOutputName(DefaultIfBlank(project.ProjectName, project.ProjectId));
            var result = (mask ?? string.Empty)
                .Replace("{project_name}", projectName)
                .Replace("{project}", projectName)
                .Replace("{service}", serviceName ?? string.Empty)
                .Replace("{env}", environmentName ?? string.Empty)
                .Replace("{.ext}", extension)
                .Replace("{ext}", extension.TrimStart('.'));
            return NormalizeRelativeOutputMask(result);
        }

        private static string NormalizeRelativeOutputMask(string value)
        {
            value = value ?? string.Empty;
            while (value.Contains(@"\\"))
            {
                value = value.Replace(@"\\", @"\");
            }
            while (value.Contains("//"))
            {
                value = value.Replace("//", "/");
            }
            return value;
        }

        private static ProjectModel LoadProject(string file, Dictionary<string, string> options)
        {
            var json = File.ReadAllText(file);
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            if (TryReadEncryptedEnvelope(json, serializer, out var envelope))
            {
                var key = DeriveKey(envelope.Crypto, options);
                try
                {
                    var projectJson = CryptoService.DecryptString(envelope.Payload, key);
                    var project = serializer.Deserialize<ProjectModel>(projectJson);
                    project.Crypto = envelope.Crypto;
                    ProjectService.EnsureProjectCollections(project);
                    DecryptValues(project, key);
                    DecryptCliExportPolicy(project, key);
                    return project;
                }
                finally
                {
                    ClearKey(key);
                }
            }

            var loaded = VaultFileService.Load(file);
            ProjectService.EnsureProjectCollections(loaded);
            if (ProjectHasEncryptedValues(loaded))
            {
                var key = DeriveKey(loaded.Crypto, options);
                try
                {
                    DecryptValues(loaded, key);
                    DecryptCliExportPolicy(loaded, key);
                }
                finally
                {
                    ClearKey(key);
                }
            }
            else if (loaded.Settings != null)
            {
                loaded.Settings.CliExportPasswordRequired = loaded.Settings.CliExportPasswordRequiredPolicy;
            }
            return loaded;
        }

        private static void RequireExplicitPasswordForCliExport(string file, Dictionary<string, string> options)
        {
            if (HasExplicitPassword(options)) return;
            if (!ReadCliExportPasswordRequired(file)) return;
            throw new InvalidOperationException("CLI export for this project requires --password or ENVSECURED_PASSWORD.");
        }

        private static bool ReadCliExportPasswordRequired(string file)
        {
            var json = File.ReadAllText(file);
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            if (TryReadEncryptedEnvelope(json, serializer, out _))
            {
                return true;
            }

            var project = serializer.Deserialize<ProjectModel>(json);
            return project?.Settings?.CliExportPasswordRequiredPolicy != false;
        }

        private static bool TryReadEncryptedEnvelope(string json, JavaScriptSerializer serializer, out EncryptedProjectFile envelope)
        {
            return EncryptedEnvelopeDetector.TryRead(json, serializer, out envelope);
        }

        private static bool HasExplicitPassword(Dictionary<string, string> options)
        {
            return !string.IsNullOrEmpty(Get(options, "password")) ||
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ENVSECURED_PASSWORD"));
        }

        private static bool ProjectHasEncryptedValues(ProjectModel project)
        {
            return project.Values.Any(v => v.IsEncrypted && v.EncryptedValue != null);
        }

        private static void DecryptCliExportPolicy(ProjectModel project, byte[] key)
        {
            project.Settings = project.Settings ?? new ProjectSettings();
            if (project.Settings.CliExportPasswordRequiredEncrypted == null)
            {
                project.Settings.CliExportPasswordRequired = true;
                return;
            }

            var value = CryptoService.DecryptString(project.Settings.CliExportPasswordRequiredEncrypted, key);
            project.Settings.CliExportPasswordRequired = string.Equals(value, "required:true:v1", StringComparison.Ordinal);
            project.Settings.CliExportPasswordRequiredPolicy = project.Settings.CliExportPasswordRequired;
        }

        private static void SaveProject(ProjectModel project, string file, Dictionary<string, string> options)
        {
            project.Settings = project.Settings ?? new ProjectSettings();
            var mode = EncryptionMode(project);
            if (mode == "WholeJson")
            {
                var key = EnsureCrypto(project, options);
                try
                {
                    var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                    var payloadProject = Clone(project);
                    foreach (var value in payloadProject.Values)
                    {
                        value.IsEncrypted = false;
                        value.EncryptedValue = null;
                    }
                    var envelope = new EncryptedProjectFile { Crypto = payloadProject.Crypto, Payload = CryptoService.EncryptString(serializer.Serialize(payloadProject), key) };
                    SaveText(serializer.Serialize(envelope), file);
                    return;
                }
                finally
                {
                    ClearKey(key);
                }
            }

            var storage = Clone(project);
            if (mode == "AllValues" || mode == "SecretsOnly")
            {
                var key = EnsureCrypto(storage, options);
                try
                {
                    foreach (var value in storage.Values)
                    {
                        var variable = storage.Variables.FirstOrDefault(v => v.Id == value.VariableId);
                        var encrypt = mode == "AllValues" || variable?.IsSecret == true;
                        if (!encrypt)
                        {
                            value.IsEncrypted = false;
                            value.EncryptedValue = null;
                            continue;
                        }
                        value.EncryptedValue = CryptoService.EncryptString(value.Value ?? string.Empty, key);
                        value.Value = null;
                        value.IsEncrypted = true;
                    }
                }
                finally
                {
                    ClearKey(key);
                }
            }
            else
            {
                foreach (var value in storage.Values)
                {
                    value.IsEncrypted = false;
                    value.EncryptedValue = null;
                }
            }
            VaultFileService.Save(storage, file);
        }

        private static byte[] EnsureCrypto(ProjectModel project, Dictionary<string, string> options)
        {
            project.Crypto = project.Crypto ?? new VaultCryptoMetadata();
            if (!string.IsNullOrWhiteSpace(project.Crypto.Salt) && project.Crypto.KeyCheck != null)
            {
                return DeriveKey(project.Crypto, options);
            }

            var password = Password(options);
            var salt = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);
            project.Crypto.Salt = Convert.ToBase64String(salt);
            project.Crypto.Iterations = project.Crypto.Iterations <= 0 ? 300000 : project.Crypto.Iterations;
            var key = CryptoService.DeriveKey(password, salt, project.Crypto.Iterations);
            Array.Clear(salt, 0, salt.Length);
            project.Crypto.KeyCheck = CryptoService.EncryptString("EnvSecuredVaultKeyCheck:v1", key);
            return key;
        }

        private static byte[] DeriveKey(VaultCryptoMetadata crypto, Dictionary<string, string> options)
        {
            if (crypto == null || string.IsNullOrWhiteSpace(crypto.Salt) || crypto.KeyCheck == null)
            {
                throw new InvalidOperationException("Project has encrypted values but no crypto metadata.");
            }

            var key = CryptoService.DeriveKey(Password(options), Convert.FromBase64String(crypto.Salt), crypto.Iterations);
            CryptoService.DecryptString(crypto.KeyCheck, key);
            return key;
        }

        private static string Password(Dictionary<string, string> options)
        {
            var password = Get(options, "password") ??
                Environment.GetEnvironmentVariable("ENVSECURED_PASSWORD");
            if (!string.IsNullOrEmpty(password)) return password;
            Console.Error.Write("Vault password: ");
            return ReadPassword();
        }

        private static string ReadPassword()
        {
            var chars = new List<char>();
            char[] buffer = null;
            try
            {
                ConsoleKeyInfo key;
                while ((key = Console.ReadKey(true)).Key != ConsoleKey.Enter)
                {
                    if (key.Key == ConsoleKey.Backspace && chars.Count > 0) chars.RemoveAt(chars.Count - 1);
                    else if (!char.IsControl(key.KeyChar)) chars.Add(key.KeyChar);
                }
                Console.Error.WriteLine();

                buffer = chars.ToArray();
                return new string(buffer);
            }
            finally
            {
                if (buffer != null) Array.Clear(buffer, 0, buffer.Length);
                for (var i = 0; i < chars.Count; i++)
                {
                    chars[i] = '\0';
                }
                chars.Clear();
            }
        }

        private static void ClearKey(byte[] key)
        {
            if (key != null) Array.Clear(key, 0, key.Length);
        }

        private static void DecryptValues(ProjectModel project, byte[] key)
        {
            foreach (var value in project.Values.Where(v => v.IsEncrypted && v.EncryptedValue != null))
            {
                value.Value = CryptoService.DecryptString(value.EncryptedValue, key);
                value.EncryptedValue = null;
            }
        }

        private static ProjectModel Clone(ProjectModel project)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            return serializer.Deserialize<ProjectModel>(serializer.Serialize(project));
        }

        private static void SaveText(string text, string path)
        {
            var tempPath = path + ".tmp";
            var backupPath = path + ".bak";
            try
            {
                File.WriteAllText(tempPath, text);
                if (File.Exists(path))
                {
                    File.Copy(path, backupPath, true);
                    File.Delete(path);
                }

                File.Move(tempPath, path);
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }
            }
            catch
            {
                try
                {
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                }
                catch
                {
                }
                throw;
            }
        }

        private static IEnumerable<KeyValuePair<string, string>> ParseConfigFile(string path)
        {
            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                if (line.StartsWith("export ", StringComparison.OrdinalIgnoreCase)) line = line.Substring("export ".Length).TrimStart();
                var equalsIndex = line.IndexOf('=');
                if (equalsIndex <= 0) continue;
                var key = line.Substring(0, equalsIndex).Trim();
                var value = line.Substring(equalsIndex + 1).Trim();
                if (value.Length >= 2 &&
                    ((value.StartsWith("\"") && value.EndsWith("\"")) ||
                    (value.StartsWith("'") && value.EndsWith("'"))))
                {
                    value = value.Substring(1, value.Length - 2);
                }
                yield return new KeyValuePair<string, string>(key.ToUpperInvariant(), value);
            }
        }

        private static string FormatOutputValues(Dictionary<string, string> values, string format)
        {
            if (format == "JSON") return new JavaScriptSerializer().Serialize(values);
            if (format == "XML") return "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + Environment.NewLine + "<config>" + Environment.NewLine + string.Join(Environment.NewLine, values.Select(p => $"  <add key=\"{System.Security.SecurityElement.Escape(p.Key)}\" value=\"{System.Security.SecurityElement.Escape(p.Value)}\" />")) + Environment.NewLine + "</config>";
            if (format == "YAML") return string.Join(Environment.NewLine, values.Select(p => $"{p.Key}: {QuoteJson(p.Value)}"));
            if (format == "TOML") return string.Join(Environment.NewLine, values.Select(p => $"{p.Key} = {QuoteJson(p.Value)}"));
            return string.Join(Environment.NewLine, values.Select(p => $"{p.Key}={p.Value}"));
        }

        private static string FormatStructuredOutput(ProjectModel project, List<OutputTarget> targets, string format)
        {
            if (format == "JSON") return new JavaScriptSerializer().Serialize(BuildStructuredOutputObject(project, targets));
            if (format == "XML") return FormatStructuredXml(project, targets);
            if (format == "YAML") return FormatStructuredYaml(project, targets);
            if (format == "TOML") return FormatStructuredToml(project, targets);
            return string.Join(Environment.NewLine, targets.SelectMany(target => BuildOutputValues(project, target.Service, target.Environment)).Select(pair => $"{pair.Key}={pair.Value}"));
        }

        private static Dictionary<string, object> BuildStructuredOutputObject(ProjectModel project, List<OutputTarget> targets)
        {
            var result = new Dictionary<string, object>();
            var environments = new Dictionary<string, object>();
            var services = new Dictionary<string, object>();
            foreach (var target in targets)
            {
                var values = BuildOutputValues(project, target.Service, target.Environment);
                if (target.Service == null && target.Environment == null) result["global"] = values;
                else if (target.Service == null) environments[ExportEnvironmentName(target.Environment, "JSON")] = values;
                else
                {
                    var serviceName = ExportServiceName(target.Service, "JSON", false);
                    if (!services.TryGetValue(serviceName, out var serviceObject))
                    {
                        serviceObject = new Dictionary<string, object>();
                        services[serviceName] = serviceObject;
                    }

                    var serviceMap = (Dictionary<string, object>)serviceObject;
                    if (target.Environment == null) serviceMap["global"] = values;
                    else
                    {
                        if (!serviceMap.TryGetValue("environments", out var serviceEnvironmentsObject))
                        {
                            serviceEnvironmentsObject = new Dictionary<string, object>();
                            serviceMap["environments"] = serviceEnvironmentsObject;
                        }
                        ((Dictionary<string, object>)serviceEnvironmentsObject)[ExportEnvironmentName(target.Environment, "JSON")] = values;
                    }
                }
            }
            if (environments.Count > 0) result["environments"] = environments;
            if (services.Count > 0) result["services"] = services;
            return result;
        }

        private static string FormatStructuredToml(ProjectModel project, List<OutputTarget> targets)
        {
            var lines = new List<string>();
            foreach (var target in targets)
            {
                var table = target.Service == null && target.Environment == null
                    ? "global"
                    : target.Service == null
                        ? "environments." + TomlPathSegment(ExportEnvironmentName(target.Environment, "TOML"))
                        : target.Environment == null
                            ? "services." + TomlPathSegment(ExportServiceName(target.Service, "TOML", false)) + ".global"
                            : "services." + TomlPathSegment(ExportServiceName(target.Service, "TOML", false)) + ".environments." + TomlPathSegment(ExportEnvironmentName(target.Environment, "TOML"));
                if (lines.Count > 0) lines.Add(string.Empty);
                lines.Add("[" + table + "]");
                lines.AddRange(BuildOutputValues(project, target.Service, target.Environment).Select(pair => TomlKey(pair.Key) + " = " + QuoteJson(pair.Value)));
            }
            return string.Join(Environment.NewLine, lines);
        }

        private static string FormatStructuredYaml(ProjectModel project, List<OutputTarget> targets)
        {
            var lines = new List<string>();
            var global = targets.FirstOrDefault(t => t.Service == null && t.Environment == null);
            if (global != null)
            {
                lines.Add("global:");
                AppendYamlValues(lines, BuildOutputValues(project, null, null), 2);
            }

            var environmentTargets = targets.Where(t => t.Service == null && t.Environment != null).ToList();
            if (environmentTargets.Count > 0)
            {
                lines.Add("environments:");
                foreach (var target in environmentTargets)
                {
                    lines.Add("  " + YamlKey(ExportEnvironmentName(target.Environment, "YAML")) + ":");
                    AppendYamlValues(lines, BuildOutputValues(project, null, target.Environment), 4);
                }
            }

            var serviceTargets = targets.Where(t => t.Service != null).GroupBy(t => t.Service.Id).ToList();
            if (serviceTargets.Count > 0)
            {
                lines.Add("services:");
                foreach (var serviceGroup in serviceTargets)
                {
                    var service = serviceGroup.First().Service;
                    lines.Add("  " + YamlKey(ExportServiceName(service, "YAML", false)) + ":");
                    var serviceGlobal = serviceGroup.FirstOrDefault(t => t.Environment == null);
                    if (serviceGlobal != null)
                    {
                        lines.Add("    global:");
                        AppendYamlValues(lines, BuildOutputValues(project, service, null), 6);
                    }

                    var serviceEnvironmentTargets = serviceGroup.Where(t => t.Environment != null).ToList();
                    if (serviceEnvironmentTargets.Count > 0)
                    {
                        lines.Add("    environments:");
                        foreach (var target in serviceEnvironmentTargets)
                        {
                            lines.Add("      " + YamlKey(ExportEnvironmentName(target.Environment, "YAML")) + ":");
                            AppendYamlValues(lines, BuildOutputValues(project, service, target.Environment), 8);
                        }
                    }
                }
            }
            return string.Join(Environment.NewLine, lines);
        }

        private static string FormatStructuredXml(ProjectModel project, List<OutputTarget> targets)
        {
            var lines = new List<string> { "<?xml version=\"1.0\" encoding=\"utf-8\"?>", "<config>" };
            foreach (var target in targets)
            {
                if (target.Service == null && target.Environment == null)
                {
                    lines.Add("  <global>");
                    AppendXmlValues(lines, BuildOutputValues(project, null, null), 4);
                    lines.Add("  </global>");
                }
                else if (target.Service == null)
                {
                    lines.Add($"  <environment name=\"{EscapeXml(ExportEnvironmentName(target.Environment, "XML"))}\">");
                    AppendXmlValues(lines, BuildOutputValues(project, null, target.Environment), 4);
                    lines.Add("  </environment>");
                }
                else if (target.Environment == null)
                {
                    lines.Add($"  <service name=\"{EscapeXml(ExportServiceName(target.Service, "XML", false))}\">");
                    lines.Add("    <global>");
                    AppendXmlValues(lines, BuildOutputValues(project, target.Service, null), 6);
                    lines.Add("    </global>");
                    lines.Add("  </service>");
                }
                else
                {
                    lines.Add($"  <service name=\"{EscapeXml(ExportServiceName(target.Service, "XML", false))}\" environment=\"{EscapeXml(ExportEnvironmentName(target.Environment, "XML"))}\">");
                    AppendXmlValues(lines, BuildOutputValues(project, target.Service, target.Environment), 4);
                    lines.Add("  </service>");
                }
            }
            lines.Add("</config>");
            return string.Join(Environment.NewLine, lines);
        }

        private static void AppendYamlValues(List<string> lines, Dictionary<string, string> values, int indent)
        {
            var prefix = new string(' ', indent);
            foreach (var pair in values) lines.Add(prefix + YamlKey(pair.Key) + ": " + QuoteJson(pair.Value));
        }

        private static void AppendXmlValues(List<string> lines, Dictionary<string, string> values, int indent)
        {
            var prefix = new string(' ', indent);
            foreach (var pair in values) lines.Add(prefix + $"<add key=\"{EscapeXml(pair.Key)}\" value=\"{EscapeXml(pair.Value)}\" />");
        }

        private static Target ResolveTarget(ProjectModel project, Dictionary<string, string> options)
        {
            var serviceName = Get(options, "service");
            var envName = Get(options, "env");
            var service = string.IsNullOrWhiteSpace(serviceName) || serviceName.Equals("global", StringComparison.OrdinalIgnoreCase) ? null : FindService(project, serviceName) ?? throw new InvalidOperationException("Service not found.");
            var env = string.IsNullOrWhiteSpace(envName) || envName.Equals("global", StringComparison.OrdinalIgnoreCase) ? null : FindEnvironment(project, envName) ?? throw new InvalidOperationException("Environment not found.");
            if (service == null && env == null) return new Target(ValueScope.Global, null, null);
            if (service == null) return new Target(ValueScope.Environment, null, env.Id);
            if (env == null) return new Target(ValueScope.Service, service.Id, null);
            return new Target(ValueScope.ServiceEnvironment, service.Id, env.Id);
        }

        private static ServiceModel ResolveOptionalService(ProjectModel project, string value)
        {
            if (string.IsNullOrWhiteSpace(value) || Same(value, "global")) return null;
            return FindService(project, value) ?? throw new InvalidOperationException("Service not found: " + value);
        }

        private static EnvironmentModel ResolveOptionalEnvironment(ProjectModel project, string value)
        {
            if (string.IsNullOrWhiteSpace(value) || Same(value, "global")) return null;
            return FindEnvironment(project, value) ?? throw new InvalidOperationException("Environment not found: " + value);
        }

        private static string TargetLabel(ServiceModel service, EnvironmentModel environment)
        {
            return (service?.Name ?? "global") + "/" + (environment?.Name ?? "global");
        }

        private static string[] ResolveGenerateEnvironments(ProjectModel project, Dictionary<string, string> options)
        {
            if (options.ContainsKey("all-envs") && ParseBool(options["all-envs"]))
            {
                return project.Environments.Where(e => e.IsActive).Select(e => e.Id).ToArray();
            }

            var environment = ResolveOptionalEnvironment(project, Get(options, "env"));
            if (environment == null)
            {
                throw new InvalidOperationException("--env is required for owner-environment generated values. Use --all-envs true to generate for every active environment.");
            }

            return new[] { environment.Id };
        }

        private static VariableDefinitionModel EnsureVariable(ProjectModel project, string key, bool secret)
        {
            var variable = FindVariable(project, key);
            if (variable != null)
            {
                if (secret)
                {
                    variable.IsSecret = true;
                    variable.Type = VariableType.Password;
                }
                return variable;
            }
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["key"] = key };
            if (secret) options["secret"] = "true";
            AddVariable(project, options);
            return FindVariable(project, key);
        }

        private static void EnsureContract(ProjectModel project, string variableId, string serviceId, bool required)
        {
            var contract = project.Contracts.FirstOrDefault(c => c.VariableId == variableId && c.ServiceId == serviceId);
            if (contract == null)
            {
                if (!ProjectService.HasGlobalValue(project, variableId))
                {
                    project.Contracts.Add(new VariableContractModel { Id = ProjectService.NewId(), VariableId = variableId, ServiceId = serviceId, Required = required, SortOrder = project.Contracts.Count * 10 });
                }
            }
            else
            {
                contract.Excluded = false;
                contract.Required = required;
            }
        }

        private static void InferTarget(ProjectModel project, string path, Dictionary<string, string> options)
        {
            var name = Path.GetFileName(path) ?? string.Empty;
            var service = project.Services.FirstOrDefault(s => name.IndexOf(s.Name, StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf(s.Id, StringComparison.OrdinalIgnoreCase) >= 0);
            var env = project.Environments.FirstOrDefault(e => name.IndexOf(e.Name, StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf(e.Id, StringComparison.OrdinalIgnoreCase) >= 0);
            if (service != null) options["service"] = service.Id;
            if (env != null) options["env"] = env.Id;
        }

        private static VariableValueModel FindDirectValue(ProjectModel project, string variableId, ValueScope scope, string serviceId, string environmentId) => project.Values.LastOrDefault(v => v.VariableId == variableId && v.Scope == scope && v.ServiceId == serviceId && v.EnvironmentId == environmentId);
        private static VariableDefinitionModel FindVariable(ProjectModel project, string key) => project.Variables.FirstOrDefault(v => Same(v.Key, key) || Same(v.Id, key));
        private static ServiceModel FindService(ProjectModel project, string value) => project.Services.FirstOrDefault(s => Same(s.Id, value) || Same(s.Name, value) || Same(s.DisplayName, value));
        private static EnvironmentModel FindEnvironment(ProjectModel project, string value) => project.Environments.FirstOrDefault(e => Same(e.Id, value) || Same(e.Name, value) || Same(e.DisplayName, value));
        private static bool IsRequired(ProjectModel project, string variableId) => project.Contracts.Any(c => c.VariableId == variableId && c.Required);
        private static string VariableKey(ProjectModel project, string id) => string.IsNullOrWhiteSpace(id) ? string.Empty : project.Variables.FirstOrDefault(v => v.Id == id)?.Key ?? id;
        private static string ServiceName(ProjectModel project, string id) => string.IsNullOrWhiteSpace(id) ? string.Empty : project.Services.FirstOrDefault(s => s.Id == id)?.Name ?? id;
        private static string EnvironmentName(ProjectModel project, string id) => string.IsNullOrWhiteSpace(id) ? string.Empty : project.Environments.FirstOrDefault(e => e.Id == id)?.Name ?? id;
        private static string CliDisplayValue(VariableDefinitionModel variable, string value, bool showSecrets) => variable?.IsSecret == true && !showSecrets ? "********" : value;
        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        private static bool SameNullable(string a, string b) => string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        private static string Get(Dictionary<string, string> options, string key) => options.TryGetValue(key, out var value) ? value : null;
        private static bool Has(string[] args, string value) => args.Any(a => Same(a, value));
        private static bool Flag(Dictionary<string, string> options, string key) => options.ContainsKey(key) && (options[key] == "true" || options[key] == string.Empty);
        private static bool ParseBool(string value) => string.IsNullOrWhiteSpace(value) || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1" || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
        private static int ParseInt(string value, string name) { if (int.TryParse(value, out var result)) return result; throw new InvalidOperationException("--" + name + " must be an integer."); }
        private static string Required(Dictionary<string, string> options, string key) => Get(options, key) ?? throw new InvalidOperationException("Missing --" + key);
        private static string Slug(string value) => new string((value ?? string.Empty).Trim().ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
        private static string UniqueVariableId(ProjectModel project, string key) { var id = Slug(key); var result = id; var i = 2; while (project.Variables.Any(v => v.Id == result)) result = id + "-" + i++; return result; }
        private static string EscapeXml(string value) => System.Security.SecurityElement.Escape(value ?? string.Empty);
        private static string TomlKey(string value) => "\"" + (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        private static string TomlPathSegment(string value) => TomlKey(value);
        private static string YamlKey(string value) => (value ?? string.Empty).All(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-') ? value : QuoteJson(value);
        private static string ExportServiceName(ServiceModel service, string format, bool pathName)
        {
            if (service == null) return string.Empty;
            format = NormalizeOutputFormat(format);
            if (format == "CONFIG") return DefaultIfBlank(service.ConfigName, pathName ? OutputFolderPathSegment(service) : service.Name);
            if (format == "TOML") return DefaultIfBlank(service.TomlName, service.Name);
            if (format == "YAML") return DefaultIfBlank(service.YamlName, service.Name);
            if (format == "XML") return DefaultIfBlank(service.XmlName, service.Name);
            if (format == "JSON") return DefaultIfBlank(service.JsonName, service.Name);
            return service.Name;
        }

        private static string OutputFolderPathSegment(ServiceModel service)
        {
            if (service == null) return string.Empty;
            return service.OutputFolder == null ? service.Name : service.OutputFolder.Trim();
        }

        private static void EnsureUniqueOutputFolder(ProjectModel project, ServiceModel currentService, string outputFolder)
        {
            var key = NormalizeOutputFolderKey(outputFolder);
            var duplicate = project.Services.FirstOrDefault(s =>
                s != currentService &&
                string.Equals(NormalizeOutputFolderKey(s.OutputFolder), key, StringComparison.OrdinalIgnoreCase));
            if (duplicate != null)
            {
                throw new InvalidOperationException("Service output folder must be unique. Empty output folder can be used by only one service.");
            }
        }

        private static string NormalizeOutputFolderKey(string value)
        {
            return (value ?? string.Empty).Trim().Trim('\\', '/');
        }

        private static string ExportEnvironmentName(EnvironmentModel environment, string format)
        {
            if (environment == null) return string.Empty;
            format = NormalizeOutputFormat(format);
            if (format == "CONFIG") return DefaultIfBlank(environment.ConfigName, environment.Name);
            if (format == "TOML") return DefaultIfBlank(environment.TomlName, environment.Name);
            if (format == "YAML") return DefaultIfBlank(environment.YamlName, environment.Name);
            if (format == "XML") return DefaultIfBlank(environment.XmlName, environment.Name);
            if (format == "JSON") return DefaultIfBlank(environment.JsonName, environment.Name);
            return environment.Name;
        }

        private static string QuoteJson(string value) => new JavaScriptSerializer().Serialize(value ?? string.Empty);
        private static string DefaultIfBlank(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
        private static string SafeOutputName(string value)
        {
            value = string.IsNullOrWhiteSpace(value) ? "project" : value.Trim();
            foreach (var invalid in Path.GetInvalidFileNameChars().Concat(Path.GetInvalidPathChars()).Distinct()) value = value.Replace(invalid, '-');
            return value;
        }
        private static string NormalizeOutputFormat(string value) { value = (value ?? "CONFIG").Trim().ToUpperInvariant(); return new[] { "CONFIG", "TOML", "YAML", "XML", "JSON" }.Contains(value) ? value : "CONFIG"; }
        private static string NormalizeOutputExtension(string extension, string format) { if (string.IsNullOrWhiteSpace(extension)) return DefaultOutputExtension(format); extension = extension.Trim(); return extension.StartsWith(".") ? extension : "." + extension; }
        private static string DefaultOutputExtension(string format) { format = NormalizeOutputFormat(format); if (format == "TOML") return ".toml"; if (format == "YAML") return ".yaml"; if (format == "XML") return ".xml"; if (format == "JSON") return ".json"; return ".env"; }
        private static string GetProjectExtension(ProjectModel project, string format) => project.Settings == null ? DefaultOutputExtension(format) : NormalizeOutputExtension(project.Settings.OutputExtension, format);
        private static string EncryptionMode(ProjectModel project) { var mode = project.Settings?.EncryptionMode; if (!string.IsNullOrWhiteSpace(mode)) return NormalizeEncryptionMode(mode); return project.Settings?.EncryptAllValues == true ? "AllValues" : "Open"; }
        private static string NormalizeEncryptionMode(string value) { value = (value ?? "Open").Trim().ToLowerInvariant(); if (value == "wholejson" || value == "whole-json") return "WholeJson"; if (value == "allvalues" || value == "all-values") return "AllValues"; if (value == "secretsonly" || value == "secrets-only") return "SecretsOnly"; return "Open"; }
        private static bool IsSecurityDowngrade(string currentMode, string nextMode) => EncryptionRank(nextMode) < EncryptionRank(currentMode);
        private static int EncryptionRank(string mode) { mode = NormalizeEncryptionMode(mode); if (mode == "WholeJson") return 3; if (mode == "AllValues") return 2; if (mode == "SecretsOnly") return 1; return 0; }
        private static void AutoAssignVariableToMatchingServices(ProjectModel project, VariableDefinitionModel variable) { foreach (var service in project.Services.Where(s => !string.IsNullOrWhiteSpace(s.DefaultPrefix) && variable.Key.StartsWith(s.DefaultPrefix.Trim(), StringComparison.OrdinalIgnoreCase))) EnsureContract(project, variable.Id, service.Id, true); }
        private static VariableType ParseVariableType(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return VariableType.String;
            return (VariableType)Enum.Parse(typeof(VariableType), value, true);
        }

        private static void ApplyServiceExportNames(ServiceModel service, Dictionary<string, string> options)
        {
            if (options.ContainsKey("config-name")) service.ConfigName = options["config-name"];
            if (options.ContainsKey("toml-name")) service.TomlName = options["toml-name"];
            if (options.ContainsKey("yaml-name")) service.YamlName = options["yaml-name"];
            if (options.ContainsKey("xml-name")) service.XmlName = options["xml-name"];
            if (options.ContainsKey("json-name")) service.JsonName = options["json-name"];
        }

        private static void ApplyEnvironmentExportNames(EnvironmentModel env, Dictionary<string, string> options)
        {
            if (options.ContainsKey("config-name")) env.ConfigName = options["config-name"];
            if (options.ContainsKey("toml-name")) env.TomlName = options["toml-name"];
            if (options.ContainsKey("yaml-name")) env.YamlName = options["yaml-name"];
            if (options.ContainsKey("xml-name")) env.XmlName = options["xml-name"];
            if (options.ContainsKey("json-name")) env.JsonName = options["json-name"];
        }

        private static Dictionary<string, string> ParseOptions(string[] args)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (!arg.StartsWith("--")) throw new InvalidOperationException("Unexpected argument: " + arg);
                var key = arg.Substring(2);
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) result[key] = args[++i];
                else result[key] = "true";
            }
            return result;
        }

        private static void Error(string message) => Console.Error.WriteLine("Error: " + message);

        private static void PrintHelp()
        {
            Console.WriteLine("EnvSecured Studio CLI");
            Console.WriteLine("Usage:");
            Console.WriteLine("  EnvSecured.exe <command> [options]");
            Console.WriteLine();
            Console.WriteLine("Project commands:");
            Console.WriteLine("  new --file <path> --name <name>");
            Console.WriteLine("  save-as --file <path> --to <path> [--overwrite true] [--delete-source true]");
            Console.WriteLine("  project --file <path> [--name name] [--id id] [--description text]");
            Console.WriteLine("  info|validate --file <path>");
            Console.WriteLine("  list --file <path> --what variables|values|services|envs [--show-secrets]");
            Console.WriteLine("  get --file <path> --key KEY [--service backend|global] [--env dev|global] [--value calculated|raw] [--calculated true|false] [--format json|--json] [--show-secrets]");
            Console.WriteLine("  --register-association | --unregister-association");
            Console.WriteLine("  --check-update | --download-update");
            Console.WriteLine("    --check-update exit codes: 0 no update, 10 update available, 2 check failed");
            Console.WriteLine();
            Console.WriteLine("Editing:");
            Console.WriteLine("  add-service --file <path> --name backend [--prefix BACKEND_] [--folder backend] [--strict-contracts]");
            Console.WriteLine("  edit-service --file <path> --service backend [--name name] [--display text] [--folder path] [--prefix PREFIX_] [--active true|false] [--shared-without-contract true|false]");
            Console.WriteLine("  delete-service --file <path> --service backend");
            Console.WriteLine("  add-env --file <path> --name dev");
            Console.WriteLine("  edit-env --file <path> --env dev [--name name] [--display text] [--active true|false]");
            Console.WriteLine("  delete-env --file <path> --env dev");
            Console.WriteLine("  add-var --file <path> --key DATABASE_HOST [--secret] [--allow-shared-secret] [--allow-null] [--allow-blank]");
            Console.WriteLine("  edit-var --file <path> --key KEY [--new-key KEY] [--update-refs true|false] [--owner-service service|global] [--move-owner-values true|false] [--secret true|false] [--allow-shared-secret true|false] [--allow-null true|false] [--allow-blank true|false] [--active true|false] [--generated true|false] [--generator password|token-hex|token-base62|guid] [--generator-length 32] [--generator-scope owner-global|owner-env] [--generator-mode manual|rotate-on-sync]");
            Console.WriteLine("  delete-var --file <path> --key KEY");
            Console.WriteLine("  set --file <path> --key KEY --value VALUE [--service backend|global] [--env dev|global] [--secret]");
            Console.WriteLine("  delete-value --file <path> --key KEY [--service backend|global] [--env dev|global]");
            Console.WriteLine("  generate --file <path> --key KEY [--env dev|--all-envs true] [--overwrite true|false]");
            Console.WriteLine("  use|unuse --file <path> --key KEY --service backend [--optional] [--visible true|false] [--override true|false] [--allow-broken-scope true]");
            Console.WriteLine("  auto-assign --file <path> [--key KEY]");
            Console.WriteLine("  compact-values --file <path>");
            Console.WriteLine();
            Console.WriteLine("Import/export:");
            Console.WriteLine("  import --file <path> --input a.env[;b.env] [--service backend] [--env dev] [--secret] [--optional]");
            Console.WriteLine("  settings --file <path> [--output-root C:\\project] [--format CONFIG|TOML|YAML|XML|JSON] [--ext .env] [--single-file true|false] [--single-file-mask {project_name}{.ext}] [--render-mode data|manifest|both] [--manifest-mask apps\\{service}\\.env.example] [--manifest-values empty|demo] [--cli-export-password true|false] [--encryption open|secrets-only|all-values|whole-json] [--allow-security-downgrade true]");
            Console.WriteLine("  export-target --file <path> [--all] [--service backend|global] [--env dev|global] [--enabled true|false]");
            Console.WriteLine("  export --file <path> [--all] [--service backend|global|*] [--env dev|global|*] [--output-root C:\\project] [--format CONFIG|TOML|YAML|XML|JSON] [--ext .env] [--single-file true|false] [--single-file-mask {project_name}{.ext}] [--render-mode data|manifest|both] [--manifest-mask mask] [--global-mask mask] [--env-mask mask] [--service-mask mask] [--service-env-mask mask]");
            Console.WriteLine();
            Console.WriteLine("Encrypted projects: pass --password <pwd> or ENVSECURED_PASSWORD.");
        }

        private sealed class Target
        {
            public Target(ValueScope scope, string serviceId, string environmentId) { Scope = scope; ServiceId = serviceId; EnvironmentId = environmentId; }
            public ValueScope Scope;
            public string ServiceId;
            public string EnvironmentId;
        }

        private sealed class OutputTarget
        {
            public OutputTarget(ServiceModel service, EnvironmentModel environment) { Service = service; Environment = environment; }
            public ServiceModel Service;
            public EnvironmentModel Environment;
        }
    }
}

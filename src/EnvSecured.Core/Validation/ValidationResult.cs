using EnvSecured.Core.Models;

namespace EnvSecured.Core.Validation
{
    public sealed class ValidationResult
    {
        public ValidationSeverity Severity { get; set; }
        public string Code { get; set; }
        public string Message { get; set; }
        public string VariableId { get; set; }
        public string ServiceId { get; set; }
        public string EnvironmentId { get; set; }
    }
}

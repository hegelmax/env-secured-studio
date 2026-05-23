namespace EnvSecured.Core.Models
{
    public enum VariableType { String, Number, Boolean, Url, Email, Password, Token, Path, ConnectionString, Json }
    public enum ValueScope { Global, Environment, Service, ServiceEnvironment }
    public enum ValidationSeverity { Info, Warning, Error }
}

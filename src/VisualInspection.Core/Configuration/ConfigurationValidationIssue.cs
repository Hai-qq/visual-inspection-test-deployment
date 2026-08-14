namespace VisualInspection.Core.Configuration;

public enum ConfigurationValidationSeverity
{
    Warning,
    Error
}

public sealed record ConfigurationValidationIssue(
    ConfigurationValidationSeverity Severity,
    string Code,
    string Path,
    string Message);

namespace API.Helpers;

/// <summary>
/// Helper class for input validation
/// </summary>
public static class ValidationHelper
{
    /// <summary>
    /// Validates a configuration name to prevent path traversal attacks
    /// </summary>
    /// <param name="name">Configuration name to validate</param>
    /// <returns>True if valid, false otherwise</returns>
    public static bool IsValidConfigName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        // Prevent path traversal
        if (name.Contains("..") || name.Contains("/") || name.Contains("\\"))
            return false;

        // Prevent special characters that could be used for injection
        if (name.Contains("<") || name.Contains(">") || name.Contains("|") || name.Contains("&"))
            return false;

        return true;
    }

    /// <summary>
    /// Validates a resource path to prevent path traversal attacks
    /// </summary>
    /// <param name="path">Resource path to validate</param>
    /// <returns>True if valid, false otherwise</returns>
    public static bool IsValidResourcePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        // Allow forward slashes for resource paths but prevent traversal
        if (path.Contains("..") || path.Contains("\\"))
            return false;

        // Prevent special characters
        if (path.Contains("<") || path.Contains(">") || path.Contains("|") || path.Contains("&"))
            return false;

        return true;
    }

    /// <summary>
    /// Validates a formula name
    /// </summary>
    /// <param name="formulaName">Formula name to validate</param>
    /// <returns>True if valid, false otherwise</returns>
    public static bool IsValidFormulaName(string formulaName)
    {
        return !string.IsNullOrWhiteSpace(formulaName);
    }

    /// <summary>
    /// Validates that a string is not null or whitespace
    /// </summary>
    /// <param name="value">Value to validate</param>
    /// <param name="parameterName">Parameter name for error messages</param>
    /// <returns>Validation result with error message if invalid</returns>
    public static (bool IsValid, string? ErrorMessage) ValidateRequired(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (false, $"{parameterName} is required");
        }
        return (true, null);
    }
}

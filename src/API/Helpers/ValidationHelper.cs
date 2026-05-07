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
}

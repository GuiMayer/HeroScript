namespace API.Models;

/// <summary>
/// Configuration validation result
/// </summary>
public class ConfigValidationDto
{
    /// <summary>
    /// Configuration name being validated
    /// </summary>
    public string ConfigName { get; set; } = string.Empty;

    /// <summary>
    /// Whether the configuration is valid
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Validation errors (if any)
    /// </summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// Validation warnings (if any)
    /// </summary>
    public List<string>? Warnings { get; set; }
}

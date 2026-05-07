namespace API.Models;

/// <summary>
/// Information about a configuration
/// </summary>
public class ConfigInfoDto
{
    /// <summary>
    /// Configuration name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Configuration version
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Configuration author
    /// </summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>
    /// Configuration description
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Parent configuration (for inheritance)
    /// </summary>
    public string? Parent { get; set; }

    /// <summary>
    /// Creation date (ISO 8601)
    /// </summary>
    public string CreatedAt { get; set; } = string.Empty;

    /// <summary>
    /// Whether this is the currently active configuration
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Relative path to configuration directory
    /// </summary>
    public string Path { get; set; } = string.Empty;
}

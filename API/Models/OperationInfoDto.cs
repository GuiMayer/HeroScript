namespace API.Models;

/// <summary>
/// Information about a supported mathematical operation
/// </summary>
public class OperationInfoDto
{
    /// <summary>
    /// Operation name (e.g., "ADD", "MULTIPLY")
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description of what the operation does
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Whether the operation requires values
    /// </summary>
    public bool RequiresValues { get; set; }

    /// <summary>
    /// Example usage
    /// </summary>
    public string Example { get; set; } = string.Empty;
}

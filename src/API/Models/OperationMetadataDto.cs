namespace API.Models;

/// <summary>
/// Metadata about a mathematical operation
/// </summary>
public class OperationMetadataDto
{
    /// <summary>
    /// Operation name (e.g., "ADD", "MULTIPLY")
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Mathematical symbol (e.g., "+", "*")
    /// </summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// Operation description
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Minimum number of values required
    /// </summary>
    public int MinValues { get; set; }

    /// <summary>
    /// Maximum number of values allowed (-1 for unlimited)
    /// </summary>
    public int MaxValues { get; set; }

    /// <summary>
    /// Operation category (basic, advanced, multi-value, unary)
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Operation behavior (accumulator, unary, unary-optional)
    /// </summary>
    public string Behavior { get; set; } = string.Empty;

    /// <summary>
    /// Whether this is a unary operation (operates on current value)
    /// </summary>
    public bool IsUnary { get; set; }
}

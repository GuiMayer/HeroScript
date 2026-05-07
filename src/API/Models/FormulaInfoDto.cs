namespace API.Models;

/// <summary>
/// Information about a formula
/// </summary>
public class FormulaInfoDto
{
    /// <summary>
    /// Formula name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Formula description
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Default parameters
    /// </summary>
    public Dictionary<string, float> DefaultParams { get; set; } = new();

    /// <summary>
    /// Config origin (which config this formula comes from)
    /// </summary>
    public string? Origin { get; set; }
}

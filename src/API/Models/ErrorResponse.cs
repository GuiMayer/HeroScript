namespace API.Models;

/// <summary>
/// Standardized error response format
/// </summary>
public class ErrorResponse
{
    /// <summary>
    /// Error message
    /// </summary>
    public string Error { get; set; } = string.Empty;

    /// <summary>
    /// Additional error details (only included in Development environment)
    /// </summary>
    public string? Details { get; set; }

    /// <summary>
    /// Validation errors by field name
    /// </summary>
    public Dictionary<string, string[]>? ValidationErrors { get; set; }
}

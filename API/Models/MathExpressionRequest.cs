namespace API.Models;

/// <summary>
/// Request to evaluate a custom math expression
/// </summary>
public class MathExpressionRequest
{
    /// <summary>
    /// Initial value for the expression
    /// </summary>
    public float InitialValue { get; set; }

    /// <summary>
    /// List of operations to apply
    /// </summary>
    public List<MathStepDto> Steps { get; set; } = new();
}

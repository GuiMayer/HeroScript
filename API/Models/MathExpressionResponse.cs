namespace API.Models;

/// <summary>
/// Response from evaluating a math expression
/// </summary>
public class MathExpressionResponse
{
    /// <summary>
    /// Final calculated result
    /// </summary>
    public float Result { get; set; }

    /// <summary>
    /// Steps that were executed
    /// </summary>
    public List<MathStepDto> Steps { get; set; } = new();

    /// <summary>
    /// Execution time in milliseconds
    /// </summary>
    public double ExecutionTimeMs { get; set; }

    /// <summary>
    /// Initial value used
    /// </summary>
    public float InitialValue { get; set; }
}

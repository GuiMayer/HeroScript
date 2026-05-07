namespace API.Models;

/// <summary>
/// Response from evaluating a formula
/// </summary>
public class FormulaResponse
{
    /// <summary>
    /// Final calculated result
    /// </summary>
    public float Result { get; set; }

    /// <summary>
    /// Formula name that was used
    /// </summary>
    public string FormulaName { get; set; } = string.Empty;

    /// <summary>
    /// Description of the formula
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Steps that were executed
    /// </summary>
    public List<MathStepDto> Steps { get; set; } = new();

    /// <summary>
    /// Parameters used (defaults + overrides)
    /// </summary>
    public Dictionary<string, float> ParamsUsed { get; set; } = new();

    /// <summary>
    /// Execution time in milliseconds
    /// </summary>
    public double ExecutionTimeMs { get; set; }
}

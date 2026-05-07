namespace API.Models;

/// <summary>
/// Request to evaluate a formula from MathFormulas.json
/// </summary>
public class FormulaRequest
{
    /// <summary>
    /// Name of the formula (e.g., "HYPERBOLIC_CURVE")
    /// </summary>
    public string FormulaName { get; set; } = string.Empty;

    /// <summary>
    /// Input value for the formula
    /// </summary>
    public float InputValue { get; set; }

    /// <summary>
    /// Optional parameter overrides (overrides defaults from JSON)
    /// </summary>
    public Dictionary<string, float>? ParamOverrides { get; set; }
}

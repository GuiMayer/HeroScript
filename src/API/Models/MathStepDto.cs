namespace API.Models;

/// <summary>
/// Represents a single mathematical operation step
/// </summary>
public class MathStepDto
{
    /// <summary>
    /// Operation name (ADD, SUBTRACT, MULTIPLY, DIVIDE, etc.)
    /// </summary>
    public string Operation { get; set; } = string.Empty;

    /// <summary>
    /// Values used in the operation (legacy mode - implicit accumulator)
    /// </summary>
    public float[]? Values { get; set; }

    /// <summary>
    /// Operands for explicit mode (supports "$current", "$initial", "params.NAME", or numeric literals)
    /// Mutually exclusive with Values - use one or the other
    /// </summary>
    public List<string>? Operands { get; set; }
}

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
    /// Values used in the operation
    /// </summary>
    public float[] Values { get; set; } = Array.Empty<float>();
}

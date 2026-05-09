namespace Core.Math;

/// <summary>
/// Metadados de uma operação matemática
/// </summary>
public class OperationMetadata
{
    public string Name { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int MinValues { get; set; }
    public int MaxValues { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Behavior { get; set; } = string.Empty;
    public bool IsUnary { get; set; }
}

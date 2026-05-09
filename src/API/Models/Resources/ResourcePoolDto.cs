namespace API.Models.Resources;

/// <summary>
/// Representa um pool de recurso
/// </summary>
public class ResourcePoolDto
{
    public string ResourceId { get; set; } = string.Empty;
    public float Current { get; set; }
    public float Maximum { get; set; }
    public float Minimum { get; set; }
    public float Percentage { get; set; }
}

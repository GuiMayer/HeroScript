namespace API.Models.Resources;

/// <summary>
/// Versão resumida de um recurso para listagens
/// </summary>
public class ResourceSummaryDto
{
    public string ResourceId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public float DefaultMax { get; set; }
    public List<string> Tags { get; set; } = new();
}

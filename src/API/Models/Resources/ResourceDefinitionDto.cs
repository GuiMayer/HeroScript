namespace API.Models.Resources;

/// <summary>
/// Representa uma definição completa de recurso
/// </summary>
public class ResourceDefinitionDto
{
    public string ResourceId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public float DefaultCurrent { get; set; }
    public float DefaultMax { get; set; }
    public float DefaultMin { get; set; }
    public bool CanBeNegative { get; set; }
    public bool CanExceedMax { get; set; }
    public List<string> Tags { get; set; } = new();
}

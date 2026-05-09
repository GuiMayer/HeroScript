namespace API.Models.Resources;

/// <summary>
/// Request para criar um pool de recurso
/// </summary>
public class CreatePoolRequest
{
    public string ResourceId { get; set; } = string.Empty;
    public float? InitialCurrent { get; set; }
}

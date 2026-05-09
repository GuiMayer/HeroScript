namespace API.Models.Resources;

/// <summary>
/// Response de validação de recurso
/// </summary>
public class ResourceValidationResponse
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
}

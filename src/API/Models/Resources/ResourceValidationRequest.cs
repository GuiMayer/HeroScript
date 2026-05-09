namespace API.Models.Resources;

/// <summary>
/// Request para validar uma definição de recurso
/// </summary>
public class ResourceValidationRequest
{
    public ResourceDefinitionDto Definition { get; set; } = new();
}

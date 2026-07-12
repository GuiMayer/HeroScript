using System.ComponentModel.DataAnnotations;

namespace API.Models.Entities;

/// <summary>
/// DTO para atualizar uma definição de entidade existente
/// </summary>
public record UpdateEntityDefinitionDto
{
    public string? Type { get; init; }

    [StringLength(100, MinimumLength = 1, ErrorMessage = "DisplayName must be between 1 and 100 characters")]
    public string? DisplayName { get; init; }

    public string? Description { get; init; }

    public Dictionary<string, ResourceDefinitionDto>? Resources { get; init; }

    public Dictionary<string, float>? Attributes { get; init; }
}

using System.ComponentModel.DataAnnotations;

namespace API.Models.Entities;

/// <summary>
/// DTO para criar uma nova definição de entidade
/// </summary>
public record CreateEntityDefinitionDto
{
    [Required(ErrorMessage = "DefinitionId is required")]
    [RegularExpression(@"^[a-zA-Z0-9_-]+$", ErrorMessage = "DefinitionId can only contain alphanumeric characters, hyphens, and underscores")]
    public string DefinitionId { get; init; } = string.Empty;

    [Required(ErrorMessage = "Type is required")]
    public string Type { get; init; } = string.Empty;

    [Required(ErrorMessage = "DisplayName is required")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "DisplayName must be between 1 and 100 characters")]
    public string DisplayName { get; init; } = string.Empty;

    public string? Description { get; init; }

    public Dictionary<string, ResourceDefinitionDto>? Resources { get; init; }

    public Dictionary<string, float>? Attributes { get; init; }
}

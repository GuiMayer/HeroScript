using System.ComponentModel.DataAnnotations;

namespace API.Models.StatusEffects;

/// <summary>
/// DTO para atualizar uma definição de status effect existente
/// </summary>
public record UpdateStatusDefinitionDto
{
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Name must be between 1 and 100 characters")]
    public string? Name { get; init; }

    public string? Description { get; init; }

    public string? Type { get; init; }

    public bool? IsStackable { get; init; }

    [Range(1, int.MaxValue, ErrorMessage = "MaxStacks must be positive if specified")]
    public int? MaxStacks { get; init; }

    public string? TickBehavior { get; init; }

    public List<object>? OnApply { get; init; }
    public List<object>? OnTick { get; init; }
    public List<object>? OnRemove { get; init; }
    public List<string>? Tags { get; init; }
}

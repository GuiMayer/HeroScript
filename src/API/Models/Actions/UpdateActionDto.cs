using System.ComponentModel.DataAnnotations;

namespace API.Models.Actions;

/// <summary>
/// DTO para atualizar uma definição de ação existente
/// </summary>
public record UpdateActionDto
{
    [StringLength(100, MinimumLength = 1, ErrorMessage = "DisplayName must be between 1 and 100 characters")]
    public string? DisplayName { get; init; }

    public string? Description { get; init; }

    public string? ActionType { get; init; }

    [Range(0, int.MaxValue, ErrorMessage = "Cooldown must be non-negative")]
    public int? Cooldown { get; init; }

    public bool? RequiresTarget { get; init; }
    
    public bool? MultiTarget { get; init; }

    public float? BaseDamage { get; init; }

    public List<string>? Tags { get; init; }

    public ActionCostsDto? Costs { get; init; }

    public List<EffectDefinitionDto>? Effects { get; init; }
}

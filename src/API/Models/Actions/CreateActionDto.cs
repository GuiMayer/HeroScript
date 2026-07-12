using System.ComponentModel.DataAnnotations;

namespace API.Models.Actions;

/// <summary>
/// DTO para criar uma nova definição de ação
/// </summary>
public record CreateActionDto
{
    [Required(ErrorMessage = "ActionId is required")]
    [RegularExpression(@"^[a-zA-Z0-9_-]+$", ErrorMessage = "ActionId can only contain alphanumeric characters, hyphens, and underscores")]
    public string ActionId { get; init; } = string.Empty;

    [Required(ErrorMessage = "DisplayName is required")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "DisplayName must be between 1 and 100 characters")]
    public string DisplayName { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    [Required(ErrorMessage = "ActionType is required")]
    public string ActionType { get; init; } = string.Empty;

    [Range(0, int.MaxValue, ErrorMessage = "Cooldown must be non-negative")]
    public int Cooldown { get; init; }

    public bool RequiresTarget { get; init; } = true;
    
    public bool MultiTarget { get; init; }

    public float BaseDamage { get; init; }

    public List<string> Tags { get; init; } = new();

    public ActionCostsDto Costs { get; init; } = new();

    public List<EffectDefinitionDto> Effects { get; init; } = new();
}

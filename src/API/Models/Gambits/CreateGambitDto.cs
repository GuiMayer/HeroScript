using System.ComponentModel.DataAnnotations;
using Core.Combat.Gambits;
using Core.Entity.Controllers;

namespace API.Models.Gambits;

/// <summary>
/// DTO para criar uma nova definição de gambit
/// </summary>
public record CreateGambitDto
{
    [Required(ErrorMessage = "GambitId is required")]
    [RegularExpression(@"^[a-zA-Z0-9_-]+$", ErrorMessage = "GambitId can only contain alphanumeric characters, hyphens, and underscores")]
    public string GambitId { get; init; } = string.Empty;

    [Required(ErrorMessage = "DisplayName is required")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "DisplayName must be between 1 and 100 characters")]
    public string DisplayName { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    [Range(0, int.MaxValue, ErrorMessage = "Priority must be non-negative")]
    public int Priority { get; init; }

    [Required(ErrorMessage = "Conditions are required")]
    [MinLength(1, ErrorMessage = "At least one condition is required")]
    public List<GambitCondition> Conditions { get; init; } = new();

    [Required(ErrorMessage = "Action is required")]
    public GambitActionDefinition Action { get; init; } = new();

    public List<string> Tags { get; init; } = new();
}

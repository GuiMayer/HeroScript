using System.ComponentModel.DataAnnotations;
using Core.Combat.Gambits;
using Core.Entity.Controllers;

namespace API.Models.Gambits;

/// <summary>
/// DTO para atualizar uma definição de gambit existente
/// </summary>
public record UpdateGambitDto
{
    [StringLength(100, MinimumLength = 1, ErrorMessage = "DisplayName must be between 1 and 100 characters")]
    public string? DisplayName { get; init; }

    public string? Description { get; init; }

    [Range(0, int.MaxValue, ErrorMessage = "Priority must be non-negative")]
    public int? Priority { get; init; }

    public List<GambitCondition>? Conditions { get; init; }

    public GambitActionDefinition? Action { get; init; }

    public List<string>? Tags { get; init; }
}

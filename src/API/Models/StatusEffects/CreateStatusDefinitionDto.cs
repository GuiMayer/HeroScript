using System.ComponentModel.DataAnnotations;

namespace API.Models.StatusEffects;

/// <summary>
/// DTO para criar uma nova definição de status effect
/// </summary>
public record CreateStatusDefinitionDto
{
    [Required(ErrorMessage = "StatusId is required")]
    [RegularExpression(@"^[a-zA-Z0-9_-]+$", ErrorMessage = "StatusId can only contain alphanumeric characters, hyphens, and underscores")]
    public string StatusId { get; init; } = string.Empty;

    [Required(ErrorMessage = "Name is required")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Name must be between 1 and 100 characters")]
    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    [Required(ErrorMessage = "Type is required")]
    public string Type { get; init; } = string.Empty;

    public bool IsStackable { get; init; }

    [Range(1, int.MaxValue, ErrorMessage = "MaxStacks must be positive if specified")]
    public int? MaxStacks { get; init; }

    public string TickBehavior { get; init; } = "END_OF_TURN";

    public List<object> OnApply { get; init; } = new();
    public List<object> OnTick { get; init; } = new();
    public List<object> OnRemove { get; init; } = new();
    public List<string> Tags { get; init; } = new();
}

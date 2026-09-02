using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.Combat.Models;

namespace Core.Combat.TurnPhase;

public sealed record PhaseDefinition
{
    private ImmutableArray<ActionType> _allowedActions = [];
    private ImmutableArray<string> _validNextPhaseIds = [];

    public string PhaseId { get; init; } = string.Empty;
    public PhaseRole Role { get; init; }
    public int Order { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<ActionType> AllowedActions
    {
        get => _allowedActions;
        init => _allowedActions = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<string> ValidNextPhaseIds
    {
        get => _validNextPhaseIds;
        init => _validNextPhaseIds = value?
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToImmutableArray() ?? [];
    }
    public bool AutoTransition { get; init; }
    public bool AllowPriority { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PhaseRole
{
    Unspecified,
    Start,
    Middle,
    End
}

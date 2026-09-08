using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.Combat.Models;
using Core.Effects;

namespace Core.Combat.TurnPhase;

public sealed record PhaseDefinition
{
    private ImmutableArray<ActionType> _allowedActions = [];
    private ImmutableArray<string> _allowedCommandTags = [];
    private ImmutableArray<PhaseEdgeDefinition> _edges = [];
    private ImmutableArray<EffectDefinition> _entryEffects = [];
    private ImmutableArray<EffectDefinition> _exitEffects = [];

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
    public IReadOnlyList<string> AllowedCommandTags
    {
        get => _allowedCommandTags;
        init => _allowedCommandTags = value?
            .Select(tag => tag?.Trim() ?? string.Empty)
            .ToImmutableArray() ?? [];
    }
    public IReadOnlyList<PhaseEdgeDefinition> Edges
    {
        get => _edges;
        init => _edges = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<EffectDefinition> EntryEffects
    {
        get => _entryEffects;
        init => _entryEffects = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<EffectDefinition> ExitEffects
    {
        get => _exitEffects;
        init => _exitEffects = value?.ToImmutableArray() ?? [];
    }
    public bool AllowPriority { get; init; }
}

public sealed record PhaseEdgeDefinition
{
    private ImmutableArray<ActionType> _actionTypes = [];
    private ImmutableArray<string> _commandTags = [];

    public string EdgeId { get; init; } = string.Empty;
    public string TargetPhaseId { get; init; } = string.Empty;
    public PhaseEdgeTrigger Trigger { get; init; }
    public int Priority { get; init; }
    public string? Condition { get; init; }
    public IReadOnlyList<ActionType> ActionTypes
    {
        get => _actionTypes;
        init => _actionTypes = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<string> CommandTags
    {
        get => _commandTags;
        init => _commandTags = value?
            .Select(tag => tag?.Trim() ?? string.Empty)
            .ToImmutableArray() ?? [];
    }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PhaseEdgeTrigger
{
    Unspecified,
    Automatic,
    Command,
    ActivationExit
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PhaseRole
{
    Unspecified,
    Start,
    Middle,
    End
}

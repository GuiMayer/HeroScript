using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.CardZones;

public sealed record CardZoneSystemDefinition
{
    private ImmutableArray<CardZoneDefinition> _zones = [];
    private ImmutableArray<CardZoneFlowDefinition> _flows = [];

    public string CardZoneSystemId { get; init; } = string.Empty;
    public IReadOnlyList<CardZoneDefinition> Zones
    {
        get => _zones;
        init => _zones = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<CardZoneFlowDefinition> Flows
    {
        get => _flows;
        init => _flows = value?.ToImmutableArray() ?? [];
    }
}

public sealed record CardZoneDefinition
{
    private ImmutableDictionary<string, JsonElement> _presentation =
        ImmutableDictionary<string, JsonElement>.Empty.WithComparers(StringComparer.Ordinal);

    public string ZoneId { get; init; } = string.Empty;
    public CardZoneOwnerScope OwnerScope { get; init; }
    public CardZoneOrdering Ordering { get; init; }
    public int? Capacity { get; init; }
    public CardZoneVisibilityDefinition Visibility { get; init; } = new();
    public IReadOnlyDictionary<string, JsonElement> Presentation
    {
        get => _presentation;
        init => _presentation = value?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, JsonElement>.Empty.WithComparers(StringComparer.Ordinal);
    }
}

public sealed record CardZoneVisibilityDefinition
{
    public CardZoneVisibility Contents { get; init; } = CardZoneVisibility.Owner;
    public CardZoneOrderVisibility Order { get; init; } = CardZoneOrderVisibility.Hidden;
}

public sealed record CardZoneFlowDefinition
{
    private ImmutableArray<string> _triggers = [];
    private ImmutableArray<CardZoneFlowInvocation> _allowedInvocations = [];
    private ImmutableArray<CardZoneFlowStepDefinition> _steps = [];

    public string FlowId { get; init; } = string.Empty;
    public int Priority { get; init; }
    public IReadOnlyList<string> Triggers
    {
        get => _triggers;
        init => _triggers = value?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToImmutableArray() ?? [];
    }
    public IReadOnlyList<CardZoneFlowInvocation> AllowedInvocations
    {
        get => _allowedInvocations;
        init => _allowedInvocations = value?.Distinct().ToImmutableArray() ?? [];
    }
    public IReadOnlyList<CardZoneFlowStepDefinition> Steps
    {
        get => _steps;
        init => _steps = value?.ToImmutableArray() ?? [];
    }
}

public sealed record CardZoneFlowStepDefinition
{
    public string StepId { get; init; } = string.Empty;
    public CardZoneOperation Operation { get; init; }
    public string? SourceZoneId { get; init; }
    public string? TargetZoneId { get; init; }
    public CardZoneOwnerBinding SourceOwner { get; init; } = CardZoneOwnerBinding.FlowOwner;
    public CardZoneOwnerBinding TargetOwner { get; init; } = CardZoneOwnerBinding.FlowOwner;
    public CardZoneSelectionDefinition Selection { get; init; } = new();
    public CardZoneInsertionDefinition Insertion { get; init; } = new();
    public CardZoneInsufficientPolicy OnInsufficient { get; init; } = CardZoneInsufficientPolicy.RejectTransaction;
    public CardZoneOverflowPolicy OnOverflow { get; init; } = CardZoneOverflowPolicy.RejectTransaction;
    public string? FallbackFlowId { get; init; }
    public string? OverflowFlowId { get; init; }
    public bool RetryAfterFallback { get; init; }
    public bool MoveAvailableBeforeFallback { get; init; }
    public string? Condition { get; init; }
    public string? CardDefinitionId { get; init; }
    public CardInstanceLifetimeDefinition Lifetime { get; init; } = new();
    public string? NestedFlowId { get; init; }
}

public sealed record CardZoneSelectionDefinition
{
    private ImmutableArray<Guid> _instanceIds = [];
    private ImmutableArray<string> _definitionIds = [];
    private ImmutableArray<string> _requiredTags = [];

    public CardZoneSelectionStrategy Strategy { get; init; }
    public int? Count { get; init; }
    public string? CountFormula { get; init; }
    public IReadOnlyList<Guid> InstanceIds
    {
        get => _instanceIds;
        init => _instanceIds = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<string> DefinitionIds
    {
        get => _definitionIds;
        init => _definitionIds = Normalize(value);
    }
    public IReadOnlyList<string> RequiredTags
    {
        get => _requiredTags;
        init => _requiredTags = Normalize(value);
    }
    public string? Condition { get; init; }

    private static ImmutableArray<string> Normalize(IEnumerable<string>? values) => values?
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim())
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToImmutableArray() ?? [];
}

public sealed record CardZoneInsertionDefinition
{
    public CardZoneInsertionStrategy Strategy { get; init; } = CardZoneInsertionStrategy.Bottom;
    public int? Index { get; init; }
}

public sealed record CardInstanceLifetimeDefinition
{
    public CardInstanceLifetimeStrategy Strategy { get; init; } = CardInstanceLifetimeStrategy.Run;
    public string? Boundary { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CardZoneOwnerScope { Unspecified, Global, RunOwner, Actor }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CardZoneOwnerBinding { Unspecified, Global, RunOwner, FlowOwner, ActiveActor, SourceActor, TargetActor, Explicit }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CardZoneOrdering { Unspecified, Ordered, Unordered }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CardZoneVisibility { Unspecified, None, Owner, All, ToolCapability }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CardZoneOrderVisibility { Unspecified, Hidden, Visible, ToolCapability }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CardZoneFlowInvocation { Unspecified, Boundary, Effect, CardResolution, GameplayCommand, Tool }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CardZoneOperation { Unspecified, Create, Move, Destroy, Shuffle, Reorder, ExecuteFlow }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CardZoneSelectionStrategy { Unspecified, Explicit, Top, Bottom, First, Last, All, Random, ByDefinition, ByTags, ByCondition }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CardZoneInsertionStrategy { Unspecified, Top, Bottom, AtIndex, RandomPosition, PreserveSourceOrder, ShuffleAfterInsert, CreationOrder }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CardZoneInsufficientPolicy { Unspecified, RejectTransaction, AllowPartial, ExecuteFallbackAndRetry }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CardZoneOverflowPolicy { Unspecified, RejectTransaction, AllowPartial, RedirectOverflow }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CardInstanceLifetimeStrategy { Unspecified, Run, Permanent, UntilBoundary }

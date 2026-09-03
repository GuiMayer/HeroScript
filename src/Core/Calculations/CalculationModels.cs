using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.Combat.Models;
using Core.Determinism;
using Core.Run;
using Core.Run.Content;

namespace Core.Calculations;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CalculationBucketOperation
{
    Add,
    AddPercent,
    Multiply,
    Set,
    Minimum,
    Maximum
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CalculationRounding
{
    None,
    Floor,
    Ceiling,
    Round
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CalculationSourceKind
{
    Card,
    Actor,
    Target,
    Status,
    Relic,
    Upgrade,
    GameMode,
    Encounter,
    Modifier
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CalculationEntityScope
{
    Actor,
    Target
}

public sealed record CalculationBucketDefinition
{
    public string BucketId { get; init; } = string.Empty;
    public int Order { get; init; }
    public CalculationBucketOperation Operation { get; init; } = CalculationBucketOperation.Add;
    public CalculationRounding Rounding { get; init; }
    public float? Minimum { get; init; }
    public float? Maximum { get; init; }
}

public sealed record CalculationPipelineDefinition
{
    private ImmutableArray<CalculationBucketDefinition> _buckets = [];

    public string PipelineId { get; init; } = string.Empty;
    public string Channel { get; init; } = string.Empty;
    public IReadOnlyList<CalculationBucketDefinition> Buckets
    {
        get => _buckets;
        init => _buckets = value?.ToImmutableArray() ?? [];
    }
}

/// <summary>
/// One resolved contextual contribution. Source identifies provenance for
/// inspection only; it never selects a calculation branch.
/// </summary>
public sealed record CalculationInfluence
{
    public string InfluenceId { get; init; } = string.Empty;
    public CalculationSourceKind SourceKind { get; init; }
    public string SourceId { get; init; } = string.Empty;
    public string Channel { get; init; } = string.Empty;
    public string Bucket { get; init; } = string.Empty;
    public float Value { get; init; }
    public int Priority { get; init; }
}

public sealed record CalculationRequest
{
    private ImmutableArray<CalculationInfluence> _influences = [];
    private ImmutableHashSet<string> _tags =
        ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);

    public string CalculationId { get; init; } = string.Empty;
    public string Channel { get; init; } = string.Empty;
    public float BaseValue { get; init; }
    public IReadOnlyList<CalculationInfluence> Influences
    {
        get => _influences;
        init => _influences = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlySet<string> Tags
    {
        get => _tags;
        init => _tags = value?.ToImmutableHashSet(StringComparer.Ordinal)
            ?? ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
    }
}

public sealed record CalculationContributionTrace
{
    public string InfluenceId { get; init; } = string.Empty;
    public CalculationSourceKind SourceKind { get; init; }
    public string SourceId { get; init; } = string.Empty;
    public float Value { get; init; }
    public int Priority { get; init; }
}

public sealed record CalculationBucketTrace
{
    private ImmutableArray<CalculationContributionTrace> _contributions = [];

    public string BucketId { get; init; } = string.Empty;
    public int Order { get; init; }
    public CalculationBucketOperation Operation { get; init; }
    public float Input { get; init; }
    public IReadOnlyList<CalculationContributionTrace> Contributions
    {
        get => _contributions;
        init => _contributions = value?.ToImmutableArray() ?? [];
    }
    public float OutputBeforeBounds { get; init; }
    public float Output { get; init; }
}

public sealed record CalculationResult
{
    private ImmutableArray<CalculationBucketTrace> _buckets = [];

    public string CalculationId { get; init; } = string.Empty;
    public string PipelineId { get; init; } = string.Empty;
    public string Channel { get; init; } = string.Empty;
    public float BaseValue { get; init; }
    public float Value { get; init; }
    public IReadOnlyList<CalculationBucketTrace> Buckets
    {
        get => _buckets;
        init => _buckets = value?.ToImmutableArray() ?? [];
    }
    public string Fingerprint { get; init; } = string.Empty;
}

public sealed record CalculationSourceContext
{
    private ImmutableDictionary<string, float> _variables =
        ImmutableDictionary<string, float>.Empty.WithComparers(StringComparer.Ordinal);
    private ImmutableHashSet<string> _tags =
        ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);

    public string ContentRevision { get; init; } = string.Empty;
    public EffectiveCardDefinition? Card { get; init; }
    public RunState? Run { get; init; }
    public CombatState? Combat { get; init; }
    public CombatEntity? Actor { get; init; }
    public CombatEntity? Target { get; init; }
    public IReadOnlySet<string> Tags
    {
        get => _tags;
        init => _tags = value?.ToImmutableHashSet(StringComparer.Ordinal)
            ?? ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
    }
    public IReadOnlyDictionary<string, float> Variables
    {
        get => _variables;
        init => _variables = value?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, float>.Empty.WithComparers(StringComparer.Ordinal);
    }
}

/// <summary>
/// Reusable, data-authored contribution to a calculation pipeline. The owner
/// (status, relic, modifier, etc.) supplies provenance and runtime variables.
/// </summary>
public sealed record ContextualInfluenceDefinition
{
    private ImmutableArray<string> _requiredTags = [];
    private ImmutableArray<string> _excludedTags = [];

    public string InfluenceId { get; init; } = string.Empty;
    public CalculationEntityScope Scope { get; init; } = CalculationEntityScope.Actor;
    public string Channel { get; init; } = string.Empty;
    public string Bucket { get; init; } = string.Empty;
    public float? Value { get; init; }
    public string? Formula { get; init; }
    public int Priority { get; init; }
    public IReadOnlyList<string> RequiredTags
    {
        get => _requiredTags;
        init => _requiredTags = value?.Distinct(StringComparer.Ordinal).ToImmutableArray() ?? [];
    }
    public IReadOnlyList<string> ExcludedTags
    {
        get => _excludedTags;
        init => _excludedTags = value?.Distinct(StringComparer.Ordinal).ToImmutableArray() ?? [];
    }
}

public sealed record ResourceInfluenceBindingDefinition
{
    public string BindingId { get; init; } = string.Empty;
    public CalculationEntityScope Scope { get; init; }
    public string ResourceId { get; init; } = string.Empty;
    public string Channel { get; init; } = string.Empty;
    public string Bucket { get; init; } = string.Empty;
    public float Scale { get; init; } = 1;
    public float Offset { get; init; }
    public int Priority { get; init; }
}

internal sealed record CalculationFingerprintPayload(
    string CalculationId,
    string PipelineId,
    string Channel,
    float BaseValue,
    float Value,
    ImmutableArray<CalculationBucketTrace> Buckets)
{
    public string Compute() => CanonicalJson.ComputeHash(this);
}

using System.Collections.Immutable;
using Core.Determinism;

namespace Core.Run.Content;

/// <summary>
/// Permanent base of one card instance after typed upgrades. No actor, target,
/// status, relic or game-mode influence is included here.
/// </summary>
public sealed record EffectiveCardDefinition
{
    private ImmutableArray<CardComponentDefinition> _components = [];
    private ImmutableArray<CardUpgradeState> _appliedUpgrades = [];
    private ImmutableArray<CardUpgradeState> _transformationLedger = [];
    private ImmutableArray<CardUpgradeApplicationTrace> _upgradeTrace = [];
    private ImmutableArray<CardTransformationSlotDefinition> _transformationSlots = [];

    public Guid CardInstanceId { get; init; }
    public string DefinitionId { get; init; } = string.Empty;
    public string DefinitionFingerprint { get; init; } = string.Empty;
    public ImmutableArray<string> Tags { get; init; } = [];
    public ImmutableArray<CardCompositionApplicationTrace> CompositionTrace { get; init; } = [];
    public ImmutableArray<string> Capabilities => CardCompositionGrammar.Capabilities(Components);
    public IReadOnlyList<CardUpgradeState> AppliedUpgrades
    {
        get => _appliedUpgrades;
        init => _appliedUpgrades = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<CardUpgradeState> TransformationLedger
    {
        get => _transformationLedger;
        init => _transformationLedger = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<CardUpgradeApplicationTrace> UpgradeTrace
    {
        get => _upgradeTrace;
        init => _upgradeTrace = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<CardComponentDefinition> Components
    {
        get => _components;
        init => _components = value?.ToImmutableArray() ?? [];
    }
    public string Fingerprint { get; init; } = string.Empty;
    public IReadOnlyList<CardTransformationSlotDefinition> TransformationSlots
    {
        get => _transformationSlots;
        init => _transformationSlots = value?.ToImmutableArray() ?? [];
    }

    public T? SingleOrDefault<T>() where T : CardComponentDefinition =>
        _components.OfType<T>().SingleOrDefault();

    public IReadOnlyList<T> All<T>() where T : CardComponentDefinition =>
        _components.OfType<T>().ToImmutableArray();
}

internal sealed record EffectiveCardFingerprintPayload(
    Guid CardInstanceId,
    string DefinitionId,
    string DefinitionFingerprint,
    ImmutableArray<string> Tags,
    ImmutableArray<CardUpgradeState> TransformationLedger,
    ImmutableArray<CardUpgradeState> AppliedUpgrades,
    ImmutableArray<CardUpgradeApplicationTrace> UpgradeTrace,
    ImmutableArray<CardCompositionApplicationTrace> CompositionTrace,
    ImmutableArray<CardComponentDefinition> Components)
{
    public string ComputeFingerprint() => CanonicalJson.ComputeHash(this);
}

/// <summary>Auditable permanent base transformation. It is never reapplied as a contextual influence.</summary>
public sealed record CardUpgradeApplicationTrace
{
    public ulong TransformationId { get; init; }
    public CardTransformationCategory Category { get; init; }
    public string UpgradeId { get; init; } = string.Empty;
    public string ComponentId { get; init; } = string.Empty;
    public string Attribute { get; init; } = string.Empty;
    public CardNumericPatchOperation? Operation { get; init; }
    public float? PreviousValue { get; init; }
    public float? CurrentValue { get; init; }
    public string? PreviousChoice { get; init; }
    public string? CurrentChoice { get; init; }
}

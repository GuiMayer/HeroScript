using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.Calculations;
using Core.Combat.Models;
using Core.Common;
using Core.Effects;

namespace Core.Run.Content;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CardPresentationSubjectKind { Definition, Offer, Instance, Transformation }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CardFaceValueMode { ComposedBase, Source }

/// <summary>An offer is not an owned instance. Reading never manufactures an instance identity.</summary>
public sealed record CardPresentationIdentity
{
    public CardPresentationSubjectKind Kind { get; init; }
    public string DefinitionId { get; init; } = string.Empty;
    public Guid? CardInstanceId { get; init; }
    public string? OfferId { get; init; }

    public Result Validate() => string.IsNullOrWhiteSpace(DefinitionId) || !Enum.IsDefined(Kind) ||
        Kind == CardPresentationSubjectKind.Definition && (CardInstanceId != null || OfferId != null) ||
        Kind == CardPresentationSubjectKind.Offer && (string.IsNullOrWhiteSpace(OfferId) || CardInstanceId != null) ||
        Kind is CardPresentationSubjectKind.Instance or CardPresentationSubjectKind.Transformation &&
            (CardInstanceId == null || CardInstanceId == Guid.Empty || OfferId != null)
        ? Result.Failure("Invalid card presentation subject identity") : Result.Success();
}

public sealed record CardPresentationVersion
{
    public string ContentRevision { get; init; } = string.Empty;
    public string DefinitionFingerprint { get; init; } = string.Empty;
    public string CompositionFingerprint { get; init; } = string.Empty;
    public Guid? RunId { get; init; }
    public int? RunSequence { get; init; }
    public Guid? CombatId { get; init; }
    public ulong? CombatStep { get; init; }
    public string? ActorId { get; init; }
}

/// <summary>Impact values stay outside face facts: a sampled target outcome is not a card's identity.</summary>
public sealed record CardNumericLayers
{
    public float? Original { get; init; }
    public float? ComposedBase { get; init; }
    public float? Source { get; init; }
    public string UnitId { get; init; } = string.Empty;
    public string? Expression { get; init; }
    public string? UnresolvedReason { get; init; }

    public CardValueComparison Compare(CardFaceValueMode mode) => mode switch
    {
        CardFaceValueMode.ComposedBase => new(ComposedBase, Original, "Original"),
        CardFaceValueMode.Source => new(Source, ComposedBase, "ComposedBase"),
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
}
public sealed record CardValueComparison(float? Value, float? Reference, string ReferenceLayer);

public sealed record CardParameterPresentation(EffectNumericParameter Parameter, CardNumericLayers Values);
public sealed record CardEffectPresentation
{
    public string ComponentId { get; init; } = string.Empty;
    public string EffectPath { get; init; } = string.Empty;
    public string? ParentEffectPath { get; init; }
    public string? Boundary { get; init; }
    public EffectDefinition Definition { get; init; } = new();
    public ImmutableArray<CardParameterPresentation> Parameters { get; init; } = [];
}
public sealed record CardCostPresentation(string ComponentId, ActionCosts Costs);

/// <summary>Immutable, source-independent facts shared by offers, collections and combat views.</summary>
public sealed record CardPresentationFacts
{
    public CardPresentationIdentity Identity { get; init; } = new();
    public CardPresentationVersion Version { get; init; } = new();
    public CardFaceValueMode FaceValueMode { get; init; }
    public CardRarity Rarity { get; init; }
    public ImmutableArray<string> Tags { get; init; } = [];
    public ImmutableArray<CardCostPresentation> Costs { get; init; } = [];
    public ImmutableArray<CardEffectPresentation> Effects { get; init; } = [];
    public ImmutableArray<CardConditionComponentDefinition> Conditions { get; init; } = [];
    public ImmutableArray<CardTargetingComponentDefinition> Targeting { get; init; } = [];
    public ImmutableArray<CardDispositionComponentDefinition> Dispositions { get; init; } = [];
    public ImmutableArray<CardEffectRuleOverrideComponentDefinition> RuleOverrides { get; init; } = [];
    public ImmutableArray<CardUpgradeApplicationTrace> PermanentChanges { get; init; } = [];
    public ImmutableArray<CardValueExplanation> Explanations { get; init; } = [];
    public ImmutableArray<CardImpactExplanation> Impacts { get; init; } = [];
    public bool ImpactsTruncated { get; init; }
    public string Fingerprint { get; init; } = string.Empty;
}

public sealed record CardScalingAttribute(string BindingId, string ComponentId, string AttributeId,
    CalculationEntityScope Scope, string Bucket, bool Eligible, string? InactiveReason, float? CurrentValue);
public sealed record CardValueExplanation
{
    public string EffectPath { get; init; } = string.Empty;
    public EffectNumericParameter Parameter { get; init; }
    public ImmutableArray<CardScalingAttribute> Attributes { get; init; } = [];
    public ImmutableArray<CalculationBucketTrace> Buckets { get; init; } = [];
    public ImmutableArray<CardCalculationSource> Sources { get; init; } = [];
    public ImmutableArray<string> ContextTags { get; init; } = [];
    public bool? ConditionPassed { get; init; }
    public string? ConditionExpression { get; init; }
    public string Validity { get; init; } = "BeforeActionSourceSnapshot";
    public bool Truncated { get; init; }
    public string? UnresolvedReason { get; init; }
}
public sealed record CardCalculationSource(CalculationSourceKind Kind, string InstanceId, string? DefinitionId,
    string? AttributeId, string ComponentId, string Bucket, float Value, bool Applied);
public sealed record CardImpactParameterExplanation(EffectNumericParameter Parameter, string UnitId, float Value,
    ImmutableArray<CalculationBucketTrace> Buckets, ImmutableArray<CardCalculationSource> Sources, bool Truncated);
public sealed record CardImpactExplanation(string ImpactId, string ComponentId, string TargetEntityId, string Validity,
    bool Applied, string? SkipReason, ImmutableArray<CardImpactParameterExplanation> Parameters);

using System.Text.Json.Serialization;
using Core.Common;

namespace Core.Combat.TurnOrder;

/// <summary>
/// Tagged, JSON-authored policy that completely describes how a combat derives
/// activation order. Exactly one strategy-specific definition is allowed.
/// </summary>
public sealed record TurnOrderPolicyDefinition
{
    public TurnOrderStrategy Strategy { get; init; }
    public TurnOrderRecalculationBoundary RecalculateAt { get; init; }
    public TurnOrderTieBreakDefinition TieBreak { get; init; } = new();
    public ResourceTurnOrderDefinition? Resource { get; init; }
    public InitiativeTurnOrderDefinition? Initiative { get; init; }
    public AtbTurnOrderDefinition? Atb { get; init; }
    public ConditionalTurnOrderDefinition? Conditional { get; init; }
}

public sealed record ResourceTurnOrderDefinition
{
    public string ResourceId { get; init; } = string.Empty;
    public TurnOrderDirection Direction { get; init; }
    public float? MissingValue { get; init; }
}

public sealed record InitiativeTurnOrderDefinition
{
    public string ModifierResourceId { get; init; } = string.Empty;
    public float ResourcePerModifier { get; init; }
    public int DieSides { get; init; }
    public float? MissingValue { get; init; }
}

public sealed record AtbTurnOrderDefinition
{
    public string RateResourceId { get; init; } = string.Empty;
    public float FillRate { get; init; }
    public float ReferenceResourceValue { get; init; }
    public float ReadyThreshold { get; init; }
    public float? MissingValue { get; init; }
}

public sealed record ConditionalTurnOrderDefinition
{
    public string ScoreExpression { get; init; } = string.Empty;
    public TurnOrderDirection Direction { get; init; }
}

public sealed record TurnOrderTieBreakDefinition
{
    public TurnOrderTieBreakStrategy Strategy { get; init; }
    public TurnOrderTieBreakEpoch Epoch { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TurnOrderStrategy
{
    Unspecified,
    Fixed,
    Resource,
    Initiative,
    Atb,
    Conditional
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TurnOrderRecalculationBoundary
{
    Unspecified,
    CombatStart,
    RoundStart,
    ActivationEnd,
    ContinuousTick
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TurnOrderTieBreakStrategy
{
    Unspecified,
    StableActorId,
    PlayerControlledFirst,
    AiControlledFirst,
    SeededRandom
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TurnOrderTieBreakEpoch
{
    Unspecified,
    CombatStart,
    RoundStart,
    ActivationEnd,
    ContinuousTick
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TurnOrderDirection
{
    Unspecified,
    Ascending,
    Descending
}

public static class TurnOrderPolicyValidator
{
    public static Result Validate(TurnOrderPolicyDefinition? policy)
    {
        if (policy == null)
            return Result.Failure("Combat rules require a turnOrder policy");
        if (policy.Strategy == TurnOrderStrategy.Unspecified)
            return Result.Failure("Turn order strategy is required");
        if (policy.RecalculateAt == TurnOrderRecalculationBoundary.Unspecified)
            return Result.Failure("Turn order recalculation boundary is required");
        if (policy.TieBreak == null)
            return Result.Failure("Turn order tie-break policy is required");
        if (policy.TieBreak.Strategy == TurnOrderTieBreakStrategy.Unspecified)
            return Result.Failure("Turn order tie-break strategy is required");
        if (policy.TieBreak.Strategy == TurnOrderTieBreakStrategy.SeededRandom)
        {
            if (policy.TieBreak.Epoch == TurnOrderTieBreakEpoch.Unspecified)
                return Result.Failure("Seeded turn-order tie-break requires an epoch");
            if (policy.TieBreak.Epoch != TurnOrderTieBreakEpoch.CombatStart &&
                (int)policy.TieBreak.Epoch != (int)policy.RecalculateAt)
            {
                return Result.Failure(
                    "Seeded turn-order tie-break epoch must be CombatStart or match recalculateAt");
            }
        }
        else if (policy.TieBreak.Epoch != TurnOrderTieBreakEpoch.Unspecified)
        {
            return Result.Failure("Only seeded turn-order tie-break accepts an epoch");
        }

        var definitions = new object?[]
        {
            policy.Resource,
            policy.Initiative,
            policy.Atb,
            policy.Conditional
        }.Count(item => item != null);
        var expectedDefinitions = policy.Strategy == TurnOrderStrategy.Fixed ? 0 : 1;
        if (definitions != expectedDefinitions)
            return Result.Failure("Turn order must contain exactly the definition selected by strategy");

        if (policy.Strategy != TurnOrderStrategy.Resource && policy.Resource != null ||
            policy.Strategy != TurnOrderStrategy.Initiative && policy.Initiative != null ||
            policy.Strategy != TurnOrderStrategy.Atb && policy.Atb != null ||
            policy.Strategy != TurnOrderStrategy.Conditional && policy.Conditional != null)
        {
            return Result.Failure("Turn order contains a definition for a different strategy");
        }

        if (policy.Strategy == TurnOrderStrategy.Fixed &&
            policy.RecalculateAt != TurnOrderRecalculationBoundary.CombatStart)
            return Result.Failure("Fixed turn order must be calculated at CombatStart");

        return policy.Strategy switch
        {
            TurnOrderStrategy.Fixed => Result.Success(),
            TurnOrderStrategy.Resource => ValidateResource(policy.Resource!),
            TurnOrderStrategy.Initiative => ValidateInitiative(policy.Initiative!),
            TurnOrderStrategy.Atb => ValidateAtb(policy),
            TurnOrderStrategy.Conditional => ValidateConditional(policy.Conditional!),
            _ => Result.Failure($"Unsupported turn order strategy: {policy.Strategy}")
        };
    }

    private static Result ValidateResource(ResourceTurnOrderDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.ResourceId))
            return Result.Failure("Resource turn order requires resourceId");
        if (definition.Direction == TurnOrderDirection.Unspecified)
            return Result.Failure("Resource turn order requires direction");
        return ValidateOptionalFinite(definition.MissingValue, "Resource turn-order missingValue");
    }

    private static Result ValidateInitiative(InitiativeTurnOrderDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.ModifierResourceId))
            return Result.Failure("Initiative turn order requires modifierResourceId");
        if (!float.IsFinite(definition.ResourcePerModifier) || definition.ResourcePerModifier <= 0)
            return Result.Failure("Initiative resourcePerModifier must be finite and positive");
        if (definition.DieSides <= 0)
            return Result.Failure("Initiative dieSides must be positive");
        return ValidateOptionalFinite(definition.MissingValue, "Initiative missingValue");
    }

    private static Result ValidateAtb(TurnOrderPolicyDefinition policy)
    {
        var definition = policy.Atb!;
        if (policy.RecalculateAt != TurnOrderRecalculationBoundary.ContinuousTick)
            return Result.Failure("ATB turn order must recalculate at ContinuousTick");
        if (string.IsNullOrWhiteSpace(definition.RateResourceId))
            return Result.Failure("ATB turn order requires rateResourceId");
        if (!float.IsFinite(definition.FillRate) || definition.FillRate <= 0 ||
            !float.IsFinite(definition.ReferenceResourceValue) || definition.ReferenceResourceValue <= 0 ||
            !float.IsFinite(definition.ReadyThreshold) || definition.ReadyThreshold <= 0)
            return Result.Failure("ATB fillRate, referenceResourceValue and readyThreshold must be finite and positive");
        return ValidateOptionalFinite(definition.MissingValue, "ATB missingValue");
    }

    private static Result ValidateConditional(ConditionalTurnOrderDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.ScoreExpression))
            return Result.Failure("Conditional turn order requires scoreExpression");
        return definition.Direction == TurnOrderDirection.Unspecified
            ? Result.Failure("Conditional turn order requires direction")
            : Result.Success();
    }

    private static Result ValidateOptionalFinite(float? value, string label) =>
        value.HasValue && !float.IsFinite(value.Value)
            ? Result.Failure($"{label} must be finite")
            : Result.Success();
}

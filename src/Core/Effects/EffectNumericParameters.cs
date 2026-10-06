using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.Calculations;
using Core.Common;

namespace Core.Effects;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EffectNumericParameter { Amount, StatusStacks, StatusDuration, ModifierStacks, ModifierDuration, CardCount }

/// <summary>A calculated override of one typed field; it never changes the published definition.</summary>
public sealed record EffectNumericParameterDefinition
{
    public EffectNumericParameter Parameter { get; init; }
    public float? FlatValue { get; init; }
    public string? FormulaValue { get; init; }
    public string? InputQuantityId { get; init; }
    public string Channel { get; init; } = string.Empty;
    public string? PipelineId { get; init; }
    public string UnitId { get; init; } = string.Empty;
    public ImmutableArray<string> StageIds { get; init; } = [];
    public CalculationValuePolicy Conversion { get; init; } = new();
    public EffectSequenceDistribution? Distribution { get; init; }
}

public sealed record ResolvedEffectNumericParameter
{
    public EffectNumericParameter Parameter { get; init; }
    public CalculationResult Calculation { get; init; } = new();
}

internal static class EffectNumericParameters
{
    internal static bool Supports(EffectType type, EffectNumericParameter parameter) => parameter switch
    {
        EffectNumericParameter.Amount => type is EffectType.DAMAGE or EffectType.HEAL or EffectType.MODIFY_RESOURCE,
        EffectNumericParameter.StatusStacks or EffectNumericParameter.StatusDuration => type == EffectType.APPLY_STATUS,
        EffectNumericParameter.ModifierStacks => type is EffectType.APPLY_MODIFIER or EffectType.REMOVE_MODIFIER,
        EffectNumericParameter.ModifierDuration => type == EffectType.APPLY_MODIFIER,
        EffectNumericParameter.CardCount => type == EffectType.CARD_ZONE_FLOW,
        _ => false
    };

    internal static Result<EffectDefinition> Bind(EffectDefinition effect, ResolvedEffectNumericParameter parameter)
    {
        var value = parameter.Calculation.Value;
        if (parameter.Parameter == EffectNumericParameter.Amount) return Result<EffectDefinition>.Success(effect);
        if (!float.IsFinite(value) || value != MathF.Truncate(value) || (double)value > int.MaxValue || value < int.MinValue)
            return Result<EffectDefinition>.Failure("Calculated count must be an exact Int32; declare rounding and bounds");
        var count = (int)value;
        var duration = parameter.Parameter is EffectNumericParameter.StatusDuration or EffectNumericParameter.ModifierDuration;
        if (duration ? count is 0 or < -1 : count < 1)
            return Result<EffectDefinition>.Failure("Calculated stacks/count must be positive; duration must be positive or -1");
        if (parameter.Parameter == EffectNumericParameter.CardCount && count > EffectExecutionLimits.MaximumSteps)
            return Result<EffectDefinition>.Failure("Calculated cardCount exceeds execution limits");
        if (parameter.Parameter == EffectNumericParameter.CardCount && !effect.CardInstanceIds.IsEmpty && effect.CardInstanceIds.Length != count)
            return Result<EffectDefinition>.Failure("Explicit card IDs must match calculated cardCount");
        return Result<EffectDefinition>.Success(parameter.Parameter switch
        {
            EffectNumericParameter.StatusStacks => effect with { StatusStacks = count },
            EffectNumericParameter.StatusDuration => effect with { StatusDuration = count },
            EffectNumericParameter.ModifierStacks => effect with { ModifierStacks = count },
            EffectNumericParameter.ModifierDuration => effect with { ModifierDuration = count },
            EffectNumericParameter.CardCount => effect with { CardCount = count },
            _ => effect
        });
    }
}

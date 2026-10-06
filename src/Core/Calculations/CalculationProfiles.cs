using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.Common;

namespace Core.Calculations;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CalculationStageScope { Shared, Actor, Target }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CalculationSignPolicy { Any, NonNegative, Positive }

/// <summary>Numeric stages are content-defined; a scope only identifies incorporated context.</summary>
public sealed record CalculationStageDefinition
{
    public string StageId { get; init; } = string.Empty;
    public CalculationStageScope Scope { get; init; }
}

public sealed record CalculationStageReceipt
{
    public string PipelineId { get; init; } = string.Empty;
    public string PipelineFingerprint { get; init; } = string.Empty;
    public string StageId { get; init; } = string.Empty;
    public CalculationStageScope Scope { get; init; }
    public string ContextId { get; init; } = string.Empty;
}

/// <summary>Transportable quantity; neither resource selection nor settlement is encoded in it.</summary>
public sealed record CalculationQuantity
{
    public float Value { get; init; }
    public string UnitId { get; init; } = "scalar";
    public string ContentRevision { get; init; } = string.Empty;
    public string CalculationFingerprint { get; init; } = string.Empty;
    public ImmutableArray<CalculationStageReceipt> IncorporatedStages { get; init; } = [];
}

public sealed record CalculationValuePolicy
{
    public CalculationRounding Rounding { get; init; }
    public CalculationMidpointRounding MidpointRounding { get; init; } = CalculationMidpointRounding.ToEven;
    public CalculationSignPolicy Sign { get; init; }
    public bool RequireInteger { get; init; }
    public float? Minimum { get; init; }
    public float? Maximum { get; init; }

    public static Result Validate(CalculationValuePolicy policy)
    {
        if (!Enum.IsDefined(policy.Rounding) || !Enum.IsDefined(policy.MidpointRounding) || !Enum.IsDefined(policy.Sign))
            return Result.Failure("Invalid numeric conversion policy");
        if (policy.Minimum is { } min && !float.IsFinite(min) ||
            policy.Maximum is { } max && !float.IsFinite(max) || policy.Minimum > policy.Maximum)
            return Result.Failure("Invalid numeric conversion bounds");
        return Result.Success();
    }
}

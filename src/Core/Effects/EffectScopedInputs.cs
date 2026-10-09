using System.Text.Json.Serialization;
using System.Collections.Immutable;
using Core.Calculations;
using Core.Common;

namespace Core.Effects;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EffectExecutionScope { EveryInvocation, OncePerAction, OncePerParentProc }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EffectChildTiming { AfterParentImpact, BeforeParentImpact }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EffectRandomScope { Action, ParentProc, Impact }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EffectRandomSharedContextCapture { SourceOnly, FirstEligibleImpact }

/// <summary>A numeric calculation in the probability unit. It never settles a resource.</summary>
public sealed record EffectRandomProbabilityDefinition
{
    public float? FlatValue { get; init; }
    public string? FormulaValue { get; init; }
    public string Channel { get; init; } = string.Empty;
    public string PipelineId { get; init; } = string.Empty;
    public string UnitId { get; init; } = "probability";
    public ImmutableArray<string> StageIds { get; init; } = [];
    public CalculationValuePolicy Conversion { get; init; } = new();
    public EffectRandomSharedContextCapture SharedContextCapture { get; init; }
}

/// <summary>Stochastic numeric inputs, not a second damage calculator. A critical is an authored use of these inputs.</summary>
public sealed record EffectRandomInputDefinition
{
    public string InputId { get; init; } = string.Empty;
    public string? GroupId { get; init; }
    public float? Chance { get; init; }
    public EffectRandomProbabilityDefinition? Probability { get; init; }
    public EffectRandomScope Scope { get; init; }
}

public sealed record EffectRandomInputResult
{
    public string InputId { get; init; } = string.Empty;
    public EffectRandomScope Scope { get; init; }
    public string ScopeId { get; init; } = string.Empty;
    public double? Roll { get; init; }
    public bool Success { get; init; }
    public float Probability { get; init; }
    public CalculationResult? Calculation { get; init; }
    public string ContentRevision { get; init; } = string.Empty;
    public string CapturedAtImpactId { get; init; } = string.Empty;
    public string CapturedAtProcId { get; init; } = string.Empty;
    public string SnapshotHash { get; init; } = string.Empty;
    public string? RunSnapshotHash { get; init; }
}

public static class EffectRandomProbabilityPolicies
{
    public static Result Validate(EffectRandomProbabilityDefinition probability)
    {
        if (probability.FlatValue == null && string.IsNullOrWhiteSpace(probability.FormulaValue) ||
            probability.FlatValue is { } flat && !float.IsFinite(flat) ||
            string.IsNullOrWhiteSpace(probability.Channel) || string.IsNullOrWhiteSpace(probability.PipelineId) ||
            probability.UnitId != "probability" || !Enum.IsDefined(probability.SharedContextCapture) ||
            probability.StageIds.Any(string.IsNullOrWhiteSpace) || probability.StageIds.Distinct(StringComparer.Ordinal).Count() != probability.StageIds.Length)
            return Result.Failure("Calculated random inputs require a finite numeric source, channel, pipeline and probability unit");
        return CalculationValuePolicy.Validate(probability.Conversion);
    }

    public static Result ValidatePipeline(EffectRandomProbabilityDefinition probability, CalculationPipelineDefinition pipeline,
        EffectRandomScope scope)
    {
        var valid = Validate(probability);
        if (valid.IsFailure) return valid;
        if (pipeline.UnitId != "probability" || pipeline.Channel != probability.Channel ||
            probability.StageIds.Any(id => !pipeline.Stages.Any(stage => stage.StageId == id)))
            return Result.Failure("Probability pipeline unit, channel or stages do not match the random input");
        if (pipeline.ResourceInfluenceBindings.Any(binding => binding.Settlement != null) ||
            pipeline.Buckets.Any(bucket => bucket.Operation == CalculationBucketOperation.ConsumeCapacity))
            return Result.Failure("Probability calculations cannot contain resource settlements or capacity consumption");
        if (scope != EffectRandomScope.Impact && probability.SharedContextCapture == EffectRandomSharedContextCapture.SourceOnly &&
            (UsesTarget(probability.FormulaValue) || pipeline.Buckets.Any(bucket => UsesTarget(bucket.Formula)) ||
             pipeline.Stages.Any(stage => stage.Scope == CalculationStageScope.Target) ||
             pipeline.StatInfluenceBindings.Any(binding => binding.Scope == CalculationEntityScope.Target) ||
             pipeline.ResourceInfluenceBindings.Any(binding => binding.Scope == CalculationEntityScope.Target)))
            return Result.Failure("Shared target-dependent probability requires explicit FirstEligibleImpact capture");
        return Result.Success();
    }

    private static bool UsesTarget(string? formula) => formula?.Contains("target.", StringComparison.OrdinalIgnoreCase) == true ||
        formula?.Contains("target_", StringComparison.OrdinalIgnoreCase) == true ||
        formula?.Contains("repeat_index", StringComparison.OrdinalIgnoreCase) == true;
}

internal static class EffectInputNamespaces
{
    public static bool IsFactVariable(string token)
    {
        if (token is "continuation.requested_change" or "continuation.applied_change" or "continuation.limited_change") return true;
        if (!token.StartsWith("rolls.", StringComparison.Ordinal)) return false;
        var parts = token.Split('.');
        return parts.Length == 3 && parts[2] is "success" or "probability" && parts[1].Length is > 0 and <= 64 &&
            parts[1].All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
    }
}

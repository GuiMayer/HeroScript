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
    public ImmutableSortedDictionary<string, EffectNumericParameterDefinition> Captures { get; init; } =
        ImmutableSortedDictionary<string, EffectNumericParameterDefinition>.Empty.WithComparers(StringComparer.Ordinal);
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
    public ImmutableSortedDictionary<string, CalculationResult> Captures { get; init; } =
        ImmutableSortedDictionary<string, CalculationResult>.Empty.WithComparers(StringComparer.Ordinal);
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
        if (probability.Captures.Count > 8) return Result.Failure("Random inputs support at most eight numeric captures");
        foreach (var (id, capture) in probability.Captures)
        {
            if (!SafeId(id) || capture.Parameter != EffectNumericParameter.Amount || capture.InputQuantityId != null ||
                capture.Distribution != null || string.IsNullOrWhiteSpace(capture.PipelineId) ||
                capture.FormulaValue?.Contains("captures.", StringComparison.OrdinalIgnoreCase) == true)
                return Result.Failure("Random captures require independent named Amount calculations with an explicit pipeline");
            var errors = EffectDefinitionValidator.Validate([new EffectDefinition
                { Type = EffectType.MODIFY_RESOURCE, TargetResource = "__numeric_capture", Parameters = [capture] }]);
            if (!errors.IsEmpty) return Result.Failure(string.Join(";", errors));
        }
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

    public static Result ValidateCapturePipeline(EffectNumericParameterDefinition capture, CalculationPipelineDefinition pipeline,
        EffectRandomScope scope, EffectRandomSharedContextCapture context)
    {
        var valid = ValidatePipeline(new() { FlatValue = capture.FlatValue, FormulaValue = capture.FormulaValue, Channel = capture.Channel,
            PipelineId = capture.PipelineId!, StageIds = capture.StageIds, SharedContextCapture = context },
            pipeline with { UnitId = "probability" }, scope);
        if (valid.IsFailure) return valid;
        return capture.UnitId != pipeline.UnitId ? Result.Failure("Random capture unit does not match its pipeline") : Result.Success();
    }

    internal static bool SafeId(string id) => id.Length is > 0 and <= 64 &&
        id.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
}

internal static class EffectRandomInputVariables
{
    public static void Add(IDictionary<string, float> variables, EffectRandomInputResult input)
    {
        variables[$"rolls.{input.InputId}.success"] = input.Success ? 1 : 0;
        variables[$"rolls.{input.InputId}.probability"] = input.Probability;
        foreach (var (id, calculation) in input.Captures) variables[$"rolls.{input.InputId}.values.{id}"] = calculation.Value;
        foreach (var (id, value) in input.Calculation?.Checkpoints ?? ImmutableSortedDictionary<string, float>.Empty)
            variables[$"rolls.{input.InputId}.checkpoints.{id}"] = value;
    }
}

internal static class EffectInputNamespaces
{
    public static bool IsFactVariable(string token)
    {
        if (token is "continuation.requested_change" or "continuation.applied_change" or "continuation.limited_change") return true;
        if (token.StartsWith("captures.", StringComparison.Ordinal)) return EffectRandomProbabilityPolicies.SafeId(token[9..]);
        if (!token.StartsWith("rolls.", StringComparison.Ordinal)) return false;
        var parts = token.Split('.');
        return parts.Length is 3 or 4 && EffectRandomProbabilityPolicies.SafeId(parts[1]) &&
            (parts.Length == 3 && parts[2] is "success" or "probability" ||
             parts.Length == 4 && parts[2] is "values" or "checkpoints" && EffectRandomProbabilityPolicies.SafeId(parts[3]));
    }
}

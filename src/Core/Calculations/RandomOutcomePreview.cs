using System.Collections.Immutable;
using Core.Content;
using Core.Effects;
using Core.Determinism;

namespace Core.Calculations;

/// <summary>Conditional numerical alternatives, never a promised range for a whole action.</summary>
public sealed record RandomNumericAlternative
{
    public bool Success { get; init; }
    public float Probability { get; init; }
    public CalculationResult? Calculation { get; init; }
    public string? UnavailableReason { get; init; }
}

public sealed record RandomImpactPreview
{
    public string ImpactId { get; init; } = string.Empty;
    public string TargetEntityId { get; init; } = string.Empty;
    public string InputId { get; init; } = string.Empty;
    public EffectRandomScope Scope { get; init; }
    public float Probability { get; init; }
    public ImmutableSortedDictionary<string, CalculationResult> Captures { get; init; } =
        ImmutableSortedDictionary<string, CalculationResult>.Empty.WithComparers(StringComparer.Ordinal);
    public ImmutableSortedDictionary<string, float> Checkpoints { get; init; } =
        ImmutableSortedDictionary<string, float>.Empty.WithComparers(StringComparer.Ordinal);
    public bool ConditionalOnReachingImpact { get; init; } = true;
    public string Validity { get; init; } = "LocalNumericAlternativesWithCapturedContributions";
    public ImmutableArray<RandomNumericAlternative> Alternatives { get; init; } = [];
}

public sealed record RandomOutcomePreview
{
    public ImmutableArray<RandomImpactPreview> Impacts { get; init; } = [];
    public bool Truncated { get; init; }
    public bool IsGlobalOutcomeRange { get; init; } // Always false: branches/defense/death change later contexts.
}

/// <summary>Uses the calculation engine and immutable traces. No RNG, execution, settlements or Cartesian expansion.</summary>
public static class RandomOutcomePreviewProjector
{
    public const int MaximumInputs = 64;
    public static bool HasStochasticInput(IEnumerable<EffectExecutionStep> steps) => steps.Any(step =>
        step.ChanceRoll != null || step.RandomInputs.Any(input => input.Probability is > 0 and < 1));

    public static RandomOutcomePreview Project(IReadOnlyList<EffectExecutionStep> steps,
        ContentRuntime? runtime = null, ICalculationEngine? calculations = null)
    {
        var inputs = steps.SelectMany(step => step.RandomInputs.Select(input => (Step: step, Input: input))).Take(MaximumInputs + 1).ToArray();
        var impacts = ImmutableArray.CreateBuilder<RandomImpactPreview>();
        foreach (var (step, input) in inputs.Take(MaximumInputs))
        {
            var alternatives = ImmutableArray.CreateBuilder<RandomNumericAlternative>();
            foreach (var success in new[] { false, true })
            {
                var probability = success ? input.Probability : 1 - input.Probability;
                if (probability <= 0) continue;
                var trace = step.Parameters.FirstOrDefault(parameter => parameter.Parameter == EffectNumericParameter.Amount)?.Calculation
                    ?? step.Calculation;
                var ambiguous = step.RandomInputs.Count(other => other.Probability is > 0 and < 1) > 1;
                var reason = ambiguous ? "multiple_dependent_inputs" : trace == null ? "no_numeric_impact" :
                    trace.BaseTrace.Any(item => item.Attribute.EndsWith("FormulaValue", StringComparison.Ordinal))
                        ? "base_formula_requires_execution_context" : runtime == null || calculations == null ? "calculation_services_unavailable" :
                    runtime.Manifest.Revision != trace.ContentRevision ? "content_revision_mismatch" : null;
                CalculationResult? result = null;
                if (reason == null)
                {
                    var pipeline = runtime!.GetDefinition<CalculationPipelineDefinition>("calculation-pipelines", trace!.PipelineId);
                    if (pipeline.IsFailure) reason = pipeline.Error;
                    else if (CanonicalJson.ComputeHash(pipeline.Value) != trace.PipelineFingerprint) reason = "pipeline_fingerprint_mismatch";
                    else
                    {
                        var variables = trace.Variables.SetItem($"rolls.{input.InputId}.success", success ? 1 : 0);
                        var recomputed = calculations!.Calculate(new()
                        {
                            CalculationId = trace.CalculationId + (success ? ":alternative:success" : ":alternative:failure"),
                            ContentRevision = trace.ContentRevision, Channel = trace.Channel, UnitId = trace.Quantity.UnitId,
                            BaseValue = trace.BaseValue, ValuePolicy = trace.ValuePolicy, CaptureOnly = true,
                            StageIds = trace.Buckets.Select(bucket => bucket.StageId).OfType<string>().Distinct(StringComparer.Ordinal).ToImmutableArray(),
                            StageContextIds = trace.Quantity.IncorporatedStages.Where(stage => stage.PipelineId == trace.PipelineId)
                                .GroupBy(stage => stage.StageId).ToImmutableSortedDictionary(group => group.Key, group => group.Last().ContextId, StringComparer.Ordinal),
                            Influences = trace.Buckets.SelectMany(bucket => bucket.Contributions.Select(contribution => new CalculationInfluence
                            {
                                InfluenceId = contribution.InfluenceId, SourceKind = contribution.SourceKind, SourceId = contribution.SourceId,
                                Channel = trace.Channel, Bucket = bucket.BucketId, Value = contribution.Value,
                                Priority = contribution.Priority, OrderKey = contribution.OrderKey
                            })).ToImmutableArray(),
                            Variables = variables, Tags = trace.Tags.ToImmutableHashSet(StringComparer.Ordinal), BaseTrace = trace.BaseTrace
                        }, pipeline.Value);
                        if (recomputed.IsSuccess) result = recomputed.Value;
                        else reason = recomputed.Error;
                    }
                }
                alternatives.Add(new() { Success = success, Probability = probability, Calculation = result, UnavailableReason = reason });
            }
            impacts.Add(new() { ImpactId = step.Identity?.ImpactId ?? string.Empty, TargetEntityId = step.TargetEntityId,
                InputId = input.InputId, Scope = input.Scope, Probability = input.Probability, Captures = input.Captures,
                Checkpoints = input.Calculation?.Checkpoints ?? ImmutableSortedDictionary<string, float>.Empty, Alternatives = alternatives.ToImmutable() });
        }
        return new() { Impacts = impacts.ToImmutable(), Truncated = inputs.Length > MaximumInputs };
    }
}

using System.Collections.Immutable;
using Core.Calculations;
using Core.Combat.Models;
using Core.Content;
using Core.Determinism;
using Core.Effects;

namespace Core.Run.Content;

/// <summary>Formats already-resolved impact facts. Never evaluates effects or samples an alternative.</summary>
public static class CardImpactExplanationProjector
{
    public static CardPresentationFacts Attach(CardPresentationFacts facts, EffectiveCardDefinition card, RunState run,
        CombatState combat, ContentRuntime runtime, IReadOnlyList<EffectExecutionStep> steps, string validity)
    {
        if (facts.Version.ContentRevision != runtime.Manifest.Revision || facts.Version.ContentRevision != run.Determinism.ContentRevision ||
            facts.Version.CompositionFingerprint != card.Fingerprint || steps.Any(step => step.ContentRevision != facts.Version.ContentRevision))
            throw new ArgumentException("Impact explanation revision/composition mismatch");
        var impacts = ImmutableArray.CreateBuilder<CardImpactExplanation>();
        var eligibleSteps = steps.Where(step => step.Calculation != null || !step.Parameters.IsEmpty).ToArray();
        foreach (var step in eligibleSteps.Take(64))
        {
            var parameters = ImmutableArray.CreateBuilder<CardImpactParameterExplanation>();
            var resolved = step.Parameters;
            if (resolved.IsEmpty && step.Calculation != null)
                resolved = [new() { Parameter = EffectNumericParameter.Amount, Calculation = step.Calculation }];
            foreach (var parameter in resolved)
            {
                var calculation = parameter.Calculation;
                var profile = runtime.GetDefinition<CalculationPipelineDefinition>("calculation-pipelines", calculation.PipelineId);
                var sources = profile.IsSuccess ? CardSourcePresentationProjector.Sources(calculation, profile.Value, card, run, combat) : [];
                parameters.Add(new(parameter.Parameter, calculation.Quantity.UnitId, calculation.Value,
                    calculation.Buckets.Take(64).Select(bucket => bucket with { Contributions = bucket.Contributions.Take(64).ToArray() }).ToImmutableArray(),
                    sources.Take(64).ToImmutableArray(), calculation.Buckets.Count > 64 || sources.Length > 64));
            }
            impacts.Add(new(step.Identity?.ImpactId ?? step.EffectInstanceId, step.Provenance.ComponentId ?? string.Empty,
                step.TargetEntityId, validity, step.Applied, step.SkipReason, parameters.ToImmutable()));
        }
        var result = facts with { Impacts = impacts.ToImmutable(), ImpactsTruncated = eligibleSteps.Length > 64, Fingerprint = string.Empty };
        return result with { Fingerprint = CanonicalJson.ComputeHash(result) };
    }
}

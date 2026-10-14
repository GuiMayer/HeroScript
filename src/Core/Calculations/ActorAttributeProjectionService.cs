using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;
using Core.Content;
using Core.Effects;
using Core.Math;
using Core.Run;

namespace Core.Calculations;

public sealed record ActorAttributeValueProjection(string AttributeId, string DisplayName, string Unit,
    float BaseValue, float EffectiveValue,
    [property: System.Text.Json.Serialization.JsonIgnore] CalculationResult Calculation, ImmutableArray<string> ContextTags)
{
    public string PipelineId => Calculation.PipelineId;
    public string PipelineFingerprint => Calculation.PipelineFingerprint;
    public bool ContributionsTruncated => Calculation.Buckets.Sum(bucket => bucket.Contributions.Count) > 64;
    public ImmutableArray<CalculationContributionTrace> Contributions => Calculation.Buckets.SelectMany(bucket => bucket.Contributions).Take(64).ToImmutableArray();
}
public sealed record ActorAttributesProjection(Guid RunId, Guid? CombatId, int RunSequence, ulong? CombatStep,
    string ContentRevision, string ActorId, string Authority, ImmutableArray<ActorAttributeValueProjection> Attributes)
{
    public ImmutableArray<ActorCriticalRuleProjection> CriticalRules { get; init; } = [];
}
public sealed record ActorCriticalRuleProjection(string RuleId, float GuaranteedTiers, float NextTierChancePercent,
    float BonusPerTierPercent, ImmutableArray<string> ContextTags);

public interface IActorAttributeProjectionService
{
    Result<ActorAttributesProjection> Project(RunState run, CombatState? combat, string actorId);
}

/// <summary>Read-only numerical projection using exactly the same providers and engine as gameplay.</summary>
public sealed class ActorAttributeProjectionService(IContentRuntimeResolver runtimes, IRuntimeFormulaEvaluator formulas,
    ICalculationEngine engine, ICalculationInfluenceProvider influences) : IActorAttributeProjectionService
{
    public Result<ActorAttributesProjection> Project(RunState run, CombatState? combat, string actorId)
    {
        var revision = run.Determinism.ContentRevision;
        if (combat != null && combat.GetActor(actorId) == null)
            return Result<ActorAttributesProjection>.Failure("Attribute owner not found");
        if (run.ResolvedMode?.Definition.EffectResolutionPolicyId == null)
            return Result<ActorAttributesProjection>.Success(new(run.RunId, combat?.CombatId, run.Sequence,
                combat?.Determinism.Step, revision, actorId, combat == null ? "PersistentPlayer" : "ActiveEncounter", []));
        CombatActorState? actor = combat?.GetActor(actorId);
        if (combat == null && run.PlayerEntity is { } player && player.InstanceId == actorId)
            actor = new() { InstanceId = player.InstanceId, DefinitionId = player.DefinitionId, Name = player.Name,
                ContentRevision = player.ContentRevision, Components = player.Components };
        if (actor == null) return Result<ActorAttributesProjection>.Failure("Attribute owner not found");
        var values = ImmutableArray.CreateBuilder<ActorAttributeValueProjection>();
        var criticalRules = ImmutableArray.CreateBuilder<ActorCriticalRuleProjection>();
        if (run.ResolvedMode?.Definition.EffectResolutionPolicyId is { } id)
        {
            var runtime = runtimes.Resolve(revision, run.ConfigName);
            if (runtime.IsFailure) return Result<ActorAttributesProjection>.Failure(runtime.Error);
            var policy = runtime.Value.GetDefinition<EffectResolutionPolicyDefinition>("effect-resolution-policies", id);
            if (policy.IsFailure) return Result<ActorAttributesProjection>.Failure(policy.Error);
            var calculator = new CalculationResolver(formulas, runtimes, engine, influences);
            var baseCalculator = new CalculationResolver(formulas, runtimes, engine, new EntityStatInfluenceProvider());
            foreach (var attribute in policy.Value.Attributes)
            {
                var pipeline = runtime.Value.GetDefinition<CalculationPipelineDefinition>("calculation-pipelines", attribute.PipelineId);
                if (pipeline.IsFailure) return Result<ActorAttributesProjection>.Failure(pipeline.Error);
                var parameter = new EffectNumericParameterDefinition { Parameter = EffectNumericParameter.Amount, FlatValue = attribute.FlatValue,
                    PipelineId = attribute.PipelineId, Channel = pipeline.Value.Channel, UnitId = pipeline.Value.UnitId };
                // Tags are authored context, not a promise that every card receives this value.
                var context = new CalculationSourceContext { Run = run, Combat = combat, Actor = actor, Target = actor,
                    ContentRevision = revision, CaptureOnly = true, Tags = attribute.Tags.ToImmutableHashSet(StringComparer.Ordinal),
                    Variables = GameplayFormulaContext.Build(actor, actor, actor, run) };
                var owner = new EffectDefinition { Type = EffectType.MODIFY_ATTRIBUTE };
                var calculationId = $"attributes:{actorId}:{attribute.AttributeId}";
                var effective = calculator.ResolveParameter(owner, parameter, calculationId, context);
                var baseline = baseCalculator.ResolveParameter(owner, parameter, calculationId + ":base", context);
                if (effective.IsFailure || baseline.IsFailure)
                    return Result<ActorAttributesProjection>.Failure(effective.IsFailure ? effective.Error : baseline.Error);
                values.Add(new(attribute.AttributeId, attribute.DisplayName, attribute.DisplayUnit,
                    baseline.Value.Value * attribute.DisplayScale + attribute.DisplayOffset,
                    effective.Value.Value * attribute.DisplayScale + attribute.DisplayOffset,
                    effective.Value.Calculation!, attribute.Tags));
            }
            foreach (var rule in policy.Value.Rules.Where(rule => rule.CriticalInput?.Probability != null &&
                rule.TierCheckpointId != null && rule.BonusCaptureId != null))
            {
                var tags = rule.ProjectionTags.Concat(rule.RequiredTags).ToImmutableHashSet(StringComparer.Ordinal);
                var context = new CalculationSourceContext { Run = run, Combat = combat, Actor = actor, Target = actor,
                    ContentRevision = revision, CaptureOnly = true, Tags = tags, Variables = GameplayFormulaContext.Build(actor, actor, actor, run) };
                var probability = calculator.ResolveProbability(new() { Type = EffectType.DAMAGE }, rule.CriticalInput!.Probability!,
                    rule.CriticalInput.Scope, $"attributes:{actorId}:{rule.RuleId}:probability", context);
                if (probability.IsFailure) return Result<ActorAttributesProjection>.Failure(probability.Error);
                if (!probability.Value.Calculation!.Checkpoints.TryGetValue(rule.TierCheckpointId!, out var tiers) ||
                    !probability.Value.Captures.TryGetValue(rule.BonusCaptureId!, out var bonus))
                    return Result<ActorAttributesProjection>.Failure("Critical projection mapping was not produced by its pipeline");
                criticalRules.Add(new(rule.RuleId, tiers, probability.Value.Value * 100, bonus.Value * 100,
                    tags.Order(StringComparer.Ordinal).ToImmutableArray()));
            }
        }
        return Result<ActorAttributesProjection>.Success(new(run.RunId, combat?.CombatId, run.Sequence, combat?.Determinism.Step,
            revision, actorId, combat == null ? "PersistentPlayer" : "ActiveEncounter", values.ToImmutable()) { CriticalRules = criticalRules.ToImmutable() });
    }
}

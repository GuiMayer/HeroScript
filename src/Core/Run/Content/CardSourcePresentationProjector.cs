using System.Collections.Immutable;
using Core.Calculations;
using Core.Combat.Models;
using Core.Common;
using Core.Content;
using Core.Determinism;
using Core.Effects;
using Core.Math;

namespace Core.Run.Content;

public interface ICardSourcePresentationProjector
{
    Result<CardPresentationFacts> Project(CardPresentationFacts baseline, EffectiveCardDefinition card,
        RunState run, CombatState combat, string actorId);
}

/// <summary>Captures numerical Actor prefixes without legal actions, targets, settlements or RNG.</summary>
public sealed class CardSourcePresentationProjector(IContentRuntimeResolver runtimes, IRuntimeFormulaEvaluator formulas,
    ICalculationEngine engine, ICalculationInfluenceProvider influences) : ICardSourcePresentationProjector
{
    public Result<CardPresentationFacts> Project(CardPresentationFacts baseline, EffectiveCardDefinition card,
        RunState run, CombatState combat, string actorId)
    {
        var actor = combat.GetActor(actorId);
        if (actor == null || baseline.Identity.Validate().IsFailure || baseline.Identity.DefinitionId != card.DefinitionId ||
            baseline.Identity.CardInstanceId != null && baseline.Identity.CardInstanceId != card.CardInstanceId ||
            baseline.Version.ContentRevision != run.Determinism.ContentRevision ||
            baseline.Version.CompositionFingerprint != card.Fingerprint)
            return Result<CardPresentationFacts>.Failure("Source presentation does not match its captured card/context");
        var loaded = runtimes.Resolve(run.Determinism.ContentRevision, run.ConfigName);
        if (loaded.IsFailure) return Result<CardPresentationFacts>.Failure(loaded.Error);
        var runtime = loaded.Value;
        EffectResolutionPolicyDefinition? policy = null;
        if (run.ResolvedMode?.Definition.EffectResolutionPolicyId is { } id)
        {
            var resolved = runtime.GetDefinition<EffectResolutionPolicyDefinition>("effect-resolution-policies", id);
            if (resolved.IsFailure) return Result<CardPresentationFacts>.Failure(resolved.Error);
            policy = resolved.Value;
        }
        var calculator = new CalculationResolver(formulas, runtimes, engine, influences);
        var effects = ImmutableArray.CreateBuilder<CardEffectPresentation>();
        var explanations = ImmutableArray.CreateBuilder<CardValueExplanation>();
        foreach (var presented in baseline.Effects)
        {
            var resolved = EffectResolutionPolicyResolver.Resolve(presented.Definition, policy, runtime, card,
                card.Tags.ToImmutableHashSet(StringComparer.Ordinal));
            if (resolved.IsFailure) return Result<CardPresentationFacts>.Failure(resolved.Error);
            var effect = resolved.Value.Effect;
            var tags = CalculationResolver.NormalizeTags(effect, card.Tags.ToImmutableHashSet(StringComparer.Ordinal));
            var variables = GameplayFormulaContext.Build(actor, null, actor, run);
            var executionTags = card.Tags.IsEmpty ? effect.Tags.ToHashSet(StringComparer.Ordinal) : card.Tags.ToHashSet(StringComparer.Ordinal);
            bool? condition = (effect.RequiredTags ?? []).All(executionTags.Contains) && !(effect.ExcludedTags ?? []).Any(executionTags.Contains);
            if (condition == true && !string.IsNullOrWhiteSpace(effect.Condition))
            {
                var assessed = GameplayFormulaContext.Evaluate(formulas, effect.Condition, run.Determinism.ContentRevision, variables);
                condition = assessed.IsSuccess ? assessed.Value > 0 : null;
            }
            var parameters = ImmutableArray.CreateBuilder<CardParameterPresentation>();
            foreach (var parameter in presented.Parameters)
            {
                var authored = effect.Parameters.FirstOrDefault(value => value.Parameter == parameter.Parameter);
                if (authored == null && parameter.Parameter != EffectNumericParameter.Amount)
                {
                    parameters.Add(parameter with { Values = parameter.Values with { Source = parameter.Values.ComposedBase } });
                    continue;
                }
                var numeric = authored ?? new EffectNumericParameterDefinition { Parameter = parameter.Parameter,
                    FlatValue = effect.FlatValue, FormulaValue = effect.FormulaValue,
                    Channel = effect.CalculationChannel, PipelineId = effect.CalculationPipelineId, UnitId = "scalar" };
                var profile = CalculationResolver.ResolvePipeline(effect with { CalculationChannel = numeric.Channel,
                    CalculationPipelineId = numeric.PipelineId }, run, runtime);
                if (profile.IsFailure) return Result<CardPresentationFacts>.Failure(profile.Error);
                var sourceIds = numeric.Distribution?.SourceStageIds ?? profile.Value.Stages
                    .TakeWhile(stage => stage.Scope == CalculationStageScope.Actor).Select(stage => stage.StageId).ToImmutableArray();
                var unscopedTarget = profile.Value.Stages.IsEmpty && (profile.Value.StatInfluenceBindings.Any(binding => binding.Scope == CalculationEntityScope.Target) ||
                    profile.Value.ResourceInfluenceBindings.Any(binding => binding.Scope == CalculationEntityScope.Target));
                string? unavailable = numeric.InputQuantityId != null ? "transported_quantity_required" :
                    !profile.Value.Stages.IsEmpty && sourceIds.IsEmpty || unscopedTarget ? "source_scope_not_declared" : null;
                var attributes = Attributes(profile.Value, sourceIds, tags, actor);
                CalculationResult? calculation = null;
                if (unavailable == null)
                {
                    var captured = calculator.ResolveParameter(effect, numeric with { StageIds = sourceIds, Distribution = null },
                        $"presentation:{presented.EffectPath}:{parameter.Parameter}:source", new() {
                            Run = run, Combat = combat, Actor = actor, Card = card, ComponentId = presented.ComponentId,
                            ContentRevision = run.Determinism.ContentRevision, CaptureOnly = true,
                            Tags = tags, Variables = variables });
                    if (captured.IsSuccess) calculation = captured.Value.Calculation;
                    else unavailable = "calculation_context_required";
                }
                parameters.Add(parameter with { Values = parameter.Values with { Source = calculation?.Value,
                    UnitId = numeric.UnitId, UnresolvedReason = unavailable } });
                explanations.Add(new() { EffectPath = presented.EffectPath, Parameter = parameter.Parameter, Attributes = attributes,
                    Buckets = calculation?.Buckets.Take(64).Select(bucket => bucket with { Contributions = bucket.Contributions.Take(64).ToArray() }).ToImmutableArray() ?? [],
                    ContextTags = tags.Order(StringComparer.Ordinal).ToImmutableArray(), ConditionPassed = condition, ConditionExpression = effect.Condition,
                    Truncated = calculation != null && (calculation.Buckets.Count > 64 || calculation.Buckets.Sum(bucket => bucket.Contributions.Count) > 64),
                    Sources = calculation == null ? [] : Sources(calculation, profile.Value, card, run, combat).Take(64).ToImmutableArray(), UnresolvedReason = unavailable });
            }
            effects.Add(presented with { Parameters = parameters.ToImmutable() });
        }
        var facts = baseline with { FaceValueMode = CardFaceValueMode.Source, Effects = effects.ToImmutable(),
            Explanations = explanations.ToImmutable(), Fingerprint = string.Empty,
            Version = baseline.Version with { RunId = run.RunId, RunSequence = run.Sequence,
                CombatId = combat.CombatId, CombatStep = combat.Determinism.Step, ActorId = actorId } };
        return Result<CardPresentationFacts>.Success(facts with { Fingerprint = CanonicalJson.ComputeHash(facts) });
    }

    private static ImmutableArray<CardScalingAttribute> Attributes(CalculationPipelineDefinition profile,
        ImmutableArray<string> stageIds, IReadOnlySet<string> tags, CombatActorState actor) => profile.StatInfluenceBindings
        .Where(binding => binding.Scope == CalculationEntityScope.Actor && (stageIds.IsEmpty ||
            profile.Buckets.Any(bucket => bucket.BucketId == binding.Bucket && stageIds.Contains(bucket.StageId!))))
        .OrderBy(binding => binding.BindingId, StringComparer.Ordinal).Select(binding => {
            var eligible = binding.RequiredTags.All(tags.Contains) && !binding.ExcludedTags.Any(tags.Contains);
            var stats = actor.Component<StatEntityComponentState>(binding.ComponentId);
            float? value = stats?.Values.TryGetValue(binding.ValueId, out var raw) == true ? raw :
                binding.MissingValue == MissingResourcePolicy.Zero ? binding.DefaultValue : null;
            return new CardScalingAttribute(binding.BindingId, binding.ComponentId, binding.ValueId, binding.Scope,
                binding.Bucket, eligible, eligible ? value == null ? "attribute_not_present" : null : "tags_do_not_match", value);
        }).ToImmutableArray();

    internal static ImmutableArray<CardCalculationSource> Sources(CalculationResult calculation, CalculationPipelineDefinition profile,
        EffectiveCardDefinition card, RunState run, CombatState combat)
    {
        var result = ImmutableArray.CreateBuilder<CardCalculationSource>();
        foreach (var bucket in calculation.Buckets)
        foreach (var contribution in bucket.Contributions)
        {
            var binding = profile.StatInfluenceBindings.FirstOrDefault(value => value.BindingId == contribution.InfluenceId);
            var status = combat.StatusEffects.Values.SelectMany(values => values).FirstOrDefault(value => value.InstanceId.ToString() == contribution.SourceId);
            var modifier = run.Modifiers.FirstOrDefault(value => value.InstanceId.ToString() == contribution.SourceId);
            var relic = run.Relics.FirstOrDefault(value => value.RelicInstanceId.ToString() == contribution.SourceId);
            var definitionId = contribution.SourceKind switch {
                CalculationSourceKind.Card => card.DefinitionId,
                CalculationSourceKind.Status => status?.StatusId,
                CalculationSourceKind.Modifier => modifier?.ModifierId,
                CalculationSourceKind.Relic => relic?.DefinitionId,
                CalculationSourceKind.GameMode => run.ModeId,
                _ => null };
            result.Add(new(contribution.SourceKind, contribution.SourceId, definitionId, binding?.ValueId,
                binding?.ComponentId ?? contribution.InfluenceId, bucket.BucketId, contribution.Value, contribution.Applied));
        }
        return result.ToImmutable();
    }
}

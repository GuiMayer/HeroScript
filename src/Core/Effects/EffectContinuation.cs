using System.Collections.Immutable;
using Core.Calculations;
using Core.Combat.Models;
using Core.Common;

namespace Core.Effects;

/// <summary>Opt-in transfer of a causally defeated impact's applied-domain remainder.</summary>
public sealed record EffectContinuationDefinition
{
    public int MaximumHops { get; init; } = 1;
    public EffectTarget Selector { get; init; } = EffectTarget.RANDOM_ENEMY;
    public string? SelectionResourceId { get; init; }
    public string OverflowPipelineId { get; init; } = string.Empty;
    public string OverflowChannel { get; init; } = string.Empty;
    public ImmutableArray<string> OverflowStageIds { get; init; } = [];
    public ImmutableArray<string> ImpactStageIds { get; init; } = [];
    public bool CarryChildren { get; init; }
    public bool RetestChance { get; init; }
    public bool RetestCondition { get; init; }
    public bool RollImpactInputs { get; init; }
}

public sealed record EffectContinuationTrace
{
    public int Hop { get; init; }
    public string SourceImpactId { get; init; } = string.Empty;
    public string FromEntityId { get; init; } = string.Empty;
    public string? ToEntityId { get; init; }
    public string ResourceId { get; init; } = string.Empty;
    public ImmutableArray<string> VisitedEntityIds { get; init; } = [];
    public CalculationResult? Overflow { get; init; }
    public string? StopReason { get; init; }
}

internal sealed record EffectContinuationPlan(EffectContinuationTrace Trace, CombatState Combat,
    EffectDefinition? Effect = null, CalculationQuantity? Quantity = null);

/// <summary>Orchestrates facts and targeting; all remainder arithmetic belongs to authored pipelines.</summary>
internal sealed class EffectContinuationPlanner(ICalculationResolver calculations)
{
    public static Result ValidateProfiles(EffectDefinition effect, CalculationPipelineDefinition impact,
        CalculationPipelineDefinition overflow)
    {
        var policy = effect.Continuation!;
        var amount = effect.Parameters.FirstOrDefault(parameter => parameter.Parameter == EffectNumericParameter.Amount);
        if (amount == null || impact.UnitId != amount.UnitId || overflow.UnitId != amount.UnitId ||
            overflow.Channel != policy.OverflowChannel || policy.OverflowStageIds.IsEmpty || policy.ImpactStageIds.IsEmpty)
            return Result.Failure("Continuation requires compatible staged Amount and overflow profiles with explicit units/stages");
        if (policy.ImpactStageIds.Any(id => !impact.Stages.Any(stage => stage.StageId == id && stage.Scope == CalculationStageScope.Target)) ||
            policy.OverflowStageIds.Any(id => !overflow.Stages.Any(stage => stage.StageId == id && stage.Scope == CalculationStageScope.Target)))
            return Result.Failure("Continuation stages must be target-scoped; origin cannot be applied again");
        if (policy.OverflowStageIds.Intersect(impact.Stages.Select(stage => stage.StageId), StringComparer.Ordinal).Any())
            return Result.Failure("Continuation overflow stages require distinct semantic IDs");
        return Result.Success();
    }

    public Result<EffectContinuationPlan> Plan(EffectTriggerExecutionRequest request, EffectDefinition effect,
        EffectApplicationRecord application, CalculationResult calculation, int hop,
        ImmutableArray<string> visited, IReadOnlyDictionary<string, float> variables, string componentId)
    {
        var policy = effect.Continuation!;
        var trace = new EffectContinuationTrace { Hop = hop, SourceImpactId = application.Identity!.ImpactId,
            FromEntityId = application.TargetEntityId, ResourceId = application.ResourceId!, VisitedEntityIds = visited };
        Result<EffectContinuationPlan> Stop(string reason, CalculationResult? overflow = null) =>
            Result<EffectContinuationPlan>.Success(new(trace with { StopReason = reason, Overflow = overflow }, request.Combat));
        if (application.ResourceOutcome?.CausedDefeat != true) return Stop("no_causal_defeat");
        if (hop > policy.MaximumHops) return Stop("hop_limit");
        var outcome = application.ResourceOutcome;
        var facts = variables.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        facts["continuation.requested_change"] = (float)outcome.RequestedChange;
        facts["continuation.applied_change"] = (float)outcome.AppliedChange;
        facts["continuation.limited_change"] = (float)outcome.LimitedChange;
        if (facts.Values.Any(value => !float.IsFinite(value)))
            return Result<EffectContinuationPlan>.Failure("Continuation facts cannot be represented in the numeric domain");
        var amount = effect.Parameters.Single(parameter => parameter.Parameter == EffectNumericParameter.Amount);
        var overflowParameter = amount with { FlatValue = null, FormulaValue = null, InputQuantityId = null,
            Distribution = null, PipelineId = policy.OverflowPipelineId, Channel = policy.OverflowChannel,
            StageIds = policy.OverflowStageIds, Conversion = new() { Sign = CalculationSignPolicy.NonNegative } };
        var remainder = calculations.ResolveParameter(effect, overflowParameter,
            $"{application.Identity.ImpactId}:overflow:{hop}", new()
            { Combat = request.Combat, Run = request.Run, Actor = request.Combat.GetActor(request.SourceEntityId),
                Target = request.Combat.GetActor(application.TargetEntityId), Card = request.Card, ComponentId = componentId,
                ContentRevision = request.ContentRevision, Tags = request.Tags, Variables = facts,
                CaptureOnly = true, InputQuantity = calculation.Quantity });
        if (remainder.IsFailure) return Result<EffectContinuationPlan>.Failure(remainder.Error);
        var overflow = remainder.Value.Calculation!;
        if (overflow.Value == 0) return Stop("no_overflow", overflow);
        var owner = request.Combat.GetActor(request.OwnerEntityId)!;
        var candidates = request.Combat.GetAllActors().Where(actor => actor.IsAlive &&
            !visited.Contains(actor.InstanceId, StringComparer.Ordinal) && request.Combat.Relationship(owner, actor) == SideRelationship.Enemy).ToArray();
        if (candidates.Length == 0) return Stop("no_next_target", overflow);
        // Keep the owner for relationship resolution; do not change the live world's membership.
        var filtered = request.Combat with { Actors = candidates.Append(owner).DistinctBy(actor => actor.InstanceId)
            .ToDictionary(actor => actor.InstanceId, StringComparer.Ordinal) };
        var selected = EffectTargetResolver.Resolve(filtered, filtered, request.OwnerEntityId, [],
            effect with { Target = policy.Selector, SelectionResourceId = policy.SelectionResourceId });
        if (selected.IsFailure) return Result<EffectContinuationPlan>.Failure(selected.Error);
        var nextId = selected.Value.TargetIds.Single();
        var next = effect with { Repeat = 1, Target = EffectTarget.TARGET,
            ExecutionScope = EffectExecutionScope.EveryInvocation, ExecutionGroupId = null,
            Chance = policy.RetestChance ? effect.Chance : 1,
            Condition = policy.RetestCondition ? effect.Condition : null,
            RandomInputs = policy.RollImpactInputs ? effect.RandomInputs.Where(input => input.Scope == EffectRandomScope.Impact).ToImmutableArray() : [],
            ChainedEffects = policy.CarryChildren ? effect.ChainedEffects : null,
            Parameters = effect.Parameters.Select(parameter => parameter.Parameter == EffectNumericParameter.Amount
                ? parameter with { FlatValue = null, FormulaValue = null, InputQuantityId = "continuation.budget",
                    Distribution = null, StageIds = policy.ImpactStageIds } : parameter).ToImmutableArray() };
        return Result<EffectContinuationPlan>.Success(new(trace with { ToEntityId = nextId, Overflow = overflow },
            request.Combat with { Determinism = selected.Value.Context }, next, overflow.Quantity));
    }
}

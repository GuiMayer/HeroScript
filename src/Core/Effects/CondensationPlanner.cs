using System.Collections.Immutable;
using Core.Calculations;
using Core.Combat.Models;
using Core.Common;
using Core.Determinism;
using Core.Run;

namespace Core.Effects;

public sealed record CondensationPlanResult(CombatState Combat, RunState? Run, CondensationOutcome? Outcome,
    ImmutableArray<EffectStackChange> Changes, ImmutableArray<CalculationResult> Calculations, string? SkipReason, int Work);

/// <summary>Only orchestration: authoritative stack adapters and the numeric services do the actual work.</summary>
public sealed class CondensationPlanner(ICalculationEngine calculations, StackPayloadResolver payloads)
{
    public Result<CondensationPlanResult> Plan(EffectTriggerExecutionRequest request, CombatState actionStart, RunState? runStart,
        CondensationRecipeDefinition recipe, string targetId, string procId)
    {
        var validated = CondensationRecipeValidator.Validate(recipe);
        if (validated.IsFailure) return Result<CondensationPlanResult>.Failure(validated.Error);
        var owner = recipe.OwnerBinding switch
        {
            CondensationOwnerBinding.TargetEntity => new GameplayOwner { Kind = GameplayOwnerKind.Entity, Id = targetId },
            CondensationOwnerBinding.SourceEntity => new GameplayOwner { Kind = GameplayOwnerKind.Entity, Id = request.SourceEntityId },
            CondensationOwnerBinding.Run when request.Run != null => new GameplayOwner { Kind = GameplayOwnerKind.Run, Id = request.Run.RunId.ToString() },
            CondensationOwnerBinding.Explicit => recipe.Selection.Owner,
            CondensationOwnerBinding.Any => null,
            _ => null
        };
        if (recipe.OwnerBinding == CondensationOwnerBinding.Run && request.Run == null)
            return Result<CondensationPlanResult>.Failure("Run stack selection requires a run");
        var start = recipe.SelectionTiming == CondensationSelectionTiming.ActionStart;
        var captured = AccumulatedStackTransitions.Capture(start ? actionStart : request.Combat,
            start ? runStart : request.Run, recipe.RecipeId, recipe.Selection with { Owner = owner });
        if (captured.IsFailure) return Result<CondensationPlanResult>.Failure(captured.Error);
        var selection = captured.Value;
        if (selection.Sources.IsEmpty)
            return recipe.EmptySelection == CondensationAbsencePolicy.Fail
                ? Result<CondensationPlanResult>.Failure("Condensation selection is empty") : Skip("empty_selection");
        if (selection.Sources.Any(source => source.ContentRevision != request.ContentRevision))
            return Result<CondensationPlanResult>.Failure("Condensation cannot implicitly combine content revisions");
        if (selection.Sources.Any(source => Changed(source, request.Combat, request.Run)))
            return recipe.Conflict == CondensationConflictPolicy.Fail
                ? Result<CondensationPlanResult>.Failure("Condensation captured selection changed") : Skip("selection_changed");
        var consumed = AccumulatedStackTransitions.Consume(request.Combat, request.Run, selection);
        if (consumed.IsFailure) return Result<CondensationPlanResult>.Failure(consumed.Error);
        var before = recipe.EvaluationTiming == CondensationEvaluationTiming.BeforeConsumption;
        var evaluation = request with { Combat = before ? request.Combat : consumed.Value.Combat,
            Run = before ? request.Run : consumed.Value.Run };
        var traces = ImmutableArray.CreateBuilder<CalculationResult>();
        var quantities = ImmutableSortedDictionary.CreateBuilder<string, CalculationQuantity>(StringComparer.Ordinal);
        var work = selection.Sources.Length;
        foreach (var aggregate in recipe.Aggregates.OrderBy(item => item.ParameterId, StringComparer.Ordinal))
        {
            CalculationQuantity quantity;
            var id = $"{procId}:aggregate:{aggregate.ParameterId}";
            if (aggregate.Kind == CondensationAggregateKind.StackCount)
            {
                // Exact discrete counts must remain representable by the calculator's float contract.
                if (selection.Sources.Sum(source => (long)source.Stacks) > 16_777_216)
                    return Result<CondensationPlanResult>.Failure("Condensation count exceeds exact numeric range");
                var count = calculations.Calculate(new()
                {
                    CalculationId = id, ContentRevision = request.ContentRevision, Channel = "stack_count", UnitId = "stacks", CaptureOnly = true,
                    Influences = selection.Sources.Select(source => new CalculationInfluence
                    { InfluenceId = $"{source.Store}:{source.InstanceId}", SourceId = source.StateFingerprint,
                        Bucket = "sum", Channel = "stack_count", Value = source.Stacks }).ToArray(),
                    ValuePolicy = new() { RequireInteger = true }
                }, new()
                {
                    PipelineId = "__stack_count__", Channel = "stack_count", UnitId = "stacks",
                    Stages = [new() { StageId = "stack_count" }], Buckets = [new() { BucketId = "sum", StageId = "stack_count" }]
                });
                if (count.IsFailure) return Result<CondensationPlanResult>.Failure(count.Error);
                quantity = count.Value.Quantity;
                traces.Add(count.Value);
            }
            else
            {
                if (selection.Sources.Any(source => source.PayloadLots.IsEmpty))
                    return Result<CondensationPlanResult>.Failure("Selected stack instance has no required payload");
                var lotCount = selection.Sources.Sum(source => (long)source.PayloadLots.Length);
                if (lotCount + work > EffectExecutionLimits.MaximumSteps)
                    return Result<CondensationPlanResult>.Failure("Condensation work limit exceeded");
                work += (int)lotCount;
                var lots = selection.Sources.SelectMany(source => source.PayloadLots).ToImmutableArray();
                var evaluated = payloads.Evaluate(evaluation with { StackPayloadLots = lots }, targetId, aggregate.PayloadParameterId!, id);
                if (evaluated.IsFailure) return Result<CondensationPlanResult>.Failure(evaluated.Error);
                quantity = evaluated.Value.Quantity;
                traces.AddRange(evaluated.Value.Calculations);
            }
            quantities.Add($"condensation.{aggregate.ParameterId}", quantity);
        }
        return Result<CondensationPlanResult>.Success(new(consumed.Value.Combat, consumed.Value.Run,
            new() { Selection = selection, EvaluationTiming = recipe.EvaluationTiming, Inputs = quantities.ToImmutable() },
            consumed.Value.Changes, traces.ToImmutable(), null, work + recipe.Aggregates.Length));

        Result<CondensationPlanResult> Skip(string reason) => Result<CondensationPlanResult>.Success(
            new(request.Combat, request.Run, null, [], [], reason, selection.Sources.Length));
    }

    private static bool Changed(AccumulatedStackReference source, CombatState combat, RunState? run)
    {
        if (source.Store == EffectStackStore.Status)
        {
            var status = combat.StatusEffects.GetValueOrDefault(source.Owner.Id, []).FirstOrDefault(item => item.InstanceId == source.InstanceId);
            return status == null || !status.IsActive || CanonicalJson.ComputeHash(status) != source.StateFingerprint;
        }
        var modifier = run?.Modifiers.FirstOrDefault(item => item.InstanceId == source.InstanceId);
        return modifier == null || !modifier.IsActive || CanonicalJson.ComputeHash(modifier) != source.StateFingerprint;
    }
}

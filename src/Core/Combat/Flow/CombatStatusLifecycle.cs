using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;
using Core.Math;
using Core.StatusEffects;

namespace Core.Combat.Flow;

public enum CombatStatusLifecycleEventKind { Triggered, Expired }

public sealed record CombatStatusLifecycleEvent
{
    public CombatStatusLifecycleEventKind Kind { get; init; }
    public StatusTriggerBoundary Boundary { get; init; }
    public string TargetId { get; init; } = string.Empty;
    public Guid InstanceId { get; init; }
    public string StatusId { get; init; } = string.Empty;
    public StatusEffectBehavior Behavior { get; init; }
    public float Value { get; init; }
    public int Priority { get; init; }
}

public sealed record CombatStatusLifecycleResult(
    CombatState Combat,
    IReadOnlyList<CombatStatusLifecycleEvent> Events);

public interface ICombatStatusLifecycle
{
    Result<CombatStatusLifecycleResult> Process(
        CombatState combat,
        StatusTriggerBoundary boundary,
        string? activeActorId = null);
}

/// <summary>
/// Pure lifecycle transition for statuses stored in a combat snapshot. It does
/// not use the process-wide status session store, so aliases shared by parallel
/// runs cannot leak state into each other.
/// </summary>
public sealed class CombatStatusLifecycle : ICombatStatusLifecycle
{
    private readonly IRuntimeFormulaEvaluator _formulas;

    public CombatStatusLifecycle(IRuntimeFormulaEvaluator formulas) =>
        _formulas = formulas ?? throw new ArgumentNullException(nameof(formulas));

    public Result<CombatStatusLifecycleResult> Process(
        CombatState combat,
        StatusTriggerBoundary boundary,
        string? activeActorId = null)
    {
        ArgumentNullException.ThrowIfNull(combat);
        if (boundary == StatusTriggerBoundary.Unspecified)
            return Result<CombatStatusLifecycleResult>.Failure("Status boundary is required");

        var targets = ResolveTargets(combat, boundary, activeActorId);
        if (targets.IsFailure)
            return Result<CombatStatusLifecycleResult>.Failure(targets.Error);

        var current = combat;
        var statusSnapshots = combat.StatusEffects.ToImmutableDictionary(StringComparer.Ordinal).ToBuilder();
        var events = new List<CombatStatusLifecycleEvent>();
        foreach (var targetId in targets.Value)
        {
            var actor = current.GetEntity(targetId);
            if (actor == null)
                return Result<CombatStatusLifecycleResult>.Failure($"Status lifecycle actor not found: {targetId}");
            var active = statusSnapshots.TryGetValue(targetId, out var stored) ? stored : [];
            foreach (var status in active
                         .Where(item => item.IsActive && item.Definition.TriggerBoundary == boundary)
                         .OrderByDescending(item => item.Definition.Priority)
                         .ThenBy(item => item.InstanceId))
            {
                var value = ResolveValue(status);
                if (value.IsFailure)
                    return Result<CombatStatusLifecycleResult>.Failure(value.Error);
                actor = Apply(actor, status.Definition.Behavior, value.Value);
                events.Add(new CombatStatusLifecycleEvent
                {
                    Kind = CombatStatusLifecycleEventKind.Triggered,
                    Boundary = boundary,
                    TargetId = targetId,
                    InstanceId = status.InstanceId,
                    StatusId = status.StatusId,
                    Behavior = status.Definition.Behavior,
                    Value = value.Value,
                    Priority = status.Definition.Priority
                });
            }
            current = current.ReplaceEntity(actor);

            var retained = ImmutableArray.CreateBuilder<StatusEffectInstance>();
            foreach (var status in active.OrderBy(item => item.InstanceId))
            {
                if (!status.IsActive)
                    continue;
                if (status.Duration < 0 || status.Definition.DurationTickBoundary != boundary)
                {
                    retained.Add(status);
                    continue;
                }

                var duration = checked(status.Duration - 1);
                if (duration > 0)
                {
                    retained.Add(status with { Duration = duration });
                    continue;
                }
                events.Add(new CombatStatusLifecycleEvent
                {
                    Kind = CombatStatusLifecycleEventKind.Expired,
                    Boundary = boundary,
                    TargetId = targetId,
                    InstanceId = status.InstanceId,
                    StatusId = status.StatusId,
                    Behavior = status.Definition.Behavior,
                    Priority = status.Definition.Priority
                });
            }
            if (retained.Count == 0)
                statusSnapshots.Remove(targetId);
            else
                statusSnapshots[targetId] = retained.ToImmutable();
        }

        return Result<CombatStatusLifecycleResult>.Success(new CombatStatusLifecycleResult(
            current with { StatusEffects = statusSnapshots.ToImmutable() },
            events));
    }

    private static Result<IReadOnlyList<string>> ResolveTargets(
        CombatState combat,
        StatusTriggerBoundary boundary,
        string? activeActorId)
    {
        if (boundary is StatusTriggerBoundary.StartActivation or StatusTriggerBoundary.EndActivation)
        {
            if (string.IsNullOrWhiteSpace(activeActorId))
                return Result<IReadOnlyList<string>>.Failure(
                    $"Active actor is required for status boundary {boundary}");
            return Result<IReadOnlyList<string>>.Success([activeActorId]);
        }

        var ordered = (combat.ActivationState?.ActivationOrder ?? combat.TurnOrder ?? [])
            .Concat(combat.GetAllEntities().Select(entity => entity.EntityId))
            .Distinct(StringComparer.Ordinal)
            .Where(actorId => combat.GetEntity(actorId) != null)
            .ToArray();
        return Result<IReadOnlyList<string>>.Success(ordered);
    }

    private Result<float> ResolveValue(StatusEffectInstance status)
    {
        if (string.IsNullOrWhiteSpace(status.Definition.FormulaValue))
        {
            return Result<float>.Success(
                status.Definition.BaseValue *
                (status.Definition.ScalesWithStacks ? status.Stacks : 1));
        }

        var variables = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
        {
            ["stacks"] = status.Stacks,
            ["duration"] = status.Duration
        };
        return !string.IsNullOrWhiteSpace(status.ContentRevision) &&
               _formulas is IRevisionedRuntimeFormulaEvaluator revisioned
            ? revisioned.EvaluateAtRevision(status.Definition.FormulaValue, status.ContentRevision, variables)
            : _formulas.Evaluate(status.Definition.FormulaValue, variables);
    }

    private static CombatEntity Apply(CombatEntity actor, StatusEffectBehavior behavior, float value)
    {
        if (value <= 0)
            return actor;
        var health = actor.GetResource("health");
        if (health == null)
            return actor;
        return behavior switch
        {
            StatusEffectBehavior.DAMAGE_OVER_TIME => actor.UpdateResource("health", health.Set(health.Current - value)),
            StatusEffectBehavior.HEAL_OVER_TIME => actor.UpdateResource("health", health.Gain(value)),
            _ => actor
        };
    }
}

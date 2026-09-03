using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;
using Core.Content;
using Core.Effects;
using Core.Math;
using Core.StatusEffects;

namespace Core.Combat.Flow;

public enum CombatStatusLifecycleEventKind { Triggered, Expired }

public sealed record CombatStatusLifecycleEvent
{
    private ImmutableArray<EffectApplicationRecord> _applications = [];

    public CombatStatusLifecycleEventKind Kind { get; init; }
    public StatusTriggerBoundary Boundary { get; init; }
    public string TargetId { get; init; } = string.Empty;
    public Guid InstanceId { get; init; }
    public string StatusId { get; init; } = string.Empty;
    public string? TriggerId { get; init; }
    public int Priority { get; init; }
    public IReadOnlyList<EffectApplicationRecord> Applications
    {
        get => _applications;
        init => _applications = value?.ToImmutableArray() ?? [];
    }
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
/// Pure lifecycle transition for status components pinned in a combat
/// snapshot. A status only selects when its generic effects run; resource and
/// status changes are delegated to the same immutable processor used by cards.
/// </summary>
public sealed class CombatStatusLifecycle : ICombatStatusLifecycle
{
    private readonly IRuntimeFormulaEvaluator _formulas;
    private readonly IImmutableEffectProcessor _effects;
    private readonly IContentRuntimeResolver? _contentRuntimes;

    public CombatStatusLifecycle(
        IRuntimeFormulaEvaluator formulas,
        IImmutableEffectProcessor? effects = null,
        IContentRuntimeResolver? contentRuntimes = null)
    {
        _formulas = formulas ?? throw new ArgumentNullException(nameof(formulas));
        _effects = effects ?? new ImmutableEffectProcessor();
        _contentRuntimes = contentRuntimes;
    }

    public Result<CombatStatusLifecycleResult> Process(
        CombatState combat,
        StatusTriggerBoundary boundary,
        string? activeActorId = null)
    {
        ArgumentNullException.ThrowIfNull(combat);
        if (boundary == StatusTriggerBoundary.Unspecified)
            return Result<CombatStatusLifecycleResult>.Failure("Status boundary is required");

        var targets = ResolveLifecycleTargets(combat, boundary, activeActorId);
        if (targets.IsFailure)
            return Result<CombatStatusLifecycleResult>.Failure(targets.Error);

        var current = combat;
        var events = new List<CombatStatusLifecycleEvent>();
        foreach (var targetId in targets.Value)
        {
            var activeAtBoundary = current.StatusEffects.GetValueOrDefault(targetId, [])
                .Where(status => status.IsActive)
                .OrderByDescending(status => status.Definition.Priority)
                .ThenBy(status => status.InstanceId)
                .ToArray();
            foreach (var status in activeAtBoundary)
            {
                foreach (var trigger in status.Definition.Triggers
                             .Where(item => string.Equals(
                                 item.Boundary,
                                 boundary.ToString(),
                                 StringComparison.Ordinal))
                             .OrderByDescending(item => item.Priority)
                             .ThenBy(item => item.TriggerId, StringComparer.Ordinal))
                {
                    var execution = ExecuteTrigger(current, status, trigger);
                    if (execution.IsFailure)
                        return Result<CombatStatusLifecycleResult>.Failure(execution.Error);
                    current = execution.Value.State;
                    events.Add(new CombatStatusLifecycleEvent
                    {
                        Kind = CombatStatusLifecycleEventKind.Triggered,
                        Boundary = boundary,
                        TargetId = targetId,
                        InstanceId = status.InstanceId,
                        StatusId = status.StatusId,
                        TriggerId = trigger.TriggerId,
                        Priority = trigger.Priority,
                        Applications = execution.Value.Records
                    });
                }
            }

            var initialIds = activeAtBoundary.Select(item => item.InstanceId).ToHashSet();
            var latest = current.StatusEffects.GetValueOrDefault(targetId, []);
            var retained = ImmutableArray.CreateBuilder<StatusEffectInstance>();
            foreach (var status in latest.OrderBy(item => item.InstanceId))
            {
                if (!status.IsActive)
                    continue;
                if (!initialIds.Contains(status.InstanceId) ||
                    status.Duration < 0 ||
                    status.Definition.DurationTickBoundary != boundary)
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
                    Priority = status.Definition.Priority
                });
            }
            current = retained.Count == 0
                ? current with { StatusEffects = current.StatusEffects.Remove(targetId) }
                : current with { StatusEffects = current.StatusEffects.SetItem(targetId, retained.ToImmutable()) };
        }

        return Result<CombatStatusLifecycleResult>.Success(new(current, events));
    }

    private Result<EffectBatchResult> ExecuteTrigger(
        CombatState combat,
        StatusEffectInstance status,
        EffectTriggerDefinition trigger)
    {
        if (string.IsNullOrWhiteSpace(trigger.TriggerId))
            return Result<EffectBatchResult>.Failure($"Status {status.StatusId} has a trigger without triggerId");
        var expanded = ExpandEffects(combat, status, trigger);
        if (expanded.IsFailure)
            return Result<EffectBatchResult>.Failure(expanded.Error);
        return _effects.Apply(expanded.Value.Combat, expanded.Value.Commands);
    }

    private Result<ExpandedStatusEffects> ExpandEffects(
        CombatState combat,
        StatusEffectInstance status,
        EffectTriggerDefinition trigger)
    {
        var current = combat;
        var commands = ImmutableArray.CreateBuilder<ResolvedEffectCommand>();
        foreach (var (effect, index) in trigger.Effects.Select((item, index) => (item, index)))
        {
            var expanded = ExpandEffect(current, status, trigger, effect, $"{index}");
            if (expanded.IsFailure)
                return expanded;
            current = expanded.Value.Combat;
            commands.AddRange(expanded.Value.Commands);
        }
        return Result<ExpandedStatusEffects>.Success(new(current, commands.ToImmutable()));
    }

    private Result<ExpandedStatusEffects> ExpandEffect(
        CombatState combat,
        StatusEffectInstance status,
        EffectTriggerDefinition trigger,
        EffectDefinition effect,
        string path)
    {
        if (effect.Repeat < 1)
            return Result<ExpandedStatusEffects>.Failure($"Status effect {path} repeat must be positive");
        if (effect.Chance is < 0 or > 1)
            return Result<ExpandedStatusEffects>.Failure($"Status effect {path} chance must be between 0 and 1");

        var current = combat;
        var context = combat.Determinism;
        var commands = ImmutableArray.CreateBuilder<ResolvedEffectCommand>();
        for (var repeat = 0; repeat < effect.Repeat; repeat++)
        {
            if (effect.Chance < 1)
            {
                if (effect.Chance <= 0)
                    continue;
                var chance = context.DrawDouble();
                context = chance.Context;
                if (chance.Value >= effect.Chance)
                    continue;
            }
            var targets = ResolveEffectTargets(current, status.TargetId, effect.Target, context);
            if (targets.IsFailure)
                return Result<ExpandedStatusEffects>.Failure(targets.Error);
            context = targets.Value.Context;
            foreach (var targetId in targets.Value.TargetIds)
            {
                var variables = BuildVariables(current, status, targetId);
                var condition = EvaluateCondition(effect.Condition, status, variables);
                if (condition.IsFailure)
                    return Result<ExpandedStatusEffects>.Failure(condition.Error);
                if (!condition.Value)
                    continue;
                var value = ResolveValue(effect, status, variables);
                if (value.IsFailure)
                    return Result<ExpandedStatusEffects>.Failure(value.Error);
                var statusDefinition = ResolveAppliedStatus(effect, status);
                if (statusDefinition.IsFailure)
                    return Result<ExpandedStatusEffects>.Failure(statusDefinition.Error);
                commands.Add(new ResolvedEffectCommand
                {
                    EffectInstanceId = $"{status.InstanceId:N}:{trigger.TriggerId}:{path}:{repeat}:{targetId}",
                    Definition = effect,
                    SourceEntityId = status.SourceId ?? status.TargetId,
                    TargetEntityIds = [targetId],
                    ResolvedValue = value.Value,
                    StatusDefinition = statusDefinition.Value,
                    Provenance = new EffectProvenance
                    {
                        Kind = EffectProvenanceKind.Status,
                        SourceId = status.InstanceId.ToString(),
                        ComponentId = trigger.TriggerId
                    }
                });
            }
        }
        current = current with { Determinism = context };
        foreach (var (nested, index) in (effect.ChainedEffects ?? [])
                     .Concat(effect.ConditionalEffects ?? [])
                     .Select((item, index) => (item, index)))
        {
            var child = ExpandEffect(current, status, trigger, nested, $"{path}.{index}");
            if (child.IsFailure)
                return child;
            current = child.Value.Combat;
            commands.AddRange(child.Value.Commands);
        }
        return Result<ExpandedStatusEffects>.Success(new(current, commands.ToImmutable()));
    }

    private Result<StatusEffectDefinition?> ResolveAppliedStatus(
        EffectDefinition effect,
        StatusEffectInstance owner)
    {
        if (effect.Type != EffectType.APPLY_STATUS)
            return Result<StatusEffectDefinition?>.Success(null);
        if (string.IsNullOrWhiteSpace(effect.StatusId))
            return Result<StatusEffectDefinition?>.Failure("APPLY_STATUS requires statusId");
        if (_contentRuntimes == null || string.IsNullOrWhiteSpace(owner.ContentRevision))
            return Result<StatusEffectDefinition?>.Failure(
                $"Status trigger cannot resolve applied status {effect.StatusId} without pinned content");
        var runtime = _contentRuntimes.Resolve(owner.ContentRevision);
        if (runtime.IsFailure)
            return Result<StatusEffectDefinition?>.Failure(runtime.Error);
        var definition = runtime.Value.GetDefinition<StatusEffectDefinition>("status-effects", effect.StatusId);
        return definition.IsFailure
            ? Result<StatusEffectDefinition?>.Failure(definition.Error)
            : Result<StatusEffectDefinition?>.Success(definition.Value);
    }

    private Result<float> ResolveValue(
        EffectDefinition effect,
        StatusEffectInstance status,
        Dictionary<string, float> variables)
    {
        if (effect.Type is not (EffectType.DAMAGE or EffectType.HEAL or EffectType.MODIFY_RESOURCE))
            return Result<float>.Success(0);
        var value = effect.FlatValue ?? 0;
        if (string.IsNullOrWhiteSpace(effect.FormulaValue))
            return Result<float>.Success(value);
        var evaluated = Evaluate(effect.FormulaValue, status, variables);
        return evaluated.IsFailure
            ? evaluated
            : Result<float>.Success(value + evaluated.Value);
    }

    private Result<bool> EvaluateCondition(
        string? expression,
        StatusEffectInstance status,
        Dictionary<string, float> variables)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return Result<bool>.Success(true);
        var result = Evaluate(expression, status, variables);
        return result.IsFailure
            ? Result<bool>.Failure(result.Error)
            : Result<bool>.Success(result.Value > 0);
    }

    private Result<float> Evaluate(
        string expression,
        StatusEffectInstance status,
        Dictionary<string, float> variables) =>
        !string.IsNullOrWhiteSpace(status.ContentRevision) &&
        _formulas is IRevisionedRuntimeFormulaEvaluator revisioned
            ? revisioned.EvaluateAtRevision(expression, status.ContentRevision, variables)
            : _formulas.Evaluate(expression, variables);

    private static Dictionary<string, float> BuildVariables(
        CombatState combat,
        StatusEffectInstance status,
        string targetId)
    {
        var target = combat.GetEntity(targetId)!;
        var source = combat.GetEntity(status.SourceId ?? string.Empty) ?? combat.GetEntity(status.TargetId)!;
        var variables = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            ["stacks"] = status.Stacks,
            ["duration"] = status.Duration
        };
        AddResources(variables, "source", source);
        AddResources(variables, "target", target);
        return variables;
    }

    private static void AddResources(
        IDictionary<string, float> variables,
        string prefix,
        CombatEntity entity)
    {
        foreach (var (id, pool) in entity.ResourceState.Resources.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            variables[$"{prefix}_{id}_current"] = pool.Current;
            variables[$"{prefix}_{id}_max"] = pool.Maximum;
            variables[$"{prefix}_{id}_min"] = pool.Minimum;
        }
    }

    private static Result<ResolvedTargets> ResolveEffectTargets(
        CombatState combat,
        string ownerId,
        EffectTarget target,
        Core.Determinism.DeterministicContext context)
    {
        var owner = combat.GetEntity(ownerId);
        if (owner == null)
            return Result<ResolvedTargets>.Failure($"Status owner not found: {ownerId}");
        var candidates = target switch
        {
            EffectTarget.SELF or EffectTarget.TARGET => [ownerId],
            EffectTarget.ALL_ENEMIES => combat.GetAllEntities()
                .Where(entity => entity.IsAlive && entity.IsHero != owner.IsHero)
                .OrderBy(entity => entity.EntityId, StringComparer.Ordinal)
                .Select(entity => entity.EntityId)
                .ToArray(),
            EffectTarget.ALL_ALLIES => combat.GetAllEntities()
                .Where(entity => entity.IsAlive && entity.IsHero == owner.IsHero)
                .OrderBy(entity => entity.EntityId, StringComparer.Ordinal)
                .Select(entity => entity.EntityId)
                .ToArray(),
            EffectTarget.RANDOM_ENEMY => combat.GetAllEntities()
                .Where(entity => entity.IsAlive && entity.IsHero != owner.IsHero)
                .OrderBy(entity => entity.EntityId, StringComparer.Ordinal)
                .Select(entity => entity.EntityId)
                .ToArray(),
            _ => []
        };
        if (candidates.Length == 0)
            return Result<ResolvedTargets>.Failure($"Status effect target {target} resolved no entities");
        if (target != EffectTarget.RANDOM_ENEMY)
            return Result<ResolvedTargets>.Success(new(candidates, context));
        var draw = context.DrawInt32(candidates.Length);
        return Result<ResolvedTargets>.Success(new([candidates[draw.Value]], draw.Context));
    }

    private static Result<IReadOnlyList<string>> ResolveLifecycleTargets(
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
        return Result<IReadOnlyList<string>>.Success(
            (combat.ActivationState?.ActivationOrder ?? combat.TurnOrder ?? [])
            .Concat(combat.GetAllEntities().Select(entity => entity.EntityId))
            .Distinct(StringComparer.Ordinal)
            .Where(actorId => combat.GetEntity(actorId) != null)
            .ToArray());
    }

    private sealed record ExpandedStatusEffects(
        CombatState Combat,
        ImmutableArray<ResolvedEffectCommand> Commands);

    private sealed record ResolvedTargets(
        IReadOnlyList<string> TargetIds,
        Core.Determinism.DeterministicContext Context);
}

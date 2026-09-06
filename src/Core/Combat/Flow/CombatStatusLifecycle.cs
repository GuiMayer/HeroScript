using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;
using Core.Effects;
using Core.StatusEffects;
using Core.Run;

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
    public ImmutableArray<EffectExecutionStep> Steps { get; init; } = [];
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
        RunState run,
        CombatState combat,
        StatusTriggerBoundary boundary,
        string? activeActorId = null);
}

/// <summary>
/// Owns only status timing and duration. Trigger interpretation and state
/// mutation belong to the source-agnostic trigger/effect pipeline.
/// </summary>
public sealed class CombatStatusLifecycle : ICombatStatusLifecycle
{
    private readonly IEffectTriggerExecutor _triggers;

    public CombatStatusLifecycle(IEffectTriggerExecutor triggers) =>
        _triggers = triggers ?? throw new ArgumentNullException(nameof(triggers));

    public Result<CombatStatusLifecycleResult> Process(
        RunState run,
        CombatState combat,
        StatusTriggerBoundary boundary,
        string? activeActorId = null)
    {
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(run);
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
                    var executed = _triggers.Execute(new EffectTriggerExecutionRequest
                    {
                        Combat = current,
                        Run = run,
                        Trigger = trigger,
                        OwnerEntityId = status.TargetId,
                        SourceEntityId = status.SourceId ?? status.TargetId,
                        ContentRevision = status.ContentRevision ?? current.Determinism.ContentRevision,
                        Variables = new Dictionary<string, float>(StringComparer.Ordinal)
                        {
                            ["stacks"] = status.Stacks,
                            ["duration"] = status.Duration
                        },
                        Provenance = new EffectProvenance
                        {
                            Kind = EffectProvenanceKind.Status,
                            SourceId = status.InstanceId.ToString()
                        }
                    });
                    if (executed.IsFailure)
                        return Result<CombatStatusLifecycleResult>.Failure(
                            $"Status {status.StatusId}/{trigger.TriggerId}: {executed.Error}");
                    current = executed.Value.State;
                    events.Add(new CombatStatusLifecycleEvent
                    {
                        Kind = CombatStatusLifecycleEventKind.Triggered,
                        Boundary = boundary,
                        TargetId = targetId,
                        InstanceId = status.InstanceId,
                        StatusId = status.StatusId,
                        TriggerId = trigger.TriggerId,
                        Priority = trigger.Priority,
                        Applications = executed.Value.Records,
                        Steps = executed.Value.Steps
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
}

using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;
using Core.Effects;
using Core.StatusEffects;
using Core.Run;
using Core.GameplayEvents.Responses;

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
    IReadOnlyList<CombatStatusLifecycleEvent> Events,
    RunState? Run = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    internal Core.GameplayEvents.GameplayEffectObservation? Observation { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    internal ImmutableArray<EffectExecutionStep> ResponseSteps { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    internal ImmutableArray<EffectApplicationRecord> ResponseApplications { get; init; } = [];
}

public interface ICombatStatusLifecycle
{
    Result<CombatStatusLifecycleResult> Process(
        RunState run,
        CombatState combat,
        StatusTriggerBoundary boundary,
        string? activeActorId = null);
    Result<Core.GameplayEvents.ObservedStatusLifecycleExecution> ProcessObserved(RunState run, CombatState combat, StatusTriggerBoundary boundary, Core.GameplayEvents.GameplayEffectObservation observation, string? activeActorId = null) =>
        Result<Core.GameplayEvents.ObservedStatusLifecycleExecution>.Failure("This implementation does not support gameplay shadow observation");

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
        string? activeActorId = null) => ProcessCore(run, combat, boundary, activeActorId, null);

    public Result<Core.GameplayEvents.ObservedStatusLifecycleExecution> ProcessObserved(
        RunState run, CombatState combat, StatusTriggerBoundary boundary,
        Core.GameplayEvents.GameplayEffectObservation observation, string? activeActorId = null)
    {
        if (observation?.Events == null || observation.Events.Scope.ContentRevision != run.Determinism.ContentRevision)
            return Result<Core.GameplayEvents.ObservedStatusLifecycleExecution>.Failure("Status observation requires the captured combat revision");
        var result = ProcessCore(run, combat, boundary, activeActorId, observation);
        return result.IsFailure ? Result<Core.GameplayEvents.ObservedStatusLifecycleExecution>.Failure(result.Error)
            : Result<Core.GameplayEvents.ObservedStatusLifecycleExecution>.Success(new(result.Value, result.Value.Observation!));
    }

    private Result<CombatStatusLifecycleResult> ProcessCore(RunState run, CombatState combat, StatusTriggerBoundary boundary,
        string? activeActorId, Core.GameplayEvents.GameplayEffectObservation? observation)
    {
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(run);
        if (boundary == StatusTriggerBoundary.Unspecified)
            return Result<CombatStatusLifecycleResult>.Failure("Status boundary is required");
        var targets = ResolveLifecycleTargets(combat, boundary, activeActorId);
        if (targets.IsFailure)
            return Result<CombatStatusLifecycleResult>.Failure(targets.Error);

        if (_triggers is IGameplayOccurrenceExecutor prepare)
        {
            var prepared = prepare.PrepareOccurrenceObservation(run, combat, observation);
            if (prepared.IsFailure) return Result<CombatStatusLifecycleResult>.Failure(prepared.Error);
            observation = prepared.Value;
        }
        var current = combat;
        var responseSteps = ImmutableArray.CreateBuilder<EffectExecutionStep>();
        var responseApplications = ImmutableArray.CreateBuilder<EffectApplicationRecord>();
        var events = new List<CombatStatusLifecycleEvent>();
        var boundarySnapshot = observation?.BoundaryStatusSnapshot ?? combat.StatusEffects;
        IGameplayBoundaryExecutionScope? scope = null;
        if (boundarySnapshot.Values.SelectMany(items => items).Any(status => status.IsActive && status.Definition.Triggers.Count > 0))
        {
            var started = _triggers.BeginBoundaryScope(run, combat, observation);
            if (started.IsFailure) return Result<CombatStatusLifecycleResult>.Failure(started.Error);
            scope = started.Value;
        }
        foreach (var targetId in targets.Value)
        {
            var activeAtBoundary = boundarySnapshot.GetValueOrDefault(targetId, [])
                .Where(status => status.IsActive)
                .OrderByDescending(status => status.Definition.Priority)
                .ThenBy(status => status.InstanceId)
                .ToArray();
            if (scope != null)
            {
                var invocations = GameplayBoundarySources.Status(run, current, activeAtBoundary);
                var dispatched = scope.Dispatch(run, current, GameplayBoundarySources.Kind(boundary.ToString()), boundary.ToString(),
                    activeActorId, invocations, observation);
                if (dispatched.IsFailure) return Result<CombatStatusLifecycleResult>.Failure(dispatched.Error);
                current = dispatched.Value.Execution.State; run = dispatched.Value.Execution.Run ?? run;
                observation = dispatched.Value.Observation;
                foreach (var frame in dispatched.Value.Invocations)
                {
                    var status = activeAtBoundary.Single(item => item.InstanceId == frame.Invocation.StatusInstanceId);
                    var trigger = frame.Invocation.Template.Trigger;
                    events.Add(new() { Kind = CombatStatusLifecycleEventKind.Triggered, Boundary = boundary, TargetId = targetId,
                        InstanceId = status.InstanceId, StatusId = status.StatusId, TriggerId = trigger.TriggerId, Priority = trigger.Priority,
                        Applications = frame.Execution.Records, Steps = frame.Execution.Steps });
                }
            }

            var beforeTicks = current; var beforeTickRun = run; var beforeTickObservation = observation;
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
                    var updated = status with { Duration = duration };
                    if (observation != null)
                    {
                        var observed = Core.GameplayEvents.GameplayStatusObservation.Tick(observation, status, updated);
                        if (observed.IsFailure) return Result<CombatStatusLifecycleResult>.Failure(observed.Error);
                        observation = observed.Value;
                    }
                    retained.Add(updated);
                    continue;
                }
                if (observation != null)
                {
                    var observed = Core.GameplayEvents.GameplayStatusObservation.Tick(observation, status, null);
                    if (observed.IsFailure) return Result<CombatStatusLifecycleResult>.Failure(observed.Error);
                    observation = observed.Value;
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
            if (_triggers is IGameplayOccurrenceExecutor dispatcher && beforeTickObservation != null && observation != null &&
                beforeTickObservation.Events.Occurrences.Count != observation.Events.Occurrences.Count)
            {
                var dispatched = dispatcher.DispatchOccurrencesObserved(beforeTickRun, beforeTicks, run, current, beforeTickObservation, observation);
                if (dispatched.IsFailure) return Result<CombatStatusLifecycleResult>.Failure(dispatched.Error);
                current = dispatched.Value.Execution.State; run = dispatched.Value.Execution.Run ?? run;
                observation = dispatched.Value.Observation;
                responseSteps.AddRange(dispatched.Value.Execution.Steps); responseApplications.AddRange(dispatched.Value.Execution.Records);
            }
        }
        return Result<CombatStatusLifecycleResult>.Success(new(current, events, run) { Observation = observation,
            ResponseSteps = responseSteps.ToImmutable(), ResponseApplications = responseApplications.ToImmutable() });

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
            (combat.ActivationState?.ActivationOrder ?? combat.TurnOrderState.Order)
            .Concat(combat.GetAllActors().Select(entity => entity.InstanceId))
            .Distinct(StringComparer.Ordinal)
            .Where(actorId => combat.GetActor(actorId) != null)
            .ToArray());
    }
}

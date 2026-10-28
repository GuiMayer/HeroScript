using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;
using Core.Effects;
using Core.Run;
using Core.GameplayEvents.Responses;

namespace Core.Combat.Flow;

public static class CombatTriggerBoundaries
{
    public const string CombatStart = "CombatStart";
    public const string CombatEnd = "CombatEnd";
}

public sealed record CombatRelicLifecycleEvent
{
    private ImmutableArray<EffectApplicationRecord> _applications = [];

    public Guid RelicInstanceId { get; init; }
    public string RelicId { get; init; } = string.Empty;
    public string TriggerId { get; init; } = string.Empty;
    public string Boundary { get; init; } = string.Empty;
    public GameplayOwner Owner { get; init; } = new();
    public ImmutableArray<EffectExecutionStep> Steps { get; init; } = [];
    public IReadOnlyList<EffectApplicationRecord> Applications
    {
        get => _applications;
        init => _applications = value?.ToImmutableArray() ?? [];
    }
}

public sealed record CombatRelicLifecycleResult(
    CombatState Combat,
    IReadOnlyList<CombatRelicLifecycleEvent> Events,
    RunState? Run = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    internal Core.GameplayEvents.GameplayEffectObservation? Observation { get; init; }
}

public interface ICombatRelicLifecycle
{
    Result<CombatRelicLifecycleResult> Process(
        RunState run,
        CombatState combat,
        string boundary);
    Result<Core.GameplayEvents.ObservedGameplayExecution<CombatRelicLifecycleResult>> ProcessObserved(RunState run, CombatState combat, string boundary, Core.GameplayEvents.GameplayEffectObservation observation) =>
        Result<Core.GameplayEvents.ObservedGameplayExecution<CombatRelicLifecycleResult>>.Failure("This implementation does not support gameplay shadow observation");

}

/// <summary>Executes relic triggers pinned in the immutable run state.</summary>
public sealed class CombatRelicLifecycle : ICombatRelicLifecycle
{
    private readonly IEffectTriggerExecutor _triggers;

    public CombatRelicLifecycle(IEffectTriggerExecutor triggers) =>
        _triggers = triggers ?? throw new ArgumentNullException(nameof(triggers));

    public Result<CombatRelicLifecycleResult> Process(
        RunState run,
        CombatState combat,
        string boundary) => ProcessCore(run, combat, boundary, null);

    public Result<Core.GameplayEvents.ObservedGameplayExecution<CombatRelicLifecycleResult>> ProcessObserved(
        RunState run, CombatState combat, string boundary, Core.GameplayEvents.GameplayEffectObservation observation)
    {
        if (observation?.Events == null || observation.Events.Scope.ContentRevision != run.Determinism.ContentRevision)
            return Result<Core.GameplayEvents.ObservedGameplayExecution<CombatRelicLifecycleResult>>.Failure("Relic observation requires the captured run revision");
        var result = ProcessCore(run, combat, boundary, observation);
        return result.IsFailure ? Result<Core.GameplayEvents.ObservedGameplayExecution<CombatRelicLifecycleResult>>.Failure(result.Error)
            : Result<Core.GameplayEvents.ObservedGameplayExecution<CombatRelicLifecycleResult>>.Success(new(result.Value, result.Value.Observation!));
    }

    private Result<CombatRelicLifecycleResult> ProcessCore(RunState run, CombatState combat, string boundary,
        Core.GameplayEvents.GameplayEffectObservation? observation)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(combat);
        if (string.IsNullOrWhiteSpace(boundary))
            return Result<CombatRelicLifecycleResult>.Failure("Relic boundary is required");
        var once = boundary is CombatTriggerBoundaries.CombatStart or CombatTriggerBoundaries.CombatEnd;
        if (once && combat.CompletedLifecycleBoundaries.Contains(boundary))
            return Result<CombatRelicLifecycleResult>.Success(new(combat, [], run) { Observation = observation });

        var current = combat;
        var events = new List<CombatRelicLifecycleEvent>();
        var invocations = GameplayBoundarySources.Relics(run, combat);
        if (!invocations.IsEmpty)
        {
            var started = _triggers.BeginBoundaryScope(run, combat, observation);
            if (started.IsFailure) return Result<CombatRelicLifecycleResult>.Failure(started.Error);
            var dispatched = started.Value.Dispatch(run, current, GameplayBoundarySources.Kind(boundary), boundary,
                combat.ActivationState?.ActiveActorId, invocations, observation);
            if (dispatched.IsFailure) return Result<CombatRelicLifecycleResult>.Failure(dispatched.Error);
            current = dispatched.Value.Execution.State; run = dispatched.Value.Execution.Run ?? run;
            observation = dispatched.Value.Observation;
            foreach (var frame in dispatched.Value.Invocations)
            {
                var relicId = Guid.Parse(frame.Invocation.Origin.ContainerId);
                var relic = run.Relics.Single(item => item.RelicInstanceId == relicId);
                events.Add(new() { RelicInstanceId = relicId, RelicId = relic.DefinitionId, TriggerId = frame.Invocation.Template.Trigger.TriggerId,
                    Boundary = boundary, Owner = relic.Owner, Applications = frame.Execution.Records, Steps = frame.Execution.Steps });
            }
        }
        if (once) current = current with { CompletedLifecycleBoundaries = current.CompletedLifecycleBoundaries.Add(boundary) };
        return Result<CombatRelicLifecycleResult>.Success(new(current, events, run) { Observation = observation });

    }
}

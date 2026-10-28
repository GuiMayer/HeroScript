using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;
using Core.Determinism;
using Core.Effects;
using Core.Resources;
using Core.Run;

namespace Core.Combat.Flow;

public sealed record CombatResourceLifecycleResult
{
    [System.Text.Json.Serialization.JsonIgnore]
    internal Core.GameplayEvents.GameplayEffectObservation? Observation { get; init; }

    private ImmutableArray<EffectApplicationRecord> _records = [];
    public CombatState Combat { get; init; } = null!;
    public RunState? Run { get; init; }
    public IReadOnlyList<EffectApplicationRecord> Records
    {
        get => _records;
        init => _records = value?.ToImmutableArray() ?? [];
    }
    public ImmutableArray<EffectExecutionStep> Steps { get; init; } = [];
    public string Fingerprint { get; init; } = string.Empty;
}

public interface ICombatResourceLifecycle
{
    Result<CombatResourceLifecycleResult> Process(
        RunState run, CombatState combat, string actorId, RegenerationTiming timing,
        IReadOnlySet<string>? excludedResourceIds = null);
}

/// <summary>Adapts authored resource timing to ordinary effects. All calculations and mutations belong to the shared transaction executor.</summary>
public sealed class CombatResourceLifecycle(IEffectTriggerExecutor triggers) : ICombatResourceLifecycle
{
    public Result<CombatResourceLifecycleResult> Process(
        RunState run, CombatState combat, string actorId, RegenerationTiming timing,
        IReadOnlySet<string>? excludedResourceIds = null) => ProcessCore(run, combat, actorId, timing, excludedResourceIds, null);

    public Result<Core.GameplayEvents.ObservedGameplayExecution<CombatResourceLifecycleResult>> ProcessObserved(
        RunState run, CombatState combat, string actorId, RegenerationTiming timing,
        Core.GameplayEvents.GameplayEffectObservation observation, IReadOnlySet<string>? excludedResourceIds = null)
    {
        if (observation?.Events == null || observation.Events.Scope.ContentRevision != run.Determinism.ContentRevision)
            return Result<Core.GameplayEvents.ObservedGameplayExecution<CombatResourceLifecycleResult>>.Failure("Resource observation requires the captured run revision");
        var result = ProcessCore(run, combat, actorId, timing, excludedResourceIds, observation);
        return result.IsFailure ? Result<Core.GameplayEvents.ObservedGameplayExecution<CombatResourceLifecycleResult>>.Failure(result.Error)
            : Result<Core.GameplayEvents.ObservedGameplayExecution<CombatResourceLifecycleResult>>.Success(new(result.Value, result.Value.Observation!));
    }

    private Result<CombatResourceLifecycleResult> ProcessCore(RunState run, CombatState combat, string actorId,
        RegenerationTiming timing, IReadOnlySet<string>? excludedResourceIds, Core.GameplayEvents.GameplayEffectObservation? observation)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(combat);
        if (!Enum.IsDefined(timing))
            return Result<CombatResourceLifecycleResult>.Failure($"Unsupported resource lifecycle timing: {timing}");
        var actor = combat.GetActor(actorId);
        if (actor == null)
            return Result<CombatResourceLifecycleResult>.Failure($"Resource lifecycle actor not found: {actorId}");
        var excluded = excludedResourceIds?.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase)
            ?? ImmutableHashSet<string>.Empty;
        var current = combat;
        var currentRun = run;
        var records = ImmutableArray.CreateBuilder<EffectApplicationRecord>();
        var steps = ImmutableArray.CreateBuilder<EffectExecutionStep>();
        // Selection of rules is fixed at entry. Values observe previous effects in this boundary.
        foreach (var (resourceId, pool) in actor.ResourceState.Resources.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var regeneration = pool.Definition?.Regeneration;
            if (excluded.Contains(resourceId) || regeneration is not { Enabled: true } || regeneration.Timing != timing)
                continue;
            var source = $"resource:{resourceId}:regeneration";
            var applied = ExecuteTrigger(new EffectTriggerExecutionRequest
            {
                Run = currentRun, Combat = current, SourceEntityId = actorId, OwnerEntityId = actorId,
                ContentRevision = run.Determinism.ContentRevision,
                Provenance = new() { Kind = EffectProvenanceKind.Rule, SourceId = source, ComponentId = timing.ToString() },
                Trigger = new() { TriggerId = source, Boundary = timing.ToString(), Effects = [regeneration.ToEffect(resourceId)] }
            });
            if (applied.IsFailure)
                return Result<CombatResourceLifecycleResult>.Failure($"Resource lifecycle {resourceId}: {applied.Error}");
            current = applied.Value.State;
            currentRun = applied.Value.Run ?? currentRun;
            records.AddRange(applied.Value.Records);
            foreach (var step in applied.Value.Steps) steps.Add(step with { Index = steps.Count });
        }
        return Result<CombatResourceLifecycleResult>.Success(new()
        {
            Combat = current, Run = currentRun, Records = records.ToImmutable(), Steps = steps.ToImmutable(), Observation = observation,
            Fingerprint = CanonicalJson.ComputeObjectHash(CanonicalJson.Property("combat", current),
                CanonicalJson.Property("run", currentRun), CanonicalJson.Property("steps", steps.ToImmutable()))
        });

        Result<EffectBatchResult> ExecuteTrigger(EffectTriggerExecutionRequest request)
        {
            if (observation == null) return triggers.Execute(request);
            var observed = triggers.ExecuteObserved(request, observation);
            if (observed.IsFailure) return Result<EffectBatchResult>.Failure(observed.Error);
            observation = observed.Value.Observation;
            return Result<EffectBatchResult>.Success(observed.Value.Execution);
        }
    }
}

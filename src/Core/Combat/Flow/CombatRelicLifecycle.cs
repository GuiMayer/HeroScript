using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;
using Core.Effects;
using Core.Run;

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
    RunState? Run = null);

public interface ICombatRelicLifecycle
{
    Result<CombatRelicLifecycleResult> Process(
        RunState run,
        CombatState combat,
        string boundary);
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
        string boundary)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(combat);
        if (string.IsNullOrWhiteSpace(boundary))
            return Result<CombatRelicLifecycleResult>.Failure("Relic boundary is required");
        var once = boundary is CombatTriggerBoundaries.CombatStart or CombatTriggerBoundaries.CombatEnd;
        if (once && combat.CompletedLifecycleBoundaries.Contains(boundary))
            return Result<CombatRelicLifecycleResult>.Success(new(combat, [], run));

        var current = combat;
        var events = new List<CombatRelicLifecycleEvent>();
        foreach (var relic in run.Relics.OrderBy(item => item.RelicInstanceId))
        {
            foreach (var trigger in relic.Triggers
                         .Where(item => string.Equals(item.Boundary, boundary, StringComparison.Ordinal))
                         .OrderByDescending(item => item.Priority)
                         .ThenBy(item => item.TriggerId, StringComparer.Ordinal))
            {
                var owners = current.GetAllActors().Where(entity => relic.Owner.Includes(entity, current, run))
                    .Where(entity => boundary is not ("StartActivation" or "EndActivation") ||
                        entity.InstanceId == current.ActivationState?.ActiveActorId)
                    .OrderBy(entity => entity.InstanceId, StringComparer.Ordinal).ToArray();
                foreach (var owner in owners)
                {
                var executed = _triggers.Execute(new EffectTriggerExecutionRequest
                {
                    Combat = current,
                    Run = run,
                    Trigger = trigger,
                    OwnerEntityId = owner.InstanceId,
                    SourceEntityId = owner.InstanceId,
                    ContentRevision = relic.ContentRevision,
                    Variables = new Dictionary<string, float>(StringComparer.Ordinal)
                    {
                        ["stacks"] = relic.Stacks
                    },
                    Provenance = new EffectProvenance
                    {
                        Kind = EffectProvenanceKind.Relic,
                        SourceId = relic.RelicInstanceId.ToString()
                    }
                });
                if (executed.IsFailure)
                    return Result<CombatRelicLifecycleResult>.Failure(
                        $"Relic {relic.DefinitionId}/{trigger.TriggerId}: {executed.Error}");
                current = executed.Value.State;
                run = executed.Value.Run ?? run;
                events.Add(new CombatRelicLifecycleEvent
                {
                    RelicInstanceId = relic.RelicInstanceId,
                    RelicId = relic.DefinitionId,
                    TriggerId = trigger.TriggerId,
                    Boundary = boundary,
                    Owner = relic.Owner,
                    Applications = executed.Value.Records,
                    Steps = executed.Value.Steps
                });
                }
            }
        }
        if (once) current = current with { CompletedLifecycleBoundaries = current.CompletedLifecycleBoundaries.Add(boundary) };
        return Result<CombatRelicLifecycleResult>.Success(new(current, events, run));
    }
}

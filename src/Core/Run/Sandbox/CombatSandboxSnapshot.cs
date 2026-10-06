using System.Collections.Immutable;
using Core.Combat;
using Core.Combat.Models;
using Core.Combat.Modifiers;
using Core.Combat.Reactions;
using Core.Common;
using Core.Determinism;
using Core.Run.Content;
using Core.StatusEffects;
using Core.CardZones;

namespace Core.Run.Sandbox;

public sealed record SandboxCombatSnapshot
{
    private ImmutableArray<SandboxCardSnapshot> _playableCards = [];

    public SandboxRunSnapshot Run { get; init; } = new();
    public SandboxCombatSnapshotState Combat { get; init; } = new();
    public IReadOnlyList<SandboxCardSnapshot> PlayableCards
    {
        get => _playableCards;
        init => _playableCards = value?.ToImmutableArray() ?? [];
    }
}

public sealed record SandboxRunSnapshot
{
    public Guid RunId { get; init; }
    public int Sequence { get; init; }
    public ulong Step { get; init; }
    public string StateHash { get; init; } = string.Empty;
    public string ContentRevision { get; init; } = string.Empty;
    public string? ScenarioHash { get; init; }
    public string? AttemptKey { get; init; }
    public CombatScenarioDefinition? Scenario { get; init; }
}

public sealed record SandboxCombatSnapshotState
{
    private ImmutableArray<SandboxActorSnapshot> _actors = [];

    public Guid CombatId { get; init; }
    public ulong Step { get; init; }
    public string StateHash { get; init; } = string.Empty;
    public int Turn { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? ActiveActorId { get; init; }
    public object? Phase { get; init; }
    public object? Activation { get; init; }
    public PriorityWindowState? PriorityWindow { get; init; }
    public ImmutableArray<PendingActionState> PendingActions { get; init; } = [];
    public CombatBoardState Board { get; init; } = new();
    public IReadOnlyList<SandboxActorSnapshot> Actors
    {
        get => _actors;
        init => _actors = value?.ToImmutableArray() ?? [];
    }
}

public sealed record SandboxActorSnapshot
{
    private ImmutableDictionary<string, SandboxResourceSnapshot> _resources =
        ImmutableDictionary<string, SandboxResourceSnapshot>.Empty.WithComparers(StringComparer.Ordinal);
    private ImmutableArray<StatusEffectInstance> _statuses = [];
    private ImmutableArray<ScriptModifierInstance> _modifiers = [];

    public string InstanceId { get; init; } = string.Empty;
    public string DefinitionId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string SideId { get; init; } = string.Empty;
    public ControllerBinding ControllerBinding { get; init; } = new();
    public bool IsAlive { get; init; }
    public IReadOnlyDictionary<string, SandboxResourceSnapshot> Resources
    {
        get => _resources;
        init => _resources = value?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, SandboxResourceSnapshot>.Empty.WithComparers(StringComparer.Ordinal);
    }
    public IReadOnlyList<StatusEffectInstance> Statuses
    {
        get => _statuses;
        init => _statuses = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<ScriptModifierInstance> Modifiers
    {
        get => _modifiers;
        init => _modifiers = value?.ToImmutableArray() ?? [];
    }
}

public sealed record SandboxResourceSnapshot(float Current, float Maximum, float Minimum);

public sealed record SandboxCardSnapshot
{
    private ImmutableArray<CardUpgradeState> _upgrades = [];
    private ImmutableArray<CardUpgradeState> _transformationLedger = [];

    public Guid CardInstanceId { get; init; }
    public string DefinitionId { get; init; } = string.Empty;
    public int PlayableIndex { get; init; }
    public string ZoneId { get; init; } = string.Empty;
    public string ZoneOwnerId { get; init; } = string.Empty;
    public IReadOnlyList<CardUpgradeState> Upgrades
    {
        get => _upgrades;
        init => _upgrades = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<CardUpgradeState> TransformationLedger
    {
        get => _transformationLedger;
        init => _transformationLedger = value?.ToImmutableArray() ?? [];
    }
}

public interface ICombatSandboxSnapshotService
{
    Result<SandboxCombatSnapshot> Get(Guid runId);
}

/// <summary>
/// Read-only projection tailored to a visual client. It is built exclusively
/// from the authoritative run/combat state; no combat rule is reproduced here.
/// </summary>
public sealed class CombatSandboxSnapshotService : ICombatSandboxSnapshotService
{
    private readonly IRunQueryService _runs;

    public CombatSandboxSnapshotService(IRunQueryService runs) => _runs = runs;

    public Result<SandboxCombatSnapshot> Get(Guid runId)
    {
        var run = _runs.GetRun(runId);
        if (run.IsFailure)
            return Result<SandboxCombatSnapshot>.Failure(run.Error);
        if (run.Value.Scenario == null)
            return Result<SandboxCombatSnapshot>.Failure($"Run is not a sandbox scenario: {runId}");
        var encounter = run.Value.GetActiveEncounter();
        if (encounter == null)
            return Result<SandboxCombatSnapshot>.Failure($"Sandbox run has no active combat: {runId}");

        var combat = encounter.Combat;
        var actors = combat.GetAllActors()
            .Select(entity => MapActor(entity, combat.StatusEffects, run.Value.Modifiers))
            .ToArray();
        return Result<SandboxCombatSnapshot>.Success(new SandboxCombatSnapshot
        {
            Run = new SandboxRunSnapshot
            {
                RunId = run.Value.RunId,
                Sequence = run.Value.Sequence,
                Step = run.Value.Determinism.Step,
                StateHash = CanonicalJson.ComputeHash(run.Value),
                ContentRevision = run.Value.Determinism.ContentRevision,
                ScenarioHash = run.Value.ScenarioHash,
                AttemptKey = run.Value.AttemptKey,
                Scenario = run.Value.Scenario
            },
            Combat = new SandboxCombatSnapshotState
            {
                CombatId = combat.CombatId,
                Step = combat.Determinism.Step,
                StateHash = CanonicalJson.ComputeHash(combat),
                Turn = combat.CurrentTurn,
                Status = combat.Status.ToString(),
                ActiveActorId = combat.ActivationState?.ActiveActorId,
                Phase = combat.PhaseState,
                Activation = combat.ActivationState,
                PriorityWindow = combat.PriorityWindow,
                PendingActions = combat.PendingActions,
                Board = combat.Board,
                Actors = actors
            },
            PlayableCards = MapPlayableCards(run.Value, combat.ActivationState?.ActiveActorId)
        });
    }

    private SandboxActorSnapshot MapActor(
        CombatActorState entity,
        IReadOnlyDictionary<string, ImmutableArray<StatusEffectInstance>> statusEffects,
        IReadOnlyList<ScriptModifierInstance> modifiers)
    {
        var statuses = statusEffects.TryGetValue(entity.InstanceId, out var active) ? active : [];
        return new SandboxActorSnapshot
        {
            InstanceId = entity.InstanceId,
            DefinitionId = entity.DefinitionId,
            Name = entity.Name,
            SideId = entity.SideId,
            ControllerBinding = entity.ControllerBinding,
            IsAlive = entity.IsAlive,
            Resources = entity.ResourceState.Resources
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .ToDictionary(
                    item => item.Key,
                    item => new SandboxResourceSnapshot(item.Value.Current, item.Value.Maximum, item.Value.Minimum),
                    StringComparer.Ordinal),
            Statuses = statuses,
            Modifiers = modifiers.Where(modifier =>
                    modifier.IsActive && string.Equals(modifier.OwnerId, entity.InstanceId, StringComparison.Ordinal))
                .OrderBy(modifier => modifier.InstanceId)
                .ToArray()
        };
    }

    private static IReadOnlyList<SandboxCardSnapshot> MapPlayableCards(RunState run, string? actorId)
    {
        var cards = new List<SandboxCardSnapshot>();
        var playable = CardZonePlaySource.CardsForActor(run, actorId ?? run.PlayerEntityId);
        for (var index = 0; index < playable.Count; index++)
        {
            var instanceId = playable[index];
            var instance = run.Deck.GetCard(instanceId)
                ?? throw new InvalidOperationException($"Card instance not found: {instanceId}");
            var address = run.Deck.Topology.FindZone(instanceId)?.Address
                ?? throw new InvalidOperationException($"Playable card has no zone: {instanceId}");
            cards.Add(new SandboxCardSnapshot
            {
                CardInstanceId = instanceId,
                DefinitionId = instance.DefinitionId,
                PlayableIndex = index,
                ZoneId = address.ZoneId,
                ZoneOwnerId = address.OwnerId,
                Upgrades = CardTransformationLedger.Project(instance.Upgrades).Value,
                TransformationLedger = instance.Upgrades
            });
        }
        return cards;
    }
}

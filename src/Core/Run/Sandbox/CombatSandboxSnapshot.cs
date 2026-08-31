using System.Collections.Immutable;
using Core.Combat;
using Core.Combat.Models;
using Core.Combat.Modifiers;
using Core.Common;
using Core.Determinism;
using Core.Run.Content;
using Core.StatusEffects;

namespace Core.Run.Sandbox;

public sealed record SandboxCombatSnapshot
{
    private ImmutableArray<SandboxCardSnapshot> _hand = [];

    public SandboxRunSnapshot Run { get; init; } = new();
    public SandboxCombatSnapshotState Combat { get; init; } = new();
    public IReadOnlyList<SandboxCardSnapshot> Hand
    {
        get => _hand;
        init => _hand = value?.ToImmutableArray() ?? [];
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

    public string EntityId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool IsHero { get; init; }
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

    public Guid CardInstanceId { get; init; }
    public string DefinitionId { get; init; } = string.Empty;
    public string ActionId { get; init; } = string.Empty;
    public int HandIndex { get; init; }
    public IReadOnlyList<CardUpgradeState> Upgrades
    {
        get => _upgrades;
        init => _upgrades = value?.ToImmutableArray() ?? [];
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
    private readonly IRunManager _runs;
    private readonly ICardContentCatalog _cards;
    private readonly IScriptModifierManager _modifiers;

    public CombatSandboxSnapshotService(
        IRunManager runs,
        ICardContentCatalog cards,
        IScriptModifierManager modifiers)
    {
        _runs = runs;
        _cards = cards;
        _modifiers = modifiers;
    }

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
        var actors = combat.GetAllEntities()
            .Select(entity => MapActor(entity, combat.StatusEffects))
            .OrderBy(actor => actor.IsHero ? 0 : 1)
            .ThenBy(actor => actor.EntityId, StringComparer.Ordinal)
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
                Board = combat.Board,
                Actors = actors
            },
            Hand = MapHand(run.Value.Deck, run.Value.ConfigName)
        });
    }

    private SandboxActorSnapshot MapActor(
        CombatEntity entity,
        IReadOnlyDictionary<string, ImmutableArray<StatusEffectInstance>> statusEffects)
    {
        var statuses = statusEffects.TryGetValue(entity.EntityId, out var active) ? active : [];
        return new SandboxActorSnapshot
        {
            EntityId = entity.EntityId,
            Name = entity.Name,
            IsHero = entity.IsHero,
            IsAlive = entity.IsAlive,
            Resources = entity.ResourceState.Resources
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .ToDictionary(
                    item => item.Key,
                    item => new SandboxResourceSnapshot(item.Value.Current, item.Value.Maximum, item.Value.Minimum),
                    StringComparer.Ordinal),
            Statuses = statuses,
            Modifiers = _modifiers.GetActiveModifiers(entity.EntityId)
                .OrderBy(modifier => modifier.InstanceId)
                .ToArray()
        };
    }

    private IReadOnlyList<SandboxCardSnapshot> MapHand(DeckState deck, string configName)
    {
        var cards = new List<SandboxCardSnapshot>();
        for (var index = 0; index < deck.Hand.Count; index++)
        {
            var instanceId = index < deck.HandInstanceIds.Count ? deck.HandInstanceIds[index] : Guid.Empty;
            var definitionId = deck.Hand[index];
            deck.CardInstances.TryGetValue(instanceId, out var instance);
            var content = _cards.GetCard(definitionId, configName);
            cards.Add(new SandboxCardSnapshot
            {
                CardInstanceId = instanceId,
                DefinitionId = definitionId,
                ActionId = content.IsSuccess ? content.Value.ActionId : definitionId,
                HandIndex = index,
                Upgrades = instance?.Upgrades ?? []
            });
        }
        return cards;
    }
}

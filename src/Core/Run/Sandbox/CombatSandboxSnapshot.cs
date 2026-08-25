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
    public SandboxRunSnapshot Run { get; init; } = new();
    public SandboxCombatSnapshotState Combat { get; init; } = new();
    public IReadOnlyList<SandboxCardSnapshot> Hand { get; init; } = [];
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
    public Guid CombatId { get; init; }
    public ulong Step { get; init; }
    public string StateHash { get; init; } = string.Empty;
    public int Turn { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? ActiveActorId { get; init; }
    public object? Phase { get; init; }
    public object? Activation { get; init; }
    public CombatBoardState Board { get; init; } = new();
    public IReadOnlyList<SandboxActorSnapshot> Actors { get; init; } = [];
}

public sealed record SandboxActorSnapshot
{
    public string EntityId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool IsHero { get; init; }
    public bool IsAlive { get; init; }
    public IReadOnlyDictionary<string, SandboxResourceSnapshot> Resources { get; init; } =
        ImmutableDictionary<string, SandboxResourceSnapshot>.Empty;
    public IReadOnlyList<StatusEffectInstance> Statuses { get; init; } = [];
    public IReadOnlyList<ScriptModifierInstance> Modifiers { get; init; } = [];
}

public sealed record SandboxResourceSnapshot(float Current, float Maximum, float Minimum);

public sealed record SandboxCardSnapshot
{
    public Guid CardInstanceId { get; init; }
    public string DefinitionId { get; init; } = string.Empty;
    public string ActionId { get; init; } = string.Empty;
    public int HandIndex { get; init; }
    public IReadOnlyList<CardUpgradeState> Upgrades { get; init; } = [];
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
    private readonly IStatusEffectManager _statuses;
    private readonly IScriptModifierManager _modifiers;

    public CombatSandboxSnapshotService(
        IRunManager runs,
        ICardContentCatalog cards,
        IStatusEffectManager statuses,
        IScriptModifierManager modifiers)
    {
        _runs = runs;
        _cards = cards;
        _statuses = statuses;
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
            .Select(MapActor)
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

    private SandboxActorSnapshot MapActor(CombatEntity entity)
    {
        var statuses = _statuses.GetActiveStatus(entity.EntityId);
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
            Statuses = statuses.IsSuccess
                ? statuses.Value.OrderBy(status => status.InstanceId).ToArray()
                : [],
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

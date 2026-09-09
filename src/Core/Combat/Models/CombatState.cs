using System.Collections.Immutable;
using Core.Combat.Activation;
using Core.Combat.Reactions;
using Core.Combat.TurnOrder;
using Core.Combat.TurnPhase;
using Core.Determinism;
using Core.StatusEffects;

namespace Core.Combat.Models;

/// <summary>Immutable combat aggregate with one generic actor roster.</summary>
public sealed record CombatState
{
    private ImmutableSortedDictionary<string, CombatActorState> _actors =
        ImmutableSortedDictionary<string, CombatActorState>.Empty.WithComparers(StringComparer.Ordinal);
    private ImmutableList<CombatAction> _actionHistory = [];
    private ImmutableArray<string> _actorOrder = [];

    public Guid CombatId { get; init; } = Guid.Empty;
    public Guid? RunId { get; init; }
    public string? RunNodeId { get; init; }
    public DateTime StartedAt { get; init; } = DateTime.UnixEpoch;
    public DeterministicContext Determinism { get; init; } = DeterministicContext.Create(0, "combat");
    public int CurrentTurn { get; init; } = 1;
    public CombatStatus Status { get; init; } = CombatStatus.ACTIVE;
    public CombatRelationshipPolicy Relationships { get; init; } = new();
    public ImmutableHashSet<string> CompletedLifecycleBoundaries { get; init; } = ImmutableHashSet<string>.Empty;
    public ImmutableArray<CombatSide> Sides { get; init; } = [];
    public IReadOnlyDictionary<string, CombatActorState> Actors
    {
        get => _actors;
        init => _actors = value?.ToImmutableSortedDictionary(StringComparer.Ordinal)
            ?? ImmutableSortedDictionary<string, CombatActorState>.Empty.WithComparers(StringComparer.Ordinal);
    }
    public IReadOnlyList<string> ActorOrder
    {
        get => _actorOrder;
        init => _actorOrder = value?.ToImmutableArray() ?? [];
    }

    public IReadOnlyList<CombatAction> ActionHistory
    {
        get => _actionHistory;
        init => _actionHistory = value?.ToImmutableList() ?? [];
    }
    public TurnOrderState TurnOrderState { get; init; } = new();
    public PhaseState? PhaseState { get; init; }
    public CombatBoardState Board { get; init; } = new();
    public ActivationState? ActivationState { get; init; }
    public PriorityWindowState? PriorityWindow { get; init; }
    public ImmutableArray<PendingActionState> PendingActions { get; init; } = [];
    public ImmutableDictionary<string, ImmutableArray<StatusEffectInstance>> StatusEffects { get; init; } =
        ImmutableDictionary<string, ImmutableArray<StatusEffectInstance>>.Empty.WithComparers(StringComparer.Ordinal);

    public bool IsActive => Status == CombatStatus.ACTIVE;
    public string GetSideId(CombatActorState actor) => actor.SideId;
    public SideRelationship Relationship(CombatActorState from, CombatActorState to) =>
        Relationships.Resolve(from.SideId, to.SideId);
    public ControllerKind ControllerOf(CombatActorState actor) => actor.ControllerBinding.Kind;
    public CombatActorState? GetActor(string instanceId) =>
        _actors.TryGetValue(instanceId, out var actor) ? actor : null;
    public IEnumerable<CombatActorState> GetAllActors() => _actorOrder.Length == _actors.Count &&
        _actorOrder.Distinct(StringComparer.Ordinal).Count() == _actorOrder.Length &&
        _actorOrder.All(_actors.ContainsKey)
        ? _actorOrder.Select(id => _actors[id])
        : _actors.Values;
    public IEnumerable<CombatActorState> GetActorsForSide(string sideId) =>
        _actors.Values.Where(actor => string.Equals(actor.SideId, sideId, StringComparison.Ordinal));
    public CombatState ReplaceActor(CombatActorState actor)
    {
        if (!_actors.TryGetValue(actor.InstanceId, out var existing))
            throw new InvalidOperationException($"Combat actor not found: {actor.InstanceId}");
        if (!string.Equals(existing.DefinitionId, actor.DefinitionId, StringComparison.Ordinal) ||
            !string.Equals(existing.ContentRevision, actor.ContentRevision, StringComparison.Ordinal) ||
            !string.Equals(existing.SideId, actor.SideId, StringComparison.Ordinal) ||
            existing.ControllerBinding != actor.ControllerBinding)
        {
            throw new InvalidOperationException(
                $"Combat actor identity and bindings cannot change during replacement: {actor.InstanceId}");
        }
        return this with { Actors = _actors.SetItem(actor.InstanceId, actor) };
    }
}

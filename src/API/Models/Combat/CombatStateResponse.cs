namespace API.Models.Combat;

public class CombatStateResponse
{
    public Guid CombatId { get; set; }
    public Guid? RunId { get; set; }
    public string? RunNodeId { get; set; }
    public ulong Seed { get; set; }
    public ulong Step { get; set; }
    public string ContentRevision { get; set; } = string.Empty;
    public string EngineVersion { get; set; } = string.Empty;
    public string StateHash { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int CurrentTurn { get; set; }
    public List<ActorStateDto> Actors { get; set; } = new();
    public IReadOnlyList<Core.Combat.Models.CombatSide> Sides { get; set; }
        = Array.Empty<Core.Combat.Models.CombatSide>();
    public Core.Combat.Models.CombatRelationshipPolicy Relationships { get; set; } = new();
    public int TotalActions { get; set; }
    public Core.Combat.Models.CombatBoardState Board { get; set; } = new();
    public Core.Combat.TurnPhase.PhaseState? Phase { get; set; }
    public Core.Combat.Activation.ActivationState? Activation { get; set; }
    public Core.Combat.Reactions.PriorityWindowState? PriorityWindow { get; set; }
    public IReadOnlyList<Core.Combat.Reactions.PendingActionState> PendingActions { get; set; }
        = Array.Empty<Core.Combat.Reactions.PendingActionState>();

    public static CombatStateResponse From(Core.Combat.Models.CombatState state) => new()
    {
        CombatId = state.CombatId,
        RunId = state.RunId,
        RunNodeId = state.RunNodeId,
        Seed = state.Determinism.Seed,
        Step = state.Determinism.Step,
        ContentRevision = state.Determinism.ContentRevision,
        EngineVersion = state.Determinism.EngineVersion,
        StateHash = Core.Determinism.CanonicalJson.ComputeHash(state),
        Status = state.Status.ToString(),
        CurrentTurn = state.CurrentTurn,
        Actors = state.GetAllActors().Select(actor => new ActorStateDto
        {
            InstanceId = actor.InstanceId,
            DefinitionId = actor.DefinitionId,
            Name = actor.Name,
            SideId = actor.SideId,
            ControllerBinding = actor.ControllerBinding,
            IsAlive = actor.IsAlive,
            Resources = actor.ResourceState.Resources
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(
                    pair => pair.Key,
                    pair => new ResourcePoolDto
                    {
                        Current = pair.Value.Current,
                        Maximum = pair.Value.Maximum,
                        Minimum = pair.Value.Minimum
                    },
                    StringComparer.Ordinal),
            Statuses = state.StatusEffects.TryGetValue(actor.InstanceId, out var statuses)
                ? statuses.Select(status => new StatusEffectStateDto
                {
                    InstanceId = status.InstanceId,
                    StatusId = status.StatusId,
                    SourceId = status.SourceId,
                    Stacks = status.Stacks,
                    Duration = status.Duration,
                    IsActive = status.IsActive
                }).ToArray()
                : []
        }).ToList(),
        Sides = state.Sides,
        Relationships = state.Relationships,
        TotalActions = state.ActionHistory.Count,
        Board = state.Board,
        Phase = state.PhaseState,
        Activation = state.ActivationState,
        PriorityWindow = state.PriorityWindow,
        PendingActions = state.PendingActions
    };
}

public class ActorStateDto
{
    public string InstanceId { get; set; } = string.Empty;
    public string DefinitionId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string SideId { get; set; } = string.Empty;
    public Core.Combat.Models.ControllerBinding ControllerBinding { get; set; } = new();
    public bool IsAlive { get; set; }
    public IReadOnlyDictionary<string, ResourcePoolDto> Resources { get; set; }
        = new Dictionary<string, ResourcePoolDto>();
    public IReadOnlyList<StatusEffectStateDto> Statuses { get; set; } = [];
}

public class StatusEffectStateDto
{
    public Guid InstanceId { get; set; }
    public string StatusId { get; set; } = string.Empty;
    public string? SourceId { get; set; }
    public int Stacks { get; set; }
    public int Duration { get; set; }
    public bool IsActive { get; set; }
}

public class ResourcePoolDto
{
    public float Current { get; set; }
    public float Maximum { get; set; }
    public float Minimum { get; set; }
}

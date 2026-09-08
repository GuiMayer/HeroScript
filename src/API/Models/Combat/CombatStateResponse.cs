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
    public int TotalActions { get; set; }
    public Core.Combat.Models.CombatBoardState Board { get; set; } = new();
    public Core.Combat.TurnPhase.PhaseState? Phase { get; set; }
    public Core.Combat.Activation.ActivationState? Activation { get; set; }
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
}

public class ResourcePoolDto
{
    public float Current { get; set; }
    public float Maximum { get; set; }
    public float Minimum { get; set; }
}

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
    public HeroStateDto Hero { get; set; } = null!;
    public List<EnemyStateDto> Enemies { get; set; } = new();
    public EnergyDto Energy { get; set; } = null!;
    public int TotalActions { get; set; }
    public Core.Combat.Models.CombatBoardState Board { get; set; } = new();
    public Core.Combat.TurnPhase.PhaseState? Phase { get; set; }
}

public class HeroStateDto
{
    public string EntityId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int CurrentHp { get; set; }
    public int MaxHp { get; set; }
    public bool IsAlive { get; set; }
    public IReadOnlyDictionary<string, ResourcePoolDto> Resources { get; set; }
        = new Dictionary<string, ResourcePoolDto>();
}

public class EnemyStateDto
{
    public string EntityId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int CurrentHp { get; set; }
    public int MaxHp { get; set; }
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

public class EnergyDto
{
    public int Current { get; set; }
    public int Maximum { get; set; }
}

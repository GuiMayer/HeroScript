namespace API.Models.Combat;

public class CombatHistoryResponse
{
    public Guid CombatId { get; set; }
    public int TotalActions { get; set; }
    public List<ActionDto> Actions { get; set; } = new();
}

public class ActionDto
{
    public Guid ActionId { get; set; }
    public DateTime Timestamp { get; set; }
    public int Turn { get; set; }
    public string ActorId { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public string? PowerId { get; set; }
    public string? TargetId { get; set; }
    public int? DamageDealt { get; set; }
    public int? EnergyChange { get; set; }
}

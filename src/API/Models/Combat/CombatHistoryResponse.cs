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
    public List<ActionApplicationDto> Applications { get; set; } = new();
}

public class ActionApplicationDto
{
    public string EffectInstanceId { get; set; } = string.Empty;
    public string EffectType { get; set; } = string.Empty;
    public string TargetEntityId { get; set; } = string.Empty;
    public string? ResourceId { get; set; }
    public string? ResourceField { get; set; }
    public string? ResourceOperation { get; set; }
    public float? PreviousValue { get; set; }
    public float? CurrentValue { get; set; }
    public float? SignedAmount { get; set; }
    public string? StatusId { get; set; }
    public Guid? StatusInstanceId { get; set; }
    public string ProvenanceKind { get; set; } = string.Empty;
    public string ProvenanceSourceId { get; set; } = string.Empty;
    public string? ProvenanceComponentId { get; set; }
}

namespace API.Models.Effects;

public class ApplyEffectResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string Scope { get; set; } = string.Empty;
    public EffectResultDto EffectResult { get; set; } = new();
    public bool HasUpdatedCombatState { get; set; }
}

public class EffectResultDto
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public float? ValueApplied { get; set; }
    public string? ResourceAffected { get; set; }
    public List<string> AffectedEntityIds { get; set; } = new();
    public List<string> StatusApplied { get; set; } = new();
    public List<string> StatusRemoved { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
}

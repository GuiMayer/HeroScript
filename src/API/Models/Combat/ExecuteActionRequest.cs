namespace API.Models.Combat;

public class ExecuteActionRequest
{
    public string ActionType { get; set; } = string.Empty;  // "BASIC_ATTACK", "POWER", "PASS", "END_TURN"
    public string? PowerId { get; set; }
    public string? TargetId { get; set; }
}

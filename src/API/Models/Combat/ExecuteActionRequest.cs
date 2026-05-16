namespace API.Models.Combat;

public class ExecuteActionRequest
{
    /// <summary>
    /// ID da acao data-driven. Quando informado, tem prioridade sobre ActionType/PowerId.
    /// </summary>
    public string? ActionId { get; set; }

    public string ActionType { get; set; } = string.Empty;  // "BASIC_ATTACK", "POWER", "PASS", "END_TURN"
    public string? PowerId { get; set; }
    public string? TargetId { get; set; }
    public string? CostOptionId { get; set; }  // ID da opção de custo alternativo (opcional)
}

namespace API.Models.Resources;

/// <summary>
/// Request para validar se há recurso suficiente para um custo
/// </summary>
public class ValidateCostRequest
{
    public string ResourceId { get; set; } = string.Empty;
    public float Cost { get; set; }
    public float CurrentAmount { get; set; }
}

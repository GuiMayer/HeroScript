namespace API.Models.Resources;

/// <summary>
/// Response de validação de custo
/// </summary>
public class ValidateCostResponse
{
    public bool CanAfford { get; set; }
    public string? Error { get; set; }
}

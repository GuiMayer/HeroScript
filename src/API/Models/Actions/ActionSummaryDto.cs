namespace API.Models.Actions;

/// <summary>
/// Versão resumida de uma ação para listagens
/// </summary>
public class ActionSummaryDto
{
    public string ActionId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public int Cooldown { get; set; }
    public List<string> Tags { get; set; } = new();
    public int CostOptionsCount { get; set; }
}

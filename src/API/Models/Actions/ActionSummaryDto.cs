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
    public bool RequiresTarget { get; set; }
    public bool MultiTarget { get; set; }
    public float BaseDamage { get; set; }
    public List<string> Tags { get; set; } = new();
    public int CostOptionsCount { get; set; }
    public int EffectCount { get; set; }
}

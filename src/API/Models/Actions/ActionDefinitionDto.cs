namespace API.Models.Actions;

/// <summary>
/// Representa uma definição completa de ação
/// </summary>
public class ActionDefinitionDto
{
    public string ActionId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public int Cooldown { get; set; }
    public float BaseDamage { get; set; }
    public List<string> Tags { get; set; } = new();
    public ActionCostsDto Costs { get; set; } = new();
}

/// <summary>
/// Representa custos de uma ação
/// </summary>
public class ActionCostsDto
{
    public List<ResourceCostDto> Costs { get; set; } = new();
    public List<AlternativeCostOptionDto> AlternativeOptions { get; set; } = new();
}

/// <summary>
/// Representa um custo de recurso
/// </summary>
public class ResourceCostDto
{
    public string ResourceId { get; set; } = string.Empty;
    public float Amount { get; set; }
    public bool AllowOverdraft { get; set; }
}

/// <summary>
/// Representa uma opção de custo alternativo
/// </summary>
public class AlternativeCostOptionDto
{
    public string OptionId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<ResourceCostDto> Costs { get; set; } = new();
}

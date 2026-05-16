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
    public bool RequiresTarget { get; set; } = true;
    public bool MultiTarget { get; set; }
    /// <summary>
    /// Campo derivado legado. A fonte de verdade sao os efeitos em Effects.
    /// </summary>
    public float BaseDamage { get; set; }
    public List<string> Tags { get; set; } = new();
    public ActionCostsDto Costs { get; set; } = new();
    public List<EffectDefinitionDto> Effects { get; set; } = new();
}

/// <summary>
/// Representa um efeito data-driven dentro de uma acao.
/// </summary>
public class EffectDefinitionDto
{
    public string EffectId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Target { get; set; } = "TARGET";
    public string Timing { get; set; } = "IMMEDIATE";
    public float? FlatValue { get; set; }
    public string? FormulaValue { get; set; }
    public bool IsPercentage { get; set; }
    public string? TargetResource { get; set; }
    public string? StatusId { get; set; }
    public int? StatusStacks { get; set; }
    public int? StatusDuration { get; set; }
    public string? ModifierKey { get; set; }
    public float? ModifierValue { get; set; }
    public string? ModifierFormula { get; set; }
    public string? Condition { get; set; }
    public List<string>? RequiredTags { get; set; }
    public List<string>? ExcludedTags { get; set; }
    public float Chance { get; set; } = 1.0f;
    public int Repeat { get; set; } = 1;
    public List<string> Tags { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
    public List<EffectDefinitionDto>? ChainedEffects { get; set; }
    public List<EffectDefinitionDto>? ConditionalEffects { get; set; }
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

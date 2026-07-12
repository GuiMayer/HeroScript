using Core.Combat.Models;

namespace API.Models.Gambits;

/// <summary>
/// DTO para definição de gambit
/// </summary>
public class GambitDefinitionDto
{
    /// <summary>
    /// ID único do gambit
    /// </summary>
    public string GambitId { get; set; } = string.Empty;
    
    /// <summary>
    /// Nome para exibição
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;
    
    /// <summary>
    /// Descrição do gambit
    /// </summary>
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// Prioridade (maior = executa primeiro)
    /// </summary>
    public int Priority { get; set; }
    
    /// <summary>
    /// Condições que devem ser satisfeitas
    /// </summary>
    public List<GambitConditionDto> Conditions { get; set; } = new();
    
    /// <summary>
    /// Ação a ser executada
    /// </summary>
    public GambitActionDto Action { get; set; } = new();
    
    /// <summary>
    /// Intenção do gambit (para UI/telegraphing)
    /// </summary>
    public GambitIntentDto Intent { get; set; } = new();
    
    /// <summary>
    /// Tags para categorização
    /// </summary>
    public List<string> Tags { get; set; } = new();
}

/// <summary>
/// DTO para intenção de gambit
/// </summary>
public class GambitIntentDto
{
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? TelegraphType { get; set; }
    public List<string> Tags { get; set; } = new();
}

/// <summary>
/// DTO para condição de gambit
/// </summary>
public class GambitConditionDto
{
    public string Type { get; set; } = "ALWAYS";
    public string? Target { get; set; }
    public string? ResourceId { get; set; }
    public float? LessThanOrEqual { get; set; }
    public float? GreaterThanOrEqual { get; set; }
}

/// <summary>
/// DTO para ação de gambit
/// </summary>
public class GambitActionDto
{
    public string ActionType { get; set; } = "PASS";
    public string? PowerId { get; set; }
    public string? Target { get; set; }
    public int? CostOptionId { get; set; }
}

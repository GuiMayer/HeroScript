using Core.Resources;

namespace Core.Combat;

/// <summary>
/// Interface para serviço de verificação de affordability de ações
/// </summary>
public interface IActionAffordabilityService
{
    /// <summary>
    /// Obtém todas as ações que o jogador pode pagar
    /// </summary>
    /// <param name="actions">Lista de ações disponíveis</param>
    /// <param name="resources">Recursos disponíveis do jogador</param>
    /// <returns>Lista de ações que podem ser pagas</returns>
    IEnumerable<ActionDefinition> GetAffordableActions(
        IEnumerable<ActionDefinition> actions,
        IReadOnlyDictionary<string, ResourcePool> resources);

    /// <summary>
    /// Obtém opções de custo disponíveis para uma ação
    /// </summary>
    /// <param name="action">Definição da ação</param>
    /// <param name="resources">Recursos disponíveis do jogador</param>
    /// <returns>Informações sobre custos e opções disponíveis</returns>
    ActionCostOptions GetCostOptions(
        ActionDefinition action,
        IReadOnlyDictionary<string, ResourcePool> resources);

    /// <summary>
    /// Verifica se o jogador pode pagar por uma ação
    /// </summary>
    /// <param name="action">Definição da ação</param>
    /// <param name="resources">Recursos disponíveis do jogador</param>
    /// <returns>Resultado da verificação com detalhes</returns>
    AffordabilityResult CanAfford(
        ActionDefinition action,
        IReadOnlyDictionary<string, ResourcePool> resources);
}

/// <summary>
/// Opções de custo disponíveis para uma ação
/// </summary>
public class ActionCostOptions
{
    public string ActionId { get; set; } = string.Empty;
    public List<ResourceCostInfo> NormalCosts { get; set; } = new();
    public List<AlternativeCostOptionInfo> AlternativeOptions { get; set; } = new();
    public List<string> AffordableOptionIds { get; set; } = new();
}

/// <summary>
/// Informações sobre um custo de recurso
/// </summary>
public class ResourceCostInfo
{
    public string ResourceId { get; set; } = string.Empty;
    public float Amount { get; set; }
    public bool AllowOverdraft { get; set; }
}

/// <summary>
/// Informações sobre uma opção de custo alternativo
/// </summary>
public class AlternativeCostOptionInfo
{
    public string OptionId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<ResourceCostInfo> Costs { get; set; } = new();
    public bool Affordable { get; set; }
}

/// <summary>
/// Resultado da verificação de affordability
/// </summary>
public class AffordabilityResult
{
    public string ActionId { get; set; } = string.Empty;
    public bool CanAfford { get; set; }
    public List<string> AffordableOptionIds { get; set; } = new();
    public string? Error { get; set; }
}

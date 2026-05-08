namespace Core.Combat;

/// <summary>
/// Uma opção de custo alternativo.
/// Representa uma das escolhas possíveis para pagar uma ação.
/// </summary>
public record AlternativeCostOption
{
    /// <summary>
    /// ID único da opção (ex: "mana_cost", "health_cost").
    /// </summary>
    public string OptionId { get; init; } = string.Empty;
    
    /// <summary>
    /// Descrição da opção para exibição ao jogador.
    /// </summary>
    public string Description { get; init; } = string.Empty;
    
    /// <summary>
    /// Lista de custos de recursos para esta opção.
    /// Todos os custos nesta lista devem ser pagos (AND logic dentro da opção).
    /// </summary>
    public List<ResourceCost> Costs { get; init; } = new();
    
    /// <summary>
    /// Verifica se há recursos suficientes para pagar esta opção.
    /// </summary>
    public bool CanAfford(Dictionary<string, Resources.ResourcePool> resources)
    {
        foreach (var cost in Costs)
        {
            if (!resources.TryGetValue(cost.ResourceId, out var pool))
                return false;
            
            if (!cost.AllowOverdraft && !pool.CanAfford(cost.Amount))
                return false;
        }
        return true;
    }
    
    /// <summary>
    /// Obtém mensagem de erro se não puder pagar esta opção.
    /// </summary>
    public string? GetAffordabilityError(Dictionary<string, Resources.ResourcePool> resources)
    {
        foreach (var cost in Costs)
        {
            if (!resources.TryGetValue(cost.ResourceId, out var pool))
                return $"Resource not found: {cost.ResourceId}";
            
            if (!cost.AllowOverdraft && !pool.CanAfford(cost.Amount))
            {
                var resourceName = pool.Definition?.DisplayName ?? cost.ResourceId;
                return $"Insufficient {resourceName}: has {pool.Current}, needs {cost.Amount}";
            }
        }
        return null;
    }
}

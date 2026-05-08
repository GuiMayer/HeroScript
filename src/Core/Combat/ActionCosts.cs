namespace Core.Combat;

/// <summary>
/// Custos de recursos para uma ação.
/// Uma ação pode consumir múltiplos recursos (ex: energy + mana).
/// </summary>
public record ActionCosts
{
    /// <summary>
    /// Lista de custos de recursos.
    /// </summary>
    public List<ResourceCost> Costs { get; init; } = new();
    
    /// <summary>
    /// Verifica se há recursos suficientes para pagar todos os custos.
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
    /// Obtém mensagem de erro se não puder pagar os custos.
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

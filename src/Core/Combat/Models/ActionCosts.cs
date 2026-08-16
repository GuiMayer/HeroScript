using System.Collections.Immutable;

namespace Core.Combat.Models;

/// <summary>
/// Custos de recursos para uma ação.
/// Uma ação pode consumir múltiplos recursos (ex: energy + mana).
/// </summary>
public record ActionCosts
{
    private ImmutableList<ResourceCost> _costs = [];
    private ImmutableList<AlternativeCostOption> _alternativeCosts = [];

    /// <summary>
    /// Lista de custos de recursos (AND logic - todos devem ser pagos).
    /// </summary>
    public IReadOnlyList<ResourceCost> Costs
    {
        get => _costs;
        init => _costs = value?.ToImmutableList() ?? [];
    }
    
    /// <summary>
    /// Lista de opções de custos alternativos (OR logic - escolher uma opção).
    /// Se não vazia, o jogador deve escolher UMA opção para pagar.
    /// </summary>
    public IReadOnlyList<AlternativeCostOption> AlternativeCosts
    {
        get => _alternativeCosts;
        init => _alternativeCosts = value?.ToImmutableList() ?? [];
    }
    
    /// <summary>
    /// Verifica se há recursos suficientes para pagar todos os custos.
    /// Se houver custos alternativos, verifica se pelo menos UMA opção pode ser paga.
    /// </summary>
    public bool CanAfford(Dictionary<string, Resources.ResourcePool> resources)
    {
        // Verificar custos normais (AND logic)
        foreach (var cost in Costs)
        {
            if (!resources.TryGetValue(cost.ResourceId, out var pool))
                return false;
            
            if (!cost.AllowOverdraft && !pool.CanAfford(cost.Amount))
                return false;
        }
        
        // Se houver custos alternativos, verificar se pelo menos UMA opção pode ser paga
        if (AlternativeCosts.Count > 0)
        {
            return AlternativeCosts.Any(option => option.CanAfford(resources));
        }
        
        return true;
    }
    
    /// <summary>
    /// Obtém mensagem de erro se não puder pagar os custos.
    /// </summary>
    public string? GetAffordabilityError(Dictionary<string, Resources.ResourcePool> resources)
    {
        // Verificar custos normais
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
        
        // Se houver custos alternativos, verificar se pelo menos uma opção pode ser paga
        if (AlternativeCosts.Count > 0)
        {
            var affordableOptions = GetAffordableOptions(resources);
            if (affordableOptions.Count == 0)
            {
                var optionDescriptions = string.Join(" OR ", AlternativeCosts.Select(o => o.Description));
                return $"Cannot afford any alternative: {optionDescriptions}";
            }
        }
        
        return null;
    }
    
    /// <summary>
    /// Obtém lista de opções alternativas que podem ser pagas.
    /// </summary>
    public List<AlternativeCostOption> GetAffordableOptions(Dictionary<string, Resources.ResourcePool> resources)
    {
        return AlternativeCosts
            .Where(option => option.CanAfford(resources))
            .ToList();
    }
    
    /// <summary>
    /// Verifica se uma opção específica pode ser paga.
    /// </summary>
    public bool CanAffordOption(string optionId, Dictionary<string, Resources.ResourcePool> resources)
    {
        var option = AlternativeCosts.FirstOrDefault(o => o.OptionId == optionId);
        if (option == null)
            return false;
        
        return option.CanAfford(resources);
    }
    
    /// <summary>
    /// Obtém opção alternativa por ID.
    /// </summary>
    public AlternativeCostOption? GetOption(string optionId)
    {
        return AlternativeCosts.FirstOrDefault(o => o.OptionId == optionId);
    }
}

using Core.Resources;

namespace Core.Combat;

/// <summary>
/// Serviço para verificação de affordability de ações
/// </summary>
public class ActionAffordabilityService : IActionAffordabilityService
{
    /// <summary>
    /// Obtém todas as ações que o jogador pode pagar
    /// </summary>
    public IEnumerable<ActionDefinition> GetAffordableActions(
        IEnumerable<ActionDefinition> actions,
        IReadOnlyDictionary<string, ResourcePool> resources)
    {
        var resourcesDict = ConvertToDictionary(resources);
        
        return actions.Where(action => 
            action.Costs.CanAfford(resourcesDict) || 
            action.Costs.GetAffordableOptions(resourcesDict).Any());
    }

    /// <summary>
    /// Obtém opções de custo disponíveis para uma ação
    /// </summary>
    public ActionCostOptions GetCostOptions(
        ActionDefinition action,
        IReadOnlyDictionary<string, ResourcePool> resources)
    {
        var resourcesDict = ConvertToDictionary(resources);
        var affordableOptions = action.Costs.GetAffordableOptions(resourcesDict);

        return new ActionCostOptions
        {
            ActionId = action.ActionId,
            NormalCosts = action.Costs.Costs.Select(c => new ResourceCostInfo
            {
                ResourceId = c.ResourceId,
                Amount = c.Amount,
                AllowOverdraft = c.AllowOverdraft
            }).ToList(),
            AlternativeOptions = action.Costs.AlternativeCosts.Select(opt => new AlternativeCostOptionInfo
            {
                OptionId = opt.OptionId,
                Description = opt.Description,
                Costs = opt.Costs.Select(c => new ResourceCostInfo
                {
                    ResourceId = c.ResourceId,
                    Amount = c.Amount,
                    AllowOverdraft = c.AllowOverdraft
                }).ToList(),
                Affordable = opt.CanAfford(resourcesDict)
            }).ToList(),
            AffordableOptionIds = affordableOptions.Select(o => o.OptionId).ToList()
        };
    }

    /// <summary>
    /// Verifica se o jogador pode pagar por uma ação
    /// </summary>
    public AffordabilityResult CanAfford(
        ActionDefinition action,
        IReadOnlyDictionary<string, ResourcePool> resources)
    {
        var resourcesDict = ConvertToDictionary(resources);
        var canAfford = action.Costs.CanAfford(resourcesDict);
        var affordableOptions = action.Costs.GetAffordableOptions(resourcesDict);
        var affordabilityError = action.Costs.GetAffordabilityError(resourcesDict);

        return new AffordabilityResult
        {
            ActionId = action.ActionId,
            CanAfford = canAfford,
            AffordableOptionIds = affordableOptions.Select(o => o.OptionId).ToList(),
            Error = affordabilityError
        };
    }

    /// <summary>
    /// Converte IReadOnlyDictionary para Dictionary para compatibilidade com ActionCosts
    /// </summary>
    private Dictionary<string, ResourcePool> ConvertToDictionary(IReadOnlyDictionary<string, ResourcePool> resources)
    {
        return resources.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }
}

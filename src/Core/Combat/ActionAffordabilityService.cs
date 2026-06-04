using Core.Combat.Models;
using Core.Common;
using Core.Logging;
using Core.Resources;

namespace Core.Combat;

/// <summary>
/// Serviço para verificação de affordability de ações
/// Segue padrões estabelecidos em docs/core-service-patterns.md
/// </summary>
public class ActionAffordabilityService : IActionAffordabilityService
{
    private readonly ILogger _logger;
    private readonly IActionCostEvaluator _costEvaluator;

    public ActionAffordabilityService(ILogger logger, IActionCostEvaluator costEvaluator)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _costEvaluator = costEvaluator ?? throw new ArgumentNullException(nameof(costEvaluator));
    }

    /// <summary>
    /// Obtém todas as ações que o jogador pode pagar
    /// </summary>
    public Result<IReadOnlyList<ActionDefinition>> GetAffordableActions(
        IEnumerable<ActionDefinition> actions,
        IReadOnlyDictionary<string, ResourcePool> resources)
    {
        if (actions == null)
            return Result<IReadOnlyList<ActionDefinition>>.Failure("Actions cannot be null");

        if (resources == null)
            return Result<IReadOnlyList<ActionDefinition>>.Failure("Resources cannot be null");

        try
        {
            _logger.LogDebug($"Checking affordability for {actions.Count()} actions");

            var resourcesDict = ConvertToDictionary(resources);
            var affordableActions = actions.Where(action =>
                CanAffordCosts(action.Costs.Costs, resourcesDict) ||
                GetAffordableOptions(action.Costs.AlternativeCosts, resourcesDict).Any()).ToList();

            _logger.LogDebug($"Found {affordableActions.Count} affordable actions");

            return Result<IReadOnlyList<ActionDefinition>>.Success(affordableActions.AsReadOnly());
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to get affordable actions: {ex.Message}", ex);
            return Result<IReadOnlyList<ActionDefinition>>.Failure($"Failed to get affordable actions: {ex.Message}");
        }
    }

    /// <summary>
    /// Obtém opções de custo disponíveis para uma ação
    /// </summary>
    public Result<ActionCostOptions> GetCostOptions(
        ActionDefinition action,
        IReadOnlyDictionary<string, ResourcePool> resources)
    {
        if (action == null)
            return Result<ActionCostOptions>.Failure("Action cannot be null");

        if (resources == null)
            return Result<ActionCostOptions>.Failure("Resources cannot be null");

        try
        {
            _logger.LogDebug($"Getting cost options for action {action.ActionId}");

            var resourcesDict = ConvertToDictionary(resources);
            var affordableOptions = GetAffordableOptions(action.Costs.AlternativeCosts, resourcesDict);

            var costOptions = new ActionCostOptions
            {
                ActionId = action.ActionId,
                NormalCosts = action.Costs.Costs.Select(c => new ResourceCostInfo
                {
                    ResourceId = c.ResourceId,
                    Amount = CalculateCostOrFallback(c, resourcesDict),
                    AllowOverdraft = c.AllowOverdraft
                }).ToList(),
                AlternativeOptions = action.Costs.AlternativeCosts.Select(opt => new AlternativeCostOptionInfo
                {
                    OptionId = opt.OptionId,
                    Description = opt.Description,
                    Costs = opt.Costs.Select(c => new ResourceCostInfo
                    {
                        ResourceId = c.ResourceId,
                        Amount = CalculateCostOrFallback(c, resourcesDict),
                        AllowOverdraft = c.AllowOverdraft
                    }).ToList(),
                    Affordable = CanAffordCosts(opt.Costs, resourcesDict)
                }).ToList(),
                AffordableOptionIds = affordableOptions.Select(o => o.OptionId).ToList()
            };

            _logger.LogDebug($"Action {action.ActionId} has {costOptions.AffordableOptionIds.Count} affordable options");

            return Result<ActionCostOptions>.Success(costOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to get cost options for action {action?.ActionId}: {ex.Message}", ex);
            return Result<ActionCostOptions>.Failure($"Failed to get cost options: {ex.Message}");
        }
    }

    /// <summary>
    /// Verifica se o jogador pode pagar por uma ação
    /// </summary>
    public Result<AffordabilityResult> CanAfford(
        ActionDefinition action,
        IReadOnlyDictionary<string, ResourcePool> resources)
    {
        if (action == null)
            return Result<AffordabilityResult>.Failure("Action cannot be null");

        if (resources == null)
            return Result<AffordabilityResult>.Failure("Resources cannot be null");

        try
        {
            _logger.LogDebug($"Checking if action {action.ActionId} is affordable");

            var resourcesDict = ConvertToDictionary(resources);
            var canAfford = CanAffordCosts(action.Costs.Costs, resourcesDict);
            var affordableOptions = GetAffordableOptions(action.Costs.AlternativeCosts, resourcesDict);
            var affordabilityError = GetAffordabilityError(action.Costs.Costs, action.Costs.AlternativeCosts, resourcesDict);

            var result = new AffordabilityResult
            {
                ActionId = action.ActionId,
                CanAfford = canAfford,
                AffordableOptionIds = affordableOptions.Select(o => o.OptionId).ToList(),
                Error = affordabilityError
            };

            _logger.LogDebug($"Action {action.ActionId} affordability: {result.CanAfford}");

            return Result<AffordabilityResult>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to check affordability for action {action?.ActionId}: {ex.Message}", ex);
            return Result<AffordabilityResult>.Failure($"Failed to check affordability: {ex.Message}");
        }
    }

    /// <summary>
    /// Converte IReadOnlyDictionary para Dictionary para compatibilidade com ActionCosts
    /// </summary>
    private Dictionary<string, ResourcePool> ConvertToDictionary(IReadOnlyDictionary<string, ResourcePool> resources)
    {
        return resources.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    private bool CanAffordCosts(IEnumerable<ResourceCost> costs, IReadOnlyDictionary<string, ResourcePool> resources)
    {
        foreach (var cost in costs)
        {
            var canAfford = _costEvaluator.CanAfford(cost, resources);
            if (canAfford.IsFailure || !canAfford.Value)
                return false;
        }

        return true;
    }

    private List<AlternativeCostOption> GetAffordableOptions(
        IEnumerable<AlternativeCostOption> options,
        IReadOnlyDictionary<string, ResourcePool> resources)
    {
        return options.Where(option => CanAffordCosts(option.Costs, resources)).ToList();
    }

    private string? GetAffordabilityError(
        IReadOnlyList<ResourceCost> normalCosts,
        IReadOnlyList<AlternativeCostOption> alternativeCosts,
        IReadOnlyDictionary<string, ResourcePool> resources)
    {
        foreach (var cost in normalCosts)
        {
            var amount = _costEvaluator.CalculateCost(cost, resources);
            if (amount.IsFailure)
                return amount.Error;

            if (!resources.TryGetValue(cost.ResourceId, out var pool))
                return $"Resource not found: {cost.ResourceId}";

            if (!cost.AllowOverdraft && !pool.CanAfford(amount.Value))
            {
                var resourceName = pool.Definition?.DisplayName ?? cost.ResourceId;
                return $"Insufficient {resourceName}: has {pool.Current}, needs {amount.Value}";
            }
        }

        if (alternativeCosts.Count > 0 && GetAffordableOptions(alternativeCosts, resources).Count == 0)
        {
            var optionDescriptions = string.Join(" OR ", alternativeCosts.Select(o => o.Description));
            return $"Cannot afford any alternative: {optionDescriptions}";
        }

        return null;
    }

    private float CalculateCostOrFallback(ResourceCost cost, IReadOnlyDictionary<string, ResourcePool> resources)
    {
        var result = _costEvaluator.CalculateCost(cost, resources);
        return result.IsSuccess ? result.Value : cost.Amount;
    }
}

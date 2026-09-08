using Core.Combat.Models;
using Core.Common;
using Core.Logging;

namespace Core.Combat.TurnOrder;

/// <summary>
/// Calculadora condicional: Usa regras customizadas baseadas em condições
/// Permite lógica complexa de ordenação
/// </summary>
public class ConditionalTurnOrderCalculator : ITurnOrderCalculator
{
    private readonly ILogger? _logger;
    private readonly Func<CombatState, List<string>> _customOrderFunction;
    
    public TurnStrategy Strategy => TurnStrategy.CONDITIONAL;
    
    /// <summary>
    /// Construtor
    /// </summary>
    /// <param name="customOrderFunction">Função customizada que calcula a ordem de turnos</param>
    /// <param name="logger">Logger opcional</param>
    public ConditionalTurnOrderCalculator(
        Func<CombatState, List<string>> customOrderFunction,
        ILogger? logger = null)
    {
        _customOrderFunction = customOrderFunction ?? throw new ArgumentNullException(nameof(customOrderFunction));
        _logger = logger;
    }
    
    public Result<List<string>> CalculateTurnOrder(CombatState state)
    {
        try
        {
            var turnOrder = _customOrderFunction(state);
            
            _logger?.LogDebug($"Conditional turn order calculated: {string.Join(", ", turnOrder)}");
            
            return Result<List<string>>.Success(turnOrder);
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Error calculating conditional turn order: {ex.Message}");
            return Result<List<string>>.Failure($"Failed to calculate conditional turn order: {ex.Message}");
        }
    }
    
    public Result Initialize(CombatState state)
    {
        _logger?.LogDebug("Conditional turn order calculator initialized");
        return Result.Success();
    }
    
    public Result UpdateAfterAction(CombatState state, string actorId)
    {
        // Ordem condicional pode mudar a cada turno, mas não precisa de atualização específica
        return Result.Success();
    }
    
    /// <summary>
    /// Creates a calculator ordered by the percentage of a configured resource.
    /// Entities with the lowest percentage act first.
    /// </summary>
    public static ConditionalTurnOrderCalculator CreateResourceBasedCalculator(
        string resourceId,
        float? missingResourceValue = null,
        ILogger? logger = null)
    {
        ValidateResourceConfiguration(resourceId, missingResourceValue);
        return new ConditionalTurnOrderCalculator(state =>
        {
            return state.GetAllActors()
                .Select(entity => (
                    entity.InstanceId,
                    Percent: GetResourcePercent(entity, resourceId, missingResourceValue)))
                .OrderBy(e => e.Percent)
                .ThenBy(e => e.InstanceId, StringComparer.Ordinal)
                .Select(e => e.InstanceId)
                .ToList();
        }, logger);
    }
    
    /// <summary>
    /// Creates a controller-priority order without assigning semantic roles to definitions.
    /// </summary>
    public static ConditionalTurnOrderCalculator CreateAiFirstCalculator(ILogger? logger = null)
    {
        return new ConditionalTurnOrderCalculator(state =>
        {
            return state.GetAllActors()
                .OrderBy(actor => actor.ControllerBinding.Kind == ControllerKind.AI ? 0 : 1)
                .ThenBy(actor => actor.InstanceId, StringComparer.Ordinal)
                .Select(actor => actor.InstanceId)
                .ToList();
        }, logger);
    }
    
    /// <summary>
    /// Creates a hybrid calculator: actors below the configured priority-resource
    /// threshold act first, then all actors are ordered by a second resource.
    /// </summary>
    public static ConditionalTurnOrderCalculator CreateHybridCalculator(
        string orderResourceId,
        string priorityResourceId,
        float priorityThreshold,
        float? missingResourceValue = null,
        ILogger? logger = null)
    {
        ValidateResourceConfiguration(orderResourceId, missingResourceValue);
        ValidateResourceConfiguration(priorityResourceId, missingResourceValue);
        if (!float.IsFinite(priorityThreshold) || priorityThreshold is < 0f or > 1f)
            throw new ArgumentOutOfRangeException(nameof(priorityThreshold));
        return new ConditionalTurnOrderCalculator(state =>
        {
            return state.GetAllActors()
                .Select(entity =>
                {
                    var priority = GetResourcePercent(entity, priorityResourceId, missingResourceValue);
                    return (
                        entity.InstanceId,
                        Order: GetResourceCurrent(entity, orderResourceId, missingResourceValue),
                        HasPriority: priority < priorityThreshold);
                })
                .OrderByDescending(e => e.HasPriority)
                .ThenByDescending(e => e.Order)
                .ThenBy(e => e.InstanceId, StringComparer.Ordinal)
                .Select(e => e.InstanceId)
                .ToList();
        }, logger);
    }

    private static float GetResourcePercent(
        CombatActorState entity,
        string resourceId,
        float? missingResourceValue)
    {
        if (entity.ResourceState.Resources.TryGetValue(resourceId, out var resource))
            return resource.GetPercentage() / 100f;
        if (missingResourceValue.HasValue)
            return missingResourceValue.Value;
        throw new InvalidOperationException(
            $"Conditional turn-order resource '{resourceId}' is missing from actor '{entity.InstanceId}'");
    }

    private static float GetResourceCurrent(
        CombatActorState entity,
        string resourceId,
        float? missingResourceValue)
    {
        if (entity.ResourceState.Resources.TryGetValue(resourceId, out var resource))
            return resource.Current;
        if (missingResourceValue.HasValue)
            return missingResourceValue.Value;
        throw new InvalidOperationException(
            $"Conditional turn-order resource '{resourceId}' is missing from actor '{entity.InstanceId}'");
    }

    private static void ValidateResourceConfiguration(
        string resourceId,
        float? missingResourceValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        if (missingResourceValue.HasValue && !float.IsFinite(missingResourceValue.Value))
            throw new ArgumentOutOfRangeException(nameof(missingResourceValue));
    }
}

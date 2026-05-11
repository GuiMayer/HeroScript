using Core.Combat.Models;
using Core.Common;
using Core.Events;
using Core.Events.Domain;
using Core.Logging;
using Core.Math;

namespace Core.Resources;

/// <summary>
/// Processes resource regeneration based on timing and context.
/// Handles both fixed-amount and formula-based regeneration.
/// </summary>
public class ResourceRegenerationProcessor : IResourceRegenerationProcessor
{
    private readonly IMathEngine _mathEngine;
    private readonly ILogger _logger;
    private readonly IEventBus? _eventBus;

    public ResourceRegenerationProcessor(
        IMathEngine mathEngine,
        ILogger logger,
        IEventBus? eventBus = null)
    {
        _mathEngine = mathEngine ?? throw new ArgumentNullException(nameof(mathEngine));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _eventBus = eventBus;
    }

    public Result<EntityResourceState> ProcessRegeneration(
        EntityResourceState entityResourceState,
        RegenerationTiming timing,
        Dictionary<string, float>? context = null)
    {
        if (entityResourceState == null)
            return Result<EntityResourceState>.Failure("EntityResourceState cannot be null");

        var updatedResources = new Dictionary<string, ResourcePool>();
        var hasChanges = false;

        foreach (var (resourceId, pool) in entityResourceState.Resources)
        {
            var def = pool.Definition;

            // Skip if regeneration is not enabled
            if (def.Regeneration == null || !def.Regeneration.Enabled)
            {
                updatedResources[resourceId] = pool;
                continue;
            }

            // Skip if timing doesn't match
            if (def.Regeneration.Timing != timing)
            {
                updatedResources[resourceId] = pool;
                continue;
            }

            // Calculate regeneration amount
            var amount = CalculateRegenerationAmount(def, pool, context);

            // Apply regeneration
            ResourcePool newPool;
            if (amount >= 0)
            {
                newPool = pool.Gain(amount);
            }
            else
            {
                // Handle negative regeneration (e.g., block zeroing)
                var absAmount = System.Math.Abs(amount);
                if (absAmount > pool.Current)
                {
                    // If trying to spend more than available, set to minimum
                    newPool = pool.Set(pool.Minimum);
                }
                else
                {
                    newPool = pool.Spend(absAmount);
                }
            }

            updatedResources[resourceId] = newPool;

            // Track if any changes occurred
            if (System.Math.Abs(newPool.Current - pool.Current) > 0.001f)
            {
                hasChanges = true;

                // Publish event
                _eventBus?.Publish(new ResourceRegeneratedEvent
                {
                    EntityId = entityResourceState.EntityId,
                    ResourceId = resourceId,
                    OldValue = pool.Current,
                    NewValue = newPool.Current,
                    Amount = amount,
                    Timing = timing
                });

                _logger.LogDebug(
                    $"Regenerated {resourceId} for {entityResourceState.EntityId}: " +
                    $"{pool.Current:F2} -> {newPool.Current:F2} ({amount:+0.##;-0.##}) at {timing}");
            }
            else
            {
                updatedResources[resourceId] = pool; // No change, keep original
            }
        }

        // Return updated state
        var newState = new EntityResourceState
        {
            EntityId = entityResourceState.EntityId,
            Resources = updatedResources
        };

        if (hasChanges)
        {
            _logger.LogDebug($"Processed regeneration for {entityResourceState.EntityId} at {timing}");
        }

        return Result<EntityResourceState>.Success(newState);
    }

    public float CalculateRegenerationAmount(
        ResourceDefinition definition,
        ResourcePool currentPool,
        Dictionary<string, float>? context = null)
    {
        var regen = definition.Regeneration;
        if (regen == null || !regen.Enabled)
            return 0f;

        // If has formula, use MathEngine
        if (!string.IsNullOrEmpty(regen.Formula))
        {
            try
            {
                // Create context with pool values
                var formulaContext = new Dictionary<string, float>
                {
                    ["current"] = currentPool.Current,
                    ["max"] = currentPool.Maximum,
                    ["min"] = currentPool.Minimum,
                    ["percent"] = currentPool.GetPercentage()
                };

                // Merge with external context
                if (context != null)
                {
                    foreach (var (key, value) in context)
                    {
                        formulaContext[key] = value;
                    }
                }

                // Evaluate formula
                var expr = _mathEngine.BuildFromFormula(regen.Formula, 0, formulaContext);
                var result = expr.Build();

                _logger.LogDebug(
                    $"Evaluated regeneration formula '{regen.Formula}' for {definition.ResourceId}: {result:F2}");

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    $"Failed to evaluate regeneration formula '{regen.Formula}' for {definition.ResourceId}: {ex.Message}. " +
                    $"Falling back to AmountPerTurn.");

                // Fallback to fixed amount
                return regen.AmountPerTurn;
            }
        }

        // Use fixed amount
        return regen.AmountPerTurn;
    }
}

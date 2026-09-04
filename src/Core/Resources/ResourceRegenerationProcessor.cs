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

    public Result<ResourceSet> ProcessRegeneration(
        ResourceSet resourceState,
        RegenerationTiming timing,
        Dictionary<string, float>? context = null)
    {
        if (resourceState == null)
            return Result<ResourceSet>.Failure("ResourceSet cannot be null");

        var mutations = new List<ResolvedResourceMutation>();

        foreach (var (resourceId, pool) in resourceState.Resources)
        {
            var def = pool.Definition;

            // Skip if regeneration is not enabled
            if (def.Regeneration == null || !def.Regeneration.Enabled)
                continue;

            // Skip if timing doesn't match
            if (def.Regeneration.Timing != timing)
                continue;

            // Calculate regeneration amount
            var amount = CalculateRegenerationAmount(def, pool, context);

            mutations.Add(new ResolvedResourceMutation
            {
                MutationId = $"regeneration:{timing}:{resourceId}",
                ResourceId = resourceId,
                Operation = amount >= 0
                    ? ResourceMutationOperation.Add
                    : ResourceMutationOperation.Subtract,
                Value = System.Math.Abs(amount)
            });
        }

        var applied = resourceState.Apply(mutations);
        if (applied.IsFailure)
            return Result<ResourceSet>.Failure(applied.Error);

        foreach (var record in applied.Value.Records.Where(record =>
                     System.Math.Abs(record.CurrentValue - record.PreviousValue) > 0.001f))
        {
            var amount = record.CurrentValue - record.PreviousValue;
            _eventBus?.Publish(new ResourceRegeneratedEvent
            {
                OwnerId = resourceState.OwnerId,
                ResourceId = record.ResourceId,
                OldValue = record.PreviousValue,
                NewValue = record.CurrentValue,
                Amount = amount,
                Timing = timing
            });
            _logger.LogDebug(
                $"Regenerated {record.ResourceId} for {resourceState.OwnerId}: " +
                $"{record.PreviousValue:F2} -> {record.CurrentValue:F2} ({amount:+0.##;-0.##}) at {timing}");
        }

        if (applied.Value.Records.Count > 0)
            _logger.LogDebug($"Processed regeneration for {resourceState.OwnerId} at {timing}");

        return Result<ResourceSet>.Success(applied.Value.State);
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

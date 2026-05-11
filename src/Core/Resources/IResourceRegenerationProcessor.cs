using Core.Combat.Models;
using Core.Common;

namespace Core.Resources;

/// <summary>
/// Interface for processing resource regeneration.
/// Handles regeneration logic for resources based on timing and context.
/// </summary>
public interface IResourceRegenerationProcessor
{
    /// <summary>
    /// Processes regeneration of resources for an entity.
    /// </summary>
    /// <param name="entityResourceState">Current resource state of the entity</param>
    /// <param name="timing">Timing of regeneration (START_TURN, END_TURN, OUT_OF_COMBAT)</param>
    /// <param name="context">Optional context for formula evaluation (turn_number, game state, etc.)</param>
    /// <returns>Result containing updated EntityResourceState or failure</returns>
    Result<EntityResourceState> ProcessRegeneration(
        EntityResourceState entityResourceState,
        RegenerationTiming timing,
        Dictionary<string, float>? context = null);
    
    /// <summary>
    /// Calculates the regeneration amount for a resource without applying it.
    /// </summary>
    /// <param name="definition">Resource definition containing regeneration config</param>
    /// <param name="currentPool">Current state of the resource pool</param>
    /// <param name="context">Optional context for formula evaluation</param>
    /// <returns>Amount to regenerate (positive for gain, negative for loss)</returns>
    float CalculateRegenerationAmount(
        ResourceDefinition definition,
        ResourcePool currentPool,
        Dictionary<string, float>? context = null);
}

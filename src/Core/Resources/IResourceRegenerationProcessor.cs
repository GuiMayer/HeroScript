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
    /// <param name="resourceState">Current resource state of the owner</param>
    /// <param name="timing">Timing of regeneration (START_TURN, END_TURN, OUT_OF_COMBAT)</param>
    /// <param name="context">Optional immutable context for formula evaluation</param>
    /// <returns>Updated immutable state plus ordered mutation records, or failure</returns>
    Result<ResourceRegenerationResult> ProcessRegeneration(
        ResourceSet resourceState,
        RegenerationTiming timing,
        ResourceRegenerationContext? context = null);
}

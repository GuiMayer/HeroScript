using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Config;

/// <summary>
/// Interface for resource loading service.
/// Handles loading and merging of JSON resources across configuration inheritance chains.
/// </summary>
public interface IResourceLoader
{
    /// <summary>
    /// Loads a resource by traversing the configuration inheritance chain.
    /// </summary>
    /// <param name="relativePath">Relative path to the resource (e.g., "resources/items.json")</param>
    /// <param name="configChain">Configuration inheritance chain, from most specific to base</param>
    /// <param name="strictMode">If true, throws on missing resources; if false, returns empty dictionary</param>
    /// <returns>Merged resource data as a dictionary of JsonElements</returns>
    Dictionary<string, JsonElement> LoadResource(
        string relativePath,
        IEnumerable<string> configChain,
        bool strictMode = false);

    /// <summary>
    /// Asynchronously loads a resource by traversing the configuration inheritance chain.
    /// </summary>
    /// <param name="relativePath">Relative path to the resource (e.g., "resources/items.json")</param>
    /// <param name="configChain">Configuration inheritance chain, from most specific to base</param>
    /// <param name="strictMode">If true, throws on missing resources; if false, returns empty dictionary</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Merged resource data as a dictionary of JsonElements</returns>
    Task<Dictionary<string, JsonElement>> LoadResourceAsync(
        string relativePath,
        IEnumerable<string> configChain,
        bool strictMode = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates all cached resources, forcing reload on next access.
    /// </summary>
    void InvalidateCache();

    /// <summary>
    /// Invalidates a specific cached resource.
    /// </summary>
    /// <param name="relativePath">Relative path to the resource to invalidate</param>
    void InvalidateCache(string relativePath);

    /// <summary>
    /// Gets the origins of resources (which config each resource came from).
    /// </summary>
    /// <param name="relativePath">Relative path to the resource</param>
    /// <returns>Dictionary mapping resource keys to their origin config names</returns>
    Dictionary<string, string> GetResourceOrigins(string relativePath);

    /// <summary>
    /// Gets cache statistics for monitoring and diagnostics.
    /// </summary>
    /// <returns>Dictionary containing cache metrics (hit rate, size, etc.)</returns>
    Dictionary<string, object> GetCacheStats();
}

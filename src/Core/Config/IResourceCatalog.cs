using System.Text.Json;
using Core.Common;

namespace Core.Config;

/// <summary>
/// Lazy typed view over JSON resources for a specific resource directory.
/// </summary>
public interface IResourceCatalog<TDefinition>
{
    Result<TDefinition> Get(string id, string configName);
    IReadOnlyList<string> Discover(string configName);
    IReadOnlyList<TDefinition> GetAll(string configName);
    void Invalidate(string? id = null);
}

public sealed class ResourceCatalog<TDefinition> : IResourceCatalog<TDefinition>
{
    private readonly IConfigManager _configManager;
    private readonly IResourceLoader _resourceLoader;
    private readonly string _relativeDirectory;
    private readonly Func<TDefinition, string> _idSelector;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly Dictionary<string, TDefinition> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public ResourceCatalog(
        IConfigManager configManager,
        IResourceLoader resourceLoader,
        string relativeDirectory,
        Func<TDefinition, string> idSelector,
        JsonSerializerOptions? jsonOptions = null)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _resourceLoader = resourceLoader ?? throw new ArgumentNullException(nameof(resourceLoader));
        _relativeDirectory = string.IsNullOrWhiteSpace(relativeDirectory)
            ? throw new ArgumentException("Relative directory cannot be empty", nameof(relativeDirectory))
            : relativeDirectory.Trim('/').Trim('\\');
        _idSelector = idSelector ?? throw new ArgumentNullException(nameof(idSelector));
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    }

    public Result<TDefinition> Get(string id, string configName)
    {
        if (string.IsNullOrWhiteSpace(id))
            return Result<TDefinition>.Failure("Resource id cannot be empty");

        var cacheKey = BuildCacheKey(configName, id);
        lock (_lock)
        {
            if (_cache.TryGetValue(cacheKey, out var cached))
                return Result<TDefinition>.Success(cached);
        }

        try
        {
            var chain = _configManager.ResolveInheritanceChain(configName).ToArray();
            var data = _resourceLoader.LoadResource($"{_relativeDirectory}/{id}.json", chain, strictMode: false);
            if (data.Count == 0)
                return Result<TDefinition>.Failure($"Resource not found: {id}");

            var element = data.TryGetValue(id, out var exact) ? exact : data.Values.First();
            var definition = JsonSerializer.Deserialize<TDefinition>(element.GetRawText(), _jsonOptions);
            if (definition == null)
                return Result<TDefinition>.Failure($"Failed to deserialize resource: {id}");

            var resolvedId = _idSelector(definition);
            if (string.IsNullOrWhiteSpace(resolvedId))
                return Result<TDefinition>.Failure($"Resource '{id}' has an empty id");

            lock (_lock)
            {
                _cache[cacheKey] = definition;
            }

            return Result<TDefinition>.Success(definition);
        }
        catch (Exception ex)
        {
            return Result<TDefinition>.Failure($"Failed to load resource '{id}': {ex.Message}", ex);
        }
    }

    public IReadOnlyList<string> Discover(string configName)
    {
        var chain = _configManager.ResolveInheritanceChain(configName);
        return _resourceLoader.DiscoverResources(_relativeDirectory, chain).ToList();
    }

    public IReadOnlyList<TDefinition> GetAll(string configName)
    {
        return Discover(configName)
            .Select(id => Get(id, configName))
            .Where(result => result.IsSuccess)
            .Select(result => result.Value)
            .ToList();
    }

    public void Invalidate(string? id = null)
    {
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                _cache.Clear();
                return;
            }

            foreach (var key in _cache.Keys.Where(k => k.EndsWith($"::{id}", StringComparison.OrdinalIgnoreCase)).ToList())
                _cache.Remove(key);
        }
    }

    private static string BuildCacheKey(string configName, string id) => $"{configName}::{id}";
}

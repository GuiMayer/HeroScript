using System.Text.Json;
using Core.Common;
using Core.Caching;

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

public sealed class ResourceCatalog<TDefinition> : IResourceCatalog<TDefinition>, ICacheService
{
    private readonly IConfigManager _configManager;
    private readonly IResourceLoader _resourceLoader;
    private readonly string _relativeDirectory;
    private readonly Func<TDefinition, string> _idSelector;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly Dictionary<string, TDefinition> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private long _hits;
    private long _misses;
    private DateTime? _lastInvalidation;

    public string CacheName => $"Definitions:{_relativeDirectory.Replace('\\', '/')}";
    public CacheLayer Layer => CacheLayer.Definition;

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
            {
                _hits++;
                return Result<TDefinition>.Success(cached);
            }
            _misses++;
        }

        try
        {
            var chain = _configManager.ResolveInheritanceChain(configName).ToArray();
            var data = _resourceLoader.LoadResource($"{_relativeDirectory}/{id}.json", chain, strictMode: true);
            if (data.Count == 0)
                return Result<TDefinition>.Failure($"Resource not found: {id}");

            if (!data.TryGetValue(id, out var element))
                return Result<TDefinition>.Failure($"Resource file must declare definition id: {id}");
            var definition = JsonSerializer.Deserialize<TDefinition>(element.GetRawText(), _jsonOptions);
            if (definition == null)
                return Result<TDefinition>.Failure($"Failed to deserialize resource: {id}");

            var resolvedId = _idSelector(definition);
            if (string.IsNullOrWhiteSpace(resolvedId))
                return Result<TDefinition>.Failure($"Resource '{id}' has an empty id");
            if (!string.Equals(resolvedId, id, StringComparison.Ordinal))
            {
                return Result<TDefinition>.Failure(
                    $"Resource identity mismatch: expected {id}, got {resolvedId}");
            }

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
                _lastInvalidation = DateTime.UtcNow; // nondeterministic-boundary: operational telemetry
                return;
            }

            foreach (var key in _cache.Keys.Where(k => k.EndsWith($"::{id}", StringComparison.OrdinalIgnoreCase)).ToList())
                _cache.Remove(key);
            _lastInvalidation = DateTime.UtcNow; // nondeterministic-boundary: operational telemetry
        }
    }

    CacheServiceStats ICacheService.GetStats()
    {
        lock (_lock)
        {
            var requests = _hits + _misses;
            return new CacheServiceStats
            {
                CacheName = CacheName,
                Capacity = int.MaxValue,
                Count = _cache.Count,
                Hits = _hits,
                Misses = _misses,
                HitRate = requests == 0 ? 0 : (double)_hits / requests,
                LastInvalidation = _lastInvalidation
            };
        }
    }

    private static string BuildCacheKey(string configName, string id) => $"{configName}::{id}";
}

using System.Collections.Immutable;
using Core.Common;
using Core.Config;
using Core.Determinism;

namespace Core.Content;

/// <summary>
/// Builds a canonical manifest from the effective resources consumed by the
/// engine. A manifest remains addressable by revision after a refresh so active
/// runs keep an immutable content identity.
/// </summary>
public sealed class ContentManifestProvider : IContentManifestProvider
{
    private static readonly ImmutableArray<ContentSource> Sources =
    [
        new("actions", "actions"),
        new("cards", "cards"),
        new("entities", "Entities"),
        new("resources", "resources"),
        new("formulas", "Pipelines", file => file.Equals("MathFormulas", StringComparison.OrdinalIgnoreCase)),
        new("pipelines", "Pipelines", file => file.StartsWith("DamagePipeline", StringComparison.OrdinalIgnoreCase)),
        new("status-effects", "StatusEffects"),
        new("modifiers", "Modifiers"),
        new("gambits", "Gambits"),
        new("runs", "runs"),
        new("card-pools", "card-pools"),
        new("card-selections", "card-selections"),
        new("shops", "shops"),
        new("preparations", "preparations"),
        new("activation-rules", "combat-turn-rules"),
        new("phase-sequences", "phase-sequences")
    ];

    private readonly IConfigManager _configManager;
    private readonly IResourceLoader _resourceLoader;
    private readonly object _lock = new();
    private readonly Dictionary<string, ContentManifest> _currentByConfig =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ContentManifest> _manifestsByRevision =
        new(StringComparer.Ordinal);

    public ContentManifestProvider(IConfigManager configManager, IResourceLoader resourceLoader)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _resourceLoader = resourceLoader ?? throw new ArgumentNullException(nameof(resourceLoader));
    }

    public Result<ContentManifest> GetManifest(string configName)
    {
        if (string.IsNullOrWhiteSpace(configName))
            return Result<ContentManifest>.Failure("Config name is required");

        lock (_lock)
        {
            if (_currentByConfig.TryGetValue(configName, out var cached))
                return Result<ContentManifest>.Success(cached);
        }

        return BuildAndStore(configName);
    }

    public Result<ContentManifest> RefreshManifest(string configName)
    {
        if (string.IsNullOrWhiteSpace(configName))
            return Result<ContentManifest>.Failure("Config name is required");

        return BuildAndStore(configName);
    }

    public Result<ContentManifest> GetByRevision(string revision)
    {
        if (string.IsNullOrWhiteSpace(revision))
            return Result<ContentManifest>.Failure("Content revision is required");

        lock (_lock)
        {
            return _manifestsByRevision.TryGetValue(revision, out var manifest)
                ? Result<ContentManifest>.Success(manifest)
                : Result<ContentManifest>.Failure($"Content revision not found: {revision}");
        }
    }

    public IReadOnlyList<ContentManifest> GetKnownManifests()
    {
        lock (_lock)
        {
            return _manifestsByRevision.Values
                .OrderBy(manifest => manifest.ConfigName, StringComparer.Ordinal)
                .ThenBy(manifest => manifest.Revision, StringComparer.Ordinal)
                .ToImmutableArray();
        }
    }

    private Result<ContentManifest> BuildAndStore(string configName)
    {
        var built = BuildManifest(configName);
        if (built.IsFailure)
            return built;

        lock (_lock)
        {
            _currentByConfig[configName] = built.Value;
            _manifestsByRevision[built.Value.Revision] = built.Value;
        }

        return built;
    }

    private Result<ContentManifest> BuildManifest(string configName)
    {
        try
        {
            var configChain = _configManager.ResolveInheritanceChain(configName).ToImmutableArray();
            if (configChain.IsEmpty)
                return Result<ContentManifest>.Failure($"Configuration chain is empty: {configName}");

            var artifacts = new List<ContentArtifactManifest>();
            foreach (var source in Sources)
            {
                var files = _resourceLoader
                    .DiscoverResources(source.Directory, configChain, "*.json")
                    .Where(source.Includes)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(file => file, StringComparer.Ordinal);

                foreach (var file in files)
                {
                    var path = $"{source.Directory}/{file}.json";
                    var definitions = _resourceLoader.LoadResource(path, configChain, strictMode: false);
                    if (definitions.Count == 0)
                        continue;

                    artifacts.Add(new ContentArtifactManifest
                    {
                        Kind = source.Kind,
                        Path = path.Replace('\\', '/'),
                        Hash = CanonicalJson.ComputeHash(definitions),
                        DefinitionCount = definitions.Count
                    });
                }
            }

            if (artifacts.Count == 0)
                return Result<ContentManifest>.Failure($"No effective content found for configuration: {configName}");

            var orderedArtifacts = artifacts
                .OrderBy(artifact => artifact.Kind, StringComparer.Ordinal)
                .ThenBy(artifact => artifact.Path, StringComparer.Ordinal)
                .ToImmutableArray();
            var payload = new ContentManifestPayload(1, configName, configChain, orderedArtifacts);

            return Result<ContentManifest>.Success(new ContentManifest
            {
                SchemaVersion = payload.SchemaVersion,
                ConfigName = payload.ConfigName,
                ConfigChain = payload.ConfigChain,
                Artifacts = payload.Artifacts,
                Revision = CanonicalJson.ComputeHash(payload)
            });
        }
        catch (Exception ex)
        {
            return Result<ContentManifest>.Failure(
                $"Failed to build content manifest for '{configName}': {ex.Message}",
                ex);
        }
    }

    private sealed record ContentSource(
        string Kind,
        string Directory,
        Func<string, bool>? Filter = null)
    {
        public bool Includes(string file) => Filter?.Invoke(file) ?? true;
    }

    private sealed record ContentManifestPayload(
        int SchemaVersion,
        string ConfigName,
        IReadOnlyList<string> ConfigChain,
        IReadOnlyList<ContentArtifactManifest> Artifacts);
}

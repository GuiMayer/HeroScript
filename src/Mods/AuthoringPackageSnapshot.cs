using System.Text.Json;
using Core.Common;

namespace Mods;

/// <summary>A directory provider's manifests, settings and declared source bytes captured for one authoring request.</summary>
public sealed class AuthoringPackageSnapshot : IPackageProvider
{
    private readonly PackageDiscovery _discovery;
    private readonly IReadOnlyDictionary<string, ReadOnlyMemory<byte>> _sources;

    internal AuthoringPackageSnapshot(string providerId, PackageDiscovery discovery,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> sources)
    {
        ProviderId = providerId;
        _discovery = discovery;
        _sources = sources;
    }

    public string ProviderId { get; }
    public IEnumerable<KeyValuePair<string, ReadOnlyMemory<byte>>> Sources => _sources;
    public ReadOnlyMemory<byte>? FindSource(string key) =>
        _sources.TryGetValue(key, out var bytes) ? bytes : (ReadOnlyMemory<byte>?)null;

    public Task<PackageDiscovery> DiscoverAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_discovery);

    public Task<Result<JsonElement>> ReadJsonAsync(PackageSource package, string relativePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (package.ProviderId != ProviderId || !_sources.TryGetValue(SourceKey(package, relativePath), out var bytes))
            return Task.FromResult(Result<JsonElement>.Failure($"Package file not found: {relativePath}"));

        try
        {
            using var document = JsonDocument.Parse(DirectoryPackageProvider.WithoutBom(bytes));
            return Task.FromResult(Result<JsonElement>.Success(document.RootElement.Clone()));
        }
        catch (JsonException exception)
        {
            return Task.FromResult(Result<JsonElement>.Failure($"Invalid package JSON '{relativePath}': {exception.Message}"));
        }
    }

    public AuthoringPackageSnapshot WithCandidate(PackageSource original, PackageManifest manifest,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> changes, IEnumerable<string>? removals = null)
    {
        var sources = new Dictionary<string, ReadOnlyMemory<byte>>(_sources, StringComparer.Ordinal);
        foreach (var change in changes)
            sources[change.Key] = change.Value;
        if (removals is not null)
            foreach (var path in removals)
                sources.Remove(path);
        return new(ProviderId, _discovery with
        {
            Packages = _discovery.Packages.Select(package =>
                package == original ? package with { Manifest = manifest } : package).ToArray()
        }, sources);
    }

    public static string SourceKey(PackageSource package, string relativePath)
    {
        var path = relativePath.Replace('\\', '/');
        var normalized = string.Join('/', path.Split('/', StringSplitOptions.RemoveEmptyEntries));
        return package.SourceId == "." ? normalized : $"{package.SourceId}/{normalized}";
    }
}

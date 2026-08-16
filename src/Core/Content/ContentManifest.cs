using System.Collections.Immutable;

namespace Core.Content;

/// <summary>
/// Immutable description of one effective content artifact after configuration
/// inheritance and delta merging have been applied.
/// </summary>
public sealed record ContentArtifactManifest
{
    public string Kind { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string Hash { get; init; } = string.Empty;
    public int DefinitionCount { get; init; }
}

/// <summary>
/// Immutable content inputs that may influence a deterministic run.
/// Revision is the canonical SHA-256 hash of the manifest payload.
/// </summary>
public sealed record ContentManifest
{
    private ImmutableArray<string> _configChain = [];
    private ImmutableArray<ContentArtifactManifest> _artifacts = [];

    public int SchemaVersion { get; init; } = 1;
    public string ConfigName { get; init; } = string.Empty;
    public IReadOnlyList<string> ConfigChain
    {
        get => _configChain;
        init => _configChain = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<ContentArtifactManifest> Artifacts
    {
        get => _artifacts;
        init => _artifacts = value?.ToImmutableArray() ?? [];
    }
    public string Revision { get; init; } = string.Empty;
}

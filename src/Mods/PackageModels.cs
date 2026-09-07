using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Content;

namespace Mods;

public enum PackageDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public enum PackageContentOperation
{
    Definition,
    Patch
}

public enum PackagePatchStrategy
{
    Merge,
    Replace
}

public sealed record PackageDependency
{
    public string PackageId { get; init; } = string.Empty;
    public string VersionRange { get; init; } = "*";
}

public sealed record PackageConflict
{
    public string PackageId { get; init; } = string.Empty;
    public string VersionRange { get; init; } = "*";
}

public sealed record PackagePatchTarget
{
    public string? PackageId { get; init; }
    public string Kind { get; init; } = string.Empty;
    public string ArtifactPath { get; init; } = string.Empty;
    public string? DefinitionId { get; init; }
}

/// <summary>
/// Declares one data file as either a new definition artifact or an explicit
/// patch. Package files are never inferred from the host environment.
/// </summary>
public sealed record PackageContentEnvelope
{
    public int SchemaVersion { get; init; } = 1;

    [JsonConverter(typeof(JsonStringEnumConverter<PackageContentOperation>))]
    public PackageContentOperation Operation { get; init; }

    public string SourcePath { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string? ArtifactPath { get; init; }
    public string? DefinitionId { get; init; }
    public PackagePatchTarget? Target { get; init; }
    public string? ExpectedHash { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<PackagePatchStrategy>))]
    public PackagePatchStrategy PatchStrategy { get; init; } = PackagePatchStrategy.Merge;
}

public sealed record PackageManifest
{
    private ImmutableArray<PackageDependency> _dependencies = [];
    private ImmutableArray<PackageConflict> _conflicts = [];
    private ImmutableArray<PackageContentEnvelope> _content = [];

    public int SchemaVersion { get; init; } = 1;
    public string PackageId { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public bool DataOnly { get; init; } = true;
    public IReadOnlyList<PackageDependency> Dependencies
    {
        get => _dependencies;
        init => _dependencies = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<PackageConflict> Conflicts
    {
        get => _conflicts;
        init => _conflicts = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<PackageContentEnvelope> Content
    {
        get => _content;
        init => _content = value?.ToImmutableArray() ?? [];
    }
}

public sealed record SettingPackageReference
{
    public string PackageId { get; init; } = string.Empty;
    public string VersionRange { get; init; } = "*";
}

public sealed record SettingDefinition
{
    private ImmutableArray<SettingPackageReference> _packages = [];

    public int SchemaVersion { get; init; } = 1;
    public string SettingId { get; init; } = string.Empty;
    public IReadOnlyList<SettingPackageReference> Packages
    {
        get => _packages;
        init => _packages = value?.ToImmutableArray() ?? [];
    }
}

public sealed record PackageDiagnostic
{
    public PackageDiagnosticSeverity Severity { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? PackageId { get; init; }
    public string? SourcePath { get; init; }
}

public sealed record PackageSource
{
    public string ProviderId { get; init; } = string.Empty;
    public string SourceId { get; init; } = string.Empty;
    public PackageManifest Manifest { get; init; } = new();
}

public sealed record SettingSource
{
    public string ProviderId { get; init; } = string.Empty;
    public string PackageId { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public SettingDefinition Definition { get; init; } = new();
}

public sealed record PackageDiscovery
{
    private ImmutableArray<PackageSource> _packages = [];
    private ImmutableArray<SettingSource> _settings = [];
    private ImmutableArray<PackageDiagnostic> _diagnostics = [];

    public IReadOnlyList<PackageSource> Packages
    {
        get => _packages;
        init => _packages = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<SettingSource> Settings
    {
        get => _settings;
        init => _settings = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<PackageDiagnostic> Diagnostics
    {
        get => _diagnostics;
        init => _diagnostics = value?.ToImmutableArray() ?? [];
    }
}

public sealed record ContentProvenanceEntry
{
    public string Kind { get; init; } = string.Empty;
    public string ArtifactPath { get; init; } = string.Empty;
    public string? DefinitionId { get; init; }
    public string JsonPointer { get; init; } = string.Empty;
    public string PackageId { get; init; } = string.Empty;
    public string PackageVersion { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public PackageContentOperation Operation { get; init; }
}

public sealed record CompiledPackageReference(string PackageId, string Version);

public sealed record SettingCompilation
{
    private ImmutableArray<CompiledPackageReference> _packages = [];
    private ImmutableArray<ContentProvenanceEntry> _provenance = [];
    private ImmutableArray<PackageDiagnostic> _diagnostics = [];

    public SettingDefinition Setting { get; init; } = new();
    public ContentBundle Bundle { get; init; } = new();
    public IReadOnlyList<CompiledPackageReference> Packages
    {
        get => _packages;
        init => _packages = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<ContentProvenanceEntry> Provenance
    {
        get => _provenance;
        init => _provenance = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<PackageDiagnostic> Diagnostics
    {
        get => _diagnostics;
        init => _diagnostics = value?.ToImmutableArray() ?? [];
    }
}

public sealed record SettingCatalog
{
    private ImmutableArray<SettingSource> _settings = [];
    private ImmutableArray<PackageSource> _packages = [];
    private ImmutableArray<PackageDiagnostic> _diagnostics = [];

    public IReadOnlyList<SettingSource> Settings
    {
        get => _settings;
        init => _settings = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<PackageSource> Packages
    {
        get => _packages;
        init => _packages = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<PackageDiagnostic> Diagnostics
    {
        get => _diagnostics;
        init => _diagnostics = value?.ToImmutableArray() ?? [];
    }
}

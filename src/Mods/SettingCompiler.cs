using System.Collections.Immutable;
using System.Text.Json;
using Core.Common;
using Core.Content;
using Core.Determinism;

namespace Mods;

public interface ISettingCompiler
{
    Task<SettingCatalog> GetCatalogAsync(CancellationToken cancellationToken = default);
    Task<Result<SettingCompilation>> CompileAsync(
        string settingId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves a package dependency graph and compiles its data into the same
/// immutable ContentBundle consumed by gameplay runtimes.
/// </summary>
public sealed class SettingCompiler : ISettingCompiler, ISettingBundleCompiler
{
    private readonly ImmutableArray<IPackageProvider> _providers;
    private readonly IContentKindRegistry _kinds;

    public SettingCompiler(
        IEnumerable<IPackageProvider> providers,
        IContentKindRegistry? kinds = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers
            .OrderBy(provider => provider.ProviderId, StringComparer.Ordinal)
            .ToImmutableArray();
        if (_providers.Length == 0)
            throw new ArgumentException("At least one explicit package provider is required", nameof(providers));
        var duplicate = _providers
            .GroupBy(provider => provider.ProviderId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
            throw new ArgumentException($"Package provider id collision: {duplicate.Key}", nameof(providers));
        _kinds = kinds ?? ContentKindRegistry.Default;
    }

    public async Task<SettingCatalog> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        var packages = new List<PackageSource>();
        var settings = new List<SettingSource>();
        var diagnostics = new List<PackageDiagnostic>();
        foreach (var provider in _providers)
        {
            try
            {
                var discovery = await provider.DiscoverAsync(cancellationToken).ConfigureAwait(false);
                packages.AddRange(discovery.Packages);
                settings.AddRange(discovery.Settings);
                diagnostics.AddRange(discovery.Diagnostics);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                diagnostics.Add(new PackageDiagnostic
                {
                    Severity = PackageDiagnosticSeverity.Error,
                    Code = "PACKAGE_PROVIDER_FAILED",
                    Message = $"Package provider '{provider.ProviderId}' failed: {exception.Message}"
                });
            }
        }

        ValidateCatalog(packages, settings, diagnostics);
        return new SettingCatalog
        {
            Packages = packages
                .OrderBy(package => package.Manifest.PackageId, StringComparer.Ordinal)
                .ThenBy(package => ParseVersion(package.Manifest.Version))
                .ThenBy(package => package.ProviderId, StringComparer.Ordinal)
                .ThenBy(package => package.SourceId, StringComparer.Ordinal)
                .ToImmutableArray(),
            Settings = settings
                .OrderBy(setting => setting.Definition.SettingId, StringComparer.Ordinal)
                .ThenBy(setting => setting.ProviderId, StringComparer.Ordinal)
                .ThenBy(setting => setting.SourcePath, StringComparer.Ordinal)
                .ToImmutableArray(),
            Diagnostics = diagnostics
                .Distinct()
                .OrderBy(diagnostic => diagnostic.Severity)
                .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.PackageId, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.SourcePath, StringComparer.Ordinal)
                .ToImmutableArray()
        };
    }

    public async Task<Result<ContentBundle>> CompileBundleAsync(
        string settingId,
        CancellationToken cancellationToken = default)
    {
        var compiled = await CompileAsync(settingId, cancellationToken).ConfigureAwait(false);
        return compiled.IsFailure
            ? Result<ContentBundle>.Failure(compiled.Error)
            : Result<ContentBundle>.Success(compiled.Value.Bundle);
    }

    public async Task<Result<SettingCompilation>> CompileAsync(
        string settingId,
        CancellationToken cancellationToken = default)
    {
        if (!IsIdentifier(settingId))
            return Result<SettingCompilation>.Failure("Setting id contains unsupported characters");
        var catalog = await GetCatalogAsync(cancellationToken).ConfigureAwait(false);
        var catalogErrors = catalog.Diagnostics
            .Where(diagnostic => diagnostic.Severity == PackageDiagnosticSeverity.Error)
            .ToArray();
        if (catalogErrors.Length > 0)
            return Result<SettingCompilation>.Failure(FormatDiagnostics(catalogErrors));
        var settingMatches = catalog.Settings
            .Where(candidate => string.Equals(
                candidate.Definition.SettingId,
                settingId,
                StringComparison.Ordinal))
            .ToArray();
        if (settingMatches.Length == 0)
            return Result<SettingCompilation>.Failure($"Setting not found: {settingId}");
        if (settingMatches.Length != 1)
            return Result<SettingCompilation>.Failure($"Setting is defined more than once: {settingId}");

        var resolution = ResolvePackages(settingMatches[0].Definition, catalog.Packages);
        if (resolution.IsFailure)
            return Result<SettingCompilation>.Failure(resolution.Error);
        var ordered = OrderPackages(resolution.Value);
        if (ordered.IsFailure)
            return Result<SettingCompilation>.Failure(ordered.Error);
        var compiled = await CompileContentAsync(
            settingMatches[0].Definition,
            ordered.Value,
            cancellationToken).ConfigureAwait(false);
        if (compiled.IsFailure)
            return compiled;
        return Result<SettingCompilation>.Success(compiled.Value with
        {
            Diagnostics = catalog.Diagnostics
                .Where(diagnostic => diagnostic.Severity != PackageDiagnosticSeverity.Error)
                .ToImmutableArray()
        });
    }

    private async Task<Result<SettingCompilation>> CompileContentAsync(
        SettingDefinition setting,
        IReadOnlyList<PackageSource> packages,
        CancellationToken cancellationToken)
    {
        var providerById = _providers.ToDictionary(provider => provider.ProviderId, StringComparer.Ordinal);
        var artifacts = new Dictionary<string, CompiledArtifact>(StringComparer.Ordinal);
        var artifactPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var provenance = new Dictionary<string, ContentProvenanceEntry>(StringComparer.Ordinal);
        foreach (var package in packages)
        {
            var packageManifest = package.Manifest;
            // Content order is explicit domain data in the manifest. This lets
            // chained patches declare their precondition sequence without ever
            // depending on filesystem enumeration order.
            foreach (var envelope in packageManifest.Content)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var validation = ValidateEnvelope(packageManifest, envelope);
                if (validation != null)
                    return Result<SettingCompilation>.Failure(validation);
                var payloadResult = await providerById[package.ProviderId]
                    .ReadJsonAsync(package, envelope.SourcePath, cancellationToken)
                    .ConfigureAwait(false);
                if (payloadResult.IsFailure)
                    return Result<SettingCompilation>.Failure(payloadResult.Error);

                if (envelope.Operation == PackageContentOperation.Definition)
                {
                    var path = NormalizeArtifactPath(envelope.ArtifactPath!);
                    if (artifactPaths.TryGetValue(path, out var collided))
                    {
                        return Result<SettingCompilation>.Failure(
                            string.Equals(collided, path, StringComparison.Ordinal)
                                ? $"Content definition conflict at {path}; use an explicit patch envelope"
                                : $"Content artifact path case collision: {collided} and {path}");
                    }
                    var definitionPayload = string.IsNullOrWhiteSpace(envelope.DefinitionId)
                        ? payloadResult.Value.Clone()
                        : JsonSerializer.SerializeToElement(new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                        {
                            [envelope.DefinitionId] = payloadResult.Value.Clone()
                        });
                    if (definitionPayload.ValueKind != JsonValueKind.Object)
                        return Result<SettingCompilation>.Failure($"Definition artifact must be a JSON object: {path}");
                    var normalizedPayload = NormalizeArtifact(envelope.Kind, definitionPayload);
                    if (normalizedPayload.IsFailure)
                        return Result<SettingCompilation>.Failure(normalizedPayload.Error);
                    artifactPaths[path] = path;
                    artifacts[path] = new CompiledArtifact(
                        envelope.Kind,
                        normalizedPayload.Value,
                        packageManifest.PackageId);
                    RecordDefinitionProvenance(
                        provenance,
                        path,
                        envelope.Kind,
                        normalizedPayload.Value,
                        package,
                        envelope);
                    continue;
                }

                var target = envelope.Target!;
                var targetPath = NormalizeArtifactPath(target.ArtifactPath);
                if (!artifactPaths.TryGetValue(targetPath, out var canonicalTargetPath) ||
                    !artifacts.TryGetValue(canonicalTargetPath, out var current))
                {
                    return Result<SettingCompilation>.Failure(
                        $"Patch target does not exist: {target.Kind}/{targetPath}");
                }
                if (!string.Equals(targetPath, canonicalTargetPath, StringComparison.Ordinal))
                    return Result<SettingCompilation>.Failure($"Patch target path has incorrect case: {targetPath}");
                if (!string.Equals(current.Kind, target.Kind, StringComparison.Ordinal))
                    return Result<SettingCompilation>.Failure(
                        $"Patch target kind mismatch at {targetPath}: expected {current.Kind}, got {target.Kind}");
                if (!string.IsNullOrWhiteSpace(target.PackageId) &&
                    !string.Equals(target.PackageId, current.DefiningPackageId, StringComparison.Ordinal))
                {
                    return Result<SettingCompilation>.Failure(
                        $"Patch target package mismatch at {targetPath}: expected {current.DefiningPackageId}, got {target.PackageId}");
                }

                var selected = SelectTarget(current.Payload, target.DefinitionId);
                if (selected.IsFailure)
                    return Result<SettingCompilation>.Failure(selected.Error);
                var actualHash = CanonicalJson.ComputeHash(selected.Value);
                if (!string.Equals(actualHash, envelope.ExpectedHash, StringComparison.Ordinal))
                {
                    return Result<SettingCompilation>.Failure(
                        $"Patch precondition failed at {targetPath}{FormatDefinition(target.DefinitionId)}: " +
                        $"expected {envelope.ExpectedHash}, actual {actualHash}");
                }
                var patchedValue = envelope.PatchStrategy == PackagePatchStrategy.Replace
                    ? payloadResult.Value.Clone()
                    : ApplyMergePatch(selected.Value, payloadResult.Value);
                var updatedPayload = ReplaceTarget(current.Payload, target.DefinitionId, patchedValue);
                if (updatedPayload.IsFailure)
                    return Result<SettingCompilation>.Failure(updatedPayload.Error);
                var normalizedPatch = NormalizeArtifact(current.Kind, updatedPayload.Value);
                if (normalizedPatch.IsFailure)
                    return Result<SettingCompilation>.Failure(normalizedPatch.Error);
                artifacts[canonicalTargetPath] = current with { Payload = normalizedPatch.Value };
                RecordPatchProvenance(
                    provenance,
                    canonicalTargetPath,
                    target.Kind,
                    target.DefinitionId,
                    payloadResult.Value,
                    package,
                    envelope);
            }
        }

        if (artifacts.Count == 0)
            return Result<SettingCompilation>.Failure($"Setting compiles no content: {setting.SettingId}");
        var artifactPayloads = artifacts
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToImmutableDictionary(
                pair => pair.Key,
                pair => pair.Value.Payload.Clone(),
                StringComparer.Ordinal);
        var artifactManifests = artifacts
            .Select(pair => new ContentArtifactManifest
            {
                Kind = pair.Value.Kind,
                Path = pair.Key,
                Hash = CanonicalJson.ComputeHash(pair.Value.Payload),
                DefinitionCount = pair.Value.Payload.ValueKind == JsonValueKind.Object
                    ? pair.Value.Payload.EnumerateObject().Count()
                    : 0
            })
            .OrderBy(artifact => artifact.Kind, StringComparer.Ordinal)
            .ThenBy(artifact => artifact.Path, StringComparer.Ordinal)
            .ToImmutableArray();
        var packageChain = packages
            .Select(package => $"{package.Manifest.PackageId}@{package.Manifest.Version}")
            .ToImmutableArray();
        var manifestPayload = new ContentManifestPayload(
            1,
            setting.SettingId,
            packageChain,
            artifactManifests);
        var manifest = new ContentManifest
        {
            SchemaVersion = manifestPayload.SchemaVersion,
            ConfigName = manifestPayload.ConfigName,
            ConfigChain = manifestPayload.ConfigChain,
            Artifacts = manifestPayload.Artifacts,
            Revision = CanonicalJson.ComputeHash(manifestPayload)
        };
        return Result<SettingCompilation>.Success(new SettingCompilation
        {
            Setting = setting,
            Packages = packages
                .Select(package => new CompiledPackageReference(
                    package.Manifest.PackageId,
                    package.Manifest.Version))
                .ToImmutableArray(),
            Bundle = new ContentBundle
            {
                Manifest = manifest,
                Artifacts = artifactPayloads
            },
            Provenance = provenance.Values
                .OrderBy(entry => entry.Kind, StringComparer.Ordinal)
                .ThenBy(entry => entry.ArtifactPath, StringComparer.Ordinal)
                .ThenBy(entry => entry.DefinitionId, StringComparer.Ordinal)
                .ThenBy(entry => entry.JsonPointer, StringComparer.Ordinal)
                .ToImmutableArray()
        });
    }

    private static Result<IReadOnlyList<PackageSource>> ResolvePackages(
        SettingDefinition setting,
        IReadOnlyList<PackageSource> packages)
    {
        if (setting.Packages.Count == 0)
            return Result<IReadOnlyList<PackageSource>>.Failure($"Setting has no packages: {setting.SettingId}");
        var available = packages
            .GroupBy(package => package.Manifest.PackageId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(package => ParseVersion(package.Manifest.Version)).ToArray(),
                StringComparer.Ordinal);
        var requirements = setting.Packages
            .GroupBy(reference => reference.PackageId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(reference => reference.VersionRange).ToList(),
                StringComparer.Ordinal);
        var selected = new Dictionary<string, PackageSource>(StringComparer.Ordinal);
        var solved = Solve(available, requirements, selected, out var error);
        if (solved == null)
            return Result<IReadOnlyList<PackageSource>>.Failure(error ?? "Package dependency graph cannot be resolved");
        foreach (var package in solved.Values.OrderBy(package => package.Manifest.PackageId, StringComparer.Ordinal))
        {
            foreach (var conflict in package.Manifest.Conflicts.OrderBy(conflict => conflict.PackageId, StringComparer.Ordinal))
            {
                if (!solved.TryGetValue(conflict.PackageId, out var candidate))
                    continue;
                if (!SemanticVersionRange.TryParse(conflict.VersionRange, out var range))
                    return Result<IReadOnlyList<PackageSource>>.Failure(
                        $"Invalid conflict version range in {package.Manifest.PackageId}: {conflict.VersionRange}");
                if (range.Contains(ParseVersion(candidate.Manifest.Version)))
                {
                    return Result<IReadOnlyList<PackageSource>>.Failure(
                        $"Package conflict: {package.Manifest.PackageId} conflicts with " +
                        $"{candidate.Manifest.PackageId}@{candidate.Manifest.Version}");
                }
            }
        }
        return Result<IReadOnlyList<PackageSource>>.Success(solved.Values.ToArray());
    }

    private static Dictionary<string, PackageSource>? Solve(
        IReadOnlyDictionary<string, PackageSource[]> available,
        Dictionary<string, List<string>> requirements,
        Dictionary<string, PackageSource> selected,
        out string? error)
    {
        error = null;
        foreach (var (id, selectedPackage) in selected)
        {
            if (!requirements.TryGetValue(id, out var ranges))
                continue;
            foreach (var expression in ranges)
            {
                if (!SemanticVersionRange.TryParse(expression, out var range))
                {
                    error = $"Invalid version range for {id}: {expression}";
                    return null;
                }
                if (!range.Contains(ParseVersion(selectedPackage.Manifest.Version)))
                    return null;
            }
        }

        var unresolved = requirements.Keys
            .Where(id => !selected.ContainsKey(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (unresolved == null)
            return selected;
        if (!available.TryGetValue(unresolved, out var candidates))
        {
            error = $"Required package is missing: {unresolved}";
            return null;
        }
        var parsedRanges = new List<SemanticVersionRange>();
        foreach (var expression in requirements[unresolved])
        {
            if (!SemanticVersionRange.TryParse(expression, out var range))
            {
                error = $"Invalid version range for {unresolved}: {expression}";
                return null;
            }
            parsedRanges.Add(range);
        }
        foreach (var candidate in candidates.Where(candidate =>
                     parsedRanges.All(range => range.Contains(ParseVersion(candidate.Manifest.Version)))))
        {
            var nextSelected = new Dictionary<string, PackageSource>(selected, StringComparer.Ordinal)
            {
                [unresolved] = candidate
            };
            var nextRequirements = requirements.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToList(),
                StringComparer.Ordinal);
            var dependencyError = false;
            foreach (var dependency in candidate.Manifest.Dependencies)
            {
                if (!SemanticVersionRange.TryParse(dependency.VersionRange, out _))
                {
                    error = $"Invalid dependency version range in {candidate.Manifest.PackageId}: {dependency.VersionRange}";
                    dependencyError = true;
                    break;
                }
                if (!nextRequirements.TryGetValue(dependency.PackageId, out var ranges))
                    nextRequirements[dependency.PackageId] = ranges = [];
                ranges.Add(dependency.VersionRange);
            }
            if (dependencyError)
                return null;
            var solved = Solve(available, nextRequirements, nextSelected, out var nestedError);
            if (solved != null)
                return solved;
            error ??= nestedError;
        }
        error ??= $"No compatible version of package {unresolved} satisfies " +
                  string.Join(" and ", requirements[unresolved]);
        return null;
    }

    private static Result<IReadOnlyList<PackageSource>> OrderPackages(IReadOnlyList<PackageSource> packages)
    {
        var byId = packages.ToDictionary(package => package.Manifest.PackageId, StringComparer.Ordinal);
        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        var ordered = new List<PackageSource>();
        var path = new List<string>();
        string? error = null;

        bool Visit(string id)
        {
            if (state.TryGetValue(id, out var mark))
            {
                if (mark == 2) return true;
                var cycleStart = path.IndexOf(id);
                error = "Package dependency cycle: " + string.Join(" -> ", path.Skip(cycleStart).Append(id));
                return false;
            }
            state[id] = 1;
            path.Add(id);
            foreach (var dependency in byId[id].Manifest.Dependencies
                         .Select(dependency => dependency.PackageId)
                         .Distinct(StringComparer.Ordinal)
                         .OrderBy(dependency => dependency, StringComparer.Ordinal))
            {
                if (!byId.ContainsKey(dependency) || !Visit(dependency))
                    return false;
            }
            path.RemoveAt(path.Count - 1);
            state[id] = 2;
            ordered.Add(byId[id]);
            return true;
        }

        foreach (var id in byId.Keys.OrderBy(id => id, StringComparer.Ordinal))
        {
            if (!Visit(id))
                return Result<IReadOnlyList<PackageSource>>.Failure(error ?? $"Invalid package graph at {id}");
        }
        return Result<IReadOnlyList<PackageSource>>.Success(ordered);
    }

    private static void ValidateCatalog(
        IReadOnlyList<PackageSource> packages,
        IReadOnlyList<SettingSource> settings,
        ICollection<PackageDiagnostic> diagnostics)
    {
        foreach (var group in packages.GroupBy(package => package.Manifest.PackageId, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Select(package => package.Manifest.PackageId).Distinct(StringComparer.Ordinal).Count() > 1)
                diagnostics.Add(Error("PACKAGE_ID_CASE_COLLISION", $"Package ids differ only by case: {group.Key}"));
        }
        foreach (var group in packages.GroupBy(
                     package => $"{package.Manifest.PackageId}\n{package.Manifest.Version}",
                     StringComparer.Ordinal))
        {
            if (group.Count() > 1)
                diagnostics.Add(Error("PACKAGE_DUPLICATE", $"Package version is defined more than once: {group.Key.Replace('\n', '@')}"));
        }
        foreach (var package in packages)
        {
            var manifest = package.Manifest;
            if (manifest.SchemaVersion != 1)
                diagnostics.Add(Error("PACKAGE_SCHEMA_UNSUPPORTED", $"Unsupported package schema: {manifest.SchemaVersion}", manifest.PackageId));
            if (!IsIdentifier(manifest.PackageId))
                diagnostics.Add(Error("PACKAGE_ID_INVALID", $"Invalid package id: {manifest.PackageId}", manifest.PackageId));
            if (!SemanticVersion.TryParse(manifest.Version, out _))
                diagnostics.Add(Error("PACKAGE_VERSION_INVALID", $"Invalid semantic version: {manifest.Version}", manifest.PackageId));
            if (!manifest.DataOnly)
                diagnostics.Add(Error("PACKAGE_CODE_REJECTED", "Only data-only packages are supported", manifest.PackageId));
            foreach (var dependency in manifest.Dependencies)
            {
                if (!IsIdentifier(dependency.PackageId) || !SemanticVersionRange.TryParse(dependency.VersionRange, out _))
                    diagnostics.Add(Error("PACKAGE_DEPENDENCY_INVALID", $"Invalid dependency: {dependency.PackageId} {dependency.VersionRange}", manifest.PackageId));
            }
            foreach (var conflict in manifest.Conflicts)
            {
                if (!IsIdentifier(conflict.PackageId) || !SemanticVersionRange.TryParse(conflict.VersionRange, out _))
                    diagnostics.Add(Error("PACKAGE_CONFLICT_INVALID", $"Invalid conflict: {conflict.PackageId} {conflict.VersionRange}", manifest.PackageId));
            }
        }
        foreach (var group in settings.GroupBy(setting => setting.Definition.SettingId, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Count() > 1)
                diagnostics.Add(Error("SETTING_DUPLICATE", $"Setting is defined more than once: {group.Key}"));
        }
        foreach (var setting in settings)
        {
            if (setting.Definition.SchemaVersion != 1)
                diagnostics.Add(Error("SETTING_SCHEMA_UNSUPPORTED", $"Unsupported setting schema: {setting.Definition.SchemaVersion}", setting.PackageId));
            if (!IsIdentifier(setting.Definition.SettingId))
                diagnostics.Add(Error("SETTING_ID_INVALID", $"Invalid setting id: {setting.Definition.SettingId}", setting.PackageId));
            foreach (var reference in setting.Definition.Packages)
            {
                if (!IsIdentifier(reference.PackageId) || !SemanticVersionRange.TryParse(reference.VersionRange, out _))
                    diagnostics.Add(Error("SETTING_PACKAGE_INVALID", $"Invalid setting package: {reference.PackageId} {reference.VersionRange}", setting.PackageId));
            }
        }
    }

    private string? ValidateEnvelope(PackageManifest manifest, PackageContentEnvelope envelope)
    {
        if (envelope.SchemaVersion != 1)
            return $"Unsupported content envelope schema in {manifest.PackageId}: {envelope.SchemaVersion}";
        if (!IsSafeRelativePath(envelope.SourcePath))
            return $"Invalid content source path in {manifest.PackageId}: {envelope.SourcePath}";
        if (envelope.Operation == PackageContentOperation.Definition)
        {
            var descriptor = _kinds.Get(envelope.Kind);
            if (descriptor.IsFailure)
                return descriptor.Error;
            if (string.IsNullOrWhiteSpace(envelope.Kind) || !IsSafeRelativePath(envelope.ArtifactPath))
                return $"Definition envelope requires kind and artifactPath in {manifest.PackageId}";
            if (!NormalizeArtifactPath(envelope.ArtifactPath!).StartsWith(
                    descriptor.Value.CanonicalDirectory + "/",
                    StringComparison.Ordinal))
            {
                return $"Definition artifact path is not canonical for {envelope.Kind}: {envelope.ArtifactPath}";
            }
            if (envelope.DefinitionId != null && !IsIdentifier(envelope.DefinitionId))
                return $"Definition envelope has an invalid definitionId in {manifest.PackageId}";
            if (envelope.Target != null || envelope.ExpectedHash != null)
                return $"Definition envelope cannot declare patch target/precondition in {manifest.PackageId}";
            return null;
        }
        if (envelope.Target == null || string.IsNullOrWhiteSpace(envelope.Target.Kind) ||
            !IsSafeRelativePath(envelope.Target.ArtifactPath))
        {
            return $"Patch envelope requires an explicit target in {manifest.PackageId}";
        }
        var targetDescriptor = _kinds.Get(envelope.Target.Kind);
        if (targetDescriptor.IsFailure)
            return targetDescriptor.Error;
        if (!NormalizeArtifactPath(envelope.Target.ArtifactPath).StartsWith(
                targetDescriptor.Value.CanonicalDirectory + "/",
                StringComparison.Ordinal))
        {
            return $"Patch artifact path is not canonical for {envelope.Target.Kind}: {envelope.Target.ArtifactPath}";
        }
        if (!IsSha256(envelope.ExpectedHash))
            return $"Patch envelope requires a lowercase SHA-256 expectedHash in {manifest.PackageId}";
        if (!string.IsNullOrWhiteSpace(envelope.Kind) || envelope.ArtifactPath != null || envelope.DefinitionId != null)
            return $"Patch envelope must describe content through target, not definition fields, in {manifest.PackageId}";
        return null;
    }

    private Result<JsonElement> NormalizeArtifact(string kind, JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            return Result<JsonElement>.Failure($"Content artifact for '{kind}' must be an object");
        var definitions = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var definition in payload.EnumerateObject())
        {
            var normalized = _kinds.Normalize(kind, definition.Name, definition.Value);
            if (normalized.IsFailure)
                return Result<JsonElement>.Failure(normalized.Error);
            var validation = _kinds.Validate(kind, definition.Name, normalized.Value);
            if (validation.IsFailure)
                return Result<JsonElement>.Failure(validation.Error);
            definitions[definition.Name] = normalized.Value;
        }
        return Result<JsonElement>.Success(JsonSerializer.SerializeToElement(definitions));
    }

    private static Result<JsonElement> SelectTarget(JsonElement artifact, string? definitionId)
    {
        if (string.IsNullOrWhiteSpace(definitionId))
            return Result<JsonElement>.Success(artifact.Clone());
        if (artifact.ValueKind != JsonValueKind.Object || !artifact.TryGetProperty(definitionId, out var definition))
            return Result<JsonElement>.Failure($"Patch definition target does not exist: {definitionId}");
        return Result<JsonElement>.Success(definition.Clone());
    }

    private static Result<JsonElement> ReplaceTarget(
        JsonElement artifact,
        string? definitionId,
        JsonElement replacement)
    {
        if (string.IsNullOrWhiteSpace(definitionId))
            return Result<JsonElement>.Success(replacement.Clone());
        if (artifact.ValueKind != JsonValueKind.Object)
            return Result<JsonElement>.Failure($"Patch definition target requires an object artifact: {definitionId}");
        var values = artifact.EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.Clone(),
            StringComparer.Ordinal);
        if (!values.ContainsKey(definitionId))
            return Result<JsonElement>.Failure($"Patch definition target does not exist: {definitionId}");
        values[definitionId] = replacement.Clone();
        return Result<JsonElement>.Success(JsonSerializer.SerializeToElement(values));
    }

    private static JsonElement ApplyMergePatch(JsonElement current, JsonElement patch)
    {
        if (patch.ValueKind != JsonValueKind.Object)
            return patch.Clone();
        var values = current.ValueKind == JsonValueKind.Object
            ? current.EnumerateObject().ToDictionary(
                property => property.Name,
                property => property.Value.Clone(),
                StringComparer.Ordinal)
            : new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in patch.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Null)
            {
                values.Remove(property.Name);
                continue;
            }
            values[property.Name] = values.TryGetValue(property.Name, out var previous)
                ? ApplyMergePatch(previous, property.Value)
                : property.Value.Clone();
        }
        return JsonSerializer.SerializeToElement(values);
    }

    private static void RecordDefinitionProvenance(
        IDictionary<string, ContentProvenanceEntry> provenance,
        string artifactPath,
        string kind,
        JsonElement payload,
        PackageSource package,
        PackageContentEnvelope envelope)
    {
        foreach (var definition in payload.EnumerateObject())
        {
            RecordProvenanceTree(
                provenance,
                artifactPath,
                kind,
                definition.Name,
                $"/{EscapePointer(definition.Name)}",
                definition.Value,
                package,
                envelope);
        }
    }

    private static void RecordPatchProvenance(
        IDictionary<string, ContentProvenanceEntry> provenance,
        string artifactPath,
        string kind,
        string? definitionId,
        JsonElement patch,
        PackageSource package,
        PackageContentEnvelope envelope)
    {
        var pointer = string.IsNullOrWhiteSpace(definitionId)
            ? string.Empty
            : $"/{EscapePointer(definitionId)}";
        RecordProvenanceTree(
            provenance,
            artifactPath,
            kind,
            definitionId,
            pointer,
            patch,
            package,
            envelope);
    }

    private static void RecordProvenanceTree(
        IDictionary<string, ContentProvenanceEntry> provenance,
        string artifactPath,
        string kind,
        string? definitionId,
        string pointer,
        JsonElement value,
        PackageSource package,
        PackageContentEnvelope envelope)
    {
        var keyPrefix = $"{artifactPath}\n";
        if (value.ValueKind == JsonValueKind.Null)
        {
            foreach (var key in provenance.Keys
                         .Where(key => key.StartsWith(keyPrefix + pointer, StringComparison.Ordinal))
                         .ToArray())
                provenance.Remove(key);
            return;
        }
        provenance[keyPrefix + pointer] = new ContentProvenanceEntry
        {
            Kind = kind,
            ArtifactPath = artifactPath,
            DefinitionId = definitionId,
            JsonPointer = pointer,
            PackageId = package.Manifest.PackageId,
            PackageVersion = package.Manifest.Version,
            SourcePath = envelope.SourcePath,
            Operation = envelope.Operation
        };
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
                RecordProvenanceTree(provenance, artifactPath, kind, definitionId,
                    $"{pointer}/{EscapePointer(property.Name)}", property.Value, package, envelope);
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
                RecordProvenanceTree(provenance, artifactPath, kind, definitionId,
                    $"{pointer}/{index++}", item, package, envelope);
        }
    }

    private static string EscapePointer(string value) => value.Replace("~", "~0").Replace("/", "~1");
    private static string NormalizeArtifactPath(string path) => path.Replace('\\', '/');
    private static string FormatDefinition(string? definitionId) =>
        string.IsNullOrWhiteSpace(definitionId) ? string.Empty : $"/{definitionId}";
    private static bool IsIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 128 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');
    private static bool IsSafeRelativePath(string? path) =>
        !string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path) &&
        !path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is "." or "..");
    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static SemanticVersion ParseVersion(string value) =>
        SemanticVersion.TryParse(value, out var version) ? version : default;
    private static string FormatDiagnostics(IEnumerable<PackageDiagnostic> diagnostics) =>
        string.Join("; ", diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
    private static PackageDiagnostic Error(string code, string message, string? packageId = null) => new()
    {
        Severity = PackageDiagnosticSeverity.Error,
        Code = code,
        Message = message,
        PackageId = packageId
    };

    private sealed record CompiledArtifact(string Kind, JsonElement Payload, string DefiningPackageId);
    private sealed record ContentManifestPayload(
        int SchemaVersion,
        string ConfigName,
        IReadOnlyList<string> ConfigChain,
        IReadOnlyList<ContentArtifactManifest> Artifacts);
}

using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Common;
using Core.Config;
using Core.Determinism;

namespace Core.Content;

public sealed record ContentBundle
{
    public ContentManifest Manifest { get; init; } = new();
    public ImmutableDictionary<string, JsonElement> Artifacts { get; init; } =
        ImmutableDictionary<string, JsonElement>.Empty.WithComparers(StringComparer.Ordinal);
}

public sealed record ContentDraft
{
    public Guid DraftId { get; init; }
    public int Version { get; init; }
    public string ConfigName { get; init; } = "default";
    public ContentBundle Bundle { get; init; } = new();
    public string? PublishedRevision { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
}

public sealed record ContentValidationResult
{
    public bool IsValid => Errors.IsEmpty;
    public ImmutableArray<string> Errors { get; init; } = [];
    public ImmutableArray<string> Warnings { get; init; } = [];
    public ContentManifest? Manifest { get; init; }
}

public interface IContentPublicationService
{
    Task<Result<ContentDraft>> CreateDraftAsync(string configName, CancellationToken cancellationToken = default);
    Task<Result<ContentDraft>> RefreshDraftAsync(Guid draftId, int expectedVersion, CancellationToken cancellationToken = default);
    Task<Result<ContentDraft>> GetDraftAsync(Guid draftId, CancellationToken cancellationToken = default);
    ContentValidationResult Validate(ContentBundle bundle);
    Task<ContentValidationResult> ValidateDraftAsync(Guid draftId, CancellationToken cancellationToken = default);
    Task<Result<ContentBundle>> PublishDraftAsync(Guid draftId, int expectedVersion, CancellationToken cancellationToken = default);
    Task<Result<ContentBundle>> GetPublishedAsync(string revision, CancellationToken cancellationToken = default);
    Task<Result<ContentBundle>> ResolveBundleAsync(
        string revision,
        string? configName = null,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ContentManifest>> GetPublishedManifestsAsync(CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyDictionary<string, JsonElement>>> GetDefinitionsAsync(
        string kind,
        string? revision,
        string configName,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Captures effective resource overlays into reviewable drafts and publishes
/// immutable, content-addressed bundles. Authoring timestamps and draft ids are
/// operational metadata and never participate in the revision hash.
/// </summary>
public sealed class ContentPublicationService : IContentPublicationService, IDisposable
{
    private readonly string _storePath;
    private readonly IContentManifestProvider _manifests;
    private readonly IResourceLoader _resourceLoader;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions;

    public ContentPublicationService(
        string storePath,
        IContentManifestProvider manifests,
        IResourceLoader resourceLoader)
    {
        _storePath = storePath;
        _manifests = manifests;
        _resourceLoader = resourceLoader;
        Directory.CreateDirectory(GetDraftDirectory());
        Directory.CreateDirectory(GetPublishedDirectory());
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    public async Task<Result<ContentDraft>> CreateDraftAsync(
        string configName,
        CancellationToken cancellationToken = default)
    {
        var captured = Capture(configName);
        if (captured.IsFailure)
            return Result<ContentDraft>.Failure(captured.Error);

        var now = DateTime.UtcNow; // nondeterministic-boundary: admin authoring metadata
        var draft = new ContentDraft
        {
            DraftId = Guid.NewGuid(), // nondeterministic-boundary: admin authoring identity
            Version = 1,
            ConfigName = configName,
            Bundle = captured.Value,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        try
        {
            await SaveDraftAsync(draft, overwrite: false, cancellationToken).ConfigureAwait(false);
            return Result<ContentDraft>.Success(draft);
        }
        catch (Exception exception)
        {
            return Result<ContentDraft>.Failure($"Failed to create content draft: {exception.Message}", exception);
        }
    }

    public async Task<Result<ContentDraft>> RefreshDraftAsync(
        Guid draftId,
        int expectedVersion,
        CancellationToken cancellationToken = default)
    {
        var loaded = await GetDraftAsync(draftId, cancellationToken).ConfigureAwait(false);
        if (loaded.IsFailure)
            return loaded;
        if (loaded.Value.PublishedRevision != null)
            return Result<ContentDraft>.Failure($"Published draft is immutable: {draftId}");
        if (loaded.Value.Version != expectedVersion)
            return Result<ContentDraft>.Failure(
                $"VERSION_CONFLICT: expected draft version {expectedVersion}, current is {loaded.Value.Version}");

        var captured = Capture(loaded.Value.ConfigName);
        if (captured.IsFailure)
            return Result<ContentDraft>.Failure(captured.Error);
        var updated = loaded.Value with
        {
            Version = checked(loaded.Value.Version + 1),
            Bundle = captured.Value,
            UpdatedAtUtc = DateTime.UtcNow // nondeterministic-boundary: admin authoring metadata
        };

        try
        {
            await SaveDraftAsync(updated, overwrite: true, cancellationToken).ConfigureAwait(false);
            return Result<ContentDraft>.Success(updated);
        }
        catch (Exception exception)
        {
            return Result<ContentDraft>.Failure($"Failed to refresh content draft: {exception.Message}", exception);
        }
    }

    public async Task<Result<ContentDraft>> GetDraftAsync(
        Guid draftId,
        CancellationToken cancellationToken = default)
    {
        var path = GetDraftPath(draftId);
        if (!File.Exists(path))
            return Result<ContentDraft>.Failure($"Content draft not found: {draftId}");

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            var draft = JsonSerializer.Deserialize<ContentDraft>(json, _jsonOptions);
            return draft == null
                ? Result<ContentDraft>.Failure($"Content draft is invalid: {draftId}")
                : Result<ContentDraft>.Success(draft);
        }
        catch (Exception exception)
        {
            return Result<ContentDraft>.Failure($"Failed to read content draft: {exception.Message}", exception);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<ContentValidationResult> ValidateDraftAsync(
        Guid draftId,
        CancellationToken cancellationToken = default)
    {
        var loaded = await GetDraftAsync(draftId, cancellationToken).ConfigureAwait(false);
        return loaded.IsFailure
            ? new ContentValidationResult { Errors = [loaded.Error] }
            : Validate(loaded.Value.Bundle);
    }

    public ContentValidationResult Validate(ContentBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        return ValidateBundle(bundle);
    }

    public async Task<Result<ContentBundle>> PublishDraftAsync(
        Guid draftId,
        int expectedVersion,
        CancellationToken cancellationToken = default)
    {
        var loaded = await GetDraftAsync(draftId, cancellationToken).ConfigureAwait(false);
        if (loaded.IsFailure)
            return Result<ContentBundle>.Failure(loaded.Error);
        if (loaded.Value.Version != expectedVersion)
            return Result<ContentBundle>.Failure(
                $"VERSION_CONFLICT: expected draft version {expectedVersion}, current is {loaded.Value.Version}");

        var validation = Validate(loaded.Value.Bundle);
        if (!validation.IsValid)
            return Result<ContentBundle>.Failure(string.Join("; ", validation.Errors));

        var bundle = loaded.Value.Bundle;
        var publishedPath = GetPublishedPath(bundle.Manifest.Revision);
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(publishedPath))
                await WriteAtomicAsync(publishedPath, bundle, overwrite: false, cancellationToken).ConfigureAwait(false);

            var publishedDraft = loaded.Value with
            {
                PublishedRevision = bundle.Manifest.Revision,
                UpdatedAtUtc = DateTime.UtcNow // nondeterministic-boundary: admin authoring metadata
            };
            await WriteAtomicAsync(GetDraftPath(draftId), publishedDraft, overwrite: true, cancellationToken)
                .ConfigureAwait(false);

            var activation = _manifests.ActivatePublishedManifest(bundle.Manifest);
            if (activation.IsFailure)
                return Result<ContentBundle>.Failure(activation.Error);
            return Result<ContentBundle>.Success(bundle);
        }
        catch (Exception exception)
        {
            return Result<ContentBundle>.Failure($"Failed to publish content draft: {exception.Message}", exception);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<Result<ContentBundle>> GetPublishedAsync(
        string revision,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(revision))
            return Result<ContentBundle>.Failure("Content revision is required");
        if (!IsContentRevision(revision))
            return Result<ContentBundle>.Failure("Content revision must be a lowercase SHA-256 hash");
        var path = GetPublishedPath(revision);
        if (!File.Exists(path))
            return Result<ContentBundle>.Failure($"Published content revision not found: {revision}");

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            var bundle = JsonSerializer.Deserialize<ContentBundle>(json, _jsonOptions);
            if (bundle == null)
                return Result<ContentBundle>.Failure($"Published content revision is invalid: {revision}");
            _manifests.RegisterPublishedManifest(bundle.Manifest);
            return Result<ContentBundle>.Success(bundle);
        }
        catch (Exception exception)
        {
            return Result<ContentBundle>.Failure($"Failed to read published content: {exception.Message}", exception);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<IReadOnlyList<ContentManifest>> GetPublishedManifestsAsync(
        CancellationToken cancellationToken = default)
    {
        var manifests = new List<ContentManifest>();
        foreach (var path in Directory.GetFiles(GetPublishedDirectory(), "*.json").OrderBy(path => path, StringComparer.Ordinal))
        {
            var revision = Path.GetFileNameWithoutExtension(path);
            var bundle = await GetPublishedAsync(revision, cancellationToken).ConfigureAwait(false);
            if (bundle.IsSuccess)
                manifests.Add(bundle.Value.Manifest);
        }
        return manifests;
    }

    public async Task<Result<ContentBundle>> ResolveBundleAsync(
        string revision,
        string? configName = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(revision))
            return Result<ContentBundle>.Failure("Content revision is required");

        var published = await GetPublishedAsync(revision, cancellationToken).ConfigureAwait(false);
        if (published.IsSuccess)
        {
            if (!string.IsNullOrWhiteSpace(configName) &&
                !string.Equals(published.Value.Manifest.ConfigName, configName, StringComparison.OrdinalIgnoreCase))
            {
                return Result<ContentBundle>.Failure(
                    $"Content revision '{revision}' belongs to configuration " +
                    $"'{published.Value.Manifest.ConfigName}', not '{configName}'");
            }

            return published;
        }

        var effectiveConfigName = configName;
        if (string.IsNullOrWhiteSpace(effectiveConfigName))
        {
            var knownManifest = _manifests.GetByRevision(revision);
            if (knownManifest.IsFailure)
                return Result<ContentBundle>.Failure(published.Error);
            effectiveConfigName = knownManifest.Value.ConfigName;
        }

        var current = Capture(effectiveConfigName);
        if (current.IsFailure)
            return Result<ContentBundle>.Failure(current.Error);
        if (!string.Equals(current.Value.Manifest.Revision, revision, StringComparison.Ordinal))
            return Result<ContentBundle>.Failure(published.Error);

        return current;
    }

    public async Task<Result<IReadOnlyDictionary<string, JsonElement>>> GetDefinitionsAsync(
        string kind,
        string? revision,
        string configName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(kind))
            return Result<IReadOnlyDictionary<string, JsonElement>>.Failure("Content kind is required");

        ContentBundle bundle;
        if (string.IsNullOrWhiteSpace(revision))
        {
            var current = Capture(configName);
            if (current.IsFailure)
                return Result<IReadOnlyDictionary<string, JsonElement>>.Failure(current.Error);
            bundle = current.Value;
        }
        else
        {
            var resolved = await ResolveBundleAsync(revision, configName, cancellationToken).ConfigureAwait(false);
            if (resolved.IsFailure)
                return Result<IReadOnlyDictionary<string, JsonElement>>.Failure(resolved.Error);
            bundle = resolved.Value;
        }

        var definitions = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var artifact in bundle.Manifest.Artifacts
                     .Where(item => string.Equals(item.Kind, kind, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(item => item.Path, StringComparer.Ordinal))
        {
            if (!bundle.Artifacts.TryGetValue(artifact.Path, out var document) || document.ValueKind != JsonValueKind.Object)
                continue;
            foreach (var definition in document.EnumerateObject())
                definitions[definition.Name] = definition.Value.Clone();
        }

        return Result<IReadOnlyDictionary<string, JsonElement>>.Success(definitions);
    }

    public void Dispose() => _semaphore.Dispose();

    private Result<ContentBundle> Capture(string configName)
    {
        var manifestResult = _manifests.BuildCandidate(configName);
        if (manifestResult.IsFailure)
            return Result<ContentBundle>.Failure(manifestResult.Error);

        try
        {
            var artifacts = ImmutableDictionary.CreateBuilder<string, JsonElement>(StringComparer.Ordinal);
            foreach (var artifact in manifestResult.Value.Artifacts)
            {
                var definitions = _resourceLoader.LoadResource(
                    artifact.Path,
                    manifestResult.Value.ConfigChain,
                    strictMode: false);
                artifacts[artifact.Path] = JsonSerializer.SerializeToElement(definitions).Clone();
            }

            return Result<ContentBundle>.Success(new ContentBundle
            {
                Manifest = manifestResult.Value,
                Artifacts = artifacts.ToImmutable()
            });
        }
        catch (Exception exception)
        {
            return Result<ContentBundle>.Failure($"Failed to capture content bundle: {exception.Message}", exception);
        }
    }

    private static ContentValidationResult ValidateBundle(ContentBundle bundle)
    {
        var errors = ImmutableArray.CreateBuilder<string>();
        var expectedRevision = CanonicalJson.ComputeHash(new PublicationManifestPayload(
            bundle.Manifest.SchemaVersion,
            bundle.Manifest.ConfigName,
            bundle.Manifest.ConfigChain,
            bundle.Manifest.Artifacts));
        if (!string.Equals(expectedRevision, bundle.Manifest.Revision, StringComparison.Ordinal))
            errors.Add("Content manifest revision does not match its canonical payload");
        if (bundle.Manifest.Artifacts.Count == 0)
            errors.Add("Content bundle has no artifacts");
        foreach (var artifact in bundle.Manifest.Artifacts)
        {
            if (!bundle.Artifacts.TryGetValue(artifact.Path, out var definitions))
            {
                errors.Add($"Missing artifact payload: {artifact.Path}");
                continue;
            }
            if (!string.Equals(CanonicalJson.ComputeHash(
                    definitions.Deserialize<Dictionary<string, JsonElement>>() ?? []), artifact.Hash, StringComparison.Ordinal))
            {
                errors.Add($"Artifact hash mismatch: {artifact.Path}");
            }
        }

        return new ContentValidationResult
        {
            Errors = errors.ToImmutable(),
            Manifest = bundle.Manifest
        };
    }

    private static bool IsContentRevision(string revision) =>
        revision.Length == 64 && revision.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private async Task SaveDraftAsync(ContentDraft draft, bool overwrite, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteAtomicAsync(GetDraftPath(draft.DraftId), draft, overwrite, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task WriteAtomicAsync<T>(
        string path,
        T value,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        var tmp = path + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(value, _jsonOptions);
            await File.WriteAllTextAsync(tmp, json, cancellationToken).ConfigureAwait(false);
            File.Move(tmp, path, overwrite);
        }
        catch
        {
            if (File.Exists(tmp))
                try { File.Delete(tmp); } catch { /* best effort */ }
            throw;
        }
    }

    private string GetDraftDirectory() => Path.Combine(_storePath, "drafts");
    private string GetPublishedDirectory() => Path.Combine(_storePath, "published");
    private string GetDraftPath(Guid draftId) => Path.Combine(GetDraftDirectory(), $"{draftId:N}.json");
    private string GetPublishedPath(string revision) => Path.Combine(GetPublishedDirectory(), $"{revision}.json");

    private sealed record PublicationManifestPayload(
        int SchemaVersion,
        string ConfigName,
        IReadOnlyList<string> ConfigChain,
        IReadOnlyList<ContentArtifactManifest> Artifacts);
}

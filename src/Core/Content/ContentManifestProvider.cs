using System.Collections.Immutable;
using Core.Common;
using Core.Determinism;

namespace Core.Content;

/// <summary>
/// Registry of immutable manifests that crossed the publication boundary.
/// It never discovers files or manufactures a live revision. Compilation is
/// owned by the setting compiler and activation is always explicit.
/// </summary>
public sealed class ContentManifestProvider : IContentManifestProvider
{
    private readonly object _lock = new();
    private readonly Dictionary<string, ContentManifest> _activeBySetting =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, ContentManifest> _manifestsByRevision =
        new(StringComparer.Ordinal);

    public Result<ContentManifest> GetManifest(string configName)
    {
        if (string.IsNullOrWhiteSpace(configName))
            return Result<ContentManifest>.Failure("Setting id is required");

        lock (_lock)
        {
            return _activeBySetting.TryGetValue(configName, out var manifest)
                ? Result<ContentManifest>.Success(manifest)
                : Result<ContentManifest>.Failure(
                    $"No published content revision is active for setting: {configName}");
        }
    }

    public Result<ContentManifest> GetByRevision(string revision)
    {
        if (string.IsNullOrWhiteSpace(revision))
            return Result<ContentManifest>.Failure("Content revision is required");

        lock (_lock)
        {
            return _manifestsByRevision.TryGetValue(revision, out var manifest)
                ? Result<ContentManifest>.Success(manifest)
                : Result<ContentManifest>.Failure($"Published content revision not found: {revision}");
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

    public Result RegisterPublishedManifest(ContentManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var validation = ValidatePublishedManifest(manifest);
        if (validation.IsFailure)
            return validation;

        lock (_lock)
        {
            if (_manifestsByRevision.TryGetValue(manifest.Revision, out var existing) &&
                !string.Equals(
                    CanonicalJson.ComputeHash(existing),
                    CanonicalJson.ComputeHash(manifest),
                    StringComparison.Ordinal))
            {
                return Result.Failure($"Content revision collision: {manifest.Revision}");
            }

            _manifestsByRevision[manifest.Revision] = manifest;
        }

        return Result.Success();
    }

    public Result ActivatePublishedManifest(ContentManifest manifest)
    {
        var registration = RegisterPublishedManifest(manifest);
        if (registration.IsFailure)
            return registration;

        lock (_lock)
        {
            _activeBySetting[manifest.ConfigName] = manifest;
        }

        return Result.Success();
    }

    private static Result ValidatePublishedManifest(ContentManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.ConfigName))
            return Result.Failure("Published content manifest setting id is required");
        if (manifest.Revision.Length != 64 || manifest.Revision.Any(character =>
                character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            return Result.Failure("Published content manifest revision must be a lowercase SHA-256 hash");
        }

        return Result.Success();
    }
}

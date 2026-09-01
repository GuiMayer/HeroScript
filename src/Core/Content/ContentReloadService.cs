using System.Collections.Immutable;
using Core.Caching;
using Core.Common;

namespace Core.Content;

public sealed record ContentReloadReceipt
{
    public Guid DraftId { get; init; }
    public int DraftVersion { get; init; }
    public string ConfigName { get; init; } = "default";
    public string Revision { get; init; } = string.Empty;
    public ImmutableArray<string> Warnings { get; init; } = [];
    public CacheInvalidationReport CacheInvalidation { get; init; } = new([]);
}

public interface IContentReloadService
{
    Task<Result<ContentReloadReceipt>> ReloadAsync(
        string configName,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Serializes the complete authoring reload transaction: invalidate mutable
/// views, capture one candidate, validate it, publish atomically and pre-warm
/// its immutable runtime. Existing revision runtimes are never invalidated.
/// </summary>
public sealed class ContentReloadService : IContentReloadService, IDisposable
{
    private readonly ICacheCoordinator _caches;
    private readonly IContentPublicationService _publications;
    private readonly IContentRuntimeResolver _runtimes;
    private readonly SemaphoreSlim _reloadGate = new(1, 1);

    public ContentReloadService(
        ICacheCoordinator caches,
        IContentPublicationService publications,
        IContentRuntimeResolver runtimes)
    {
        _caches = caches ?? throw new ArgumentNullException(nameof(caches));
        _publications = publications ?? throw new ArgumentNullException(nameof(publications));
        _runtimes = runtimes ?? throw new ArgumentNullException(nameof(runtimes));
    }

    public async Task<Result<ContentReloadReceipt>> ReloadAsync(
        string configName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(configName))
            return Result<ContentReloadReceipt>.Failure("Config name is required");

        await _reloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var invalidation = _caches.InvalidateAll(includeRevisioned: false);
            var draft = await _publications
                .CreateDraftAsync(configName, cancellationToken)
                .ConfigureAwait(false);
            if (draft.IsFailure)
                return Result<ContentReloadReceipt>.Failure(draft.Error);

            var validation = _publications.Validate(draft.Value.Bundle);
            if (!validation.IsValid)
            {
                return Result<ContentReloadReceipt>.Failure(
                    $"Content reload validation failed: {string.Join("; ", validation.Errors)}");
            }

            var published = await _publications
                .PublishDraftAsync(draft.Value.DraftId, draft.Value.Version, cancellationToken)
                .ConfigureAwait(false);
            if (published.IsFailure)
                return Result<ContentReloadReceipt>.Failure(published.Error);

            var runtime = _runtimes.Resolve(
                published.Value.Manifest.Revision,
                published.Value.Manifest.ConfigName);
            if (runtime.IsFailure)
                return Result<ContentReloadReceipt>.Failure(
                    $"Published content runtime could not be created: {runtime.Error}");

            return Result<ContentReloadReceipt>.Success(new ContentReloadReceipt
            {
                DraftId = draft.Value.DraftId,
                DraftVersion = draft.Value.Version,
                ConfigName = configName,
                Revision = published.Value.Manifest.Revision,
                Warnings = validation.Warnings,
                CacheInvalidation = invalidation
            });
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    public void Dispose() => _reloadGate.Dispose();
}

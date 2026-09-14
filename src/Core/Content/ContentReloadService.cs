using System.Collections.Immutable;
using Core.Caching;
using Core.Common;
using Core.Events;
using Core.Events.Domain;

namespace Core.Content;

public sealed record ContentReloadReceipt
{
    public Guid DraftId { get; init; }
    public int DraftVersion { get; init; }
    public string SettingId { get; init; } = "default";
    public string Revision { get; init; } = string.Empty;
    public ImmutableArray<string> Warnings { get; init; } = [];
    public CacheInvalidationReport CacheInvalidation { get; init; } = new([]);
}

public interface IContentReloadService
{
    Task<Result<ContentReloadReceipt>> ReloadAsync(
        string settingId,
        CancellationToken cancellationToken = default);
}

public interface ISettingBundleCompiler
{
    Task<Result<ContentBundle>> CompileBundleAsync(
        string settingId,
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
    private readonly ISettingBundleCompiler _settings;
    private readonly IOperationalEventBus? _events;
    private readonly SemaphoreSlim _reloadGate = new(1, 1);

    public ContentReloadService(
        ICacheCoordinator caches,
        IContentPublicationService publications,
        IContentRuntimeResolver runtimes,
        ISettingBundleCompiler settings,
        IOperationalEventBus? events = null)
    {
        _caches = caches ?? throw new ArgumentNullException(nameof(caches));
        _publications = publications ?? throw new ArgumentNullException(nameof(publications));
        _runtimes = runtimes ?? throw new ArgumentNullException(nameof(runtimes));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _events = events;
    }

    public async Task<Result<ContentReloadReceipt>> ReloadAsync(
        string settingId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settingId))
            return Result<ContentReloadReceipt>.Failure("Setting id is required");

        await _reloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var invalidation = _caches.InvalidateAll(includeRevisioned: false);
            var compiled = await _settings
                .CompileBundleAsync(settingId, cancellationToken)
                .ConfigureAwait(false);
            if (compiled.IsFailure)
                return Failure(settingId, compiled.Error, invalidation.InvalidatedCount);
            var draft = await _publications
                .CreateDraftAsync(compiled.Value, cancellationToken)
                .ConfigureAwait(false);
            if (draft.IsFailure)
                return Failure(settingId, draft.Error, invalidation.InvalidatedCount);

            var validation = _publications.Validate(draft.Value.Bundle);
            if (!validation.IsValid)
            {
                return Failure(
                    settingId,
                    $"Content reload validation failed: {string.Join("; ", validation.Errors)}",
                    invalidation.InvalidatedCount);
            }

            var published = await _publications
                .PublishDraftAsync(draft.Value.DraftId, draft.Value.Version, cancellationToken)
                .ConfigureAwait(false);
            if (published.IsFailure)
                return Failure(settingId, published.Error, invalidation.InvalidatedCount);

            var runtime = _runtimes.Resolve(
                published.Value.Manifest.Revision,
                published.Value.Manifest.ConfigName);
            if (runtime.IsFailure)
            {
                return Failure(
                    settingId,
                    $"Published content runtime could not be created: {runtime.Error}",
                    invalidation.InvalidatedCount);
            }

            var receipt = new ContentReloadReceipt
            {
                DraftId = draft.Value.DraftId,
                DraftVersion = draft.Value.Version,
                SettingId = settingId,
                Revision = published.Value.Manifest.Revision,
                Warnings = validation.Warnings,
                CacheInvalidation = invalidation
            };
            Publish(settingId, true, receipt.Revision, null, receipt.Warnings.Length, invalidation.InvalidatedCount);
            return Result<ContentReloadReceipt>.Success(receipt);
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    private Result<ContentReloadReceipt> Failure(
        string settingId,
        string error,
        int invalidatedCacheCount)
    {
        Publish(settingId, false, null, error, 0, invalidatedCacheCount);
        return Result<ContentReloadReceipt>.Failure(error);
    }

    private void Publish(
        string settingId,
        bool succeeded,
        string? revision,
        string? error,
        int warningCount,
        int invalidatedCacheCount)
    {
        _events?.Publish(new ContentReloadedEvent
        {
            EventId = Guid.NewGuid(), // nondeterministic-boundary: operational reload telemetry identity
            Timestamp = DateTime.UtcNow, // nondeterministic-boundary: operational reload telemetry timestamp
            SettingId = settingId,
            Succeeded = succeeded,
            Revision = revision,
            Error = error,
            WarningCount = warningCount,
            InvalidatedCacheCount = invalidatedCacheCount,
            Severity = succeeded ? EventSeverity.INFO : EventSeverity.WARN,
            Target = revision ?? settingId
        });
    }

    public void Dispose() => _reloadGate.Dispose();
}

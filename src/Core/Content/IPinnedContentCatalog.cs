using Core.Common;

namespace Core.Content;

/// <summary>
/// Read-only view over gameplay definitions pinned to a published content revision.
/// Runtime state must never be stored in a content catalog.
/// </summary>
public interface IPinnedContentCatalog<TDefinition>
{
    Result<TDefinition> Get(
        string definitionId,
        string contentRevision,
        string? configName = null);
}

public sealed class PinnedContentCatalog<TDefinition> : IPinnedContentCatalog<TDefinition>
{
    private readonly IContentRuntimeResolver _runtimes;
    private readonly string _kind;
    private readonly Func<string, TDefinition, TDefinition>? _normalize;

    public PinnedContentCatalog(
        IContentRuntimeResolver runtimes,
        string kind,
        Func<string, TDefinition, TDefinition>? normalize = null)
    {
        _runtimes = runtimes ?? throw new ArgumentNullException(nameof(runtimes));
        _kind = string.IsNullOrWhiteSpace(kind)
            ? throw new ArgumentException("Content kind cannot be empty", nameof(kind))
            : kind;
        _normalize = normalize;
    }

    public Result<TDefinition> Get(
        string definitionId,
        string contentRevision,
        string? configName = null)
    {
        if (string.IsNullOrWhiteSpace(definitionId))
            return Result<TDefinition>.Failure("Definition id cannot be empty");
        if (string.IsNullOrWhiteSpace(contentRevision))
            return Result<TDefinition>.Failure("Content revision cannot be empty");

        var runtime = _runtimes.Resolve(contentRevision, configName);
        if (runtime.IsFailure)
            return Result<TDefinition>.Failure(runtime.Error);

        var definition = runtime.Value.GetDefinition<TDefinition>(_kind, definitionId);
        return definition.IsFailure || _normalize == null
            ? definition
            : Result<TDefinition>.Success(_normalize(definitionId, definition.Value));
    }
}

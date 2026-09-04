using System.Collections.Immutable;
using Core.Common;

namespace Core.Resources;

/// <summary>
/// Immutable, owner-scoped collection of pinned resource pools. The owner can
/// be an entity, a run, a board or any future aggregate.
/// </summary>
public sealed record ResourceSet
{
    private ImmutableDictionary<string, ResourcePool> _resources =
        ImmutableDictionary<string, ResourcePool>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);

    public string OwnerId { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, ResourcePool> Resources
    {
        get => _resources;
        init => _resources = value?.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase)
            ?? ImmutableDictionary<string, ResourcePool>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
    }

    public ResourcePool? Get(string resourceId) =>
        _resources.TryGetValue(resourceId, out var pool) ? pool : null;

    public float Current(string resourceId) => Get(resourceId)?.Current ?? 0f;

    public Result<ResourceSetMutationResult> Apply(
        IReadOnlyList<ResolvedResourceMutation> mutations,
        IResourceMutationReducer? reducer = null)
    {
        if (string.IsNullOrWhiteSpace(OwnerId))
            return Result<ResourceSetMutationResult>.Failure("Resource set owner id is required");
        var reduced = (reducer ?? new ResourceMutationReducer()).Apply(_resources, mutations);
        return reduced.IsFailure
            ? Result<ResourceSetMutationResult>.Failure(reduced.Error)
            : Result<ResourceSetMutationResult>.Success(new ResourceSetMutationResult(
                this with { Resources = reduced.Value.Resources },
                reduced.Value.Records));
    }
}

public sealed record ResourceSetMutationResult(
    ResourceSet State,
    IReadOnlyList<ResourceMutationRecord> Records);

public sealed record ResourceAmount
{
    public string ResourceId { get; init; } = string.Empty;
    public float Amount { get; init; }
}

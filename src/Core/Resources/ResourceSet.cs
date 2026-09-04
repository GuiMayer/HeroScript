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

    public bool Contains(string resourceId) => _resources.ContainsKey(resourceId);

    public float Current(string resourceId) => Get(resourceId)?.Current ?? 0f;

    public ResourceSet WithResource(string resourceId, ResourcePool pool)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentNullException.ThrowIfNull(pool);
        if (!string.Equals(resourceId, pool.ResourceId, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"Resource key '{resourceId}' does not match pool id '{pool.ResourceId}'",
                nameof(pool));

        return this with { Resources = _resources.SetItem(resourceId, pool) };
    }

    public ResourceSet WithResources(IReadOnlyDictionary<string, ResourcePool> updates)
    {
        ArgumentNullException.ThrowIfNull(updates);
        var merged = _resources;
        foreach (var (resourceId, pool) in updates)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
            ArgumentNullException.ThrowIfNull(pool);
            if (!string.Equals(resourceId, pool.ResourceId, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(
                    $"Resource key '{resourceId}' does not match pool id '{pool.ResourceId}'",
                    nameof(updates));
            merged = merged.SetItem(resourceId, pool);
        }

        return this with { Resources = merged };
    }

    public ResourcePool? FirstInCategory(ResourceCategory category) =>
        _resources.Values.FirstOrDefault(pool =>
            pool.Definition is not null && pool.Definition.Category == category);

    /// <summary>
    /// Rebinds every existing pool to a new immutable definition graph. Bounds
    /// belong to runtime state and are preserved; the current value is clamped
    /// again because the new definition may change overflow/negative rules.
    /// </summary>
    public Result<ResourceSet> RebindDefinitions(
        IReadOnlyDictionary<string, ResourceDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var byIdBuilder = ImmutableDictionary.CreateBuilder<string, ResourceDefinition>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var (resourceId, definition) in definitions)
        {
            if (!byIdBuilder.TryAdd(resourceId, definition))
                return Result<ResourceSet>.Failure($"Duplicate resource definition: {resourceId}");
        }
        var byId = byIdBuilder.ToImmutable();
        var rebound = ImmutableDictionary.CreateBuilder<string, ResourcePool>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var (resourceId, pool) in _resources.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (!byId.TryGetValue(resourceId, out var definition))
            {
                return Result<ResourceSet>.Failure(
                    $"Resource definition not found while rebinding owner '{OwnerId}': {resourceId}");
            }

            var validation = ResourceDefinitionValidator.Validate(definition, resourceId);
            if (validation.IsFailure)
            {
                return Result<ResourceSet>.Failure(
                    $"Invalid resource definition while rebinding owner '{OwnerId}': {validation.Error}");
            }

            if (!string.Equals(pool.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase))
            {
                return Result<ResourceSet>.Failure(
                    $"Resource key does not match pool id while rebinding owner '{OwnerId}': " +
                    $"{resourceId}/{pool.ResourceId}");
            }

            try
            {
                var updated = (pool with { Definition = definition }).Set(pool.Current);
                rebound[resourceId] = updated;
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                return Result<ResourceSet>.Failure(
                    $"Resource '{resourceId}' could not be rebound for owner '{OwnerId}': {exception.Message}",
                    exception);
            }
        }

        return Result<ResourceSet>.Success(this with { Resources = rebound.ToImmutable() });
    }

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

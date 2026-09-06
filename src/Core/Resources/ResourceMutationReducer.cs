using System.Collections.Immutable;
using Core.Common;

namespace Core.Resources;

public enum ResourceValueField
{
    Current,
    Minimum,
    Maximum
}

public enum ResourceMutationOperation
{
    Add,
    Subtract,
    Set
}

public sealed record ResolvedResourceMutation
{
    public string MutationId { get; init; } = string.Empty;
    public string ResourceId { get; init; } = string.Empty;
    public ResourceValueField Field { get; init; } = ResourceValueField.Current;
    public ResourceMutationOperation Operation { get; init; }
    public float Value { get; init; }
}

public sealed record ResourceMutationRecord(
    string MutationId,
    string ResourceId,
    ResourceValueField Field,
    ResourceMutationOperation Operation,
    float PreviousValue,
    float CurrentValue);

public sealed record ResourceMutationBatchResult(
    IReadOnlyDictionary<string, ResourcePool> Resources,
    IReadOnlyList<ResourceMutationRecord> Records);

public interface IResourceMutationReducer
{
    Result<ResourceMutationBatchResult> Apply(
        IReadOnlyDictionary<string, ResourcePool> resources,
        IReadOnlyList<ResolvedResourceMutation> mutations);
}

/// <summary>
/// Pure, atomic reducer for every bounded numeric resource mutation. It has no
/// knowledge of damage, cards, turns, owners or presentation.
/// </summary>
public sealed class ResourceMutationReducer : IResourceMutationReducer
{
    public Result<ResourceMutationBatchResult> Apply(
        IReadOnlyDictionary<string, ResourcePool> resources,
        IReadOnlyList<ResolvedResourceMutation> mutations)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(mutations);

        var current = resources.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);
        var records = ImmutableArray.CreateBuilder<ResourceMutationRecord>();
        foreach (var mutation in mutations)
        {
            var validation = Validate(mutation);
            if (validation.IsFailure)
                return Result<ResourceMutationBatchResult>.Failure(validation.Error);
            if (!current.TryGetValue(mutation.ResourceId, out var pool))
                return Result<ResourceMutationBatchResult>.Failure(
                    $"Resource not found: {mutation.ResourceId}");
            if (!string.Equals(pool.ResourceId, mutation.ResourceId, StringComparison.OrdinalIgnoreCase))
                return Result<ResourceMutationBatchResult>.Failure(
                    $"Resource key does not match pool id: {mutation.ResourceId}/{pool.ResourceId}");
            if (pool.Definition == null)
                return Result<ResourceMutationBatchResult>.Failure(
                    $"Resource pool has no pinned definition: {pool.ResourceId}");
            if (!string.Equals(
                    pool.Definition.ResourceId,
                    pool.ResourceId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return Result<ResourceMutationBatchResult>.Failure(
                    $"Resource pool does not match its pinned definition: {pool.ResourceId}/{pool.Definition.ResourceId}");
            }

            var previous = Read(pool, mutation.Field);
            var next = mutation.Operation switch
            {
                ResourceMutationOperation.Add => previous + mutation.Value,
                ResourceMutationOperation.Subtract => previous - mutation.Value,
                ResourceMutationOperation.Set => mutation.Value,
                _ => float.NaN
            };
            if (!IsFinite(next))
                return Result<ResourceMutationBatchResult>.Failure(
                    $"Resource mutation produced a non-finite value: {mutation.MutationId}");

            var applied = Write(pool, mutation.Field, next);
            if (applied.IsFailure)
                return Result<ResourceMutationBatchResult>.Failure(
                    $"Resource mutation {mutation.MutationId} failed: {applied.Error}");
            current = current.SetItem(mutation.ResourceId, applied.Value);
            records.Add(new ResourceMutationRecord(
                mutation.MutationId,
                mutation.ResourceId,
                mutation.Field,
                mutation.Operation,
                previous,
                Read(applied.Value, mutation.Field)));
        }

        return Result<ResourceMutationBatchResult>.Success(new ResourceMutationBatchResult(
            current,
            records.ToImmutable()));
    }

    private static Result Validate(ResolvedResourceMutation mutation)
    {
        if (mutation == null)
            return Result.Failure("Resource mutation cannot be null");
        if (string.IsNullOrWhiteSpace(mutation.MutationId))
            return Result.Failure("Resource mutation id is required");
        if (string.IsNullOrWhiteSpace(mutation.ResourceId))
            return Result.Failure($"Resource mutation {mutation.MutationId} requires resourceId");
        if (!Enum.IsDefined(mutation.Field) || !Enum.IsDefined(mutation.Operation))
            return Result.Failure($"Resource mutation {mutation.MutationId} is invalid");
        if (mutation.Operation != ResourceMutationOperation.Set && mutation.Value < 0)
            return Result.Failure($"Resource mutation {mutation.MutationId} requires a non-negative magnitude");
        return IsFinite(mutation.Value)
            ? Result.Success()
            : Result.Failure($"Resource mutation {mutation.MutationId} value must be finite");
    }

    private static Result<ResourcePool> Write(ResourcePool pool, ResourceValueField field, float value)
    {
        try
        {
            return field switch
            {
                ResourceValueField.Current => Result<ResourcePool>.Success(pool.Set(value)),
                ResourceValueField.Minimum when value <= pool.Maximum =>
                    Result<ResourcePool>.Success(ClampCurrent(pool with { Minimum = value })),
                ResourceValueField.Maximum when value >= pool.Minimum =>
                    Result<ResourcePool>.Success(ClampCurrent(pool with { Maximum = value })),
                ResourceValueField.Minimum => Result<ResourcePool>.Failure(
                    "Resource minimum cannot exceed maximum"),
                ResourceValueField.Maximum => Result<ResourcePool>.Failure(
                    "Resource maximum cannot be below minimum"),
                _ => Result<ResourcePool>.Failure("Unsupported resource field")
            };
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Result<ResourcePool>.Failure(exception.Message, exception);
        }
    }

    private static ResourcePool ClampCurrent(ResourcePool pool) => pool.Set(pool.Current);

    private static float Read(ResourcePool pool, ResourceValueField field) => field switch
    {
        ResourceValueField.Current => pool.Current,
        ResourceValueField.Minimum => pool.Minimum,
        ResourceValueField.Maximum => pool.Maximum,
        _ => float.NaN
    };

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}

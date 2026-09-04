using System.Collections.Immutable;
using Core.Common;

namespace Core.Resources;

public sealed record ResolvedResourceCost
{
    public string ResourceId { get; init; } = string.Empty;
    public float Amount { get; init; }
    public bool AllowOverdraft { get; init; }
}

/// <summary>
/// Pure, atomic spending policy shared by combat actions, cards and run
/// submodules. Costs are evaluated by their caller and consumed in declared
/// order against a private immutable snapshot.
/// </summary>
public static class ResourceCostTransitions
{
    public static Result<ResourceSetMutationResult> Spend(
        ResourceSet resources,
        IReadOnlyList<ResolvedResourceCost> costs,
        string transactionId)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(costs);
        if (string.IsNullOrWhiteSpace(transactionId))
            return Result<ResourceSetMutationResult>.Failure("Resource cost transaction id is required");

        var current = resources;
        var records = ImmutableArray.CreateBuilder<ResourceMutationRecord>();
        foreach (var (cost, index) in costs.Select((cost, index) => (cost, index)))
        {
            if (string.IsNullOrWhiteSpace(cost.ResourceId))
                return Result<ResourceSetMutationResult>.Failure("Resource cost requires resourceId");
            if (!IsFinite(cost.Amount) || cost.Amount < 0)
            {
                return Result<ResourceSetMutationResult>.Failure(
                    $"Resource cost must be finite and non-negative: {cost.ResourceId}");
            }

            var pool = current.Get(cost.ResourceId);
            if (pool == null)
                return Result<ResourceSetMutationResult>.Failure($"Resource not found: {cost.ResourceId}");
            if (!cost.AllowOverdraft && !pool.CanAfford(cost.Amount))
            {
                return Result<ResourceSetMutationResult>.Failure(
                    $"Insufficient {pool.Definition?.DisplayName ?? cost.ResourceId}: " +
                    $"has {pool.Current}, needs {cost.Amount}");
            }

            var applied = current.Apply(
            [
                new ResolvedResourceMutation
                {
                    MutationId = $"{transactionId}:{index}:{cost.ResourceId}",
                    ResourceId = cost.ResourceId,
                    Operation = ResourceMutationOperation.Subtract,
                    Value = cost.Amount
                }
            ]);
            if (applied.IsFailure)
                return Result<ResourceSetMutationResult>.Failure(applied.Error);
            current = applied.Value.State;
            records.AddRange(applied.Value.Records);
        }

        return Result<ResourceSetMutationResult>.Success(new(current, records.ToImmutable()));
    }

    private static bool IsFinite(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value);
}

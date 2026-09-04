using Core.Common;
using Core.Resources;

namespace Core.Run;

/// <summary>Pure atomic resource transactions for a run aggregate.</summary>
public static class RunResourceTransitions
{
    public static Result<ResourceSetMutationResult> ApplyDelta(
        ResourceSet resources,
        string resourceId,
        float delta,
        string mutationId)
    {
        var pool = resources.Get(resourceId);
        if (pool == null)
            return Result<ResourceSetMutationResult>.Failure($"Run resource not found: {resourceId}");
        return resources.Apply(
        [
            new ResolvedResourceMutation
            {
                MutationId = mutationId,
                ResourceId = resourceId,
                Operation = ResourceMutationOperation.Set,
                Value = pool.Current + delta
            }
        ]);
    }

    public static Result<ResourceSetMutationResult> Spend(
        ResourceSet resources,
        IReadOnlyList<ResourceAmount> costs,
        string transactionId)
    {
        var validation = ValidateAmounts(resources, costs, requireAffordability: true);
        if (validation.IsFailure)
            return Result<ResourceSetMutationResult>.Failure(validation.Error);
        if (costs.Count == 0)
            return Result<ResourceSetMutationResult>.Success(new(resources, []));
        return resources.Apply(costs
            .OrderBy(cost => cost.ResourceId, StringComparer.Ordinal)
            .Select((cost, index) => new ResolvedResourceMutation
            {
                MutationId = $"{transactionId}:{index}:{cost.ResourceId}",
                ResourceId = cost.ResourceId,
                Operation = ResourceMutationOperation.Subtract,
                Value = cost.Amount
            })
            .ToArray());
    }

    public static Result<ResourceSetMutationResult> Gain(
        ResourceSet resources,
        IReadOnlyList<ResourceAmount> rewards,
        string transactionId)
    {
        var validation = ValidateAmounts(resources, rewards, requireAffordability: false);
        if (validation.IsFailure)
            return Result<ResourceSetMutationResult>.Failure(validation.Error);
        if (rewards.Count == 0)
            return Result<ResourceSetMutationResult>.Success(new(resources, []));
        return resources.Apply(rewards
            .OrderBy(reward => reward.ResourceId, StringComparer.Ordinal)
            .Select((reward, index) => new ResolvedResourceMutation
            {
                MutationId = $"{transactionId}:{index}:{reward.ResourceId}",
                ResourceId = reward.ResourceId,
                Operation = ResourceMutationOperation.Add,
                Value = reward.Amount
            })
            .ToArray());
    }

    private static Result ValidateAmounts(
        ResourceSet resources,
        IReadOnlyList<ResourceAmount> amounts,
        bool requireAffordability)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(amounts);
        var duplicate = amounts
            .Where(amount => !string.IsNullOrWhiteSpace(amount.ResourceId))
            .GroupBy(amount => amount.ResourceId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
            return Result.Failure($"Duplicate run resource amount: {duplicate.Key}");
        foreach (var amount in amounts)
        {
            if (string.IsNullOrWhiteSpace(amount.ResourceId))
                return Result.Failure("Run resource id is required");
            if (float.IsNaN(amount.Amount) || float.IsInfinity(amount.Amount) || amount.Amount < 0)
                return Result.Failure($"Run resource amount must be finite and non-negative: {amount.ResourceId}");
            var pool = resources.Get(amount.ResourceId);
            if (pool == null)
                return Result.Failure($"Run resource not found: {amount.ResourceId}");
            if (requireAffordability && !pool.CanAfford(amount.Amount))
                return Result.Failure($"Insufficient run resource: {amount.ResourceId}");
        }
        return Result.Success();
    }
}

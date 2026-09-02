using System.Text.Json.Serialization;

namespace Core.Resources;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ResourceThresholdBoundary
{
    Unspecified,
    AtMinimum,
    AtMaximum
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ResourceThresholdConsequence
{
    Unspecified,
    None,
    DefeatOwner
}

/// <summary>
/// Data-driven consequence evaluated when a resource reaches one of its own
/// configured bounds. Resource identity and category have no implicit outcome
/// semantics.
/// </summary>
public sealed record ResourceThresholdPolicy
{
    public string PolicyId { get; init; } = string.Empty;
    public ResourceThresholdBoundary Boundary { get; init; }
    public ResourceThresholdConsequence Consequence { get; init; }
    public int Priority { get; init; }
}

public sealed record ResourceThresholdFact(
    string ResourceId,
    string PolicyId,
    ResourceThresholdBoundary Boundary,
    ResourceThresholdConsequence Consequence,
    int Priority,
    float Value,
    float Threshold);

/// <summary>Pure evaluation of resource-owned threshold policies.</summary>
public static class ResourceThresholdEvaluator
{
    public static IReadOnlyList<ResourceThresholdFact> Evaluate(ResourcePool pool)
    {
        ArgumentNullException.ThrowIfNull(pool);
        if (pool.Definition == null)
            return [];

        return pool.Definition.ThresholdPolicies
            .Where(policy => IsReached(pool, policy.Boundary))
            .OrderByDescending(policy => policy.Priority)
            .ThenBy(policy => policy.PolicyId, StringComparer.Ordinal)
            .Select(policy => new ResourceThresholdFact(
                pool.ResourceId,
                policy.PolicyId,
                policy.Boundary,
                policy.Consequence,
                policy.Priority,
                pool.Current,
                policy.Boundary == ResourceThresholdBoundary.AtMaximum
                    ? pool.Maximum
                    : pool.Minimum))
            .ToArray();
    }

    public static bool IsOwnerDefeated(IEnumerable<ResourcePool> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        return resources
            .SelectMany(Evaluate)
            .Any(fact => fact.Consequence == ResourceThresholdConsequence.DefeatOwner);
    }

    private static bool IsReached(ResourcePool pool, ResourceThresholdBoundary boundary) => boundary switch
    {
        ResourceThresholdBoundary.AtMinimum => pool.Current <= pool.Minimum,
        ResourceThresholdBoundary.AtMaximum => pool.Current >= pool.Maximum,
        _ => false
    };
}

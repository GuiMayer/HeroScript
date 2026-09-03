using System.Text.Json.Serialization;

namespace Core.Resources;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ResourceThresholdComparison
{
    Unspecified,
    LessThan,
    LessThanOrEqual,
    Equal,
    NotEqual,
    GreaterThanOrEqual,
    GreaterThan
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ResourceThresholdSource
{
    Unspecified,
    Minimum,
    Maximum,
    Constant
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ResourceThresholdConsequence
{
    Unspecified,
    None,
    DefeatOwner
}

/// <summary>
/// Data-driven condition and consequence evaluated against one resource.
/// The threshold can follow a mutable bound or use an explicit constant;
/// resource identity and category never imply an outcome.
/// </summary>
public sealed record ResourceThresholdPolicy
{
    public string PolicyId { get; init; } = string.Empty;
    public ResourceThresholdComparison Comparison { get; init; }
    public ResourceThresholdSource ThresholdSource { get; init; }
    public float? ThresholdValue { get; init; }
    public float Tolerance { get; init; } = 0.0001f;
    public ResourceThresholdConsequence Consequence { get; init; }
    public int Priority { get; init; }
}

public sealed record ResourceThresholdFact(
    string ResourceId,
    string PolicyId,
    ResourceThresholdComparison Comparison,
    ResourceThresholdSource ThresholdSource,
    ResourceThresholdConsequence Consequence,
    int Priority,
    float Value,
    float Threshold);

/// <summary>Pure, deterministic evaluation of resource-owned threshold policies.</summary>
public static class ResourceThresholdEvaluator
{
    public static IReadOnlyList<ResourceThresholdFact> Evaluate(ResourcePool pool)
    {
        ArgumentNullException.ThrowIfNull(pool);
        if (pool.Definition == null)
            return [];

        return pool.Definition.ThresholdPolicies
            .Select(policy => (Policy: policy, Threshold: ResolveThreshold(pool, policy)))
            .Where(item => item.Threshold.HasValue && IsReached(
                pool.Current,
                item.Threshold.Value,
                item.Policy.Comparison,
                item.Policy.Tolerance))
            .OrderByDescending(item => item.Policy.Priority)
            .ThenBy(item => item.Policy.PolicyId, StringComparer.Ordinal)
            .Select(item => new ResourceThresholdFact(
                pool.ResourceId,
                item.Policy.PolicyId,
                item.Policy.Comparison,
                item.Policy.ThresholdSource,
                item.Policy.Consequence,
                item.Policy.Priority,
                pool.Current,
                item.Threshold!.Value))
            .ToArray();
    }

    /// <summary>
    /// Only the highest-priority reached policy of each resource is
    /// authoritative. Policy id is the stable tie-break for equal priority.
    /// </summary>
    public static ResourceThresholdFact? Resolve(ResourcePool pool) =>
        Evaluate(pool).FirstOrDefault();

    public static bool IsOwnerDefeated(IEnumerable<ResourcePool> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        return resources
            .OrderBy(pool => pool.ResourceId, StringComparer.Ordinal)
            .Select(Resolve)
            .Any(fact => fact?.Consequence == ResourceThresholdConsequence.DefeatOwner);
    }

    private static float? ResolveThreshold(ResourcePool pool, ResourceThresholdPolicy policy) =>
        policy.ThresholdSource switch
        {
            ResourceThresholdSource.Minimum => pool.Minimum,
            ResourceThresholdSource.Maximum => pool.Maximum,
            ResourceThresholdSource.Constant => policy.ThresholdValue,
            _ => null
        };

    private static bool IsReached(
        float value,
        float threshold,
        ResourceThresholdComparison comparison,
        float tolerance) => comparison switch
        {
            ResourceThresholdComparison.LessThan => value < threshold,
            ResourceThresholdComparison.LessThanOrEqual => value <= threshold,
            ResourceThresholdComparison.Equal => MathF.Abs(value - threshold) <= tolerance,
            ResourceThresholdComparison.NotEqual => MathF.Abs(value - threshold) > tolerance,
            ResourceThresholdComparison.GreaterThanOrEqual => value >= threshold,
            ResourceThresholdComparison.GreaterThan => value > threshold,
            _ => false
        };
}

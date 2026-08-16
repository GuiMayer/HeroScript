namespace Core.Effects;

public static class EffectResultAggregator
{
    public static EffectResult Aggregate(IReadOnlyList<EffectResult> results)
    {
        if (results.Count == 0)
            return EffectResult.CreateFailure("No results to aggregate");
        if (results.Count == 1)
            return results[0];

        return new EffectResult
        {
            Success = results.Any(result => result.Success),
            ValueApplied = results
                .Where(result => result.ValueApplied.HasValue)
                .Sum(result => result.ValueApplied!.Value),
            AffectedEntityIds = results.SelectMany(result => result.AffectedEntityIds).Distinct().ToList(),
            StatusApplied = results.SelectMany(result => result.StatusApplied).Distinct().ToList(),
            StatusRemoved = results.SelectMany(result => result.StatusRemoved).Distinct().ToList()
        };
    }
}

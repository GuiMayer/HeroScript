using Core.Common;

namespace Core.Run;

/// <summary>Pure ledger transition. The caller validates the rebuilt composition before committing it.</summary>
public static class CardInstanceUpgradeTransitions
{
    public static Result<CardInstanceState> Apply(CardInstanceState instance,
        CardUpgradeDefinition definition, string contentRevision)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return Change(instance, definition, CardTransformationOperation.Apply, null, contentRevision);
    }

    public static Result<CardInstanceState> Remove(CardInstanceState instance,
        ulong transformationId, string contentRevision) =>
        Change(instance, null, CardTransformationOperation.Remove, transformationId, contentRevision);

    public static Result<CardInstanceState> Replace(CardInstanceState instance,
        ulong transformationId, CardUpgradeDefinition definition, string contentRevision)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return Change(instance, definition, CardTransformationOperation.Replace, transformationId, contentRevision);
    }

    private static Result<CardInstanceState> Change(CardInstanceState instance,
        CardUpgradeDefinition? definition, CardTransformationOperation operation,
        ulong? target, string contentRevision)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var projection = CardTransformationLedger.Project(instance.Upgrades);
        if (projection.IsFailure) return Result<CardInstanceState>.Failure(projection.Error);
        if (string.IsNullOrWhiteSpace(contentRevision))
            return Result<CardInstanceState>.Failure("Transformation content revision is required");
        if (instance.Upgrades.Count >= CardTransformationLedger.MaximumEntries)
            return Result<CardInstanceState>.Failure("Card transformation ledger limit exceeded");
        if (target != null && !projection.Value.Any(item => item.TransformationId == target))
            return Result<CardInstanceState>.Failure($"Active transformation not found: {target}");
        if (definition != null)
        {
            if (string.IsNullOrWhiteSpace(definition.UpgradeId) || definition.MaxApplications < 1 ||
                !Enum.IsDefined(definition.Category))
                return Result<CardInstanceState>.Failure("Invalid card upgrade id, category or application limit");
            if (!definition.AppliesTo(instance.DefinitionId))
                return Result<CardInstanceState>.Failure($"Upgrade {definition.UpgradeId} does not apply to {instance.DefinitionId}");
            var applications = projection.Value.Count(item => item.TransformationId != target &&
                string.Equals(item.UpgradeId, definition.UpgradeId, StringComparison.Ordinal));
            if (applications >= definition.MaxApplications)
                return Result<CardInstanceState>.Failure($"Upgrade application limit reached: {definition.UpgradeId}");
        }
        var lastId = instance.Upgrades.LastOrDefault()?.TransformationId ?? 0;
        if (lastId == ulong.MaxValue)
            return Result<CardInstanceState>.Failure("Card transformation identity overflow");
        var entry = new CardUpgradeState
        {
            TransformationId = lastId + 1,
            Operation = operation,
            TargetTransformationId = target,
            UpgradeId = definition?.UpgradeId ?? string.Empty,
            Category = definition?.Category ?? projection.Value.Single(item => item.TransformationId == target).Category,
            SlotId = definition?.SlotId ?? (definition == null
                ? projection.Value.Single(item => item.TransformationId == target).SlotId : null),
            ContentRevision = contentRevision,
            Patches = definition?.Patches ?? []
        };
        return Result<CardInstanceState>.Success(instance with { Upgrades = instance.UpgradeItems.Add(entry) });
    }
}

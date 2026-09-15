using Core.Common;

namespace Core.Run;

/// <summary>Pure, topology-independent permanent upgrade of one card identity.</summary>
public static class CardInstanceUpgradeTransitions
{
    public static Result<CardInstanceState> Apply(
        CardInstanceState instance,
        CardUpgradeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(definition.UpgradeId))
            return Result<CardInstanceState>.Failure("Card upgrade id is required");
        if (!definition.AppliesTo(instance.DefinitionId))
            return Result<CardInstanceState>.Failure(
                $"Upgrade {definition.UpgradeId} does not apply to {instance.DefinitionId}");
        var applications = instance.UpgradeItems.Count(upgrade =>
            string.Equals(upgrade.UpgradeId, definition.UpgradeId, StringComparison.Ordinal));
        if (applications >= System.Math.Max(1, definition.MaxApplications))
            return Result<CardInstanceState>.Failure(
                $"Upgrade application limit reached: {definition.UpgradeId}");
        var upgrade = new CardUpgradeState
        {
            UpgradeId = definition.UpgradeId,
            Patches = definition.Patches
        };
        return Result<CardInstanceState>.Success(instance with
        {
            Upgrades = instance.UpgradeItems.Add(upgrade)
        });
    }
}

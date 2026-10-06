using System.Collections.Immutable;
using Core.Common;
using Core.Run.Content;

namespace Core.Run;

/// <summary>
/// The upgrade list is the only authoritative ledger. This projection validates
/// causality and derives the active sequence; it is never persisted separately.
/// Replacements occupy the original position so later numeric improvements
/// remain later improvements instead of being overwritten by a new base.
/// </summary>
public static class CardTransformationLedger
{
    public const int MaximumEntries = 1024;

    public static Result<ImmutableArray<CardUpgradeState>> Project(IReadOnlyList<CardUpgradeState> ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        if (ledger.Count > MaximumEntries)
            return Result<ImmutableArray<CardUpgradeState>>.Failure("Card transformation ledger limit exceeded");
        var active = new List<CardUpgradeState>();
        ulong previous = 0;
        foreach (var entry in ledger)
        {
            if (entry == null || entry.TransformationId == 0 || entry.TransformationId <= previous ||
                !Enum.IsDefined(entry.Operation) || !Enum.IsDefined(entry.Category) ||
                string.IsNullOrWhiteSpace(entry.ContentRevision) || entry.SlotId != null && string.IsNullOrWhiteSpace(entry.SlotId))
                return Result<ImmutableArray<CardUpgradeState>>.Failure("Invalid card transformation identity, order, category or revision");
            previous = entry.TransformationId;
            if (entry.Operation == CardTransformationOperation.Apply)
            {
                if (entry.TargetTransformationId != null || string.IsNullOrWhiteSpace(entry.UpgradeId))
                    return Result<ImmutableArray<CardUpgradeState>>.Failure("Apply requires an upgradeId and no target transformation");
                active.Add(entry);
                continue;
            }
            var index = active.FindIndex(item => item.TransformationId == entry.TargetTransformationId);
            if (index < 0)
                return Result<ImmutableArray<CardUpgradeState>>.Failure($"Active transformation not found: {entry.TargetTransformationId}");
            if (entry.Operation == CardTransformationOperation.Remove)
            {
                if (entry.Patches.Count != 0 || entry.CompositionRules.Count != 0 || !CardCompositionGrammar.IsEmpty(entry.Requirements) ||
                    !string.IsNullOrEmpty(entry.UpgradeId) || entry.Category != active[index].Category ||
                    entry.SlotId != active[index].SlotId)
                    return Result<ImmutableArray<CardUpgradeState>>.Failure("Removal cannot contain patches or an upgradeId");
                active.RemoveAt(index);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(entry.UpgradeId))
                    return Result<ImmutableArray<CardUpgradeState>>.Failure("Replacement requires an upgradeId");
                active[index] = entry;
            }
        }
        return Result<ImmutableArray<CardUpgradeState>>.Success(active.ToImmutableArray());
    }

    public static int Count(CardInstanceState card, string? upgradeId = null)
    {
        var active = Project(card.Upgrades);
        // Invalid state must not advertise any executable upgrade capacity.
        return active.IsFailure ? int.MaxValue : active.Value.Count(item => upgradeId == null ||
            string.Equals(item.UpgradeId, upgradeId, StringComparison.Ordinal));
    }
}

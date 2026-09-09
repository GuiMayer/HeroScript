using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.Combat.Models;
using Core.Run.Content;

namespace Core.Combat.Reactions;

/// <summary>
/// Immutable action proposal. It contains only data required to deterministically
/// re-evaluate the action against the snapshot in which it eventually resolves.
/// </summary>
public sealed record PendingActionState
{
    private ImmutableArray<string> _lockedTargetIds = [];
    private ImmutableArray<PendingResourceCost> _paidCosts = [];
    private ImmutableArray<string> _commandTags = [];

    public string PendingActionId { get; init; } = string.Empty;
    public int Depth { get; init; }
    public CombatActionCommand Command { get; init; } = new();
    public string CandidateFingerprint { get; init; } = string.Empty;
    public string ContentRevision { get; init; } = string.Empty;
    public string? ActionId { get; init; }
    public string? CardDefinitionId { get; init; }
    public IReadOnlyList<string> LockedTargetIds
    {
        get => _lockedTargetIds;
        init => _lockedTargetIds = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<PendingResourceCost> PaidCosts
    {
        get => _paidCosts;
        init => _paidCosts = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<string> CommandTags
    {
        get => _commandTags;
        init => _commandTags = value?.ToImmutableArray() ?? [];
    }
    public bool CostsPaid => _paidCosts.Length > 0;
}

public sealed record PendingResourceCost
{
    public string ComponentId { get; init; } = string.Empty;
    public string? OptionId { get; init; }
    public string ResourceId { get; init; } = string.Empty;
    public float Amount { get; init; }

    public static PendingResourceCost From(ResolvedCardCost cost) => new()
    {
        ComponentId = cost.ComponentId,
        OptionId = cost.OptionId,
        ResourceId = cost.ResourceId,
        Amount = cost.Amount
    };
}

/// <summary>Serializable priority cursor. The pending actions live beside it on CombatState.</summary>
public sealed record PriorityWindowState
{
    private ImmutableArray<string> _eligibleActorIds = [];

    public string WindowId { get; init; } = string.Empty;
    public string OpenedByActorId { get; init; } = string.Empty;
    public string HolderActorId { get; init; } = string.Empty;
    public int HolderIndex { get; init; }
    public int ConsecutivePasses { get; init; }
    public int ResolutionCount { get; init; }
    public IReadOnlyList<string> EligibleActorIds
    {
        get => _eligibleActorIds;
        init => _eligibleActorIds = value?.ToImmutableArray() ?? [];
    }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReactionTransitionKind
{
    ResolvedImmediately,
    Proposed,
    PriorityPassed,
    StackActionResolved,
    StackActionFizzled
}

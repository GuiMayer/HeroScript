using System.Collections.Immutable;
using Core.Common;
using Core.Determinism;
using Core.Run;

namespace Core.CardZones;

public sealed record CardZoneSelectionResult(
    ImmutableArray<Guid> InstanceIds,
    DeterministicContext Context);

public sealed record CardZoneTransition(
    CardZoneTopologyState State,
    DeterministicContext Context,
    ImmutableArray<Guid> AffectedInstanceIds,
    ImmutableArray<Guid> CreatedInstanceIds,
    ImmutableArray<Guid> DestroyedInstanceIds);

public sealed record CardZoneCardCreation
{
    private ImmutableArray<CardUpgradeState> _upgrades = [];

    public string DefinitionId { get; init; } = string.Empty;
    public IReadOnlyList<CardUpgradeState> Upgrades
    {
        get => _upgrades;
        init => _upgrades = value?.ToImmutableArray() ?? [];
    }
}

/// <summary>
/// Pure card-zone primitives. They know identities, order and capacity, but no
/// gameplay meaning, boundary, content repository or persistence service.
/// </summary>
public static class CardZoneTransitions
{
    public static Result<CardZoneTopologyState> CreateEmpty(IEnumerable<CardZoneAddress> addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        var zones = ImmutableDictionary.CreateBuilder<string, CardZoneState>(StringComparer.Ordinal);
        foreach (var address in addresses)
        {
            if (string.IsNullOrWhiteSpace(address.ZoneId) || string.IsNullOrWhiteSpace(address.OwnerId))
                return Result<CardZoneTopologyState>.Failure("Card-zone address requires zoneId and ownerId");
            if (!zones.TryAdd(address.Key, new CardZoneState { Address = address }))
                return Result<CardZoneTopologyState>.Failure($"Duplicate card-zone address: {address.Key}");
        }
        return Result<CardZoneTopologyState>.Success(new CardZoneTopologyState { Zones = zones.ToImmutable() });
    }

    public static Result<CardZoneTransition> CreateInstances(
        CardZoneTopologyState state,
        CardZoneAddress target,
        IReadOnlyList<string> definitionIds,
        CardInstanceLifetimeDefinition lifetime,
        CardZoneInsertionDefinition insertion,
        int? capacity,
        CardZoneOrdering ordering,
        DeterministicContext context)
    {
        ArgumentNullException.ThrowIfNull(definitionIds);
        return CreateInstances(state, target,
            definitionIds.Select(id => new CardZoneCardCreation { DefinitionId = id }).ToArray(),
            lifetime, insertion, capacity, ordering, context);
    }

    public static Result<CardZoneTransition> CreateInstances(
        CardZoneTopologyState state,
        CardZoneAddress target,
        IReadOnlyList<CardZoneCardCreation> cards,
        CardInstanceLifetimeDefinition lifetime,
        CardZoneInsertionDefinition insertion,
        int? capacity,
        CardZoneOrdering ordering,
        DeterministicContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(insertion);
        ArgumentNullException.ThrowIfNull(context);
        var valid = CardZoneTopologyValidator.Validate(state);
        if (valid.IsFailure) return Result<CardZoneTransition>.Failure(valid.Error);
        if (!state.ZoneItems.TryGetValue(target.Key, out var zone))
            return Result<CardZoneTransition>.Failure($"Card zone not found: {target.Key}");
        if (cards.Any(card => card == null || string.IsNullOrWhiteSpace(card.DefinitionId)))
            return Result<CardZoneTransition>.Failure("Card definition id cannot be empty");
        if (capacity.HasValue && zone.Items.Count + cards.Count > capacity.Value)
            return Result<CardZoneTransition>.Failure($"Card zone capacity exceeded: {target.Key}");

        var instances = state.InstanceItems.ToBuilder();
        var created = ImmutableArray.CreateBuilder<Guid>(cards.Count);
        var current = context;
        foreach (var card in cards)
        {
            var ordinal = current.IdSequence;
            var allocated = current.AllocateId($"card-zone:{target.Key}:{card.DefinitionId}");
            current = allocated.Context;
            created.Add(allocated.Value);
            instances.Add(allocated.Value, new CardInstanceState
            {
                CardInstanceId = allocated.Value,
                DefinitionId = card.DefinitionId,
                OwnerId = target.OwnerId,
                CreationOrdinal = ordinal,
                Lifetime = lifetime,
                Upgrades = card.Upgrades
            });
        }

        var inserted = Insert(zone.Items, created.ToImmutable(), insertion, current);
        if (inserted.IsFailure) return Result<CardZoneTransition>.Failure(inserted.Error);
        var items = Canonicalize(
            ApplyCreationOrder(inserted.Value.Items, insertion, instances.ToImmutable()), ordering);
        var next = state with
        {
            Instances = instances.ToImmutable(),
            Zones = state.ZoneItems.SetItem(target.Key, zone with { InstanceIds = items })
        };
        return Finish(next, inserted.Value.Context, created.ToImmutable(), created.ToImmutable(), []);
    }

    public static Result<CardZoneSelectionResult> Select(
        CardZoneTopologyState state,
        CardZoneAddress source,
        CardZoneSelectionDefinition selection,
        DeterministicContext context,
        Func<CardInstanceState, bool>? predicate = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(context);
        var valid = CardZoneTopologyValidator.Validate(state);
        if (valid.IsFailure) return Result<CardZoneSelectionResult>.Failure(valid.Error);
        if (!state.ZoneItems.TryGetValue(source.Key, out var zone))
            return Result<CardZoneSelectionResult>.Failure($"Card zone not found: {source.Key}");

        var eligible = zone.Items.Where(id => predicate?.Invoke(state.InstanceItems[id]) ?? true).ToImmutableArray();
        var requested = selection.Count ??
            (selection.Strategy == CardZoneSelectionStrategy.All || selection.SelectAllMatches ? eligible.Length : 1);
        if (requested < 0)
            return Result<CardZoneSelectionResult>.Failure("Card-zone selection count cannot be negative");
        var current = context;
        ImmutableArray<Guid> selected;
        switch (selection.Strategy)
        {
            case CardZoneSelectionStrategy.Explicit:
                selected = selection.InstanceIds.ToImmutableArray();
                if (selected.Distinct().Count() != selected.Length || selected.Any(id => !eligible.Contains(id)))
                    return Result<CardZoneSelectionResult>.Failure("Explicit card-zone selection contains an unavailable identity");
                break;
            case CardZoneSelectionStrategy.Top:
            case CardZoneSelectionStrategy.First:
                selected = eligible.Take(requested).ToImmutableArray();
                break;
            case CardZoneSelectionStrategy.Bottom:
            case CardZoneSelectionStrategy.Last:
                selected = eligible.TakeLast(requested).ToImmutableArray();
                break;
            case CardZoneSelectionStrategy.All:
                selected = eligible;
                break;
            case CardZoneSelectionStrategy.Random:
                var pool = eligible.ToBuilder();
                var random = ImmutableArray.CreateBuilder<Guid>(System.Math.Min(requested, pool.Count));
                while (random.Count < requested && pool.Count > 0)
                {
                    var draw = current.DrawInt32(pool.Count);
                    current = draw.Context;
                    random.Add(pool[draw.Value]);
                    pool.RemoveAt(draw.Value);
                }
                selected = random.ToImmutable();
                break;
            case CardZoneSelectionStrategy.ByDefinition:
                selected = eligible.Where(id => selection.DefinitionIds.Contains(
                        state.InstanceItems[id].DefinitionId, StringComparer.Ordinal))
                    .Take(requested).ToImmutableArray();
                break;
            case CardZoneSelectionStrategy.ByTags:
            case CardZoneSelectionStrategy.ByCondition:
                if (predicate == null)
                    return Result<CardZoneSelectionResult>.Failure($"{selection.Strategy} requires a compiled predicate");
                selected = eligible.Take(requested).ToImmutableArray();
                break;
            default:
                return Result<CardZoneSelectionResult>.Failure($"Unsupported card-zone selection strategy: {selection.Strategy}");
        }
        return Result<CardZoneSelectionResult>.Success(new(selected, current));
    }

    public static Result<CardZoneTransition> Move(
        CardZoneTopologyState state,
        CardZoneAddress source,
        CardZoneAddress target,
        IReadOnlyList<Guid> instanceIds,
        CardZoneInsertionDefinition insertion,
        int? targetCapacity,
        CardZoneOrdering sourceOrdering,
        CardZoneOrdering targetOrdering,
        DeterministicContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(instanceIds);
        ArgumentNullException.ThrowIfNull(insertion);
        var valid = CardZoneTopologyValidator.Validate(state);
        if (valid.IsFailure) return Result<CardZoneTransition>.Failure(valid.Error);
        if (!state.ZoneItems.TryGetValue(source.Key, out var sourceZone))
            return Result<CardZoneTransition>.Failure($"Card zone not found: {source.Key}");
        if (!state.ZoneItems.TryGetValue(target.Key, out var targetZone))
            return Result<CardZoneTransition>.Failure($"Card zone not found: {target.Key}");
        var ids = instanceIds.ToImmutableArray();
        if (ids.Distinct().Count() != ids.Length || ids.Any(id => !sourceZone.Items.Contains(id)))
            return Result<CardZoneTransition>.Failure("Card-zone move contains an unavailable or duplicate identity");
        if (targetCapacity.HasValue && targetZone.Items.Count + ids.Length > targetCapacity.Value)
            return Result<CardZoneTransition>.Failure($"Card zone capacity exceeded: {target.Key}");
        if (source.Key == target.Key)
            return Result<CardZoneTransition>.Failure("Card-zone move requires different source and target zones");

        var inserted = Insert(targetZone.Items, ids, insertion, context);
        if (inserted.IsFailure) return Result<CardZoneTransition>.Failure(inserted.Error);
        var zones = state.ZoneItems
            .SetItem(source.Key, sourceZone with
            {
                InstanceIds = Canonicalize(sourceZone.Items.RemoveRange(ids), sourceOrdering)
            })
            .SetItem(target.Key, targetZone with
            {
                InstanceIds = Canonicalize(
                    ApplyCreationOrder(inserted.Value.Items, insertion, state.InstanceItems), targetOrdering)
            });
        var next = state with { Zones = zones };
        return Finish(next, inserted.Value.Context, ids, [], []);
    }

    public static Result<CardZoneTransition> Destroy(
        CardZoneTopologyState state,
        IReadOnlyList<Guid> instanceIds,
        CardZoneOrdering ordering,
        DeterministicContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(instanceIds);
        var valid = CardZoneTopologyValidator.Validate(state);
        if (valid.IsFailure) return Result<CardZoneTransition>.Failure(valid.Error);
        var ids = instanceIds.ToImmutableArray();
        if (ids.Distinct().Count() != ids.Length || ids.Any(id => !state.InstanceItems.ContainsKey(id)))
            return Result<CardZoneTransition>.Failure("Card-zone destroy contains an unknown or duplicate identity");
        var zones = state.ZoneItems;
        foreach (var pair in state.ZoneItems)
        {
            var remaining = pair.Value.Items.RemoveRange(ids);
            if (remaining.Count != pair.Value.Items.Count)
                zones = zones.SetItem(pair.Key, pair.Value with { InstanceIds = Canonicalize(remaining, ordering) });
        }
        var next = state with
        {
            Instances = state.InstanceItems.RemoveRange(ids),
            Zones = zones
        };
        return Finish(next, context, ids, [], ids);
    }

    public static Result<CardZoneTransition> ApplyUpgrade(
        CardZoneTopologyState state,
        Guid cardInstanceId,
        CardUpgradeDefinition definition,
        DeterministicContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);
        var valid = CardZoneTopologyValidator.Validate(state);
        if (valid.IsFailure) return Result<CardZoneTransition>.Failure(valid.Error);
        if (!state.InstanceItems.TryGetValue(cardInstanceId, out var instance))
            return Result<CardZoneTransition>.Failure($"Card instance not found: {cardInstanceId}");
        var upgraded = CardInstanceUpgradeTransitions.Apply(instance, definition, context.ContentRevision);
        if (upgraded.IsFailure) return Result<CardZoneTransition>.Failure(upgraded.Error);
        var next = state with
        {
            Instances = state.InstanceItems.SetItem(cardInstanceId, upgraded.Value)
        };
        return Finish(next, context, [cardInstanceId], [], []);
    }

    public static Result<CardZoneTransition> Shuffle(
        CardZoneTopologyState state,
        CardZoneAddress zoneAddress,
        DeterministicContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        var valid = CardZoneTopologyValidator.Validate(state);
        if (valid.IsFailure) return Result<CardZoneTransition>.Failure(valid.Error);
        if (!state.ZoneItems.TryGetValue(zoneAddress.Key, out var zone))
            return Result<CardZoneTransition>.Failure($"Card zone not found: {zoneAddress.Key}");
        var items = zone.Items.ToBuilder();
        var current = context;
        for (var index = items.Count - 1; index > 0; index--)
        {
            var draw = current.DrawInt32(index + 1);
            current = draw.Context;
            (items[index], items[draw.Value]) = (items[draw.Value], items[index]);
        }
        var ids = items.ToImmutable();
        return Finish(state with
        {
            Zones = state.ZoneItems.SetItem(zoneAddress.Key, zone with { InstanceIds = ids })
        }, current, ids.ToImmutableArray(), [], []);
    }

    public static Result<CardZoneTransition> Reorder(
        CardZoneTopologyState state,
        CardZoneAddress zoneAddress,
        IReadOnlyList<Guid> orderedInstanceIds,
        DeterministicContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!state.ZoneItems.TryGetValue(zoneAddress.Key, out var zone))
            return Result<CardZoneTransition>.Failure($"Card zone not found: {zoneAddress.Key}");
        var ids = orderedInstanceIds.ToImmutableArray();
        if (ids.Distinct().Count() != ids.Length || !ids.ToHashSet().SetEquals(zone.Items))
            return Result<CardZoneTransition>.Failure("Card-zone reorder must contain every current identity exactly once");
        return Finish(state with
        {
            Zones = state.ZoneItems.SetItem(zoneAddress.Key, zone with { InstanceIds = ids })
        }, context, ids, [], []);
    }

    private static Result<CardZoneTransition> Finish(
        CardZoneTopologyState state,
        DeterministicContext context,
        ImmutableArray<Guid> affected,
        ImmutableArray<Guid> created,
        ImmutableArray<Guid> destroyed)
    {
        var valid = CardZoneTopologyValidator.Validate(state);
        return valid.IsFailure
            ? Result<CardZoneTransition>.Failure(valid.Error)
            : Result<CardZoneTransition>.Success(new(state, context, affected, created, destroyed));
    }

    private static ImmutableList<Guid> Canonicalize(ImmutableList<Guid> items, CardZoneOrdering ordering) =>
        ordering == CardZoneOrdering.Unordered
            ? items.OrderBy(id => id).ToImmutableList()
            : items;

    private static ImmutableList<Guid> ApplyCreationOrder(
        ImmutableList<Guid> items,
        CardZoneInsertionDefinition insertion,
        IReadOnlyDictionary<Guid, CardInstanceState> instances) =>
        insertion.Strategy == CardZoneInsertionStrategy.CreationOrder
            ? items.OrderBy(id => instances[id].CreationOrdinal).ThenBy(id => id).ToImmutableList()
            : items;

    private static Result<(ImmutableList<Guid> Items, DeterministicContext Context)> Insert(
        ImmutableList<Guid> current,
        ImmutableArray<Guid> inserted,
        CardZoneInsertionDefinition insertion,
        DeterministicContext context)
    {
        var next = current;
        var currentContext = context;
        switch (insertion.Strategy)
        {
            case CardZoneInsertionStrategy.Top:
                next = next.InsertRange(0, inserted);
                break;
            case CardZoneInsertionStrategy.Bottom:
            case CardZoneInsertionStrategy.PreserveSourceOrder:
            case CardZoneInsertionStrategy.CreationOrder:
                next = next.AddRange(inserted);
                break;
            case CardZoneInsertionStrategy.AtIndex:
                if (insertion.Index is null || insertion.Index < 0 || insertion.Index > current.Count)
                    return Result<(ImmutableList<Guid>, DeterministicContext)>.Failure("Card-zone insertion index is invalid");
                next = next.InsertRange(insertion.Index.Value, inserted);
                break;
            case CardZoneInsertionStrategy.RandomPosition:
                foreach (var id in inserted)
                {
                    var draw = currentContext.DrawInt32(next.Count + 1);
                    currentContext = draw.Context;
                    next = next.Insert(draw.Value, id);
                }
                break;
            case CardZoneInsertionStrategy.ShuffleAfterInsert:
                next = next.AddRange(inserted);
                var builder = next.ToBuilder();
                for (var index = builder.Count - 1; index > 0; index--)
                {
                    var draw = currentContext.DrawInt32(index + 1);
                    currentContext = draw.Context;
                    (builder[index], builder[draw.Value]) = (builder[draw.Value], builder[index]);
                }
                next = builder.ToImmutable();
                break;
            default:
                return Result<(ImmutableList<Guid>, DeterministicContext)>.Failure(
                    $"Unsupported card-zone insertion strategy: {insertion.Strategy}");
        }
        return Result<(ImmutableList<Guid>, DeterministicContext)>.Success((next, currentContext));
    }
}

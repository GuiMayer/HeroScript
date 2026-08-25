using System.Collections.Immutable;
using Core.Common;
using Core.Determinism;

namespace Core.Run;

public sealed record DeckTransition(
    DeckState State,
    DeterministicContext Context,
    ImmutableArray<string> Cards);

/// <summary>
/// Pure deck state machine. Input instances are never modified.
/// </summary>
public static class DeckTransitions
{
    public static Result<DeckTransition> Create(
        IReadOnlyList<string> definitionIds,
        DeterministicContext context)
    {
        ArgumentNullException.ThrowIfNull(definitionIds);
        return Create(
            definitionIds.Select(definitionId => new RunStartingCard { DefinitionId = definitionId }).ToArray(),
            context);
    }

    public static Result<DeckTransition> Create(
        IReadOnlyList<RunStartingCard> cards,
        DeterministicContext context)
    {
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(context);

        var declarations = cards
            .Where(card => !string.IsNullOrWhiteSpace(card.DefinitionId))
            .ToImmutableArray();
        var definitions = declarations.Select(card => card.DefinitionId).ToImmutableArray();
        var instances = ImmutableDictionary.CreateBuilder<Guid, CardInstanceState>();
        var instanceIds = ImmutableList.CreateBuilder<Guid>();
        var currentContext = context;
        for (var index = 0; index < definitions.Length; index++)
        {
            var allocated = currentContext.AllocateId($"card:{definitions[index]}:{index}");
            currentContext = allocated.Context;
            instanceIds.Add(allocated.Value);
            instances.Add(allocated.Value, new CardInstanceState
            {
                CardInstanceId = allocated.Value,
                DefinitionId = definitions[index],
                Upgrades = declarations[index].Upgrades
            });
        }

        var state = new DeckState
        {
            InstanceTrackingEnabled = true,
            DrawPile = definitions,
            DrawPileInstanceIds = instanceIds.ToImmutable(),
            CardInstances = instances.ToImmutable()
        };
        return Result<DeckTransition>.Success(new DeckTransition(state, currentContext, definitions));
    }

    public static Result<DeckTransition> Draw(
        DeckState state,
        int count,
        DeterministicContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);

        if (count < 0)
            return Result<DeckTransition>.Failure("Draw count cannot be negative");
        var topology = ValidateInstanceTopology(state);
        if (topology.IsFailure)
            return Result<DeckTransition>.Failure(topology.Error);

        var current = state;
        var currentContext = context;
        var drawn = ImmutableArray.CreateBuilder<string>(count);

        for (var index = 0; index < count; index++)
        {
            if (current.DrawPileItems.IsEmpty)
            {
                var shuffled = ShuffleDiscardIntoDrawPile(current, currentContext);
                current = shuffled.State;
                currentContext = shuffled.Context;
            }

            if (current.DrawPileItems.IsEmpty)
                break;

            var cardId = current.DrawPileItems[0];
            current = current with
            {
                DrawPile = current.DrawPileItems.RemoveAt(0),
                Hand = current.HandItems.Add(cardId),
                DrawPileInstanceIds = current.InstanceTrackingEnabled
                    ? current.DrawPileInstanceIdItems.RemoveAt(0)
                    : current.DrawPileInstanceIdItems,
                HandInstanceIds = current.InstanceTrackingEnabled
                    ? current.HandInstanceIdItems.Add(current.DrawPileInstanceIdItems[0])
                    : current.HandInstanceIdItems
            };
            drawn.Add(cardId);
        }

        return Result<DeckTransition>.Success(
            new DeckTransition(current, currentContext, drawn.MoveToImmutable()));
    }

    public static Result<DeckTransition> AddToHand(
        DeckState state,
        IReadOnlyList<string> cardIds,
        DeterministicContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(cardIds);
        ArgumentNullException.ThrowIfNull(context);

        var cards = cardIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToImmutableArray();
        var added = AddInstances(state, cards, context);
        if (added.IsFailure)
            return Result<DeckTransition>.Failure(added.Error);
        return Result<DeckTransition>.Success(
            new DeckTransition(
                state with
                {
                    Hand = state.HandItems.AddRange(cards),
                    CardInstances = added.Value.Instances,
                    HandInstanceIds = state.InstanceTrackingEnabled
                        ? state.HandInstanceIdItems.AddRange(added.Value.InstanceIds)
                        : state.HandInstanceIdItems
                },
                added.Value.Context,
                cards));
    }

    public static Result<DeckTransition> AddToDiscard(
        DeckState state,
        IReadOnlyList<string> cardIds,
        DeterministicContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(cardIds);
        ArgumentNullException.ThrowIfNull(context);

        var cards = cardIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToImmutableArray();
        var added = AddInstances(state, cards, context);
        if (added.IsFailure)
            return Result<DeckTransition>.Failure(added.Error);
        return Result<DeckTransition>.Success(
            new DeckTransition(
                state with
                {
                    DiscardPile = state.DiscardPileItems.AddRange(cards),
                    CardInstances = added.Value.Instances,
                    DiscardPileInstanceIds = state.InstanceTrackingEnabled
                        ? state.DiscardPileInstanceIdItems.AddRange(added.Value.InstanceIds)
                        : state.DiscardPileInstanceIdItems
                },
                added.Value.Context,
                cards));
    }

    public static Result<DeckTransition> MoveFromHand(
        DeckState state,
        IReadOnlyList<string> cardIds,
        CardConsumeDestination destination,
        DeterministicContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(cardIds);
        ArgumentNullException.ThrowIfNull(context);

        var cards = cardIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToImmutableArray();
        if (cards.IsEmpty)
            return Result<DeckTransition>.Failure("At least one card id is required");

        if (destination is not (CardConsumeDestination.None or CardConsumeDestination.Discard or CardConsumeDestination.Exhaust))
            return Result<DeckTransition>.Failure($"Unsupported card consume destination: {destination}");
        var topology = ValidateInstanceTopology(state);
        if (topology.IsFailure)
            return Result<DeckTransition>.Failure(topology.Error);

        var remainingHand = state.HandItems.ToBuilder();
        var remainingInstanceIds = state.HandInstanceIdItems.ToBuilder();
        var movedDefinitions = ImmutableArray.CreateBuilder<string>(cards.Length);
        var movedInstanceIds = ImmutableArray.CreateBuilder<Guid>(cards.Length);
        foreach (var cardReference in cards)
        {
            var index = -1;
            if (state.InstanceTrackingEnabled && Guid.TryParse(cardReference, out var instanceId))
                index = remainingInstanceIds.IndexOf(instanceId);
            if (index < 0)
                index = remainingHand.IndexOf(cardReference);
            if (index < 0)
                return Result<DeckTransition>.Failure($"Card not found in hand: {cardReference}");

            movedDefinitions.Add(remainingHand[index]);
            remainingHand.RemoveAt(index);
            if (state.InstanceTrackingEnabled)
            {
                movedInstanceIds.Add(remainingInstanceIds[index]);
                remainingInstanceIds.RemoveAt(index);
            }
        }

        if (destination == CardConsumeDestination.None)
            return Result<DeckTransition>.Success(
                new DeckTransition(state, context, movedDefinitions.ToImmutable()));

        var next = state with
        {
            Hand = remainingHand.ToImmutable(),
            HandInstanceIds = remainingInstanceIds.ToImmutable()
        };
        next = destination == CardConsumeDestination.Discard
            ? next with
            {
                DiscardPile = next.DiscardPileItems.AddRange(movedDefinitions),
                DiscardPileInstanceIds = next.DiscardPileInstanceIdItems.AddRange(movedInstanceIds)
            }
            : next with
            {
                ExhaustPile = next.ExhaustPileItems.AddRange(movedDefinitions),
                ExhaustPileInstanceIds = next.ExhaustPileInstanceIdItems.AddRange(movedInstanceIds)
            };

        return Result<DeckTransition>.Success(
            new DeckTransition(next, context, movedDefinitions.ToImmutable()));
    }

    public static DeckTransition ShuffleDiscardIntoDrawPile(
        DeckState state,
        DeterministicContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);

        if (state.DiscardPileItems.IsEmpty)
            return new DeckTransition(state, context, []);

        var topology = ValidateInstanceTopology(state);
        if (topology.IsFailure)
            throw new InvalidOperationException(topology.Error);

        var shuffled = state.DiscardPileItems.ToBuilder();
        var shuffledInstanceIds = state.DiscardPileInstanceIdItems.ToBuilder();
        var currentContext = context;
        for (var index = shuffled.Count - 1; index > 0; index--)
        {
            var draw = currentContext.DrawInt32(index + 1);
            currentContext = draw.Context;
            (shuffled[index], shuffled[draw.Value]) = (shuffled[draw.Value], shuffled[index]);
            if (state.InstanceTrackingEnabled)
            {
                (shuffledInstanceIds[index], shuffledInstanceIds[draw.Value]) =
                    (shuffledInstanceIds[draw.Value], shuffledInstanceIds[index]);
            }
        }

        var cards = shuffled.ToImmutable();
        var next = state with
        {
            DrawPile = state.DrawPileItems.AddRange(cards),
            DiscardPile = [],
            DrawPileInstanceIds = state.InstanceTrackingEnabled
                ? state.DrawPileInstanceIdItems.AddRange(shuffledInstanceIds)
                : state.DrawPileInstanceIdItems,
            DiscardPileInstanceIds = []
        };
        return new DeckTransition(next, currentContext, cards.ToImmutableArray());
    }

    public static Result<DeckTransition> ApplyUpgrade(
        DeckState state,
        Guid cardInstanceId,
        CardUpgradeDefinition definition,
        DeterministicContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(context);

        if (!state.InstanceTrackingEnabled)
            return Result<DeckTransition>.Failure("Card instance tracking is unavailable for this legacy run");
        if (!state.CardInstanceItems.TryGetValue(cardInstanceId, out var instance))
            return Result<DeckTransition>.Failure($"Card instance not found: {cardInstanceId}");
        if (string.IsNullOrWhiteSpace(definition.UpgradeId))
            return Result<DeckTransition>.Failure("Card upgrade id is required");
        if (!definition.AppliesTo(instance.DefinitionId))
            return Result<DeckTransition>.Failure(
                $"Upgrade {definition.UpgradeId} does not apply to {instance.DefinitionId}");

        var applications = instance.UpgradeItems.Count(upgrade =>
            string.Equals(upgrade.UpgradeId, definition.UpgradeId, StringComparison.Ordinal));
        if (applications >= System.Math.Max(1, definition.MaxApplications))
            return Result<DeckTransition>.Failure(
                $"Upgrade application limit reached: {definition.UpgradeId}");

        var upgrade = new CardUpgradeState
        {
            UpgradeId = definition.UpgradeId,
            Deltas = definition.Deltas
        };
        var updated = instance with { Upgrades = instance.UpgradeItems.Add(upgrade) };
        var next = state with
        {
            CardInstances = state.CardInstanceItems.SetItem(cardInstanceId, updated)
        };
        return Result<DeckTransition>.Success(
            new DeckTransition(next, context, [instance.DefinitionId]));
    }

    private static Result<AddedCardInstances> AddInstances(
        DeckState state,
        ImmutableArray<string> definitions,
        DeterministicContext context)
    {
        var topology = ValidateInstanceTopology(state);
        if (topology.IsFailure)
            return Result<AddedCardInstances>.Failure(topology.Error);
        if (!state.InstanceTrackingEnabled || definitions.IsEmpty)
        {
            return Result<AddedCardInstances>.Success(new AddedCardInstances(
                state.CardInstanceItems,
                [],
                context));
        }

        var instances = state.CardInstanceItems.ToBuilder();
        var addedIds = ImmutableList.CreateBuilder<Guid>();
        var currentContext = context;
        foreach (var definitionId in definitions)
        {
            var allocated = currentContext.AllocateId($"card:{definitionId}:acquired");
            currentContext = allocated.Context;
            addedIds.Add(allocated.Value);
            instances.Add(allocated.Value, new CardInstanceState
            {
                CardInstanceId = allocated.Value,
                DefinitionId = definitionId
            });
        }

        return Result<AddedCardInstances>.Success(new AddedCardInstances(
            instances.ToImmutable(),
            addedIds.ToImmutable(),
            currentContext));
    }

    private static Result ValidateInstanceTopology(DeckState state)
    {
        if (!state.InstanceTrackingEnabled)
            return Result.Success();
        if (state.DrawPileItems.Count != state.DrawPileInstanceIdItems.Count ||
            state.HandItems.Count != state.HandInstanceIdItems.Count ||
            state.DiscardPileItems.Count != state.DiscardPileInstanceIdItems.Count ||
            state.ExhaustPileItems.Count != state.ExhaustPileInstanceIdItems.Count)
        {
            return Result.Failure("Card instance topology does not match deck zones");
        }

        var zoneIds = state.DrawPileInstanceIdItems
            .Concat(state.HandInstanceIdItems)
            .Concat(state.DiscardPileInstanceIdItems)
            .Concat(state.ExhaustPileInstanceIdItems)
            .ToArray();
        if (zoneIds.Distinct().Count() != zoneIds.Length ||
            zoneIds.Any(instanceId => !state.CardInstanceItems.ContainsKey(instanceId)))
        {
            return Result.Failure("Card instance topology contains duplicate or unknown identities");
        }

        return Result.Success();
    }

    private sealed record AddedCardInstances(
        ImmutableDictionary<Guid, CardInstanceState> Instances,
        ImmutableList<Guid> InstanceIds,
        DeterministicContext Context);
}

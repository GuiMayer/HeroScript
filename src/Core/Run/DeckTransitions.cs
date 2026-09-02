using System.Collections.Immutable;
using Core.Common;
using Core.Determinism;

namespace Core.Run;

public sealed record DeckTransition(
    DeckState State,
    DeterministicContext Context,
    ImmutableArray<string> Cards);

/// <summary>Pure instance-only card collection and zone state machine.</summary>
public static class DeckTransitions
{
    public static Result<DeckTransition> Create(
        IReadOnlyList<string> definitionIds,
        DeterministicContext context) =>
        Create(
            definitionIds.Select(id => new RunStartingCard { DefinitionId = id }).ToArray(),
            context);

    public static Result<DeckTransition> Create(
        IReadOnlyList<RunStartingCard> cards,
        DeterministicContext context)
    {
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(context);
        if (cards.Any(card => string.IsNullOrWhiteSpace(card.DefinitionId)))
            return Result<DeckTransition>.Failure("Card definition id cannot be empty");

        var instances = ImmutableDictionary.CreateBuilder<Guid, CardInstanceState>();
        var drawPile = ImmutableList.CreateBuilder<Guid>();
        var definitions = ImmutableArray.CreateBuilder<string>();
        var currentContext = context;
        for (var index = 0; index < cards.Count; index++)
        {
            var declaration = cards[index];
            var allocated = currentContext.AllocateId($"card:{declaration.DefinitionId}:{index}");
            currentContext = allocated.Context;
            drawPile.Add(allocated.Value);
            definitions.Add(declaration.DefinitionId);
            instances.Add(allocated.Value, new CardInstanceState
            {
                CardInstanceId = allocated.Value,
                DefinitionId = declaration.DefinitionId,
                Upgrades = declaration.Upgrades
            });
        }

        return Result<DeckTransition>.Success(new DeckTransition(
            new DeckState
            {
                DrawPileInstanceIds = drawPile.ToImmutable(),
                CardInstances = instances.ToImmutable()
            },
            currentContext,
            definitions.ToImmutable()));
    }

    public static Result<DeckTransition> Draw(
        DeckState state,
        int count,
        DeterministicContext context) =>
        Draw(state, count, context, shuffleDiscardWhenEmpty: true, allowPartialDraw: true);

    public static Result<DeckTransition> Draw(
        DeckState state,
        int count,
        DeterministicContext context,
        bool shuffleDiscardWhenEmpty,
        bool allowPartialDraw)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);
        if (count < 0)
            return Result<DeckTransition>.Failure("Draw count cannot be negative");
        var topology = ValidateTopology(state);
        if (topology.IsFailure)
            return Result<DeckTransition>.Failure(topology.Error);

        var current = state;
        var currentContext = context;
        var drawn = ImmutableArray.CreateBuilder<string>(count);
        for (var index = 0; index < count; index++)
        {
            if (current.DrawPileItems.IsEmpty && shuffleDiscardWhenEmpty)
            {
                var shuffled = ShuffleDiscardIntoDrawPile(current, currentContext);
                current = shuffled.State;
                currentContext = shuffled.Context;
            }
            if (current.DrawPileItems.IsEmpty)
                break;

            var instanceId = current.DrawPileItems[0];
            var definitionId = current.GetDefinitionId(instanceId)!;
            current = current with
            {
                DrawPileInstanceIds = current.DrawPileItems.RemoveAt(0),
                HandInstanceIds = current.HandItems.Add(instanceId)
            };
            drawn.Add(definitionId);
        }

        if (!allowPartialDraw && drawn.Count != count)
        {
            return Result<DeckTransition>.Failure(
                $"Unable to draw {count} cards without a partial draw; only {drawn.Count} are available");
        }
        return Result<DeckTransition>.Success(
            new DeckTransition(current, currentContext, drawn.MoveToImmutable()));
    }

    public static Result<DeckTransition> AddToHand(
        DeckState state,
        IReadOnlyList<string> definitionIds,
        DeterministicContext context) =>
        AddToZone(state, definitionIds, context, CardZone.Hand);

    public static Result<DeckTransition> AddToDiscard(
        DeckState state,
        IReadOnlyList<string> definitionIds,
        DeterministicContext context) =>
        AddToZone(state, definitionIds, context, CardZone.Discard);

    public static Result<DeckTransition> MoveFromHand(
        DeckState state,
        IReadOnlyList<string> cardInstanceIds,
        CardConsumeDestination destination,
        DeterministicContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(cardInstanceIds);
        ArgumentNullException.ThrowIfNull(context);
        if (cardInstanceIds.Count == 0)
            return Result<DeckTransition>.Failure("At least one card instance id is required");
        if (destination is not (CardConsumeDestination.None or CardConsumeDestination.Discard or CardConsumeDestination.Exhaust))
            return Result<DeckTransition>.Failure($"Unsupported card consume destination: {destination}");
        var topology = ValidateTopology(state);
        if (topology.IsFailure)
            return Result<DeckTransition>.Failure(topology.Error);

        var parsed = new List<Guid>(cardInstanceIds.Count);
        foreach (var value in cardInstanceIds)
        {
            if (!Guid.TryParse(value, out var instanceId))
                return Result<DeckTransition>.Failure($"Invalid card instance id: {value}");
            if (!state.HandItems.Contains(instanceId))
                return Result<DeckTransition>.Failure($"Card instance not found in hand: {instanceId}");
            if (parsed.Contains(instanceId))
                return Result<DeckTransition>.Failure($"Card instance selected more than once: {instanceId}");
            parsed.Add(instanceId);
        }
        var definitions = parsed.Select(id => state.GetDefinitionId(id)!).ToImmutableArray();
        if (destination == CardConsumeDestination.None)
            return Result<DeckTransition>.Success(new DeckTransition(state, context, definitions));

        var remaining = state.HandItems.RemoveRange(parsed);
        var next = destination == CardConsumeDestination.Discard
            ? state with
            {
                HandInstanceIds = remaining,
                DiscardPileInstanceIds = state.DiscardPileItems.AddRange(parsed)
            }
            : state with
            {
                HandInstanceIds = remaining,
                ExhaustPileInstanceIds = state.ExhaustPileItems.AddRange(parsed)
            };
        return Result<DeckTransition>.Success(new DeckTransition(next, context, definitions));
    }

    public static DeckTransition ShuffleDiscardIntoDrawPile(
        DeckState state,
        DeterministicContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);
        if (state.DiscardPileItems.IsEmpty)
            return new DeckTransition(state, context, []);
        var topology = ValidateTopology(state);
        if (topology.IsFailure)
            throw new InvalidOperationException(topology.Error);

        var shuffled = state.DiscardPileItems.ToBuilder();
        var currentContext = context;
        for (var index = shuffled.Count - 1; index > 0; index--)
        {
            var draw = currentContext.DrawInt32(index + 1);
            currentContext = draw.Context;
            (shuffled[index], shuffled[draw.Value]) = (shuffled[draw.Value], shuffled[index]);
        }
        var cards = state.ResolveDefinitionIds(shuffled).ToImmutableArray();
        return new DeckTransition(
            state with
            {
                DrawPileInstanceIds = state.DrawPileItems.AddRange(shuffled),
                DiscardPileInstanceIds = []
            },
            currentContext,
            cards);
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
            return Result<DeckTransition>.Failure($"Upgrade application limit reached: {definition.UpgradeId}");

        var upgrade = new CardUpgradeState
        {
            UpgradeId = definition.UpgradeId,
            Patches = definition.Patches
        };
        var next = state with
        {
            CardInstances = state.CardInstanceItems.SetItem(
                cardInstanceId,
                instance with { Upgrades = instance.UpgradeItems.Add(upgrade) })
        };
        return Result<DeckTransition>.Success(
            new DeckTransition(next, context, [instance.DefinitionId]));
    }

    public static Result ValidateTopology(DeckState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.TopologyVersion != DeckState.CurrentTopologyVersion)
        {
            return Result.Failure(
                $"Unsupported card topology version: {state.TopologyVersion}");
        }
        var zoneIds = state.DrawPileItems
            .Concat(state.HandItems)
            .Concat(state.DiscardPileItems)
            .Concat(state.ExhaustPileItems)
            .ToArray();
        if (zoneIds.Distinct().Count() != zoneIds.Length)
            return Result.Failure("Card instance topology contains duplicate zone identities");
        if (zoneIds.Any(id => !state.CardInstanceItems.ContainsKey(id)))
            return Result.Failure("Card instance topology contains an unknown identity");
        if (state.CardInstanceItems.Any(pair => pair.Key == Guid.Empty ||
                                                pair.Value.CardInstanceId != pair.Key ||
                                                string.IsNullOrWhiteSpace(pair.Value.DefinitionId)))
            return Result.Failure("Card collection contains an invalid instance");
        return Result.Success();
    }

    private static Result<DeckTransition> AddToZone(
        DeckState state,
        IReadOnlyList<string> definitionIds,
        DeterministicContext context,
        CardZone zone)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(definitionIds);
        ArgumentNullException.ThrowIfNull(context);
        var definitions = definitionIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToImmutableArray();
        var topology = ValidateTopology(state);
        if (topology.IsFailure)
            return Result<DeckTransition>.Failure(topology.Error);

        var instances = state.CardInstanceItems.ToBuilder();
        var ids = ImmutableList.CreateBuilder<Guid>();
        var currentContext = context;
        foreach (var definitionId in definitions)
        {
            var allocated = currentContext.AllocateId($"card:{definitionId}:acquired");
            currentContext = allocated.Context;
            ids.Add(allocated.Value);
            instances.Add(allocated.Value, new CardInstanceState
            {
                CardInstanceId = allocated.Value,
                DefinitionId = definitionId
            });
        }
        var next = state with { CardInstances = instances.ToImmutable() };
        next = zone == CardZone.Hand
            ? next with { HandInstanceIds = state.HandItems.AddRange(ids) }
            : next with { DiscardPileInstanceIds = state.DiscardPileItems.AddRange(ids) };
        return Result<DeckTransition>.Success(
            new DeckTransition(next, currentContext, definitions));
    }

    private enum CardZone { Hand, Discard }
}

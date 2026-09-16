using System.Collections.Immutable;
using Core.CardZones;
using Core.Common;
using Core.Resources;

namespace Core.Run;

public static class ShopTransitions
{
    public static RunStateTransition<ShopState> Create(
        RunState state,
        ShopDefinition definition,
        IReadOnlyList<ShopItemState> items,
        string offerFingerprint = "",
        string nodeId = "")
    {
        var instanceId = state.Determinism.AllocateId("shop");
        var shop = new ShopState
        {
            ShopInstanceId = instanceId.Value,
            RunId = state.RunId,
            NodeId = nodeId,
            ShopId = definition.ShopId,
            CardPoolId = definition.CardPoolId,
            OfferCount = definition.OfferCount,
            OfferFingerprint = offerFingerprint,
            Pricing = definition.Pricing,
            Reroll = definition.Reroll,
            RerollCosts = CalculateRerollCosts(definition.Reroll, 0),
            Items = items
        };
        var next = state with
        {
            Shops = state.Shops.Add(shop),
            Determinism = instanceId.Context.AdvanceStep()
        };
        return new RunStateTransition<ShopState>(next, shop);
    }

    public static Result<RunStateTransition<ShopItemState>> Buy(
        RunState state,
        Guid shopInstanceId,
        string itemId,
        ICardZoneFlowExecutor? zoneFlows = null)
    {
        var located = Locate(state, shopInstanceId);
        if (located.IsFailure)
            return Result<RunStateTransition<ShopItemState>>.Failure(located.Error);

        var (shopIndex, shop) = located.Value;
        var itemIndex = shop.Items
            .Select((item, index) => (item, index))
            .FirstOrDefault(entry => entry.item.ItemId == itemId);
        if (itemIndex.item == null)
            return Result<RunStateTransition<ShopItemState>>.Failure($"Shop item not found: {itemId}");
        if (itemIndex.item.Purchased)
            return Result<RunStateTransition<ShopItemState>>.Failure($"Shop item already purchased: {itemId}");
        var spent = RunResourceTransitions.Spend(
            state.ResourceState,
            itemIndex.item.Costs,
            $"shop:{shopInstanceId}:buy:{itemId}");
        if (spent.IsFailure)
            return Result<RunStateTransition<ShopItemState>>.Failure(spent.Error);

        var updatedItem = itemIndex.item with { Purchased = true };
        var updatedShop = shop with
        {
            Items = shop.Items.ToImmutableList().SetItem(itemIndex.index, updatedItem)
        };
        var next = state with
        {
            ResourceState = spent.Value.State,
            Shops = state.Shops.SetItem(shopIndex, updatedShop)
        };

        if (!string.IsNullOrWhiteSpace(updatedItem.CardId))
        {
            var deck = CardZoneGrantTransitions.Grant(next, [updatedItem.CardId], zoneFlows);
            if (deck.IsFailure)
                return Result<RunStateTransition<ShopItemState>>.Failure(deck.Error);
            next = next with { Deck = deck.Value.State, Determinism = deck.Value.Context };
        }

        next = next with { Determinism = next.Determinism.AdvanceStep() };
        return Result<RunStateTransition<ShopItemState>>.Success(new(next, updatedItem));
    }

    public static Result<RunStateTransition<ShopState>> Reroll(
        RunState state,
        Guid shopInstanceId,
        IReadOnlyList<ShopItemState> items,
        string offerFingerprint = "")
    {
        var located = Locate(state, shopInstanceId);
        if (located.IsFailure)
            return Result<RunStateTransition<ShopState>>.Failure(located.Error);

        var (index, shop) = located.Value;
        var spent = RunResourceTransitions.Spend(
            state.ResourceState,
            shop.RerollCosts,
            $"shop:{shopInstanceId}:reroll:{shop.RerollsUsed + 1}");
        if (spent.IsFailure)
            return Result<RunStateTransition<ShopState>>.Failure(spent.Error);

        var rerollsUsed = checked(shop.RerollsUsed + 1);
        var updated = shop with
        {
            RerollsUsed = rerollsUsed,
            RerollCosts = CalculateRerollCosts(shop.Reroll, rerollsUsed),
            Items = items,
            OfferFingerprint = offerFingerprint
        };
        var next = state with
        {
            ResourceState = spent.Value.State,
            Shops = state.Shops.SetItem(index, updated),
            Determinism = state.Determinism.AdvanceStep()
        };
        return Result<RunStateTransition<ShopState>>.Success(new(next, updated));
    }

    private static Result<(int Index, ShopState Shop)> Locate(RunState state, Guid instanceId)
    {
        for (var index = 0; index < state.Shops.Length; index++)
        {
            if (state.Shops[index].ShopInstanceId == instanceId)
                return Result<(int, ShopState)>.Success((index, state.Shops[index]));
        }

        return Result<(int, ShopState)>.Failure($"Shop not found: {instanceId}");
    }

    private static ImmutableArray<ResourceAmount> CalculateRerollCosts(
        ShopRerollRules rules,
        int rerollsUsed) =>
        RunResourceTransitions.ProgressiveAmounts(
            rules.BaseCosts,
            rules.CostsPerReroll,
            rerollsUsed);
}

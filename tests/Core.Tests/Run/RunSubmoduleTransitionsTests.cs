using Core.Determinism;
using Core.Run;
using Xunit;

namespace Core.Tests.Run;

[Trait("Category", "Unit")]
public sealed class RunSubmoduleTransitionsTests
{
    [Fact]
    public void CardSelectionPick_ReplacesNestedStateWithoutMutatingOriginal()
    {
        var state = CreateState() with
        {
            CardSelections =
            [
                new CardSelectionState
                {
                    SelectionInstanceId = Guid.Parse("10000000-0000-8000-8000-000000000001"),
                    RunId = RunId,
                    SelectionId = "reward",
                    PickCount = 1,
                    Options = new[] { new CardSelectionOptionState { CardId = "fireball" } }
                }
            ]
        };

        var result = CardSelectionTransitions.Pick(
            state,
            state.CardSelections[0].SelectionInstanceId,
            new[] { "fireball" });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.False(state.CardSelections[0].Completed);
        Assert.Empty(state.CardSelections[0].PickedCardIds);
        Assert.Empty(state.Deck.DiscardPile);
        Assert.True(result.Value.Value.Completed);
        Assert.Equal(new[] { "fireball" }, result.Value.State.Deck.DiscardPile);
        Assert.Equal(state.Determinism.Step + 1, result.Value.State.Determinism.Step);
    }

    [Fact]
    public void ShopBuy_FailureAndSuccessLeaveOriginalUntouched()
    {
        var shopId = Guid.Parse("20000000-0000-8000-8000-000000000001");
        var state = CreateState() with
        {
            Gold = 5,
            Shops =
            [
                new ShopState
                {
                    ShopInstanceId = shopId,
                    RunId = RunId,
                    ShopId = "shop",
                    Items = new[]
                    {
                        new ShopItemState { ItemId = "card", CardId = "zap", GoldCost = 10 }
                    }
                }
            ]
        };

        var failure = ShopTransitions.Buy(state, shopId, "card");
        var success = ShopTransitions.Buy(state with { Gold = 10 }, shopId, "card");

        Assert.True(failure.IsFailure);
        Assert.False(state.Shops[0].Items[0].Purchased);
        Assert.Empty(state.Deck.DiscardPile);
        Assert.True(success.IsSuccess, success.IsFailure ? success.Error : null);
        Assert.True(success.Value.Value.Purchased);
        Assert.Equal(0, success.Value.State.Gold);
        Assert.Equal(new[] { "zap" }, success.Value.State.Deck.DiscardPile);
    }

    [Fact]
    public void PreparationPlan_SameStateAllocatesSameModifierIds()
    {
        var preparationId = Guid.Parse("30000000-0000-8000-8000-000000000001");
        var state = CreateState() with
        {
            PowerPoints = 1,
            Preparations =
            [
                new PreparationState
                {
                    PreparationInstanceId = preparationId,
                    RunId = RunId,
                    PreparationId = "prep",
                    Options = new[]
                    {
                        new PreparationOptionState
                        {
                            OptionId = "train",
                            PowerPointCost = 1,
                            AddCardsToDiscard = new[] { "fireball" },
                            ApplyModifiers = new[]
                            {
                                new PreparationModifierGrantState
                                {
                                    OwnerId = "run",
                                    ModifierId = "power",
                                    SourceId = "train"
                                }
                            }
                        }
                    }
                }
            ]
        };

        var first = PreparationTransitions.PlanApply(state, preparationId, "train");
        var second = PreparationTransitions.PlanApply(state, preparationId, "train");

        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        Assert.True(second.IsSuccess, second.IsFailure ? second.Error : null);
        Assert.Equal(first.Value.Context, second.Value.Context);
        Assert.Equal(
            first.Value.ModifierGrants.Select(grant => grant.InstanceId),
            second.Value.ModifierGrants.Select(grant => grant.InstanceId));
        Assert.NotEqual(Guid.Empty, first.Value.ModifierGrants[0].InstanceId);
        Assert.Equal(0UL, state.Determinism.IdSequence);

        var committed = PreparationTransitions.CommitApply(
            state,
            first.Value,
            new[] { first.Value.ModifierGrants[0].InstanceId });

        Assert.True(committed.IsSuccess, committed.IsFailure ? committed.Error : null);
        Assert.False(state.Preparations[0].Options[0].Applied);
        Assert.True(committed.Value.Value.Applied);
        Assert.Equal(0, committed.Value.State.PowerPoints);
        Assert.Equal(new[] { "fireball" }, committed.Value.State.Deck.DiscardPile);
        // One deterministic identity is allocated for the modifier and another
        // for the newly acquired card instance.
        Assert.Equal(2UL, committed.Value.State.Determinism.IdSequence);
    }

    [Fact]
    public void PreparationCommit_RejectsPlanAfterRunAdvances()
    {
        var preparationId = Guid.Parse("40000000-0000-8000-8000-000000000001");
        var state = CreateState() with
        {
            Preparations =
            [
                new PreparationState
                {
                    PreparationInstanceId = preparationId,
                    RunId = RunId,
                    PreparationId = "prep",
                    Options = new[] { new PreparationOptionState { OptionId = "rest" } }
                }
            ]
        };
        var plan = PreparationTransitions.PlanApply(state, preparationId, "rest").Value;

        var result = PreparationTransitions.CommitApply(
            state with { Determinism = state.Determinism.AdvanceStep() },
            plan,
            Array.Empty<Guid>());

        Assert.True(result.IsFailure);
        Assert.Contains("stale", result.Error);
    }

    [Fact]
    public void NestedState_DefensivelyCopiesListsAndDictionaries()
    {
        var tags = new List<string> { "fire" };
        var pricing = new Dictionary<string, double> { ["final"] = 10 };
        var item = new ShopItemState { Tags = tags, PricingBreakdown = pricing };

        tags.Add("magic");
        pricing["final"] = 99;

        Assert.Equal(new[] { "fire" }, item.Tags);
        Assert.Equal(10, item.PricingBreakdown["final"]);
    }

    private static readonly Guid RunId = Guid.Parse("00000000-0000-8000-8000-000000000001");

    private static RunState CreateState() => new()
    {
        RunId = RunId,
        ConfigName = "test",
        PlayerEntityId = "hero",
        Determinism = DeterministicContext.Create(42, "test-content")
    };
}

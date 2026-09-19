using Core.CardZones;
using Core.Common;
using Core.Determinism;
using Core.Run;
using Core.Combat.Modifiers;
using Core.Resources;
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
            new[] { "fireball" },
            ZoneFlows());

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.False(state.CardSelections[0].Completed);
        Assert.Empty(state.CardSelections[0].PickedCardIds);
        Assert.Empty(state.Deck.GetZoneDefinitionIds("discard", "$run"));
        Assert.True(result.Value.Value.Completed);
        Assert.Equal(new[] { "fireball" }, result.Value.State.Deck.GetZoneDefinitionIds("discard", "$run"));
        Assert.Equal(state.Determinism.Step + 1, result.Value.State.Determinism.Step);
    }

    [Fact]
    public void ShopBuy_FailureAndSuccessLeaveOriginalUntouched()
    {
        var shopId = Guid.Parse("20000000-0000-8000-8000-000000000001");
        var state = WithResources(
            CreateState(),
            new ResourceAmount { ResourceId = "gold", Amount = 5 }) with
        {
            Shops =
            [
                new ShopState
                {
                    ShopInstanceId = shopId,
                    RunId = RunId,
                    ShopId = "shop",
                    Items = new[]
                    {
                        new ShopItemState
                        {
                            ItemId = "card",
                            CardId = "zap",
                            Costs = [new ResourceAmount { ResourceId = "gold", Amount = 10 }]
                        }
                    }
                }
            ]
        };

        var failure = ShopTransitions.Buy(state, shopId, "card");
        var success = ShopTransitions.Buy(
            WithResources(state, new ResourceAmount { ResourceId = "gold", Amount = 10 }),
            shopId,
            "card",
            ZoneFlows());

        Assert.True(failure.IsFailure);
        Assert.False(state.Shops[0].Items[0].Purchased);
        Assert.Empty(state.Deck.GetZoneDefinitionIds("discard", "$run"));
        Assert.True(success.IsSuccess, success.IsFailure ? success.Error : null);
        Assert.True(success.Value.Value.Purchased);
        Assert.Equal(0, success.Value.State.ResourceState.Current("gold"));
        Assert.Equal(new[] { "zap" }, success.Value.State.Deck.GetZoneDefinitionIds("discard", "$run"));
    }

    [Fact]
    public void PreparationPlan_SameStateAllocatesSameModifierIds()
    {
        var preparationId = Guid.Parse("30000000-0000-8000-8000-000000000001");
        var state = WithResources(
            CreateState(),
            new ResourceAmount { ResourceId = "power_points", Amount = 1 }) with
        {
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
                            Costs = [new ResourceAmount { ResourceId = "power_points", Amount = 1 }],
                            GrantedCardIds = new[] { "fireball" },
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
            new[]
            {
                new ScriptModifierInstance
                {
                    InstanceId = first.Value.ModifierGrants[0].InstanceId,
                    ModifierId = "power",
                    OwnerId = first.Value.ModifierGrants[0].OwnerId,
                    Definition = new ScriptModifierDefinition { ModifierId = "power" }
                }
            },
            ZoneFlows());

        Assert.True(committed.IsSuccess, committed.IsFailure ? committed.Error : null);
        Assert.False(state.Preparations[0].Options[0].Applied);
        Assert.True(committed.Value.Value.Applied);
        Assert.Equal(0, committed.Value.State.ResourceState.Current("power_points"));
        Assert.Equal(new[] { "fireball" }, committed.Value.State.Deck.GetZoneDefinitionIds("discard", "$run"));
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
            Array.Empty<ScriptModifierInstance>());

        Assert.True(result.IsFailure);
        Assert.Contains("stale", result.Error);
    }

    [Fact]
    public void NestedState_DefensivelyCopiesListsAndDictionaries()
    {
        var tags = new List<string> { "fire" };
        var pricing = new Dictionary<string, IReadOnlyDictionary<string, double>>
        {
            ["credits"] = new Dictionary<string, double> { ["final"] = 10 }
        };
        var item = new ShopItemState { Tags = tags, PricingBreakdowns = pricing };

        tags.Add("magic");
        ((Dictionary<string, double>)pricing["credits"])["final"] = 99;

        Assert.Equal(new[] { "fire" }, item.Tags);
        Assert.Equal(10, item.PricingBreakdowns["credits"]["final"]);
    }

    [Fact]
    public void ShopBuy_MultiResourceCostIsAtomic()
    {
        var shopId = Guid.Parse("50000000-0000-8000-8000-000000000001");
        var state = WithResources(CreateState(),
            new ResourceAmount { ResourceId = "credits", Amount = 20 },
            new ResourceAmount { ResourceId = "reputation", Amount = 1 }) with
        {
            Shops =
            [
                new ShopState
                {
                    ShopInstanceId = shopId,
                    Items =
                    [
                        new ShopItemState
                        {
                            ItemId = "rare_card",
                            Costs =
                            [
                                new ResourceAmount { ResourceId = "credits", Amount = 10 },
                                new ResourceAmount { ResourceId = "reputation", Amount = 2 }
                            ]
                        }
                    ]
                }
            ]
        };

        var failure = ShopTransitions.Buy(state, shopId, "rare_card");

        Assert.True(failure.IsFailure);
        Assert.Equal(20, state.ResourceState.Current("credits"));
        Assert.Equal(1, state.ResourceState.Current("reputation"));

        var affordable = WithResources(state,
            new ResourceAmount { ResourceId = "credits", Amount = 20 },
            new ResourceAmount { ResourceId = "reputation", Amount = 2 });
        var success = ShopTransitions.Buy(affordable, shopId, "rare_card");

        Assert.True(success.IsSuccess, success.IsFailure ? success.Error : null);
        Assert.Equal(10, success.Value.State.ResourceState.Current("credits"));
        Assert.Equal(0, success.Value.State.ResourceState.Current("reputation"));
    }

    private static readonly Guid RunId = Guid.Parse("00000000-0000-8000-8000-000000000001");

    private static RunState CreateState()
    {
        var graph = ZoneGraph();
        var topology = CardZoneBootstrapper.Create(
            CardZoneSystemCompiler.Compile(graph).Value,
            new CardZoneBootstrapPlan { RunOwnerId = "$run" },
            DeterministicContext.Create(42, "test-content")).Value;
        return new()
        {
            RunId = RunId,
            ConfigName = "test",
            PlayerEntityId = "hero",
            Deck = new DeckState { Topology = topology.State },
            Determinism = topology.Context,
            ResolvedMode = new() { CardZoneSystem = graph }
        };
    }

    private static ICardZoneFlowExecutor ZoneFlows() =>
        new CardZoneFlowExecutor(new TestCardZoneRules());

    private static CardZoneSystemDefinition ZoneGraph() => new()
    {
        CardZoneSystemId = "submodule-zones",
        GameplayGrantFlowId = "grant",
        Zones = [new()
        {
            ZoneId = "discard",
            OwnerScope = CardZoneOwnerScope.RunOwner,
            Ordering = CardZoneOrdering.Ordered
        }],
        Flows = [new()
        {
            FlowId = "grant",
            AllowedInvocations = [CardZoneFlowInvocation.GameplayCommand],
            Steps = [new()
            {
                StepId = "create",
                Operation = CardZoneOperation.Create,
                TargetZoneId = "discard",
                TargetOwner = CardZoneOwnerBinding.RunOwner,
                CardDefinitionId = "$input",
                Selection = new()
                {
                    Strategy = CardZoneSelectionStrategy.Top,
                    CountFormula = "requestedCount"
                }
            }]
        }]
    };

    private sealed class TestCardZoneRules : ICardZoneRuleEvaluator
    {
        public Result<bool> EvaluateCondition(string expression, CardZoneFlowContext context) =>
            Result<bool>.Failure("Conditions are not used by this test graph");

        public Result<int> EvaluateCount(string expression, CardZoneFlowContext context) =>
            context.Variables.TryGetValue(expression, out var value)
                ? Result<int>.Success(checked((int)value))
                : Result<int>.Failure($"Missing test variable: {expression}");

        public Result<bool> Matches(
            CardInstanceState instance,
            CardZoneSelectionDefinition selection,
            CardZoneFlowContext context) =>
            Result<bool>.Failure("Predicates are not used by this test graph");
    }

    private static RunState WithResources(RunState state, params ResourceAmount[] amounts)
    {
        var pools = amounts.ToDictionary(
            amount => amount.ResourceId,
            amount => new ResourcePool
            {
                ResourceId = amount.ResourceId,
                Current = amount.Amount,
                Minimum = 0,
                Maximum = 1_000_000,
                Definition = new ResourceDefinition
                {
                    ResourceId = amount.ResourceId,
                    DisplayName = amount.ResourceId,
                    DefaultMax = 1_000_000,
                    CanExceedMax = true
                }
            },
            StringComparer.OrdinalIgnoreCase);
        return state with
        {
            ResourceState = new ResourceSet
            {
                OwnerId = $"run:{state.RunId}",
                Resources = pools
            }
        };
    }
}

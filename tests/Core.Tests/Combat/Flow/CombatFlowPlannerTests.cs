using Core.Combat;
using Core.Combat.Models;
using Core.Combat.Flow;
using Core.Combat.TurnPhase;
using Core.Common;
using Core.Determinism;
using Core.Resources;
using Core.Run;
using Xunit;

namespace Core.Tests.Combat.Flow;

public sealed class CombatFlowPlannerTests
{
    [Fact]
    public void InitializeAndAdvance_UsePinnedPoliciesWithoutMutatingInputs()
    {
        var context = DeterministicContext.Create(42UL, "revision");
        var combat = CombatTransitions.Create(
            Entity("hero", isHero: true, energy: 0),
            [Entity("enemy", isHero: false, energy: 0)],
            context) with
        {
            TurnOrder = ["hero", "enemy"]
        };
        var run = new RunState
        {
            RunId = Guid.NewGuid(),
            PlayerEntityId = "hero",
            Deck = new DeckState { Hand = ["strike", "retain"] },
            Determinism = DeterministicContext.Create(99UL, "revision")
        };
        var sequence = Sequence();
        var policies = Policies();

        var initialized = CombatFlowPlanner.Initialize(run, combat, sequence, policies);
        Assert.True(initialized.IsSuccess, initialized.IsFailure ? initialized.Error : null);
        Assert.Equal("hero", initialized.Value.ActivationState!.ActiveActorId);
        Assert.True(initialized.Value.ActivationState.WaitingForInput);
        Assert.Equal("action", initialized.Value.PhaseState!.CurrentPhaseId);
        Assert.Equal(3, initialized.Value.Hero.GetResource("energy")!.Current);
        Assert.Equal(0, combat.Hero.GetResource("energy")!.Current);

        var first = CombatFlowPlanner.AdvanceActivation(
            run,
            initialized.Value,
            run.Deck,
            run.Determinism,
            sequence,
            policies,
            ResolveAction);
        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        Assert.Equal(2, first.Value.Steps.Count);
        Assert.Equal("enemy", first.Value.Combat.ActivationState!.ActiveActorId);
        Assert.False(first.Value.Combat.ActivationState.WaitingForInput);
        Assert.Equal(["retain"], first.Value.Deck.Hand);
        Assert.Equal(["strike"], first.Value.Deck.DiscardPile);

        var second = CombatFlowPlanner.AdvanceActivation(
            run,
            first.Value.Combat,
            first.Value.Deck,
            first.Value.Steps[^1].RunDeterminism!.AdvanceStep(),
            sequence,
            policies,
            ResolveAction);
        Assert.True(second.IsSuccess, second.IsFailure ? second.Error : null);
        Assert.Equal("hero", second.Value.Combat.ActivationState!.ActiveActorId);
        Assert.True(second.Value.Combat.ActivationState.WaitingForInput);
        Assert.Equal(2, second.Value.Combat.ActivationState.Round);
        Assert.Equal(["retain", "strike"], second.Value.Deck.Hand);
        Assert.Empty(second.Value.Deck.DiscardPile);
    }

    [Fact]
    public void AdvanceActivation_ExhaustsEtherealBeforeDiscardingNonRetain()
    {
        var run = new RunState
        {
            RunId = Guid.NewGuid(),
            PlayerEntityId = "hero",
            Determinism = DeterministicContext.Create(7UL, "revision")
        };
        var combat = CombatTransitions.Create(
            Entity("hero", true, 3),
            [Entity("enemy", false, 3)],
            DeterministicContext.Create(8UL, "revision")) with
        {
            TurnOrder = ["hero", "enemy"]
        };
        combat = CombatFlowPlanner.Initialize(run, combat, Sequence(), Policies()).Value;
        var deck = new DeckState { Hand = ["ethereal", "strike", "retain"] };

        var result = CombatFlowPlanner.AdvanceActivation(
            run,
            combat,
            deck,
            run.Determinism,
            Sequence(),
            Policies(),
            ResolveAction);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(["retain"], result.Value.Deck.Hand);
        Assert.Equal(["strike"], result.Value.Deck.DiscardPile);
        Assert.Equal(["ethereal"], result.Value.Deck.ExhaustPile);
    }

    private static Result<ActionDefinition> ResolveAction(string id) =>
        Result<ActionDefinition>.Success(new ActionDefinition
        {
            ActionId = id,
            Tags = id switch
            {
                "retain" => ["retain"],
                "ethereal" => ["ethereal"],
                _ => []
            }
        });

    private static PhaseSequenceDefinition Sequence() => new()
    {
        SequenceId = "test",
        Phases =
        [
            new PhaseDefinition
            {
                PhaseId = "start",
                Role = PhaseRole.Start,
                Order = 10,
                ValidNextPhaseIds = ["action"],
                AutoTransition = true
            },
            new PhaseDefinition
            {
                PhaseId = "action",
                Role = PhaseRole.Middle,
                Order = 20,
                ValidNextPhaseIds = ["end"]
            },
            new PhaseDefinition
            {
                PhaseId = "end",
                Role = PhaseRole.End,
                Order = 30,
                ValidNextPhaseIds = ["start"],
                AutoTransition = true
            }
        ]
    };

    private static CombatFlowPoliciesDefinition Policies() => new()
    {
        ActivationOrder = new()
        {
            Strategy = ActivationOrderStrategy.RoundSnapshot,
            TieBreak = ActivationTieBreak.StableActorId
        },
        DeckCycle = new()
        {
            DrawPerActivation = 1,
            HandLimit = 10,
            ActorScope = FlowActorScope.Player,
            EndDiscard = DeckEndDiscardStrategy.NonRetain,
            RetainTags = ["retain"],
            EtherealTag = "ethereal",
            ShuffleDiscardWhenDrawEmpty = true,
            AllowPartialDraw = true,
            Fatigue = FatigueStrategy.None
        },
        ResourceCycle = new()
        {
            ResourceId = "energy",
            ActorScope = FlowActorScope.All,
            StartActivation = ResourceRefreshStrategy.ResetToMax
        }
    };

    private static CombatEntity Entity(string id, bool isHero, float energy)
    {
        static ResourcePool Pool(string id, float current, float maximum, ResourceCategory category) => new()
        {
            ResourceId = id,
            Current = current,
            Maximum = maximum,
            Minimum = 0,
            Definition = new ResourceDefinition
            {
                ResourceId = id,
                DisplayName = id,
                Category = category,
                DefaultMax = maximum
            }
        };

        return new CombatEntity
        {
            EntityId = id,
            Name = id,
            IsHero = isHero,
            ResourceState = new EntityResourceState
            {
                EntityId = id,
                Resources = new Dictionary<string, ResourcePool>
                {
                    ["health"] = Pool("health", 20, 20, ResourceCategory.VITAL),
                    ["energy"] = Pool("energy", energy, 3, ResourceCategory.TACTICAL)
                }
            }
        };
    }
}

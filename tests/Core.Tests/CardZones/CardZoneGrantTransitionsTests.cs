using Core.CardZones;
using Core.Common;
using Core.Determinism;
using Core.Run;
using Xunit;

namespace Core.Tests.CardZones;

public sealed class CardZoneGrantTransitionsTests
{
    [Fact]
    public void GameplayGrant_CreatesExactCardsInAuthoredZoneAndIsRepeatable()
    {
        var graph = new CardZoneSystemDefinition
        {
            CardZoneSystemId = "award-graph",
            GameplayGrantFlowId = "grant.create-in-vault",
            Zones = [new CardZoneDefinition
            {
                ZoneId = "vault", OwnerScope = CardZoneOwnerScope.RunOwner,
                Ordering = CardZoneOrdering.Ordered
            }],
            Flows = [new CardZoneFlowDefinition
            {
                FlowId = "grant.create-in-vault",
                AllowedInvocations = [CardZoneFlowInvocation.GameplayCommand],
                Steps = [new CardZoneFlowStepDefinition
                {
                    StepId = "award", Operation = CardZoneOperation.Create,
                    TargetZoneId = "vault", TargetOwner = CardZoneOwnerBinding.RunOwner,
                    CardDefinitionId = "$input",
                    Selection = new CardZoneSelectionDefinition
                    {
                        Strategy = CardZoneSelectionStrategy.Top,
                        CountFormula = "requestedCount"
                    }
                }]
            }]
        };
        var topology = CardZoneTransitions.CreateEmpty([new CardZoneAddress
        {
            ZoneId = "vault", OwnerId = "$run"
        }]).Value;
        var run = new RunState
        {
            PlayerEntityId = "hero",
            Deck = new DeckState { Topology = topology },
            Determinism = DeterministicContext.Create(52, "revision-a"),
            ResolvedMode = new ResolvedGameMode { CardZoneSystem = graph }
        };
        var flows = new CardZoneFlowExecutor(new CountRule());

        var first = CardZoneGrantTransitions.Grant(run, ["strike", "guard", "strike"], flows);
        var repeated = CardZoneGrantTransitions.Grant(run, ["strike", "guard", "strike"], flows);

        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        Assert.Equal(CanonicalJson.ComputeHash(first.Value), CanonicalJson.ComputeHash(repeated.Value));
        Assert.Equal(["strike", "guard", "strike"], first.Value.Cards.ToArray());
        Assert.Equal(3, first.Value.State.Topology.GetZone("vault", "$run")!.InstanceIds.Count);
        Assert.Empty(first.Value.State.HandInstanceIds);
        Assert.Empty(run.Deck.Topology.GetZone("vault", "$run")!.InstanceIds);
    }

    private sealed class CountRule : ICardZoneRuleEvaluator
    {
        public Result<int> EvaluateCount(string expression, CardZoneFlowContext context) =>
            context.Variables.TryGetValue(expression, out var value)
                ? Result<int>.Success((int)value)
                : Result<int>.Failure("Missing count variable");

        public Result<bool> EvaluateCondition(string expression, CardZoneFlowContext context) =>
            Result<bool>.Failure("No conditions are authored");

        public Result<bool> Matches(CardInstanceState instance, CardZoneSelectionDefinition selection,
            CardZoneFlowContext context) => Result<bool>.Failure("No card predicates are authored");
    }
}

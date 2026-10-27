using Core.CardZones;
using Core.Combat.Flow;
using Core.Determinism;
using Core.Math;
using Core.Common;
using Core.Run;
using Moq;
using Xunit;

namespace Core.Tests.Combat.Flow;

public sealed class ParticipantVictoryTests
{
    [Fact]
    public void EmptyHandCanElectAnAiActorWithoutDefeatingOtherParticipantsOrWinningDuringSetup()
    {
        var combat = ActivationSequenceReducerTests.State();
        var graph = CardZoneSystemCompiler.Compile(new() { CardZoneSystemId = "hands", Zones = [new()
            { ZoneId = "cards", OwnerScope = CardZoneOwnerScope.Actor, Ordering = CardZoneOrdering.Ordered }] }).Value;
        var seed = CardZoneBootstrapper.Create(graph, new() { RunOwnerId = "$run", ActorIds = ["A", "B", "C", "D"],
            Batches = new[] { "A", "C", "D" }.Select(id => new CardZoneInitialBatch { ZoneId = "cards", OwnerId = id, DefinitionIds = ["card"] }).ToArray() },
            DeterministicContext.Create(7, "pinned")).Value;
        var run = new RunState { PlayerEntityId = "A", Deck = new() { Topology = seed.State }, Determinism = seed.Context };
        var policy = new OutcomePolicyDefinition { EvaluationBoundary = OutcomeEvaluationBoundary.AfterCurrentAction, TieBreak = OutcomeTieBreak.Draw,
            ParticipantVictory = new() { RuleId = "empty", Condition = "empty", TieBreak = ParticipantVictoryTieBreak.ActiveActorThenOrdinal,
                Inputs = [new() { Variable = "cards", Kind = GameplayQueryKind.ZoneCount, Zones = [new() { ZoneId = "cards", OwnerScope = CardZoneOwnerScope.Actor }] }] } };
        var formulas = new Mock<IRuntimeFormulaEvaluator>();
        formulas.Setup(value => value.Evaluate("empty", It.IsAny<Dictionary<string, float>?>(), It.IsAny<float>()))
            .Returns((string _, Dictionary<string, float>? variables, float _) => Result<float>.Success(variables!["cards"] == 0 ? 1 : 0));
        var resolver = new CombatOutcomeResolver(formulas.Object);
        Assert.Null(resolver.EvaluateWithContext(run, combat, policy, "B", CombatOutcomeEvaluationPoint.LifecycleBoundary).Value.ParticipantVictory);
        var before = CanonicalJson.ComputeHash(combat);
        var won = resolver.EvaluateWithContext(run, combat, policy, "B", CombatOutcomeEvaluationPoint.ActionResolution).Value;
        Assert.Equal(new[] { "B" }, won.ParticipantVictory!.ActorIds.ToArray());
        Assert.Equal(Core.Combat.Models.CombatStatus.DEFEAT, won.Status);
        Assert.All(won.GetAllActors(), actor => Assert.True(actor.IsAlive));
        Assert.Equal(before, CanonicalJson.ComputeHash(combat));
    }
}

using System.Collections.Immutable;
using Core.Combat;
using Core.Combat.Flow;
using Core.Combat.Models;
using Core.Common;
using Core.Determinism;
using Core.Effects;
using Core.GameplayEvents;
using Core.Run;
using Core.Tests.Effects;
using Moq;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class GameplayRootResponseTests
{
    [Fact]
    public void NormalRootAndObservedPlanningCarryOneQuotaAcrossPlayerAndAutomaticActions()
    {
        var (run, combat) = GameplayResponseContinuationTests.Fixture("effect.application.resolved");
        var ally = combat.GetActor("ally")!;
        var component = (Core.GameplayEvents.Responses.EntityResponseComponentState)ally.Components["watch"];
        combat = combat.ReplaceActor(ally with { Components = ally.Components.ToImmutableDictionary(StringComparer.Ordinal).SetItem("watch",
            component with { Responses = [component.Responses!.Value[0] with { Effects = [new() {
                Type = EffectType.REMOVE_STATUS, Target = EffectTarget.SELF, StatusId = "absent-response" }] }] }) });
        run = run with { ResolvedMode = new(), ActiveEncounterId = combat.CombatId, Encounters = [new() { NodeId = "node", Combat = combat }] };
        var manager = new Mock<IRunEncounterRuntime>();
        manager.Setup(item => item.GetRun(run.RunId)).Returns(Result<RunState>.Success(run));
        var commits = new List<CombatResolutionCommit>();
        manager.Setup(item => item.CommitCombatResolution(It.IsAny<CombatResolutionCommit>()))
            .Returns((CombatResolutionCommit commit) => { commits.Add(commit); return Result<RunState>.Success(run); });
        var effects = EffectTransactionTests.Executor(); var scopes = new List<GameplayEventScope>();
        var handler = new Mock<ICombatCommandHandler>();
        handler.Setup(item => item.HandleObserved(It.IsAny<CombatCommandHandlingRequest>(), It.IsAny<GameplayEffectObservation>()))
            .Returns((CombatCommandHandlingRequest input, GameplayEffectObservation observation) =>
            {
                var effect = effects.ExecuteObserved(EffectTransactionTests.Request(new EffectDefinition { Type = EffectType.REMOVE_STATUS, StatusId = "absent" })
                    with { Run = input.Run, Combat = input.Combat }, observation);
                Assert.True(effect.IsSuccess, effect.IsFailure ? effect.Error : null);
                scopes.Add(effect.Value.Observation.Events.Scope);
                return Result<ObservedGameplayExecution<CombatCommandHandlingResult>>.Success(new(new() {
                    NextRun = effect.Value.Execution.Run!, Step = new() { Combat = effect.Value.Execution.State, Deck = input.Run.Deck,
                        Payload = System.Text.Json.JsonSerializer.SerializeToElement(new { }),
                        RunSnapshot = effect.Value.Execution.Run, RunDeterminism = input.Run.Determinism, EffectSteps = effect.Value.Execution.Steps },
                    Candidate = new() { CandidateId = "chosen", Command = input.Command, ResolvedCommand = input.Command } }, effect.Value.Observation));
            });
        var automatic = new Mock<IAutomaticFlowDriver>();
        automatic.Setup(item => item.DriveObserved(It.IsAny<AutomaticFlowRequest>(), It.IsAny<GameplayEffectObservation>()))
            .Returns((AutomaticFlowRequest input, GameplayEffectObservation observation) =>
            {
                Assert.Equal(1, observation.ResponseProgress!.Quotas.Count);
                var effect = effects.ExecuteObserved(EffectTransactionTests.Request(new EffectDefinition { Type = EffectType.REMOVE_STATUS, StatusId = "absent" })
                    with { Run = input.Run, Combat = input.Combat }, observation);
                Assert.True(effect.IsSuccess, effect.IsFailure ? effect.Error : null);
                Assert.Equal(1, effect.Value.Observation.ResponseProgress!.Quotas.Count);
                Assert.Single(effect.Value.Execution.Steps);
                return Result<ObservedGameplayExecution<AutomaticFlowResult>>.Success(new(new() { Run = effect.Value.Execution.Run!,
                    Combat = effect.Value.Execution.State }, effect.Value.Observation));
            });
        var coordinator = new CombatRunCoordinator(Mock.Of<ICombatFactory>(), manager.Object, Mock.Of<ICombatFlowPlanner>(),
            handler.Object, automatic.Object, occurrences: effects);
        var command = new CombatActionCommand { RunId = run.RunId, ActorId = "hero", ActionType = ActionType.PASS };
        var identity = new RunCommandIdentity(Guid.Parse("cdb724c8-2213-4714-bbc3-ecf1e2ecf280"), GameplayCommandTypes.ExecuteAction,
            run.Sequence, combat.Determinism.Step);
        var hash = CanonicalJson.ComputeHash(run);
        var expected = coordinator.PlanObservedAction(run.RunId, combat.CombatId, command, identity);
        Assert.True(expected.IsSuccess, expected.IsFailure ? expected.Error : null);
        Assert.Empty(commits);
        for (var i = 0; i < 5; i++)
        {
            var actual = coordinator.ExecuteAction(combat.CombatId, command, identity);
            Assert.True(actual.IsSuccess, actual.IsFailure ? actual.Error : null);
            Assert.Equal(CanonicalJson.ComputeHash(expected.Value.Execution), CanonicalJson.ComputeHash(commits[^1]));
        }
        Assert.All(scopes, scope => Assert.Equal(identity.CommandId.ToString("D"), scope.CommandId));
        Assert.Equal(hash, CanonicalJson.ComputeHash(run));
        handler.Verify(item => item.Handle(It.IsAny<CombatCommandHandlingRequest>()), Times.Never);
        automatic.Verify(item => item.Drive(It.IsAny<AutomaticFlowRequest>()), Times.Never);
    }
}

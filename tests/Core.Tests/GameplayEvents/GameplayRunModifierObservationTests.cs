using Core.Combat.Modifiers;
using Core.Combat.Models;
using Core.Determinism;
using Core.GameplayEvents;
using Core.Run;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class GameplayRunModifierObservationTests
{
    [Theory]
    [InlineData(4096)]
    [InlineData(1)]
    public void NodeThenRunTicksPreserveCanonicalOrderAndInput(int budget)
    {
        var run = new RunState { Determinism = DeterministicContext.Create(42, "revision") };
        foreach (var boundary in new[] { ModifierDurationBoundary.Node, ModifierDurationBoundary.Run })
        {
            var applied = ModifierTransitions.Apply(run, new() { ModifierId = boundary.ToString(), DefaultDuration = 1,
                DurationBoundary = boundary }, new() { Kind = GameplayOwnerKind.Global }, "test");
            Assert.True(applied.IsSuccess, applied.IsFailure ? applied.Error : null);
            run = applied.Value.Run;
        }
        var input = CanonicalJson.ComputeHash(run);
        var node = new RunMapNodeState { NodeId = "last" };
        var progression = new RunProgressionService(RunActivityRegistry.CreateDefault());
        var initial = GameplayObservedExecutorTests.Observation(budget);
        var cursor = new GameplayRunObservation(initial);
        var actual = progression.ApplyNodeExitObserved(run, node, cursor);
        if (budget == 1) Assert.True(actual.IsFailure);
        else
        {
            Assert.True(actual.IsSuccess, actual.IsFailure ? actual.Error : null);
            Assert.Equal(CanonicalJson.ComputeHash(progression.ApplyNodeExit(run, node)), CanonicalJson.ComputeHash(actual.Value));
            Assert.Equal(new[] { "Node", "Run" }, cursor.Current.Events.Occurrences.Select(item =>
                Assert.IsType<ModifierChangePayload>(item.Payload).DefinitionId));
            Assert.Equal(RunLifecycleState.Completed, actual.Value.Lifecycle);
        }
        Assert.Empty(initial.Events.Occurrences);
        Assert.Equal(input, CanonicalJson.ComputeHash(run));
    }

    [Fact]
    public void PreparationRecordsAppliedModifierWithItsResolvedOwnerAndAllocatedIdentity()
    {
        var id = Guid.Parse("30000000-0000-0000-0000-000000000001");
        var run = new RunState { RunId = id, PlayerEntityId = "hero", Determinism = DeterministicContext.Create(42, "revision"),
            ResourceState = new() { OwnerId = "wallet" }, Preparations = [new() { PreparationInstanceId = id,
                Options = [new() { OptionId = "train", ApplyModifiers = [new() { ModifierId = "focus", OwnerId = "hero", Duration = 2 }] }] }] };
        var planned = PreparationTransitions.PlanApply(run, id, "train");
        Assert.True(planned.IsSuccess, planned.IsFailure ? planned.Error : null);
        var grant = Assert.Single(planned.Value.ModifierGrants);
        var modifier = new ScriptModifierInstance { InstanceId = grant.InstanceId, ModifierId = grant.ModifierId,
            OwnerId = grant.OwnerId, SourceId = grant.SourceId, Duration = grant.Duration!.Value,
            Definition = new() { ModifierId = "focus" } };
        var initial = GameplayObservedExecutorTests.Observation();
        var cursor = new GameplayRunObservation(initial);
        var actual = PreparationTransitions.CommitApplyObserved(run, planned.Value, [modifier], null, cursor);
        Assert.True(actual.IsSuccess, actual.IsFailure ? actual.Error : null);
        var expected = PreparationTransitions.CommitApply(run, planned.Value, [modifier]);
        Assert.Equal(CanonicalJson.ComputeHash(expected.Value), CanonicalJson.ComputeHash(actual.Value));
        var fact = Assert.Single(cursor.Current.Events.Occurrences);
        var payload = Assert.IsType<ModifierChangePayload>(fact.Payload);
        Assert.Equal("hero", payload.Owner.Id);
        Assert.Equal(grant.InstanceId.ToString("D"), payload.InstanceId);
        Assert.Equal(2, payload.AfterDuration);
        Assert.Empty(initial.Events.Occurrences);
        Assert.Empty(run.Modifiers);
    }
}

using Core.Combat.Flow;
using Core.Combat.Models;
using Core.Determinism;
using Core.Effects;
using Core.GameplayEvents;
using Core.Resources;
using Core.Run;
using Core.Tests.Effects;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class GameplayOtherLifecycleObservationTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(-99)]
    public void RegenerationPreservesCanonicalSignedAmountsExclusionsAndActorSelection(float amount)
    {
        var run = new RunState { PlayerEntityId = "hero", Determinism = DeterministicContext.Create(42, "revision") };
        var combat = EffectTransactionTests.Request().Combat;
        var actor = combat.GetActor("hero")!;
        var pool = actor.GetResource("focus")!;
        pool = pool with { Definition = pool.Definition! with { Regeneration = new()
            { Enabled = true, AmountPerTurn = amount, Timing = RegenerationTiming.START_TURN } } };
        combat = combat.ReplaceActor(actor with { ResourceState = actor.ResourceState with
            { Resources = new Dictionary<string, ResourcePool>(actor.ResourceState.Resources) { ["focus"] = pool } } });
        var before = CanonicalJson.ComputeHash(combat);
        var lifecycle = new CombatResourceLifecycle(EffectTransactionTests.Executor());
        var observation = GameplayObservedExecutorTests.Observation();
        var actual = lifecycle.ProcessObserved(run, combat, "hero", RegenerationTiming.START_TURN, observation);
        Assert.True(actual.IsSuccess, actual.IsFailure ? actual.Error : null);
        Assert.Equal(CanonicalJson.ComputeHash(lifecycle.Process(run, combat, "hero", RegenerationTiming.START_TURN).Value),
            CanonicalJson.ComputeHash(actual.Value.Execution));
        var fact = Assert.Single(actual.Value.Observation.Events.Occurrences);
        Assert.Equal("hero", fact.SubjectActorId);
        var payload = Assert.IsType<ResourceChangePayload>(fact.Payload);
        Assert.Equal(amount < 0 ? ResourceMutationOperation.Subtract : ResourceMutationOperation.Add, payload.Operation);
        Assert.Equal(MathF.Abs(amount), payload.Operand);
        Assert.Equal(10, actual.Value.Execution.Combat.GetActor("enemy")!.GetResource("focus")!.Current);
        var excluded = lifecycle.ProcessObserved(run, combat, "hero", RegenerationTiming.START_TURN, observation, new HashSet<string> { "FOCUS" });
        Assert.True(excluded.IsSuccess, excluded.IsFailure ? excluded.Error : null);
        Assert.Empty(excluded.Value.Observation.Events.Occurrences);
        Assert.Equal(before, CanonicalJson.ComputeHash(excluded.Value.Execution.Combat));
        Assert.Empty(observation.Events.Occurrences);
        Assert.Equal(before, CanonicalJson.ComputeHash(combat));
    }

    [Fact]
    public void RelicStartRemainsIdempotentAndUsesCapturedOwnerAndTriggerOrder()
    {
        var fixture = EffectSequenceBudgetTests.Fixture();
        var gain = EffectSequenceBudgetTests.Damage(2, 1) with { Type = EffectType.MODIFY_RESOURCE,
            Target = EffectTarget.SELF, Operation = ResourceEffectOperation.ADD };
        var run = RelicTransitions.Acquire(fixture.Run, new() { RelicId = "battery", Triggers =
            [new() { TriggerId = "gain", Boundary = CombatTriggerBoundaries.CombatStart, Effects = [gain] }] },
            new() { Kind = GameplayOwnerKind.Entity, Id = "hero" }).Value.State;
        var combat = EffectTransactionTests.Request().Combat;
        var lifecycle = new CombatRelicLifecycle(fixture.Executor);
        var observation = GameplayObservedExecutorTests.ObservationFor(run, combat);
        var actual = lifecycle.ProcessObserved(run, combat, CombatTriggerBoundaries.CombatStart, observation);
        Assert.True(actual.IsSuccess, actual.IsFailure ? actual.Error : null);
        Assert.Equal(CanonicalJson.ComputeHash(lifecycle.Process(run, combat, CombatTriggerBoundaries.CombatStart).Value),
            CanonicalJson.ComputeHash(actual.Value.Execution));
        var fact = Assert.Single(actual.Value.Observation.Events.Occurrences, item => item.Payload is ResourceChangePayload);
        Assert.Single(actual.Value.Observation.Events.Occurrences, item => item.Kind == "combat.started");
        Assert.Equal("hero", fact.SubjectActorId);
        Assert.Equal("relic", fact.Origin!.ContainerKind);
        var repeated = lifecycle.ProcessObserved(actual.Value.Execution.Run!, actual.Value.Execution.Combat,
            CombatTriggerBoundaries.CombatStart, actual.Value.Observation);
        Assert.True(repeated.IsSuccess, repeated.IsFailure ? repeated.Error : null);
        Assert.Same(actual.Value.Observation, repeated.Value.Observation);
        Assert.Empty(repeated.Value.Execution.Events);
        Assert.Equal(CanonicalJson.ComputeHash(actual.Value.Execution.Combat), CanonicalJson.ComputeHash(repeated.Value.Execution.Combat));
        Assert.Empty(observation.Events.Occurrences);
    }

    [Fact]
    public void ActionRevisionAndOriginDefinitionRevisionRemainDistinctAfterContentChanges()
    {
        var request = EffectTransactionTests.Request() with { ContentRevision = "old-definition", PrefixCommands = [new()
        {
            EffectInstanceId = "captured-prefix", SourceEntityId = "hero", TargetEntityIds = ["enemy"], ResolvedValue = 1,
            ContentRevision = "old-definition", Definition = new() { Type = EffectType.MODIFY_RESOURCE, TargetResource = "focus",
                Operation = ResourceEffectOperation.SUBTRACT }
        }] };
        var executor = EffectTransactionTests.Executor();
        var actual = executor.ExecuteObserved(request, GameplayObservedExecutorTests.Observation());
        Assert.True(actual.IsSuccess, actual.IsFailure ? actual.Error : null);
        Assert.Equal(CanonicalJson.ComputeHash(executor.Execute(request).Value), CanonicalJson.ComputeHash(actual.Value.Execution));
        var fact = Assert.Single(actual.Value.Observation.Events.Occurrences);
        Assert.Equal("revision", fact.Scope.ContentRevision);
        Assert.Equal("old-definition", fact.Origin!.ContentRevision);
    }
}

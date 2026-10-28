using System.Collections.Immutable;
using Core.Combat.Flow;
using Core.Combat.Models;
using Core.Determinism;
using Core.Effects;
using Core.GameplayEvents;
using Core.StatusEffects;
using Core.Tests.Effects;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class GameplayStatusObservationTests
{
    private static StatusEffectInstance Status(string id, string target, int duration, int priority) => new()
    {
        InstanceId = DeterministicId.Create(42, (ulong)priority, id), StatusId = id, TargetId = target,
        SourceId = "hero", ContentRevision = "revision", Stacks = 2, Duration = duration,
        Definition = new() { StatusId = id, Priority = priority, DurationTickBoundary = StatusTriggerBoundary.EndActivation,
            Triggers = [new() { TriggerId = "pulse", Boundary = "EndActivation", Effects = [
                EffectSequenceBudgetTests.Damage(1, 1) with { Target = EffectTarget.SELF }] }] }
    };

    [Fact]
    public void ShadowLifecyclePreservesFrozenPriorityActorEligibilityAndCanonicalStateThroughExpiry()
    {
        var fixture = EffectSequenceBudgetTests.Fixture();
        var lifecycle = new CombatStatusLifecycle(fixture.Executor);
        var combat = EffectTransactionTests.Request().Combat with { StatusEffects = new Dictionary<string, ImmutableArray<StatusEffectInstance>>
        {
            ["hero"] = [Status("unrelated", "hero", 1, 1)],
            ["enemy"] = [Status("low", "enemy", 2, 2), Status("high", "enemy", 1, 3)]
        }.ToImmutableDictionary(StringComparer.Ordinal) };
        var originalHash = CanonicalJson.ComputeHash(combat);
        var observation = GameplayObservedExecutorTests.ObservationFor(fixture.Run, combat);
        var expected = lifecycle.Process(fixture.Run, combat, StatusTriggerBoundary.EndActivation, "enemy").Value;
        var fingerprints = new HashSet<string>(StringComparer.Ordinal);
        for (var repeat = 0; repeat < 5; repeat++)
        {
            var actual = lifecycle.ProcessObserved(fixture.Run, combat, StatusTriggerBoundary.EndActivation, observation, "enemy");
            Assert.True(actual.IsSuccess, actual.IsFailure ? actual.Error : null);
            Assert.Equal(CanonicalJson.ComputeHash(expected), CanonicalJson.ComputeHash(actual.Value.Execution));
            var facts = actual.Value.Observation.Events.Occurrences;
            Assert.Equal(new[] { combat.StatusEffects["enemy"][1].InstanceId.ToString(), combat.StatusEffects["enemy"][0].InstanceId.ToString() },
                facts.Where(item => item.Payload is ResourceChangePayload).Select(item => item.Origin!.ContainerId));
            Assert.Single(facts, item => item.Kind == "status.expired");
            Assert.Single(facts, item => item.Kind == "status.duration.changed");
            Assert.All(facts.Where(item => item.Payload is StatusChangePayload), item =>
            { Assert.Null(item.SourceActorId); Assert.Null(item.AttributedActorId); Assert.Equal("enemy", item.SubjectActorId); });
            Assert.Equal(1, actual.Value.Execution.Combat.StatusEffects["hero"][0].Duration);
            Assert.Empty(actual.Value.Observation.Unobserved);
            fingerprints.Add(CanonicalJson.ComputeHash(facts));
        }
        Assert.Single(fingerprints);
        Assert.Equal(originalHash, CanonicalJson.ComputeHash(combat));
        Assert.Empty(observation.Events.Occurrences);
        var next = lifecycle.ProcessObserved(expected.Run!, expected.Combat, StatusTriggerBoundary.EndActivation, observation, "enemy");
        Assert.True(next.IsSuccess, next.IsFailure ? next.Error : null);
        Assert.Single(next.Value.Observation.Events.Occurrences, item => item.Kind == "status.expired");
        Assert.False(next.Value.Execution.Combat.StatusEffects.ContainsKey("enemy"));
    }

    [Fact]
    public void ObservationFailureAfterTickEffectsDoesNotExposeAnExpiredStateOrConsumeAnOrdinal()
    {
        var fixture = EffectSequenceBudgetTests.Fixture();
        var lifecycle = new CombatStatusLifecycle(fixture.Executor);
        var combat = EffectTransactionTests.Request().Combat with { StatusEffects =
            ImmutableDictionary<string, ImmutableArray<StatusEffectInstance>>.Empty.Add("enemy", [Status("one", "enemy", 1, 1)]) };
        var hash = CanonicalJson.ComputeHash(combat);
        var observation = GameplayObservedExecutorTests.ObservationFor(fixture.Run, combat, 1);
        var failed = lifecycle.ProcessObserved(fixture.Run, combat, StatusTriggerBoundary.EndActivation, observation, "enemy");
        Assert.True(failed.IsFailure);
        Assert.Equal(hash, CanonicalJson.ComputeHash(combat));
        Assert.Empty(observation.Events.Occurrences);
        Assert.True(lifecycle.Process(fixture.Run, combat, StatusTriggerBoundary.EndActivation, "enemy").IsSuccess);
    }
}

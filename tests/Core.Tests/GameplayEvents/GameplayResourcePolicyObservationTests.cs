using Core.Combat.Models;
using Core.Determinism;
using Core.Effects;
using Core.GameplayEvents;
using Core.Resources;
using Core.Run;
using Core.Tests.Effects;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class GameplayResourcePolicyObservationTests
{
    [Fact]
    public void AcquisitionAndRemovalKeepPersistentAndEncounterScopesDistinctAndIgnoreHistory()
    {
        var combat = GameplayOwnershipTests.State();
        var hero = combat.GetActor("hero")!;
        var run = new RunState { PlayerEntityId = "hero", Determinism = DeterministicContext.Create(42, "revision"),
            PlayerEntity = new() { InstanceId = "hero", Components = hero.Components },
            Encounters = [new() { Combat = combat }, new() { Combat = combat with
                { CombatId = Guid.Parse("70000000-0000-0000-0000-000000000001") }, Resolved = true }] };
        var before = CanonicalJson.ComputeHash(run);
        var acquired = RelicTransitions.Acquire(run, new() { RelicId = "restriction", ResourceRestrictions = [new()
            { RuleId = "limit", ResourceId = "focus", CurrentCeiling = 0, ExistingBalance = ResourceRestrictedBalancePolicy.DiscardExcess }] },
            new() { Kind = GameplayOwnerKind.Entity, Id = "hero" });
        Assert.True(acquired.IsSuccess, acquired.IsFailure ? acquired.Error : null);
        var initial = GameplayObservedExecutorTests.Observation();
        var observed = GameplayResourcePolicyObservation.Append(initial, run, acquired.Value.State);
        Assert.True(observed.IsSuccess, observed.IsFailure ? observed.Error : null);
        Assert.Equal(4, observed.Value.Events.Occurrences.Count);
        var policies = observed.Value.Events.Occurrences.Select(item => item.Payload).OfType<ResourceRestrictionPayload>().ToArray();
        Assert.Equal(2, policies.Length);
        Assert.Equal(initial.Events.Scope.BranchId, policies[0].Owner.ScopeId);
        Assert.Equal(combat.CombatId.ToString("D"), policies[1].Owner.ScopeId);
        Assert.Equal(CanonicalJson.ComputeHash(run.Encounters[1]), CanonicalJson.ComputeHash(acquired.Value.State.Encounters[1]));
        var unchanged = GameplayResourcePolicyObservation.Append(observed.Value, acquired.Value.State, RelicResourceRules.Materialize(acquired.Value.State).Value);
        Assert.Same(observed.Value.Events, unchanged.Value.Events);
        var removed = RelicTransitions.Remove(acquired.Value.State, acquired.Value.Relic.RelicInstanceId).Value;
        var removalFacts = GameplayResourcePolicyObservation.Append(initial, acquired.Value.State, removed);
        Assert.True(removalFacts.IsSuccess, removalFacts.IsFailure ? removalFacts.Error : null);
        Assert.All(removalFacts.Value.Events.Occurrences.Select(item => item.Payload).OfType<ResourceRestrictionPayload>(),
            item => Assert.Null(item.AfterCeiling));
        Assert.Equal(before, CanonicalJson.ComputeHash(run));
        Assert.Empty(initial.Events.Occurrences);
    }

    [Theory]
    [InlineData(4096)]
    [InlineData(1)]
    public void MaterializedRestrictionRecordsTheRealSetBeforeTheEffectAndRollsBackOnFailure(int budget)
    {
        var run = new RunState { PlayerEntityId = "hero", Determinism = DeterministicContext.Create(42, "revision") };
        run = RelicTransitions.Acquire(run, new() { RelicId = "ceiling", ResourceRestrictions = [new()
        { RuleId = "limit", ResourceId = "focus", CurrentCeiling = 0, ExistingBalance = ResourceRestrictedBalancePolicy.DiscardExcess }] },
            new() { Kind = GameplayOwnerKind.Entity, Id = "hero" }).Value.State;
        var request = EffectTransactionTests.Request(new EffectDefinition { EffectId = "gain", Type = EffectType.HEAL,
            Target = EffectTarget.SELF, TargetResource = "focus", FlatValue = 5 }) with { Run = run };
        var initial = GameplayObservedExecutorTests.Observation(budget);
        var before = CanonicalJson.ComputeHash(request);
        var executor = EffectTransactionTests.Executor();
        var observed = executor.ExecuteObserved(request, initial);
        if (budget == 1) Assert.True(observed.IsFailure);
        else
        {
            Assert.True(observed.IsSuccess, observed.IsFailure ? observed.Error : null);
            Assert.Equal(CanonicalJson.ComputeHash(executor.Execute(request).Value), CanonicalJson.ComputeHash(observed.Value.Execution));
            var facts = observed.Value.Observation.Events.Occurrences;
            Assert.Equal(new[] { "resource.restriction.resolved", "resource.change.resolved", "resource.change.resolved" }, facts.Select(item => item.Kind));
            var restriction = Assert.IsType<ResourceRestrictionPayload>(facts[0].Payload);
            Assert.Null(restriction.BeforeCeiling); Assert.Equal(0, restriction.AfterCeiling);
            var balance = Assert.IsType<ResourceChangePayload>(facts[1].Payload);
            Assert.Equal(ResourceMutationOperation.Set, balance.Operation);
            Assert.Equal(10, balance.Before); Assert.Equal(10, balance.Operand); Assert.Equal(0, balance.After);
            Assert.Equal(facts[0].EventId, facts[1].ParentEventId);
            Assert.Null(facts[1].SourceActorId);
            Assert.Equal("hero", facts[1].SubjectActorId);
            Assert.Empty(observed.Value.Observation.Unobserved);
        }
        Assert.Empty(initial.Events.Occurrences);
        Assert.Equal(before, CanonicalJson.ComputeHash(request));
    }
}

using Core.Combat.Models;
using Core.Determinism;
using Core.GameplayEvents;
using Core.Run;
using Core.Tests.Run;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class GameplayResourceTransportObservationTests
{
    private static GameplayEffectObservation Context(int budget = 4096) => new()
    {
        PublisherId = GameplayCoreCatalog.PublisherId, ReasonId = "effect.application",
        Events = GameplayEventTransaction.Create(new() { SettingId = "setting", ContentRevision = "resources",
            RunId = "run", BranchId = "branch", ActionId = "action", CommandId = "command", CombatId = "combat" },
            GameplayCoreCatalog.Create("setting", "resources").Value, new() { MaximumOccurrences = budget }).Value
    };

    [Theory]
    [InlineData(ActorResourceLifecycleAction.PreserveCurrent)]
    [InlineData(ActorResourceLifecycleAction.ResetToMaximum)]
    [InlineData(ActorResourceLifecycleAction.ResetToConfiguredValue)]
    [InlineData(ActorResourceLifecycleAction.EncounterOnly)]
    public void EnterUsesExactChosenPolicyAndDoesNotInventDamageOrHealing(ActorResourceLifecycleAction action)
    {
        var player = ActorResourceLifecycleTests.Player();
        var actor = ActorResourceLifecycleTests.Actor(9);
        var policy = ActorResourceLifecycleTests.Policy(action);
        var initial = Context();
        var cursor = new GameplayRunObservation(initial);
        var before = CanonicalJson.ComputeHash(new { player, actor });
        var actual = ActorResourceLifecycleTransitions.EnterCore(player, actor, policy, cursor);
        Assert.True(actual.IsSuccess, actual.IsFailure ? actual.Error : null);
        Assert.Equal(CanonicalJson.ComputeHash(ActorResourceLifecycleTransitions.Enter(player, actor, policy).Value), CanonicalJson.ComputeHash(actual.Value));
        if (action == ActorResourceLifecycleAction.EncounterOnly) Assert.Empty(cursor.Current.Events.Occurrences);
        else
        {
            var fact = Assert.Single(cursor.Current.Events.Occurrences);
            Assert.Equal("resource.transport.resolved", fact.Kind);
            Assert.Null(fact.SourceActorId);
            Assert.Equal("hero", fact.SubjectActorId);
            var payload = Assert.IsType<ResourceTransportPayload>(fact.Payload);
            Assert.True(payload.EnteringEncounter);
            Assert.Equal(action, payload.Policy);
            Assert.Equal(9, payload.Before);
            Assert.Equal(actual.Value.ResourceState.Current("resolve"), payload.After);
            Assert.Equal(60, payload.AfterMaximum);
        }
        Assert.Empty(initial.Events.Occurrences);
        Assert.Equal(before, CanonicalJson.ComputeHash(new { player, actor }));
    }

    [Fact]
    public void ExitRecordsRequestedCopyAndPreservesPersistentBoundsAndRollback()
    {
        var player = ActorResourceLifecycleTests.Player();
        var actor = ActorResourceLifecycleTests.Actor(9);
        actor = actor.WithResourceState(actor.ResourceState with { Resources = new Dictionary<string, Core.Resources.ResourcePool>
            { ["resolve"] = actor.GetResource("resolve")! with { Current = 88, Maximum = 120 } } });
        var initial = Context();
        var cursor = new GameplayRunObservation(initial);
        var actual = ActorResourceLifecycleTransitions.ExitCore(player, actor, ActorResourceLifecycleTests.Policy(), CombatStatus.VICTORY, false, cursor);
        Assert.True(actual.IsSuccess, actual.IsFailure ? actual.Error : null);
        var payload = Assert.IsType<ResourceTransportPayload>(Assert.Single(cursor.Current.Events.Occurrences).Payload);
        Assert.False(payload.EnteringEncounter);
        Assert.Equal(88, payload.RequestedCurrent);
        Assert.Equal(60, payload.After);
        Assert.Equal(60, payload.AfterMaximum);
        Assert.Equal(CanonicalJson.ComputeHash(ActorResourceLifecycleTransitions.Exit(player, actor,
            ActorResourceLifecycleTests.Policy(), CombatStatus.VICTORY).Value), CanonicalJson.ComputeHash(actual.Value));
        var prefix = GameplayActionObservation.Begin(Context(1), "test", "hero").Value;
        var rejected = ActorResourceLifecycleTransitions.ExitCore(player, actor, ActorResourceLifecycleTests.Policy(), CombatStatus.VICTORY,
            false, new GameplayRunObservation(prefix));
        Assert.True(rejected.IsFailure);
        Assert.Single(prefix.Events.Occurrences);
        Assert.Empty(initial.Events.Occurrences);
    }
}

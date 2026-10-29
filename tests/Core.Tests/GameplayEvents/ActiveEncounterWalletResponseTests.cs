using Core.Combat.Models;
using Core.Determinism;
using Core.GameplayEvents;
using Core.Run;
using Core.Tests.Effects;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class ActiveEncounterWalletResponseTests
{
    [Fact]
    public void WalletFactDuringEncounterUsesTheActualRosterWithoutReplacingActorResources()
    {
        var (run, combat) = GameplayResponseContinuationTests.Fixture("resource.change.resolved");
        run = run with { ResourceState = combat.GetActor("hero")!.ResourceState with { OwnerId = "wallet" },
            ActiveEncounterId = combat.CombatId, Encounters = [new() { NodeId = "node", Combat = combat }] };
        var original = CanonicalJson.ComputeHash(run);
        var initial = GameplayCommandObservation.Create(run, combat.CombatId, new(Guid.Parse("975673b2-f755-41a9-a64b-aa78408ff5cb"), "TEST", 0, 0)).Value;
        for (var i = 0; i < 5; i++)
        {
            var cursor = new GameplayRunObservation(initial);
            cursor.Bind(new RunGameplayOccurrenceExecutor(EffectTransactionTests.Executor()));
            var result = RunResourceTransitions.Pay(run, [new() { ResourceId = "focus", Amount = 3 }], "payment", cursor);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
            Assert.Equal(7, result.Value.Run.ResourceState.Get("focus")!.Current);
            Assert.Equal(12, result.Value.Run.GetActiveEncounter()!.Combat.GetActor("ally")!.GetResource("focus")!.Current);
            Assert.Equal(10, result.Value.Run.GetActiveEncounter()!.Combat.GetActor("hero")!.GetResource("focus")!.Current);
            Assert.Null(result.Value.Run.PlayerEntity);
            Assert.Null(cursor.Current.Events.Occurrences[0].SubjectActorId);
            Assert.Equal("ally", cursor.Current.Events.Occurrences[1].SubjectActorId);
        }
        Assert.Equal(original, CanonicalJson.ComputeHash(run)); Assert.Empty(initial.Events.Occurrences);
    }
}

using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Determinism;
using Core.Effects;
using Core.GameplayEvents.Responses;
using Core.Tests.Effects;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class ResponsePayloadTests
{
    [Fact]
    public void GenericResponseReceivesTheSameCapturedPayloadAsTheUniversalExecutor()
    {
        var fixture = StackPayloadTests.Fixture();
        var applied = fixture.Executor.Execute(EffectTransactionTests.Request(StackPayloadTests.Apply(2)) with { Run = fixture.Run });
        Assert.True(applied.IsSuccess, applied.IsFailure ? applied.Error : null);
        var status = Assert.Single(applied.Value.State.StatusEffects["enemy"]);
        var activate = StackPayloadTests.Activate() with { Target = EffectTarget.SELF };
        var response = GameplayResponseCompilationTests.Definition() with { Observe = new() { Kinds = new() { Include = ["effect.application.resolved"] } },
            Bindings = new() { Source = new() { Kind = ResponseActorBindingKind.ObserverSource },
                Target = new() { Kind = ResponseActorBindingKind.Owner } }, Effects = [activate] };
        var combat = applied.Value.State with { StatusEffects = applied.Value.State.StatusEffects.SetItem("enemy", [status with {
            Definition = status.Definition with { Responses = [response] } }]) };
        var run = applied.Value.Run! with { Deck = new() };
        var expected = fixture.Executor.Execute(new() { Run = run, Combat = applied.Value.State,
            ContentRevision = "revision", OwnerEntityId = "enemy", SourceEntityId = "hero", StackPayloadLots = status.PayloadLots,
            Trigger = new() { TriggerId = "payload", Effects = [activate] } });
        Assert.True(expected.IsSuccess, expected.IsFailure ? expected.Error : null);
        var input = CanonicalJson.ComputeHash(new { run, combat }); var hashes = new HashSet<string>();
        for (var i = 0; i < 5; i++)
        {
            var result = fixture.Executor.ExecuteObserved(EffectTransactionTests.Request(new EffectDefinition {
                Type = EffectType.REMOVE_STATUS, StatusId = "absent" }) with { Run = run, Combat = combat },
                GameplayObservedExecutorTests.ObservationFor(run, combat));
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
            Assert.Equal(expected.Value.State.GetActor("enemy")!.GetResource("focus")!.Current,
                result.Value.Execution.State.GetActor("enemy")!.GetResource("focus")!.Current);
            Assert.Equal(expected.Value.Calculations.Select(item => item.Value), result.Value.Execution.Calculations.Select(item => item.Value));
            hashes.Add(CanonicalJson.ComputeHash(result.Value));
        }
        Assert.Single(hashes); Assert.Equal(input, CanonicalJson.ComputeHash(new { run, combat }));
    }
}

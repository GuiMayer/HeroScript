using System.Collections.Immutable;
using Core.Combat;
using Core.Combat.Models;
using Core.Determinism;
using Core.Effects;
using Core.GameplayEvents;
using Core.GameplayEvents.Responses;
using Core.Run;
using Core.Tests.Effects;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class GameplayResponseContinuationTests
{
    [Fact]
    public void RootQuotaSurvivesSeparateExecutorCallsWithoutContaminatingSpeculativeForks()
    {
        var (run, combat) = Fixture("resource.change.resolved");
        var request = EffectTransactionTests.Request(new EffectDefinition { Type = EffectType.HEAL, TargetResource = "focus", FlatValue = 1 })
            with { Run = run, Combat = combat };
        var before = GameplayObservedExecutorTests.ObservationFor(run, combat);
        var original = CanonicalJson.ComputeHash(request);
        var executor = EffectTransactionTests.Executor();
        var first = executor.ExecuteObserved(request, before);
        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        Assert.Equal(12, first.Value.Execution.State.GetActor("ally")!.GetResource("focus")!.Current);
        var second = executor.ExecuteObserved(request with { Combat = first.Value.Execution.State }, first.Value.Observation);
        Assert.True(second.IsSuccess, second.IsFailure ? second.Error : null);
        Assert.Equal(12, second.Value.Execution.State.GetActor("ally")!.GetResource("focus")!.Current);
        var fork = executor.ExecuteObserved(request, before);
        Assert.Equal(CanonicalJson.ComputeHash(first.Value), CanonicalJson.ComputeHash(fork.Value));
        Assert.Equal(original, CanonicalJson.ComputeHash(request)); Assert.Empty(before.Events.Occurrences);
    }

    [Fact]
    public void AlreadyProducedBoundaryDispatchUsesTheSameMatcherBodyAndCausalParent()
    {
        var (run, combat) = Fixture("round.started");
        var before = GameplayObservedExecutorTests.ObservationFor(run, combat);
        var boundary = GameplayBoundaryObservation.Begin(before, GameplayBoundary.RoundStarted, "round", "hero",
            new() { ContainerKind = "rule", ContainerId = "test", ComponentId = "round", ContentRevision = "revision" }, "round");
        Assert.True(boundary.IsSuccess, boundary.IsFailure ? boundary.Error : null);
        var executor = EffectTransactionTests.Executor(); var input = CanonicalJson.ComputeHash(combat);
        var hashes = new HashSet<string>();
        for (var i = 0; i < 5; i++)
        {
            var result = executor.DispatchOccurrencesObserved(run, combat, run, combat, before, boundary.Value);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
            Assert.Equal(12, result.Value.Execution.State.GetActor("ally")!.GetResource("focus")!.Current);
            Assert.Equal(boundary.Value.ParentEventId, result.Value.Observation.Events.Occurrences[1].ParentEventId);
            Assert.Equal(1, result.Value.Observation.ResponseProgress!.Quotas.Count);
            hashes.Add(CanonicalJson.ComputeHash(result.Value));
        }
        Assert.Single(hashes); Assert.Equal(input, CanonicalJson.ComputeHash(combat));
        Assert.Single(boundary.Value.Events.Occurrences);
        Assert.True(executor.DispatchOccurrencesObserved(run, combat, run, combat, boundary.Value, before).IsFailure);
        Assert.True(executor.DispatchOccurrencesObserved(run, combat, run with { RunId = Guid.NewGuid() }, combat, before, boundary.Value).IsFailure);
    }

    internal static (RunState Run, CombatState Combat) Fixture(string kind)
    {
        var context = GameplayResponseCompilationTests.Context();
        var response = GameplayResponseCompilationTests.Definition() with
        {
            Observe = new() { Kinds = new() { Include = [kind] } },
            Bindings = new() { Target = new() { Kind = ResponseActorBindingKind.Owner } },
            Frequency = new() { Scope = ResponseFrequencyScope.RootAction },
            Effects = [new() { Type = EffectType.HEAL, Target = EffectTarget.SELF, TargetResource = "focus", FlatValue = 2 }]
        };
        var combat = CombatTransitions.Create(context.Combat!.GetAllActors().Select(actor => actor with
        {
            ContentRevision = "revision", Components = actor.InstanceId == "ally" ? actor.Components.ToImmutableDictionary(StringComparer.Ordinal)
                .Add("watch", new EntityResponseComponentState { ComponentId = "watch", Responses = [response] }) : actor.Components
        }), context.Combat.Determinism);
        return (context.Run, combat);
    }
}

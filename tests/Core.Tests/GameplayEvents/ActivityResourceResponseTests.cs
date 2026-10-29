using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Determinism;
using Core.Effects;
using Core.GameplayEvents;
using Core.GameplayEvents.Responses;
using Core.Run;
using Core.Tests.Effects;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class ActivityResourceResponseTests
{
    [Theory]
    [InlineData(RunActivityEffectOwner.RunWallet, 5, 14)]
    [InlineData(RunActivityEffectOwner.PlayerEntity, 10, 9)]
    public void ActivityEffectsAndTheirResponsesKeepTheWalletSeparateFromThePersistentActor(RunActivityEffectOwner binding,
        float walletValue, float actorValue)
    {
        var context = GameplayResponseCompilationTests.Context(); var actor = context.Combat!.GetActor("hero")!;
        var definition = GameplayResponseCompilationTests.Definition() with { Observe = new() { Kinds = new() {
            Include = ["resource.change.resolved"] } }, Bindings = new() { Target = new() { Kind = ResponseActorBindingKind.Owner } },
            Effects = [new() { Type = EffectType.HEAL, Target = EffectTarget.SELF, TargetResource = "focus", FlatValue = 2 }] };
        var run = context.Run with { ResourceState = actor.ResourceState with { OwnerId = "wallet" }, PlayerEntity = new() {
            InstanceId = "hero", ContentRevision = "revision", Components = actor.Components.ToImmutableDictionary(StringComparer.Ordinal)
                .Add("watch", new EntityResponseComponentState { ComponentId = "watch", Responses = [definition] }) } };
        var node = new RunMapNodeState { NodeId = "activity", EntryEffectOwner = binding, EntryEffects = [
            new() { Type = EffectType.MODIFY_RESOURCE, Target = EffectTarget.SELF, TargetResource = "focus", Operation = ResourceEffectOperation.SUBTRACT, FlatValue = 3 },
            new() { Type = EffectType.MODIFY_RESOURCE, Target = EffectTarget.SELF, TargetResource = "focus", Operation = ResourceEffectOperation.SUBTRACT, FlatValue = 2 }] };
        var executor = new RunActivityEffectExecutor(EffectTransactionTests.Executor());
        var input = CanonicalJson.ComputeHash(run); var hashes = new HashSet<string>();
        var initial = GameplayCommandObservation.Create(run, null, new(Guid.Parse("69aee508-76b9-4f3c-8ac2-922cff831559"), "TEST", 0, 0)).Value;
        for (var i = 0; i < 5; i++)
        {
            var normal = executor.Execute(run, node, RunActivityBoundary.Entry);
            Assert.True(normal.IsSuccess, normal.IsFailure ? normal.Error : null);
            Assert.Equal(walletValue, normal.Value.State.ResourceState.Get("focus")!.Current);
            Assert.Equal(actorValue, normal.Value.State.PlayerEntity!.Component<ResourceEntityComponentState>()!.State.Get("focus")!.Current);
            var observed = executor.ExecuteObserved(run, node, RunActivityBoundary.Entry, initial);
            Assert.True(observed.IsSuccess, observed.IsFailure ? observed.Error : null);
            Assert.Equal(CanonicalJson.ComputeHash(normal.Value), CanonicalJson.ComputeHash(observed.Value.Execution));
            var facts = observed.Value.Observation.Events.Occurrences;
            Assert.Equal(4, facts.Count);
            Assert.Equal(binding == RunActivityEffectOwner.RunWallet ? null : "hero", facts[0].SubjectActorId);
            Assert.Equal("hero", facts[1].SubjectActorId);
            Assert.Equal(facts[0].EventId, facts[1].ParentEventId); Assert.Equal(facts[2].EventId, facts[3].ParentEventId);
            Assert.Equal(binding == RunActivityEffectOwner.RunWallet ? "wallet" : "hero", Assert.IsType<ResourceChangePayload>(facts[0].Payload).Owner!.Id);
            Assert.Equal("hero", Assert.IsType<ResourceChangePayload>(facts[1].Payload).Owner!.Id);
            hashes.Add(CanonicalJson.ComputeHash(normal.Value));
        }
        Assert.Single(hashes); Assert.Equal(input, CanonicalJson.ComputeHash(run)); Assert.Empty(initial.Events.Occurrences);
    }
}

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

public sealed class PersistentWalletResponseTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WalletPaymentObservesTheRealOwnerAndProjectsResponsesIntoThePersistentActor(bool invalid)
    {
        var context = GameplayResponseCompilationTests.Context(); var hero = context.Combat!.GetActor("hero")!;
        var response = GameplayResponseCompilationTests.Definition() with { Observe = new() {
            Kinds = new() { Include = ["resource.change.resolved"] }, Reasons = new() { Include = ["cost.payment"] } },
            Bindings = new() { Target = new() { Kind = ResponseActorBindingKind.Owner } },
            Effects = invalid ? [new() { Type = EffectType.REMOVE_STATUS, StatusId = "unsupported", Target = EffectTarget.SELF }]
                : [new() { Type = EffectType.HEAL, TargetResource = "focus", FlatValue = 2, Target = EffectTarget.SELF }] };
        var run = context.Run with { ResourceState = hero.ResourceState with { OwnerId = "wallet" }, PlayerEntity = new() {
            InstanceId = "hero", ContentRevision = "revision", Components = hero.Components.ToImmutableDictionary(StringComparer.Ordinal)
                .Add("watch", new EntityResponseComponentState { ComponentId = "watch", Responses = [response] }) } };
        var input = CanonicalJson.ComputeHash(run); var hashes = new HashSet<string>();
        var identity = new RunCommandIdentity(Guid.Parse("6e4c3f14-3a39-4520-b486-5978b48469e9"), "TEST", 0, 0);
        var initial = GameplayCommandObservation.Create(run, null, identity).Value;
        for (var i = 0; i < 5; i++)
        {
            var cursor = new GameplayRunObservation(initial);
            cursor.Bind(new RunGameplayOccurrenceExecutor(EffectTransactionTests.Executor()));
            var paid = RunResourceTransitions.Pay(run, [new() { ResourceId = "focus", Amount = 3 }], "payment", cursor);
            if (invalid)
            {
                Assert.True(paid.IsFailure); Assert.Contains("persistable", paid.Error);
                continue;
            }
            Assert.True(paid.IsSuccess, paid.IsFailure ? paid.Error : null);
            Assert.Equal(7, paid.Value.Run.ResourceState.Get("focus")!.Current);
            Assert.Equal(12, paid.Value.Run.PlayerEntity!.Component<ResourceEntityComponentState>()!.State.Get("focus")!.Current);
            var fact = cursor.Current.Events.Occurrences[0];
            Assert.Null(fact.SubjectActorId);
            Assert.Equal("wallet", Assert.IsType<ResourceChangePayload>(fact.Payload).Owner!.Id);
            Assert.Equal(fact.EventId, cursor.Current.Events.Occurrences[1].ParentEventId);
            Assert.Equal("hero", cursor.Current.Events.Occurrences[1].SubjectActorId);
            hashes.Add(CanonicalJson.ComputeHash(new { paid.Value.Run, cursor.Current.Events.Occurrences }));
        }
        if (!invalid) Assert.Single(hashes);
        Assert.Empty(initial.Events.Occurrences); Assert.Equal(input, CanonicalJson.ComputeHash(run));
    }
}

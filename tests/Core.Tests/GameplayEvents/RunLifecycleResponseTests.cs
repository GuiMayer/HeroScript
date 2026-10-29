using System.Collections.Immutable;
using System.Text.Json;
using Core.Combat.Models;
using Core.Combat.Modifiers;
using Core.Config;
using Core.Determinism;
using Core.Effects;
using Core.GameplayEvents;
using Core.GameplayEvents.Responses;
using Core.Run;
using Core.Tests.Effects;
using Moq;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class RunLifecycleResponseTests
{
    [Theory]
    [InlineData(1, "modifier.stacks.changed")]
    [InlineData(2, "modifier.duration.changed")]
    public void NodeModifierTickUsesThePersistentDispatcher(int duration, string kind)
    {
        var context = GameplayResponseCompilationTests.Context(); var hero = context.Combat!.GetActor("hero")!;
        var run = context.Run with { PlayerEntity = new() { InstanceId = "hero", ContentRevision = "revision",
            Components = hero.Components.ToImmutableDictionary(StringComparer.Ordinal).Add("watch", new EntityResponseComponentState {
                ComponentId = "watch", Responses = [GameplayResponseCompilationTests.Definition() with { Observe = new() { Kinds = new() { Include = [kind] } },
                    Bindings = new() { Target = new() { Kind = ResponseActorBindingKind.Owner } },
                    Effects = [new() { Type = EffectType.HEAL, Target = EffectTarget.SELF, TargetResource = "focus", FlatValue = 2 }] }] }) },
            Modifiers = [new() { InstanceId = Guid.Parse("3882e06f-7a77-456e-8a6e-77f4c4f4c6b0"), ModifierId = "timer", Duration = duration,
                Stacks = 1, ContentRevision = "revision", Owner = new() { Kind = GameplayOwnerKind.Entity, Id = "hero" },
                Definition = new() { ModifierId = "timer", DurationBoundary = ModifierDurationBoundary.Node } }] };
        var initial = GameplayCommandObservation.Create(run, null, new(Guid.Parse("ab7eebbe-ccef-4eca-94ed-d2e4ff0f0277"), "TEST", 0, 0)).Value;
        var input = CanonicalJson.ComputeHash(run); var hashes = new HashSet<string>();
        for (var i = 0; i < 5; i++)
        {
            var cursor = new GameplayRunObservation(initial); cursor.Bind(new RunGameplayOccurrenceExecutor(EffectTransactionTests.Executor()));
            var result = new RunProgressionService(RunActivityRegistry.CreateDefault()).ApplyNodeExitObserved(run,
                new() { NodeId = "node", NextNodeIds = ["next"] }, cursor);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
            Assert.Equal(12, result.Value.PlayerEntity!.Component<ResourceEntityComponentState>()!.State.Get("focus")!.Current);
            Assert.Equal(kind, cursor.Current.Events.Occurrences[0].Kind);
            Assert.Equal(cursor.Current.Events.Occurrences[0].EventId, cursor.Current.Events.Occurrences[1].ParentEventId);
            hashes.Add(CanonicalJson.ComputeHash(result.Value));
        }
        Assert.Single(hashes); Assert.Empty(initial.Events.Occurrences); Assert.Equal(input, CanonicalJson.ComputeHash(run));
    }

    [Fact]
    public void ActualAbandonCommandDispatchesRunEndedBeforeCommitAndDoesNotPublishItsCandidate()
    {
        var fixture = EffectSequenceBudgetTests.Fixture();
        var hero = GameplayResponseCompilationTests.Context().Combat!.GetActor("hero")!;
        var run = fixture.Run with { RunId = Guid.Parse("72961662-f29e-462e-9a88-2625df06b12a"), Sequence = 1,
            ResolvedMode = fixture.Run.ResolvedMode! with { ProgressionPolicy = new() { AllowAbandon = true } }, PlayerEntity = new() {
                InstanceId = "hero", ContentRevision = "revision", Components = hero.Components.ToImmutableDictionary(StringComparer.Ordinal)
                    .Add("watch", new EntityResponseComponentState { ComponentId = "watch", Responses = [GameplayResponseCompilationTests.Definition() with {
                        Observe = new() { Kinds = new() { Include = ["run.ended"] } }, Bindings = new() { Target = new() { Kind = ResponseActorBindingKind.Owner } },
                        Effects = [EffectSequenceBudgetTests.Damage(2, 1) with { Type = EffectType.HEAL, Target = EffectTarget.SELF }] }] }) } };
        var manager = new RunManager(Mock.Of<IConfigManager>(), Mock.Of<IResourceLoader>());
        Assert.True(manager.HydrateForReplay(run).IsSuccess);
        var codec = new GameplayCommandCodec(GameplayCommandDescriptors.All);
        var handlers = RunCommandHandlers.Create(codec, () => new RunManager(Mock.Of<IConfigManager>(), Mock.Of<IResourceLoader>(),
            activityEffects: new RunActivityEffectExecutor(fixture.Executor)), RunActivityRegistry.CreateDefault());
        var coordinator = new RunSessionCoordinator(manager, codec, handlers, _ => throw new Exception("Must not publish"));
        var command = new GameplayCommandEnvelope(new(Guid.Parse("41a0acc8-0fea-41b9-80b5-b32bf2eb30fa"), RunCommandTypes.AbandonRun,
            run.Sequence, run.Determinism.Step), JsonSerializer.SerializeToElement(new EmptyGameplayCommand()));
        var input = CanonicalJson.ComputeHash(run);
        var planned = coordinator.PlanObserved(run.RunId, command);
        Assert.True(planned.IsSuccess, planned.IsFailure ? planned.Error : null);
        Assert.Equal(RunLifecycleState.Abandoned, planned.Value.Execution.CandidateState.Lifecycle);
        Assert.Equal(12, planned.Value.Execution.CandidateState.PlayerEntity!.Component<ResourceEntityComponentState>()!.State.Get("focus")!.Current);
        var ended = Assert.Single(planned.Value.Observation.Events.Occurrences, fact => fact.Kind == "run.ended");
        var healed = Assert.Single(planned.Value.Observation.Events.Occurrences, fact => fact.Kind == "resource.change.resolved");
        Assert.Equal(ended.EventId, healed.ParentEventId);
        Assert.Equal(input, CanonicalJson.ComputeHash(manager.GetRun(run.RunId).Value));
    }
}

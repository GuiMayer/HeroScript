using System.Collections.Immutable;
using System.Text.Json;
using Core.Combat.Models;
using Core.Content;
using Core.Determinism;
using Core.Effects;
using Core.GameplayEvents;
using Core.GameplayEvents.Responses;
using Core.Run;
using Core.Run.Content;
using Core.StatusEffects;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class GameplayContainerProviderTests
{
    [Fact]
    public void StandardProvidersCollectEffectiveFrozenContainersWithoutRunningTheirEffects()
    {
        var context = GameplayResponseCompilationTests.Context();
        var owned = GameplayResponseCompilationTests.Definition();
        var global = owned with { Bindings = new() { Owner = new() { Kind = ResponseActorBindingKind.ExplicitActor, ActorId = "hero" } } };
        var hero = context.Combat!.GetActor("hero")!;
        hero = hero with { Components = hero.Components.ToImmutableDictionary(StringComparer.Ordinal).Add("response", new EntityResponseComponentState
            { ComponentId = "response", Responses = [owned] }), ContentRevision = "actor-captured" };
        var status = new StatusEffectInstance { InstanceId = Guid.Parse("80000000-0000-0000-0000-000000000001"), TargetId = "hero", SourceId = "enemy",
            StatusId = "watch", ContentRevision = "status-captured", Stacks = 3, Duration = 2,
            Definition = new() { StatusId = "watch", Responses = [owned] } };
        var relic = new RunRelicState { RelicInstanceId = Guid.Parse("90000000-0000-0000-0000-000000000001"), DefinitionId = "watch",
            Owner = new() { Kind = GameplayOwnerKind.Entity, Id = "hero" }, ContentRevision = "relic-captured", Responses = [owned] };
        var combat = context.Combat with { Actors = context.Combat.Actors.ToDictionary(pair => pair.Key, pair => pair.Key == "hero" ? hero : pair.Value), RunNodeId = "encounter", StatusEffects = ImmutableDictionary<string, ImmutableArray<StatusEffectInstance>>.Empty.Add("hero", [status]) };
        context = context with { Combat = combat, Run = context.Run with { Relics = [relic], ResolvedMode = new()
            { Definition = new() { ModeId = "mode", Responses = [global] } }, Map = new() { Nodes = [new()
                { NodeId = "encounter", Activity = new() { Type = RunActivityType.Encounter }, Responses = [global], ResponseContentRevision = "encounter-captured" }] } } };
        IGameplayObserverProvider[] providers = [new ModeGameplayObserverProvider(), new EncounterGameplayObserverProvider(),
            new ActorGameplayObserverProvider(), new RelicGameplayObserverProvider(), new StatusGameplayObserverProvider()];
        var before = CanonicalJson.ComputeHash(new { context.Run, context.Combat });
        var hashes = new HashSet<string>();
        for (var repeat = 0; repeat < 5; repeat++)
        {
            var result = GameplayObserverCollector.Collect(context, providers);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
            Assert.Equal(5, result.Value.Length);
            Assert.Equal(5, result.Value.Select(item => item.RegistrationId).Distinct().Count());
            var observedStatus = Assert.Single(result.Value, item => item.ProviderId == "core.status");
            Assert.Equal("enemy", observedStatus.SourceActorId); Assert.Equal("hero", observedStatus.OwnerActorId);
            Assert.Equal("status-captured", observedStatus.Origin.ContentRevision); Assert.Equal(3, observedStatus.Stacks);
            hashes.Add(CanonicalJson.ComputeHash(result.Value));
        }
        Assert.Single(hashes);
        Assert.Equal(before, CanonicalJson.ComputeHash(new { context.Run, context.Combat }));
        Assert.Empty(GameplayObserverCollector.Collect(context with { Snapshot = ResponseSnapshot.AfterOccurrence }, providers).Value);
        var removed = context with { Combat = combat with { StatusEffects = ImmutableDictionary<string, ImmutableArray<StatusEffectInstance>>.Empty } };
        Assert.Equal(4, GameplayObserverCollector.Collect(removed, providers).Value.Length);
    }

    [Fact]
    public void PublicationFailsClosedForDeclarationsUntilExecutableCutoverAndOldSnapshotsStayUnchanged()
    {
        var response = GameplayResponseCompilationTests.Definition();
        var relic = new RelicDefinition { RelicId = "relic", Responses = [response] };
        var json = JsonSerializer.SerializeToElement(relic);
        Assert.True(ContentKindRegistry.Default.Validate("relics", "relic", json).IsFailure);
        var shadow = new ContentKindRegistry(ContentKindRegistry.Default.Kinds, allowShadowResponseDeclarations: true);
        Assert.True(shadow.Validate("relics", "relic", json).IsSuccess);
        var card = new CardContentDefinition { CardId = "card", Components = [new CardResponseComponentDefinition
            { ComponentId = "response", Response = response with { Availability = new() { Kind = ResponseAvailabilityKind.SelectedCard } } }] };
        Assert.True(new CardContentCompiler().Compile(card).IsFailure);
        Assert.True(new CardContentCompiler(GameplayResponseCompilationTests.Catalog()).Compile(card).IsSuccess);
        Assert.DoesNotContain("Responses", JsonSerializer.Serialize(new RelicDefinition()));
        Assert.DoesNotContain("Responses", JsonSerializer.Serialize(new StatusEffectDefinition()));
        Assert.DoesNotContain("Responses", JsonSerializer.Serialize(new GameModeDefinition()));
        Assert.DoesNotContain("Responses", JsonSerializer.Serialize(new RunMapNodeState()));
    }

    [Fact]
    public void NumericNamespacesKeepOwnerSourceAndSubjectDistinctAndNeverTraverseArbitraryObjects()
    {
        var context = GameplayResponseCompilationTests.Context();
        var hero = context.Combat!.GetActor("hero")!;
        hero = hero.WithResourceState(hero.ResourceState with { Resources = hero.ResourceState.Resources.ToImmutableDictionary()
            .SetItem("focus", hero.GetResource("focus")! with { Current = 3 }) });
        context = context with { Combat = context.Combat.ReplaceActor(hero) };
        var registration = GameplayResponseCompilationTests.Registration() with { Stacks = 3 };
        var fact = new GameplayOccurrence { SourceActorId = "enemy", SubjectActorId = "hero", AttributedActorId = "enemy", Payload = new ResourceChangePayload
            { ResourceId = "arbitrary", Operation = Core.Resources.ResourceMutationOperation.Set, Before = 10, Operand = 30, RequestedValue = 30, After = 20 } };
        var result = GameplayResponseNumbers.Read(fact, registration, context, "hero", ["event.change.operand", "event.change.applied_delta",
            "event.change.limited_delta", "owner.resources.focus.current", "event.source.resources.focus.current", "scope.container_stacks"]);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(30, result.Value["event.change.operand"]); Assert.Equal(10, result.Value["event.change.applied_delta"]);
        Assert.Equal(10, result.Value["event.change.limited_delta"]); Assert.Equal(3, result.Value["scope.container_stacks"]);
        Assert.Equal(3, result.Value["owner.resources.focus.current"]); Assert.Equal(10, result.Value["event.source.resources.focus.current"]);
        Assert.True(GameplayResponseNumbers.Read(fact, registration, context, null, ["owner.GetType"]).IsFailure);
        Assert.True(GameplayResponseNumbers.Read(fact with { SourceActorId = null }, registration, context, "hero", ["event.source.resources.focus.current"]).IsFailure);
        Assert.True(GameplayResponseNumbers.Read(fact, registration, context, "hero", Enumerable.Repeat("event.depth", 129)).IsFailure);
        var huge = fact with { Payload = new ResourceChangePayload { Before = -float.MaxValue, After = float.MaxValue } };
        Assert.True(GameplayResponseNumbers.Read(huge, registration, context, "hero", ["event.change.applied_delta"]).IsFailure);
        Assert.Empty(GameplayResponseNumbers.Read(huge, registration, context, "hero", []).Value);
    }
}

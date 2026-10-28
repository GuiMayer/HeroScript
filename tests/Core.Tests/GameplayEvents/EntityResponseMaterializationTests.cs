using System.Collections.Immutable;
using System.Text.Json;
using Core.Combat.Models;
using Core.Determinism;
using Core.Entity.Definitions;
using Core.Entity.Integration;
using Core.GameplayEvents.Responses;
using Core.GameplayEvents;
using Core.Effects;
using Core.Tests.Effects;
using Core.Combat;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class EntityResponseMaterializationTests
{
    [Fact]
    public void MaterializedThirdActorCanObserveAnotherActorsHealWithoutBorrowingSourceOrTarget()
    {
        var response = GameplayResponseCompilationTests.Definition() with
        {
            Observe = new() { Kinds = new() { Include = ["resource.change.resolved"] },
                Source = new() { MatchesOwner = false, RelationshipToOwner = SideRelationship.Ally },
                Subject = new() { MatchesOwner = false, RelationshipToOwner = SideRelationship.Ally },
                Resource = new() { Direction = ResourceChangeDirection.Increase } },
            Effects = [new() { Type = EffectType.HEAL, Target = EffectTarget.SELF, TargetResource = "focus", FlatValue = 2 }]
        };
        var definition = new EntityDefinition { DefinitionId = "watcher", DisplayName = "Watcher", Components = [
            new EntityResponseComponentDefinition { ComponentId = "observers", Responses = [response] }] };
        var context = GameplayResponseCompilationTests.Context();
        var actor = new EntityMaterializer(TestDataBuilders.MockResourceManager().Object).Materialize(definition,
            "ally", "revision", "red", new() { Kind = ControllerKind.AI, PolicyId = "observer" }).Value;
        actor = actor with { ResourceState = context.Combat!.GetActor("ally")!.ResourceState };
        var combat = CombatTransitions.Create(context.Combat.GetAllActors().Select(item => item.InstanceId == "ally" ? actor
            : item.InstanceId == "neutral" ? item with { SideId = "red" } : item), context.Combat.Determinism);
        var request = EffectTransactionTests.Request(EffectTransactionTests.Resource(EffectType.HEAL, 3)) with
            { Run = context.Run, Combat = combat, OwnerEntityId = "enemy", SourceEntityId = "enemy", SelectedTargetEntityIds = ["neutral"] };
        var input = CanonicalJson.ComputeHash(request);
        var executor = EffectTransactionTests.Executor();
        var observation = GameplayObservedExecutorTests.ObservationFor(context.Run, combat);
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        for (var repeat = 0; repeat < 5; repeat++)
        {
            var result = executor.ExecuteResponsiveObserved(request, observation, context.Catalog, [new ActorGameplayObserverProvider()]);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
            Assert.Equal(13, result.Value.Execution.State.GetActor("neutral")!.GetResource("focus")!.Current);
            Assert.Equal(12, result.Value.Execution.State.GetActor("ally")!.GetResource("focus")!.Current);
            Assert.Equal(10, result.Value.Execution.State.GetActor("enemy")!.GetResource("focus")!.Current);
            var facts = result.Value.Observation.Events.Occurrences;
            Assert.Equal("enemy", facts[0].SourceActorId); Assert.Equal("neutral", facts[0].SubjectActorId);
            Assert.Equal("ally", facts[1].SourceActorId); Assert.Equal("ally", facts[1].SubjectActorId);
            Assert.Equal(facts[0].EventId, facts[1].ParentEventId);
            hashes.Add(CanonicalJson.ComputeHash(result.Value.Execution));
        }
        Assert.Single(hashes); Assert.Equal(input, CanonicalJson.ComputeHash(request)); Assert.Empty(observation.Events.Occurrences);
        var foreign = executor.ExecuteResponsiveObserved(request with { OwnerEntityId = "hero", SourceEntityId = "hero" },
            observation, context.Catalog, [new ActorGameplayObserverProvider()]);
        Assert.True(foreign.IsSuccess, foreign.IsFailure ? foreign.Error : null);
        Assert.Equal(10, foreign.Value.Execution.State.GetActor("ally")!.GetResource("focus")!.Current);
    }

    [Fact]
    public void JsonDefinitionMaterializesCapturedResponsesIntoTheCanonicalActorComponent()
    {
        var definition = new EntityDefinition { DefinitionId = "watcher", DisplayName = "Watcher", Components = [
            new EntityResponseComponentDefinition { ComponentId = "observers", Responses = [GameplayResponseCompilationTests.Definition()] }] };
        var encoded = JsonSerializer.Serialize(definition);
        definition = JsonSerializer.Deserialize<EntityDefinition>(encoded)!;
        Assert.True(EntityDefinitionValidator.Validate(definition).IsSuccess);
        var materializer = new EntityMaterializer(TestDataBuilders.MockResourceManager().Object);
        var input = CanonicalJson.ComputeHash(definition);
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        for (var repeat = 0; repeat < 5; repeat++)
        {
            var result = materializer.Materialize(definition, "ally", "revision", "blue", new() { Kind = ControllerKind.AI, PolicyId = "observer" });
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
            var component = result.Value.Component<EntityResponseComponentState>()!;
            Assert.Equal("watch", Assert.Single(component.Responses!.Value).ResponseId);
            var context = GameplayResponseCompilationTests.Context();
            context = context with { Combat = CombatTransitions.Create(context.Combat!.GetAllActors().Select(actor =>
                actor.InstanceId == result.Value.InstanceId ? result.Value : actor), context.Combat.Determinism) };
            var collected = GameplayObserverCollector.Collect(context, [new ActorGameplayObserverProvider()]);
            Assert.True(collected.IsSuccess, collected.IsFailure ? collected.Error : null);
            Assert.Equal("ally", Assert.Single(collected.Value).OwnerActorId);
            Assert.Equal("revision", collected.Value[0].Origin.ContentRevision);
            hashes.Add(CanonicalJson.ComputeHash(result.Value));
        }
        Assert.Single(hashes); Assert.Equal(input, CanonicalJson.ComputeHash(definition));
    }

    [Fact]
    public void MalformedDeclarationsFailBeforeRuntimeAndDoNotRelaxPublication()
    {
        var response = GameplayResponseCompilationTests.Definition();
        foreach (var definitions in new ImmutableArray<GameplayResponseDefinition>[] { default, [response, response], [null!],
            Enumerable.Range(0, 17).Select(index => response with { ResponseId = "item-" + index }).ToImmutableArray() })
        {
            var definition = new EntityDefinition { DefinitionId = "unit", DisplayName = "Unit", Components = [
                new EntityResponseComponentDefinition { ComponentId = "responses", Responses = definitions }] };
            Assert.True(EntityDefinitionValidator.Validate(definition).IsFailure);
        }
        Assert.True(GameplayResponseAuthoringGuard.ValidateUnavailable(new EntityDefinition { DefinitionId = "unit", DisplayName = "Unit",
            Components = [new EntityResponseComponentDefinition { ComponentId = "responses", Responses = [response] }] }).IsFailure);
    }
}

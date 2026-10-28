using System.Collections.Immutable;
using System.Text.Json;
using Core.CardZones;
using Core.Common;
using Core.Determinism;
using Core.Effects;
using Core.GameplayEvents;
using Core.GameplayEvents.Responses;
using Core.Run;
using Core.Tests.Effects;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class GameplayResponseCompilationTests
{
    internal static GameplayResponseDefinition Definition() => new()
    {
        ResponseId = "watch", Observe = new() { Kinds = new() { Include = ["resource.change.resolved"] } },
        Effects = [new() { Type = EffectType.MODIFY_RESOURCE, Target = EffectTarget.SELF, TargetResource = "barrier",
            Operation = ResourceEffectOperation.ADD, FlatValue = 2 }]
    };

    internal static GameplayEventCatalog Catalog() => GameplayCoreCatalog.Create("setting", "revision").Value;
    internal static GameplayObserverCollectionContext Context() => new()
    { Run = new() { SettingId = "setting", PlayerEntityId = "hero", Determinism = DeterministicContext.Create(42, "revision") },
        Combat = EffectTransactionTests.Request().Combat, Catalog = Catalog() };

    internal static GameplayObserverRegistration Registration(string id = "one", GameplayResponseDefinition? definition = null) => new()
    {
        RegistrationId = id, ProviderId = "test", OwnerActorId = "hero",
        Response = GameplayResponseCompiler.Compile(definition ?? Definition(), Catalog()).Value,
        Origin = new() { ContainerKind = "status", ContainerId = id, ComponentId = "watch", ContentRevision = "captured-definition" }
    };

    internal sealed class FixedProvider(params GameplayObserverRegistration[] registrations) : IGameplayObserverProvider
    {
        public string ProviderId => "test";
        public Result<ImmutableArray<GameplayObserverRegistration>> Collect(GameplayObserverCollectionContext context) =>
            Result<ImmutableArray<GameplayObserverRegistration>>.Success(registrations.ToImmutableArray());
    }

    [Fact]
    public void JsonContractRoundTripsTypedBindingsAndRejectsUnknownInstructions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        var definition = JsonSerializer.Deserialize<GameplayResponseDefinition>("""
        { "responseId": "observer", "observe": { "kinds": { "include": ["resource.change.resolved"] },
          "subject": { "relationshipToOwner": "Enemy" }, "resource": { "field": "Current", "direction": "Increase" } },
          "condition": "event.change.applied_delta > 0", "bindings": { "owner": { "kind": "ExplicitActor", "actorId": "hero" } },
          "effects": [{ "type": "MODIFY_RESOURCE", "target": "SELF", "targetResource": "barrier", "operation": "ADD", "flatValue": 2 }] }
        """, options)!;
        var compiled = GameplayResponseCompiler.Compile(definition, Catalog());
        Assert.True(compiled.IsSuccess, compiled.IsFailure ? compiled.Error : null);
        Assert.Equal(CanonicalJson.ComputeHash(definition), CanonicalJson.ComputeHash(JsonSerializer.Deserialize<GameplayResponseDefinition>(
            JsonSerializer.Serialize(definition, options), options)));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<GameplayResponseDefinition>("""{"responseId":"bad","executeCode":"danger"}""", options));
    }

    [Theory]
    [InlineData("RESOURCE.change.resolved")]
    [InlineData("unknown.event")]
    [InlineData("resource..resolved")]
    public void SelectorsUseThePinnedCatalogAndStrictGrammar(string selector) => Assert.True(GameplayResponseCompiler.Compile(
        Definition() with { Observe = new() { Kinds = new() { Include = [selector] } } }, Catalog()).IsFailure);

    [Fact]
    public void UnsupportedOrImpossibleContractsAreNotAdvertisedAsExecutable()
    {
        var definition = Definition();
        Assert.True(GameplayResponseCompiler.Compile(definition with { Frequency = new() { Scope = ResponseFrequencyScope.Run } }, Catalog()).IsFailure);
        Assert.True(GameplayResponseCompiler.Compile(definition with { Observe = new() { Kinds = new() { Include = ["status.applied"] }, Resource = new() } }, Catalog()).IsFailure);
        Assert.True(GameplayResponseCompiler.Compile(definition with { Bindings = new() { Owner = new() { Kind = ResponseActorBindingKind.Owner } } }, Catalog()).IsFailure);
        Assert.True(GameplayResponseCompiler.Compile(definition with { Observe = new() { Kinds = new() { Include = ["resource.**"], Exclude = ["resource.**"] } } }, Catalog()).IsFailure);
        Assert.True(GameplayResponseCompiler.Compile(definition with { Observe = new() { RequiredTags = ["fire"], ExcludedTags = ["fire"] } }, Catalog()).IsFailure);
        Assert.True(GameplayResponseCompiler.Compile(definition with { Effects = default }, Catalog()).IsFailure);
    }

    [Fact]
    public void ExtensionPermissionComesFromHostAndUnknownTopicsRemainDormant()
    {
        var definition = Definition() with { AllowExtensionTopics = true, Observe = new() { Kinds = new() { Include = ["game.future.**"] } } };
        Assert.True(GameplayResponseCompiler.Compile(definition, Catalog()).IsFailure);
        var compiled = GameplayResponseCompiler.Compile(definition, Catalog(), new() { ExtensionTopics = true });
        Assert.True(compiled.IsSuccess, compiled.IsFailure ? compiled.Error : null);
        Assert.False(compiled.Value.HasKnownTopics);
        var collected = GameplayObserverCollector.Collect(Context(), [new FixedProvider(Registration() with { Response = compiled.Value })]);
        Assert.True(collected.IsSuccess); Assert.Empty(collected.Value);
    }

    [Fact]
    public void CollectionIsPureScopedUniqueAndNormalizesExtremePrioritiesWithoutOverflow()
    {
        var context = Context();
        var before = CanonicalJson.ComputeHash(context.Run);
        var low = Registration("low", Definition() with { Order = new() { Priority = int.MinValue } });
        var high = Registration("high", Definition() with { Order = new() { Priority = int.MinValue, Descending = true } });
        var result = GameplayObserverCollector.Collect(context, [new FixedProvider(high, low)]);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(new[] { "low", "high" }, result.Value.Select(item => item.RegistrationId));
        Assert.Equal(2147483648L, result.Value[1].Order.Priority);
        Assert.Equal(before, CanonicalJson.ComputeHash(context.Run));
        Assert.True(GameplayObserverCollector.Collect(context, [new FixedProvider(low, low)]).IsFailure);
        Assert.True(GameplayObserverCollector.Collect(context with { Catalog = GameplayCoreCatalog.Create("foreign", "revision").Value }, [new FixedProvider(low)]).IsFailure);
        Assert.True(GameplayObserverCollector.Collect(context, [new FixedProvider(low with { OwnerActorId = "absent" })]).IsFailure);
        Assert.True(GameplayObserverCollector.Collect(context, [new FixedProvider(low with { OwnerActorId = null })]).IsFailure);
    }

    [Fact]
    public void CardAvailabilityUsesActualZoneOwnershipAndCapturedSelection()
    {
        var graph = new CardZoneSystemDefinition { CardZoneSystemId = "test", Zones = [new()
            { ZoneId = "reserve", OwnerScope = CardZoneOwnerScope.RunOwner, Ordering = CardZoneOrdering.Ordered }] };
        var context = Context();
        var topology = CardZoneBootstrapper.Create(CardZoneSystemCompiler.Compile(graph).Value, new() { RunOwnerId = "$run",
            Batches = [new() { ZoneId = "reserve", OwnerId = "$run", DefinitionIds = ["test"] }] }, context.Run.Determinism).Value;
        var id = Assert.Single(topology.State.Instances.Keys);
        context = context with { Run = context.Run with { Deck = new() { Topology = topology.State }, ResolvedMode = new() { CardZoneSystem = graph } } };
        var definition = Definition() with { Availability = new() { Kind = ResponseAvailabilityKind.SelectedCard } };
        var registration = Registration(definition: definition) with { CardInstanceId = id };
        Assert.Empty(GameplayObserverCollector.Collect(context, [new FixedProvider(registration)]).Value);
        Assert.Single(GameplayObserverCollector.Collect(context with { SelectedCardId = id }, [new FixedProvider(registration)]).Value);
        Assert.True(GameplayObserverCollector.Collect(context with { SelectedCardId = id }, [new FixedProvider(registration with { OwnerActorId = "enemy" })]).IsFailure);
        Assert.True(GameplayObserverCollector.Collect(context, [new FixedProvider(registration with { Response = GameplayResponseCompiler.Compile(Definition(), Catalog()).Value })]).IsFailure);
    }
}

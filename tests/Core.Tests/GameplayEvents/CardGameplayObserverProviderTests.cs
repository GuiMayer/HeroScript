using System.Collections.Immutable;
using System.Text.Json;
using Core.CardZones;
using Core.Combat.Models;
using Core.Common;
using Core.Effects;
using Core.Math;
using Core.Calculations;
using Core.Tests.Effects;
using Core.Content;
using Core.Determinism;
using Core.GameplayEvents;
using Core.GameplayEvents.Responses;
using Core.Run;
using Core.Run.Content;
using Core.Run.DeckCompartments;
using Moq;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class CardGameplayObserverProviderTests
{
    [Fact]
    public void EffectiveCardObserverTracksActualZonesAndRemovalOfItsComponent()
    {
        var fixture = Fixture(false);
        var before = CanonicalJson.ComputeHash(fixture.Context.Run);
        var result = GameplayObserverCollector.Collect(fixture.Context, [fixture.Provider]);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        var registration = Assert.Single(result.Value);
        Assert.Equal("hero", registration.OwnerActorId); Assert.Equal(fixture.CardId, registration.CardInstanceId);
        var moved = CardZoneTransitions.Move(fixture.Context.Run.Deck.Topology, new() { ZoneId = "alpha", OwnerId = "$run" },
            new() { ZoneId = "beta", OwnerId = "$run" }, [fixture.CardId], new(), null, CardZoneOrdering.Ordered, CardZoneOrdering.Ordered,
            fixture.Context.Run.Determinism);
        Assert.True(moved.IsSuccess, moved.IsFailure ? moved.Error : null);
        var hidden = fixture.Context with { Run = fixture.Context.Run with { Deck = new() { Topology = moved.Value.State } } };
        Assert.Empty(GameplayObserverCollector.Collect(hidden, [fixture.Provider]).Value);
        var removed = CardInstanceUpgradeTransitions.Apply(fixture.Context.Run.Deck.GetCard(fixture.CardId)!, new()
        { UpgradeId = "remove", Patches = [new CardComponentPatchDefinition { ComponentId = "watch", Operation = CardComponentPatchOperation.Remove }] }, "upgrade-revision");
        Assert.True(removed.IsSuccess, removed.IsFailure ? removed.Error : null);
        var changed = fixture.Context with { Run = fixture.Context.Run with { Deck = new() { Topology = fixture.Context.Run.Deck.Topology with
            { Instances = fixture.Context.Run.Deck.Topology.Instances.ToImmutableDictionary().SetItem(fixture.CardId, removed.Value) } } } };
        var gone = GameplayObserverCollector.Collect(changed, [fixture.Provider]);
        Assert.True(gone.IsSuccess, gone.IsFailure ? gone.Error : null); Assert.Empty(gone.Value);
        Assert.Equal(before, CanonicalJson.ComputeHash(fixture.Context.Run));
    }

    [Fact]
    public void CompartmentContributionRegistersTheSameComponentWithItsCapturedProvenance()
    {
        var fixture = Fixture(true);
        var before = CanonicalJson.ComputeHash(fixture.Context.Run);
        var hashes = new HashSet<string>();
        for (var repeat = 0; repeat < 5; repeat++)
        {
            var result = GameplayObserverCollector.Collect(fixture.Context, [fixture.Provider]);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
            var response = Assert.Single(result.Value);
            Assert.Equal("contribution", Assert.Single(response.Contributors).ContainerKind);
            Assert.StartsWith("grant_", response.Origin.ComponentId);
            Assert.Equal(fixture.CardId, response.CardInstanceId);
            Assert.Equal("hero", response.OwnerActorId);
            hashes.Add(CanonicalJson.ComputeHash(response));
        }
        Assert.Single(hashes); Assert.Equal(before, CanonicalJson.ComputeHash(fixture.Context.Run));
        var unassigned = fixture.Context with { Run = fixture.Context.Run with { DeckCompartments = null } };
        Assert.Empty(GameplayObserverCollector.Collect(unassigned, [fixture.Provider]).Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NormalExecutionCompilesAndExecutesCardAndCompartmentObservers(bool compartment)
    {
        var fixture = Fixture(compartment);
        var compiler = new CardContentCompiler();
        var compiled = compiler.Compile("card", fixture.Runtime);
        Assert.True(compiled.IsSuccess, compiled.IsFailure ? compiled.Error : null);
        var effective = DeckCompartmentComposition.Resolve(fixture.Context.Run, fixture.Runtime, compiled.Value,
            fixture.Context.Run.Deck.GetCard(fixture.CardId)!, new EffectiveCardResolver());
        Assert.True(effective.IsSuccess, effective.IsFailure ? effective.Error : null);
        Assert.Single(effective.Value.Components.OfType<CardResponseComponentDefinition>());
        var request = EffectTransactionTests.Request(new EffectDefinition { Type = EffectType.HEAL, TargetResource = "focus", FlatValue = 1 })
            with { Run = fixture.Context.Run, Combat = fixture.Context.Combat! };
        var executor = new EffectTriggerExecutor(Mock.Of<IRuntimeFormulaEvaluator>(), new ImmutableEffectProcessor(),
            fixture.Runtimes, new CalculationEngine(), new EmptyInfluences(), allowUnconfiguredCalculations: true);
        var original = CanonicalJson.ComputeHash(request);
        var outputs = new HashSet<string>();
        for (var i = 0; i < 5; i++)
        {
            var normal = executor.Execute(request);
            Assert.True(normal.IsSuccess, normal.IsFailure ? normal.Error : null);
            Assert.Equal(12, normal.Value.State.GetActor("hero")!.GetResource("focus")!.Current);
            var observed = executor.ExecuteObserved(request, GameplayObservedExecutorTests.ObservationFor(request.Run, request.Combat));
            Assert.True(observed.IsSuccess, observed.IsFailure ? observed.Error : null);
            Assert.Equal(CanonicalJson.ComputeHash(normal.Value), CanonicalJson.ComputeHash(observed.Value.Execution));
            Assert.Equal(2, observed.Value.Observation.Events.Occurrences.Count);
            outputs.Add(CanonicalJson.ComputeHash(normal.Value));
        }
        Assert.Single(outputs); Assert.Equal(original, CanonicalJson.ComputeHash(request));
    }

    private sealed class EmptyInfluences : ICalculationInfluenceProvider
    {
        public string ProviderId => "test.empty";
        public Result<IReadOnlyList<CalculationInfluence>> Collect(CalculationSourceContext context) =>
            Result<IReadOnlyList<CalculationInfluence>>.Success([]);
    }

    private static (GameplayObserverCollectionContext Context, CardGameplayObserverProvider Provider, Guid CardId, ContentRuntime Runtime, IContentRuntimeResolver Runtimes) Fixture(bool compartment)
    {
        var context = GameplayResponseCompilationTests.Context();
        var response = new CardResponseComponentDefinition { ComponentId = "watch", Response = GameplayResponseCompilationTests.Definition() with
            { Availability = new() { Kind = ResponseAvailabilityKind.CardZones, ZoneIds = ["alpha"] },
                Effects = [new() { Type = EffectType.HEAL, Target = EffectTarget.SELF, TargetResource = "focus", FlatValue = 2 }] } };
        var definitions = new List<(string Kind, string Id, object Value)> { ("cards", "card", new CardContentDefinition { CardId = "card",
            Components = compartment ? [new CardConditionComponentDefinition { ComponentId = "base", Expression = "1" }] : [response] }) };
        if (compartment)
        {
            definitions.Add(("card-component-bundles", "responses", new CardComponentBundleDefinition { BundleId = "responses", Components = [response] }));
            definitions.Add(("deck-compartment-profiles", "layout", new DeckCompartmentProfileDefinition { ProfileId = "layout", Compartments = [new()
                { CompartmentId = "grant", Capacity = 1, Grants = [new() { GrantId = "response", BundleId = "responses" }] }] }));
            definitions.Add(("deck-compartment-policies", "outside", new DeckCompartmentPolicyDefinition { PolicyId = "outside" }));
        }
        definitions.Add(("calculation-pipelines", "identity", new CalculationPipelineDefinition { PipelineId = "identity",
            Channel = "effect_amount", Buckets = [new() { BucketId = "identity", Operation = CalculationBucketOperation.Add }] }));
        var artifacts = definitions.GroupBy(item => item.Kind).ToImmutableDictionary(group => group.Key + "/test.json",
            group => JsonSerializer.SerializeToElement(group.ToDictionary(item => item.Id, item => item.Value)));
        var runtime = ContentRuntime.Create(new() { Manifest = new() { ConfigName = "default", Revision = "revision",
            Artifacts = artifacts.Select(pair => new ContentArtifactManifest { Kind = pair.Key.Split('/')[0], Path = pair.Key,
                DefinitionCount = pair.Value.EnumerateObject().Count() }).ToArray() }, Artifacts = artifacts },
            new ContentKindRegistry(ContentKindRegistry.Default.Kinds, allowShadowResponseDeclarations: true));
        Assert.True(runtime.IsSuccess, runtime.IsFailure ? runtime.Error : null);
        var graph = new CardZoneSystemDefinition { CardZoneSystemId = "test", Zones = [new()
            { ZoneId = "alpha", OwnerScope = CardZoneOwnerScope.RunOwner, Ordering = CardZoneOrdering.Ordered }, new()
            { ZoneId = "beta", OwnerScope = CardZoneOwnerScope.RunOwner, Ordering = CardZoneOrdering.Ordered }] };
        var topology = CardZoneBootstrapper.Create(CardZoneSystemCompiler.Compile(graph).Value, new() { RunOwnerId = "$run",
            Batches = [new() { ZoneId = "alpha", OwnerId = "$run", DefinitionIds = ["card"] }] }, context.Run.Determinism).Value;
        var id = Assert.Single(topology.State.Instances.Keys);
        var run = context.Run with { Deck = new() { Topology = topology.State }, ResolvedMode = new() { CardZoneSystem = graph,
            Definition = new() { ModeId = "test", CalculationPipelineIds = ["identity"], DeckCompartmentPolicyId = compartment ? "outside" : null } } };
        if (compartment) run = run with { PlayerEntity = new() { InstanceId = "hero", Components = ImmutableDictionary<string, EntityComponentState>.Empty
                .Add("layout", new DeckCompartmentProfileComponentState { ComponentId = "layout", ProfileId = "layout" }) },
            DeckCompartments = new() { Actors = ImmutableSortedDictionary<string, ActorDeckCompartmentState>.Empty.Add("hero", new()
                { ActorId = "hero", ProfileId = "layout", Allocations = ImmutableSortedDictionary<Guid, string>.Empty.Add(id, "grant") }) } };
        context = context with { Run = run };
        var runtimes = new Mock<IContentRuntimeResolver>();
        runtimes.Setup(service => service.Resolve("revision", "default")).Returns(Result<ContentRuntime>.Success(runtime.Value));
        return (context, new(runtimes.Object), id, runtime.Value, runtimes.Object);
    }
}

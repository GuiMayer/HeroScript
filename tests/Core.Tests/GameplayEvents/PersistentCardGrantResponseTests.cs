using System.Collections.Immutable;
using System.Text.Json;
using Core.Calculations;
using Core.CardZones;
using Core.Combat.Models;
using Core.Common;
using Core.Content;
using Core.Determinism;
using Core.Effects;
using Core.GameplayEvents;
using Core.GameplayEvents.Responses;
using Core.Math;
using Core.Run;
using Core.Run.Content;
using Core.Tests.Effects;
using Moq;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class PersistentCardGrantResponseTests
{
    [Fact]
    public void CardRewardKeepsPersistentActorChangesFromItsCreationResponse()
    {
        var context = GameplayResponseCompilationTests.Context(); var hero = context.Combat!.GetActor("hero")!;
        var response = GameplayResponseCompilationTests.Definition() with { Observe = new() { Kinds = new() { Include = ["card.created"] } },
            Bindings = new() { Target = new() { Kind = ResponseActorBindingKind.Owner } },
            Effects = [EffectSequenceBudgetTests.Damage(2, 1) with { Type = EffectType.HEAL, Target = EffectTarget.SELF }] };
        var graph = new CardZoneSystemDefinition { CardZoneSystemId = "test", GameplayGrantFlowId = "grant", Zones = [new() {
            ZoneId = "collection", OwnerScope = CardZoneOwnerScope.RunOwner, Ordering = CardZoneOrdering.Ordered }], Flows = [new() {
            FlowId = "grant", AllowedInvocations = [CardZoneFlowInvocation.GameplayCommand], Steps = [new() {
                StepId = "create", Operation = CardZoneOperation.Create, CardDefinitionId = "$input", TargetZoneId = "collection",
                TargetOwner = CardZoneOwnerBinding.RunOwner, Selection = new() { Count = 1 } }] }] };
        var profiles = EffectSequenceBudgetTests.Profiles();
        var bundle = EffectSequenceBudgetTests.Bundle(profiles);
        var artifacts = bundle.Artifacts.SetItem("cards/test.json", JsonSerializer.SerializeToElement(new Dictionary<string, CardContentDefinition> {
            ["card"] = new() { CardId = "card", Components = [new CardConditionComponentDefinition { ComponentId = "base", Expression = "1" }] } }));
        var runtime = ContentRuntime.Create(bundle with { Artifacts = artifacts, Manifest = bundle.Manifest with {
            Artifacts = artifacts.Select(item => new ContentArtifactManifest { Path = item.Key, Kind = item.Key.Split('/')[0],
                DefinitionCount = item.Value.EnumerateObject().Count() }).ToArray() } });
        Assert.True(runtime.IsSuccess, runtime.IsFailure ? runtime.Error : null);
        var resolver = new Mock<IContentRuntimeResolver>();
        resolver.Setup(item => item.Resolve("revision", "default")).Returns(Result<ContentRuntime>.Success(runtime.Value));
        var run = context.Run with { PlayerEntity = new() { InstanceId = "hero", ContentRevision = "revision",
            Components = hero.Components.ToImmutableDictionary(StringComparer.Ordinal).Add("watch",
                new EntityResponseComponentState { ComponentId = "watch", Responses = [response] }) },
            ResolvedMode = new() { CardZoneSystem = graph, Definition = new() { CalculationPipelineIds = profiles.Keys.ToArray() } } };
        var seeded = CardZoneBootstrapper.Create(CardZoneSystemCompiler.Compile(graph).Value, new() { RunOwnerId = "$run" }, run.Determinism).Value;
        var selectionId = Guid.Parse("063f7486-cc28-45af-ad43-1fc48d94fa79");
        run = run with { Deck = new() { Topology = seeded.State }, Determinism = seeded.Context, CardSelections = [new() {
            SelectionInstanceId = selectionId, PickCount = 1, Options = [new() { CardId = "card" }] }] };
        var effects = new EffectTriggerExecutor(Mock.Of<IRuntimeFormulaEvaluator>(), new ImmutableEffectProcessor(), resolver.Object,
            new CalculationEngine(), new CompositeCalculationInfluenceProvider([new EntityResourceInfluenceProvider()]));
        var executor = new RunGameplayOccurrenceExecutor(effects);
        var initial = GameplayCommandObservation.Create(run, null, new(Guid.Parse("73382df2-f309-49fd-b960-230fa11287e0"), "TEST", 0, 0)).Value;
        var input = CanonicalJson.ComputeHash(run); var hashes = new HashSet<string>();
        for (var i = 0; i < 5; i++)
        {
            var cursor = new GameplayRunObservation(initial); cursor.Bind(executor);
            var flows = new ObservedRunCardZoneFlows(new CardZoneFlowExecutor(), cursor);
            var picked = CardSelectionTransitions.Pick(run, selectionId, ["card"], flows);
            Assert.True(picked.IsSuccess, picked.IsFailure ? picked.Error : null);
            Assert.Single(picked.Value.State.Deck.Topology.Instances);
            Assert.True(Assert.Single(picked.Value.State.CardSelections).Completed);
            Assert.Equal(12, picked.Value.State.PlayerEntity!.Component<ResourceEntityComponentState>()!.State.Get("focus")!.Current);
            var created = Assert.Single(cursor.Current.Events.Occurrences, fact => fact.Kind == "card.created");
            var healed = Assert.Single(cursor.Current.Events.Occurrences, fact => fact.Kind == "resource.change.resolved");
            Assert.Equal(created.EventId, healed.ParentEventId);
            hashes.Add(CanonicalJson.ComputeHash(new { picked.Value.State, cursor.Current.Events.Occurrences }));
        }
        Assert.Single(hashes); Assert.Empty(initial.Events.Occurrences); Assert.Equal(input, CanonicalJson.ComputeHash(run));
    }
}

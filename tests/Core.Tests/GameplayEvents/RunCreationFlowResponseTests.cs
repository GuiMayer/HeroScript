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

public sealed class RunCreationFlowResponseTests
{
    [Fact]
    public void StartupFlowKeepsItsResponsesAndDoesNotRepeatGenesisPlacement()
    {
        var context = GameplayResponseCompilationTests.Context(); var hero = context.Combat!.GetActor("hero")!;
        var response = GameplayResponseCompilationTests.Definition() with { Observe = new() { Kinds = new() { Include = ["card.moved"] } },
            Bindings = new() { Target = new() { Kind = ResponseActorBindingKind.Owner } },
            Effects = [EffectSequenceBudgetTests.Damage(2, 1) with { Type = EffectType.HEAL, Target = EffectTarget.SELF }] };
        var graph = new CardZoneSystemDefinition { CardZoneSystemId = "test", Zones = [new() {
            ZoneId = "collection", OwnerScope = CardZoneOwnerScope.RunOwner, Ordering = CardZoneOrdering.Ordered }, new() {
            ZoneId = "hand", OwnerScope = CardZoneOwnerScope.RunOwner, Ordering = CardZoneOrdering.Ordered }], Flows = [new() {
            FlowId = "start", Triggers = ["run.started"], AllowedInvocations = [CardZoneFlowInvocation.Boundary], Steps = [new() {
                StepId = "move", Operation = CardZoneOperation.Move, SourceZoneId = "collection", TargetZoneId = "hand",
                SourceOwner = CardZoneOwnerBinding.RunOwner, TargetOwner = CardZoneOwnerBinding.RunOwner,
                Selection = new() { Strategy = CardZoneSelectionStrategy.First, Count = 1 } }] }] };
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
        var definition = new RunDefinition { InitialCardZoneId = "collection", InitialCardOwner = CardZoneOwnerBinding.RunOwner };
        var system = CardZoneSystemCompiler.Compile(graph).Value;
        var initialized = CardZoneRunInitializer.Initialize(system, definition, [new() { DefinitionId = "card" }], "$run", "hero",
            [], 1, "revision", "default", run.Determinism, new CardZoneFlowExecutor(), applyInitialFlows: false);
        Assert.True(initialized.IsSuccess, initialized.IsFailure ? initialized.Error : null);
        Assert.Empty(initialized.Value.InitialFlowSteps);
        run = run with { Deck = new() { Topology = initialized.Value.State }, Determinism = initialized.Value.Context };
        var effects = new EffectTriggerExecutor(Mock.Of<IRuntimeFormulaEvaluator>(), new ImmutableEffectProcessor(), resolver.Object,
            new CalculationEngine(), new CompositeCalculationInfluenceProvider([new EntityResourceInfluenceProvider()]));
        var executor = new RunGameplayOccurrenceExecutor(effects);
        var initial = GameplayCommandObservation.Create(run, null, new(Guid.Parse("73382df2-f309-49fd-b960-230fa11287e0"), "TEST", 0, 0)).Value;
        var input = CanonicalJson.ComputeHash(run); var hashes = new HashSet<string>();
        for (var i = 0; i < 5; i++)
        {
            var cursor = new GameplayRunObservation(initial); cursor.Bind(executor);
            var flows = new ObservedRunCardZoneFlows(new CardZoneFlowExecutor(), cursor);
            var picked = RunCreationFlows.Execute(run, definition, 1, system, flows);
            Assert.True(picked.IsSuccess, picked.IsFailure ? picked.Error : null);
            Assert.Single(picked.Value.Deck.Topology.Instances);
            Assert.Equal(12, picked.Value.PlayerEntity!.Component<ResourceEntityComponentState>()!.State.Get("focus")!.Current);
            var moved = Assert.Single(cursor.Current.Events.Occurrences, fact => fact.Kind == "card.moved");
            var healed = Assert.Single(cursor.Current.Events.Occurrences, fact => fact.Kind == "resource.change.resolved");
            Assert.Equal(moved.EventId, healed.ParentEventId);
            hashes.Add(CanonicalJson.ComputeHash(new { picked.Value, cursor.Current.Events.Occurrences }));
        }
        Assert.Single(hashes); Assert.Empty(initial.Events.Occurrences); Assert.Equal(input, CanonicalJson.ComputeHash(run));
    }
}

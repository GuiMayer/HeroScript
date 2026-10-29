using System.Collections.Immutable;
using System.Text.Json;
using Core.CardZones;
using Core.Combat.Flow;
using Core.Combat.Models;
using Core.Combat.TurnOrder;
using Core.Combat.TurnPhase;
using Core.Common;
using Core.Content;
using Core.Determinism;
using Core.Effects;
using Core.Math;
using Core.Run;
using Core.GameplayEvents.Responses;
using Core.Run.Content;
using Core.Tests.Combat.Flow;
using Moq;
using Xunit;

namespace Core.Tests.GameplayEvents;

public sealed class ResourceRefreshDeckResponseTests
{
    [Fact]
    public void CardMovementFromRefreshResponseSurvivesTheNextActivationDeckFlow()
    {
        var (run, combat) = GameplayResponseContinuationTests.Fixture("resource.change.resolved");
        var watcher = (EntityResponseComponentState)combat.GetActor("ally")!.Components["watch"];
        combat = combat.ReplaceActor(combat.GetActor("ally")! with { Components = combat.GetActor("ally")!.Components
            .ToImmutableDictionary(StringComparer.Ordinal).Remove("watch") });
        var graph = new CardZoneSystemDefinition { CardZoneSystemId = "test", Zones = [
            new() { ZoneId = "reserve", OwnerScope = CardZoneOwnerScope.RunOwner, Ordering = CardZoneOrdering.Ordered },
            new() { ZoneId = "active", OwnerScope = CardZoneOwnerScope.RunOwner, Ordering = CardZoneOrdering.Ordered }],
            Flows = [new() { FlowId = "move", AllowedInvocations = [CardZoneFlowInvocation.Effect], Steps = [new() {
                StepId = "move", Operation = CardZoneOperation.Move, SourceZoneId = "reserve", TargetZoneId = "active",
                SourceOwner = CardZoneOwnerBinding.RunOwner, TargetOwner = CardZoneOwnerBinding.RunOwner,
                Selection = new() { Strategy = CardZoneSelectionStrategy.First, Count = 1 } }] }] };
        var seeded = CardZoneBootstrapper.Create(CardZoneSystemCompiler.Compile(graph).Value,
            new() { RunOwnerId = "$run", Batches = [new() { ZoneId = "reserve", OwnerId = "$run", DefinitionIds = ["card"] }] }, run.Determinism).Value;
        run = run with { Deck = new() { Topology = seeded.State }, Determinism = seeded.Context, ResolvedMode = new() { CardZoneSystem = graph } };
        var runtime = ContentRuntime.Create(new() { Manifest = new() { ConfigName = "default", Revision = "revision", Artifacts = [new()
            { Kind = "cards", Path = "cards/test.json", DefinitionCount = 1 }] }, Artifacts = ImmutableDictionary<string, JsonElement>.Empty.Add("cards/test.json",
            JsonSerializer.SerializeToElement(new Dictionary<string, CardContentDefinition> { ["card"] = new() { CardId = "card",
                Components = [new CardConditionComponentDefinition { ComponentId = "condition", Expression = "1" }] } })) });
        Assert.True(runtime.IsSuccess, runtime.IsFailure ? runtime.Error : null);
        var resolver = new Mock<IContentRuntimeResolver>();
        resolver.Setup(item => item.Resolve("revision", "default")).Returns(Result<ContentRuntime>.Success(runtime.Value));
        var formulas = Mock.Of<IRuntimeFormulaEvaluator>(); var flows = new CardZoneFlowExecutor();
        var triggers = new EffectTriggerExecutor(formulas, new ImmutableEffectProcessor(), resolver.Object, cardZoneFlows: flows);
        var boundaries = new CombatBoundaryExecutor(new TurnOrderResolver(formulas), new CombatStatusLifecycle(triggers),
            new CombatRelicLifecycle(triggers), new CombatResourceLifecycle(triggers), new PhaseGraphReducer(formulas, triggers),
            new CombatOutcomeResolver(), flows, occurrences: triggers);
        var policies = CombatFlowPlannerTests.Policies() with { ResourceCycle = new() {
            ResourceId = "focus", ActorScope = FlowActorScope.All, StartActivation = ResourceRefreshStrategy.Set, Amount = 3 } };
        var initialized = boundaries.InitializeTransaction(run, combat, CombatFlowPlannerTests.Sequence(), policies, CombatFlowPlannerTests.TurnPolicy());
        Assert.True(initialized.IsSuccess, initialized.IsFailure ? initialized.Error : null);
        run = initialized.Value.Run; combat = initialized.Value.Combat;
        var ally = combat.GetActor("ally")!;
        combat = combat.ReplaceActor(ally with { Components = ally.Components.ToImmutableDictionary(StringComparer.Ordinal).Add("watch",
            watcher with { Responses = [watcher.Responses!.Value[0] with { Effects = [new() { Type = EffectType.CARD_ZONE_FLOW, CardZoneFlowId = "move" }] }] }) });
        var input = CanonicalJson.ComputeHash(new { run, combat }); var hashes = new HashSet<string>();
        for (var i = 0; i < 5; i++)
        {
            var result = boundaries.AdvanceActivation(run, combat, run.Deck, run.Determinism,
                CombatFlowPlannerTests.Sequence(), policies, CombatFlowPlannerTests.TurnPolicy());
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
            Assert.Empty(result.Value.Deck.Topology.GetZone("reserve", "$run")!.InstanceIds);
            Assert.Single(result.Value.Deck.Topology.GetZone("active", "$run")!.InstanceIds);
            hashes.Add(CanonicalJson.ComputeHash(result.Value));
        }
        Assert.Single(hashes); Assert.Equal(input, CanonicalJson.ComputeHash(new { run, combat }));
    }
}

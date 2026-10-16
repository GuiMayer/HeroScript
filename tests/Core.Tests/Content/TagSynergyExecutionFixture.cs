using System.Collections.Immutable;
using Core.Calculations;
using Core.CardZones;
using Core.Combat;
using Core.Combat.Models;
using Core.Common;
using Core.Config;
using Core.Content;
using Core.Determinism;
using Core.Effects;
using Core.Logging;
using Core.Math;
using Core.Resources;
using Core.Run;
using Core.Run.Content;
using Moq;
using Xunit;

namespace Core.Tests.Content;

internal sealed record TagSynergyExecutionFixture(ContentRuntime Runtime,
    IRuntimeFormulaEvaluator Formulas, EffectTriggerExecutor Executor, RunState Run, CombatState Combat)
{
    internal static async Task<TagSynergyExecutionFixture> Create(float health = 50, float energy = 3,
        float block = 0, float critical = 0, float armor = 0)
    {
        var runtime = await AscendantWorldOneContentTests.Compile();
        var runtimes = new Mock<IContentRuntimeResolver>();
        runtimes.Setup(value => value.Resolve(runtime.Manifest.Revision, It.IsAny<string?>()))
            .Returns(Result<ContentRuntime>.Success(runtime));
        var math = new MathEngine(Mock.Of<IConfigManager>(), new FormulaLoader(Mock.Of<IResourceLoader>()), NullLogger.Instance);
        var formulas = new RuntimeFormulaEvaluator(math, new ExpressionEvaluator(NullLogger.Instance), NullLogger.Instance, runtimes.Object);
        var providers = new CompositeCalculationInfluenceProvider([new EntityStatInfluenceProvider(),
            new EntityResourceInfluenceProvider(), new CardComponentInfluenceProvider(formulas)]);
        var context = DeterministicContext.Create(123, runtime.Manifest.Revision);
        CombatActorState Actor(string id, string side, float hp, float ep, float bp) => new()
        {
            InstanceId = id, SideId = side,
            Components = new Dictionary<string, EntityComponentState> { ["stats"] = new StatEntityComponentState
            { ComponentId = "stats", Values = new Dictionary<string, float> {
                ["critical_chance"] = critical, ["critical_damage"] = 1, ["armor"] = armor,
                ["weapon_damage"] = 0, ["block_efficiency"] = 0 } } },
            ResourceState = new() { OwnerId = id, Resources = new Dictionary<string, ResourcePool> {
                ["health"] = Pool("health", hp, 100), ["energy"] = Pool("energy", ep, 3),
                ["block"] = Pool("block", bp, 999) } }
        };
        var run = new RunState { RunId = Guid.Parse("51000000-0000-4000-8000-000000000001"),
            PlayerEntityId = "hero", ConfigName = "ascendant", Determinism = context,
            ResolvedMode = new() { Definition = runtime.GetDefinition<GameModeDefinition>("modes", "ascendant_world_one").Value,
                CardZoneSystem = runtime.GetDefinition<CardZoneSystemDefinition>("card-zone-systems", "w1_zones").Value } };
        var combat = new CombatState { CombatId = Guid.Parse("52000000-0000-4000-8000-000000000001"),
            Determinism = context, Actors = new Dictionary<string, CombatActorState> {
                ["hero"] = Actor("hero", "player", health, energy, block),
                ["enemy"] = Actor("enemy", "enemy", 100, 0, 0),
                ["enemy2"] = Actor("enemy2", "enemy", 100, 0, 0) } };
        return new(runtime, formulas, new(formulas, new ImmutableEffectProcessor(), runtimes.Object,
            new CalculationEngine(formulas), providers, cardZoneFlows: new CardZoneFlowExecutor(
                new CardZoneRuntimeRuleEvaluator(formulas, new RevisionedCardZoneCardMetadataResolver(
                    runtimes.Object, new CardContentCompiler(), new EffectiveCardResolver())))), run, combat);
    }

    internal static ResourcePool Pool(string id, float current, float maximum) =>
        ResourcePool.Materialize(new ResourceDefinition { ResourceId = id, DisplayName = id }, current, maximum);
    internal EffectiveCardDefinition Card(string id) => new EffectiveCardResolver().Resolve(
        new CardContentCompiler().Compile(id, Runtime).Value, new() {
            DefinitionId = id, CardInstanceId = Guid.Parse("53000000-0000-4000-8000-000000000001") }).Value;

    internal Result<EffectBatchResult> Play(string id, CombatState? state = null, string[]? targets = null)
    {
        var card = Card(id);
        var snapshot = state ?? Combat;
        targets ??= card.All<CardTargetingComponentDefinition>().Any(x => x.MinimumTargets > 0) ? ["enemy"] : [];
        var evaluation = new CardPlayEvaluator(new ActionCostEvaluator(Formulas), Formulas)
            .Evaluate(card, snapshot, new() { ActorId = "hero", SelectedTargetIds = targets,
                ContentRevision = Runtime.Manifest.Revision });
        if (evaluation.IsFailure) return Result<EffectBatchResult>.Failure(evaluation.Error);
        if (!evaluation.Value.IsLegal) return Result<EffectBatchResult>.Failure(string.Join("; ", evaluation.Value.FailureReasons));
        var provenance = new EffectProvenance { SourceId = card.CardInstanceId.ToString() };
        return Executor.Execute(new() { Run = Run, Combat = snapshot, Card = card,
            ContentRevision = Runtime.Manifest.Revision, OwnerEntityId = "hero", SourceEntityId = "hero",
            Tags = card.Tags.ToImmutableHashSet(StringComparer.Ordinal), SelectedTargetEntityIds = targets,
            PrefixCommands = ActionEffectCosts.Compile(evaluation.Value, "hero", provenance),
            Trigger = new() { TriggerId = id, Effects = card.All<CardEffectComponentDefinition>().Select(x => x.Effect).ToArray() } });
    }

    internal static float Value(CombatState state, string resource, string actor = "hero") =>
        state.GetActor(actor)!.ResourceState.Resources[resource].Current;
}

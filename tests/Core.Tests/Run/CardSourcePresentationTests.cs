using System.Collections.Immutable;
using Core.Calculations;
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
using Core.Tests.Content;
using Moq;
using Xunit;

namespace Core.Tests.Run;

public sealed class CardSourcePresentationTests
{
    private sealed record Fixture(ContentRuntime Runtime, CardSourcePresentationProjector Projector, EffectTriggerExecutor Executor,
        CardPresentationFacts Base, EffectiveCardDefinition Card, RunState Run, CombatState Combat);
    private static async Task<Fixture> Create(string cardId = "w1_strike", float weapon = .5f)
    {
        var runtime = await AscendantWorldOneContentTests.Compile();
        var runtimes = new Mock<IContentRuntimeResolver>();
        runtimes.Setup(value => value.Resolve(runtime.Manifest.Revision, It.IsAny<string?>())).Returns(Result<ContentRuntime>.Success(runtime));
        var math = new MathEngine(Mock.Of<IConfigManager>(), new FormulaLoader(Mock.Of<IResourceLoader>()), NullLogger.Instance);
        var formulas = new RuntimeFormulaEvaluator(math, new ExpressionEvaluator(NullLogger.Instance), NullLogger.Instance, runtimes.Object);
        var engine = new CalculationEngine(formulas);
        var providers = new CompositeCalculationInfluenceProvider([new EntityStatInfluenceProvider(), new EntityResourceInfluenceProvider(), new CardComponentInfluenceProvider(formulas)]);
        var card = new CardContentCompiler().Compile(cardId, runtime).Value;
        var effective = new EffectiveCardResolver().Resolve(card, new() { DefinitionId = cardId,
            CardInstanceId = Guid.Parse("40000000-0000-4000-8000-000000000001") }).Value;
        var determinism = DeterministicContext.Create(123, runtime.Manifest.Revision);
        var run = new RunState { RunId = Guid.NewGuid(), PlayerEntityId = "hero", ConfigName = "ascendant", Determinism = determinism,
            ResolvedMode = new() { Definition = runtime.GetDefinition<GameModeDefinition>("modes", "ascendant_world_one").Value } };
        var hero = new CombatActorState { InstanceId = "hero", SideId = "player", Components = new Dictionary<string, EntityComponentState> {
            ["stats"] = new StatEntityComponentState { ComponentId = "stats", Values = new Dictionary<string, float> {
                ["weapon_damage"] = weapon, ["critical_chance"] = 0 } } }, ResourceState = new() { OwnerId = "hero", Resources = new Dictionary<string, ResourcePool> {
                ["energy"] = ResourcePool.Materialize(new ResourceDefinition { ResourceId = "energy", DisplayName = "Energy" }, 0, 3) } } };
        var enemy = new CombatActorState { InstanceId = "enemy", SideId = "enemy", ResourceState = new() { OwnerId = "enemy", Resources = new Dictionary<string, ResourcePool> {
            ["health"] = ResourcePool.Materialize(new ResourceDefinition { ResourceId = "health", DisplayName = "Health" }, 1000, 1000),
            ["block"] = ResourcePool.Materialize(new ResourceDefinition { ResourceId = "block", DisplayName = "Block" }, 100, 100) } } };
        var combat = new CombatState { CombatId = Guid.NewGuid(), Determinism = determinism,
            Actors = new Dictionary<string, CombatActorState> { ["hero"] = hero, ["enemy"] = enemy } };
        var baseline = CardBasePresentationProjector.Project(card, effective, new() { Kind = CardPresentationSubjectKind.Instance,
            DefinitionId = cardId, CardInstanceId = effective.CardInstanceId }, new() { ContentRevision = runtime.Manifest.Revision }).Value;
        return new(runtime, new(runtimes.Object, formulas, engine, providers), new(formulas, new ImmutableEffectProcessor(), runtimes.Object, engine, providers),
            baseline, effective, run, combat);
    }

    [Theory]
    [InlineData("w1_strike")] [InlineData("w1_flurry")]
    public async Task SourceCaptureMatchesExecutionBudgetEvenWithNoEnergyAndTargetBlock(string id)
    {
        var fixture = await Create(id);
        var before = CanonicalJson.ComputeHash(new { fixture.Run, fixture.Combat, fixture.Card });
        var result = fixture.Projector.Project(fixture.Base, fixture.Card, fixture.Run, fixture.Combat, "hero");
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        var numeric = result.Value.Effects[0].Parameters.Single(value => value.Parameter == EffectNumericParameter.Amount).Values;
        Assert.NotNull(numeric.Source);
        Assert.True(numeric.Source > numeric.ComposedBase);
        var explanation = result.Value.Explanations[0];
        Assert.Contains(explanation.Attributes, attribute => attribute.AttributeId == "weapon_damage" && attribute.Eligible);
        Assert.Contains(explanation.Sources, source => source.AttributeId == "weapon_damage" && source.Value == .5f);
        Assert.All(explanation.Buckets, bucket => Assert.Equal("w1_origin", bucket.StageId));
        var executed = fixture.Executor.Execute(new() { Run = fixture.Run, Combat = fixture.Combat, Card = fixture.Card,
            ContentRevision = fixture.Run.Determinism.ContentRevision, OwnerEntityId = "hero", SourceEntityId = "hero",
            Tags = fixture.Card.Tags.ToImmutableHashSet(StringComparer.Ordinal), SelectedTargetEntityIds = ["enemy"],
            Trigger = new() { TriggerId = "main", Effects = fixture.Card.All<CardEffectComponentDefinition>().Select(value => value.Effect).ToArray() } });
        Assert.True(executed.IsSuccess, executed.IsFailure ? executed.Error : null);
        Assert.Equal(numeric.Source, executed.Value.Steps.SelectMany(step => step.SequenceBudgets).First().Capture.Value);
        var withImpacts = CardImpactExplanationProjector.Attach(result.Value, fixture.Card, fixture.Run, fixture.Combat,
            fixture.Runtime, executed.Value.Steps, "ExactForCapturedSnapshotAndInput");
        Assert.Equal(CanonicalJson.ComputeHash(result.Value.Effects), CanonicalJson.ComputeHash(withImpacts.Effects));
        Assert.NotEmpty(withImpacts.Impacts);
        Assert.All(withImpacts.Impacts, impact => Assert.Equal("enemy", impact.TargetEntityId));
        Assert.Equal(before, CanonicalJson.ComputeHash(new { fixture.Run, fixture.Combat, fixture.Card }));
    }

    [Fact]
    public async Task ZeroEligibleAttributesAreVisibleAndAnotherContextCannotReuseFacts()
    {
        var fixture = await Create(weapon: 0);
        var result = fixture.Projector.Project(fixture.Base, fixture.Card, fixture.Run, fixture.Combat, "hero").Value;
        var attribute = result.Explanations[0].Attributes.Single(value => value.AttributeId == "weapon_damage");
        Assert.True(attribute.Eligible); Assert.Equal(0, attribute.CurrentValue);
        Assert.Equal(result.Effects[0].Parameters[0].Values.ComposedBase, result.Effects[0].Parameters[0].Values.Source);
        Assert.True(fixture.Projector.Project(fixture.Base with { Version = fixture.Base.Version with { ContentRevision = "other" } },
            fixture.Card, fixture.Run, fixture.Combat, "hero").IsFailure);
        Assert.True(fixture.Projector.Project(fixture.Base, fixture.Card, fixture.Run, fixture.Combat, "missing").IsFailure);
    }
}

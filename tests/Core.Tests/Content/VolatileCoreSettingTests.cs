using Core.Config;
using Core.Content;
using Core.Run.Content;
using Core.Run;
using Core.Combat.Models;
using Core.Combat;
using Core.Calculations;
using Core.Common;
using Core.Determinism;
using Core.Effects;
using Core.Entity.Definitions;
using Core.Logging;
using Core.Math;
using Core.Resources;
using System.Collections.Immutable;
using System.Text.Json;
using Moq;
using Mods;
using Xunit;

namespace Core.Tests.Content;

public sealed class VolatileCoreSettingTests
{
    [Fact]
    public async Task UnusedContinuationUpgradesMustReferencePublishedOverflowProfiles()
    {
        var bundle = (await Compile()).Bundle;
        var artifact = bundle.Artifacts.First(pair => pair.Value.ValueKind == JsonValueKind.Object &&
            pair.Value.TryGetProperty("core_cascade", out _));
        var json = System.Text.Json.Nodes.JsonNode.Parse(artifact.Value.GetRawText())!;
        json["core_cascade"]!["patches"]![0]!["continuation"]!["overflowPipelineId"] = "unpublished";
        var invalid = bundle with { Artifacts = bundle.Artifacts.SetItem(artifact.Key,
            JsonSerializer.SerializeToElement(json)) };
        var runtime = ContentRuntime.Create(invalid);
        Assert.True(runtime.IsSuccess, runtime.IsFailure ? runtime.Error : null);
        var errors = ImmutableArray.CreateBuilder<string>();
        new GameplayContentValidator(runtime.Value, errors).Validate();
        Assert.Contains(errors, error => error.Contains("unpublished"));
    }

    [Fact]
    public async Task ShippedSettingPublishesExecutableCardsAndAnIsolatedCoreJourney()
    {
        var compilation = await Compile();
        var validation = new ContentGraphValidator().Validate(compilation.Bundle);
        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Errors));
        var runtime = ContentRuntime.Create(compilation.Bundle).Value;
        foreach (var cardId in new[] { "core_strike", "core_guard", "core_heal", "core_charge_card", "core_release", "core_recover", "core_empower_card" })
        {
            var compiled = new CardContentCompiler().Compile(cardId, runtime);
            Assert.True(compiled.IsSuccess, compiled.IsFailure ? compiled.Error : null);
        }
        Assert.Equal("volatile-core", compilation.Bundle.Manifest.ConfigName);
        Assert.Equal("core_volatile", compilation.Setting.Launch!.ModeId);
    }

    internal static async Task<SettingCompilation> Compile()
    {
        var root = ResourceProviderFactory.FindProjectRoot();
        Assert.NotNull(root);
        var result = await new SettingCompiler([new DirectoryPackageProvider("shipped", Path.Combine(root!, "data", "configs"))]).CompileAsync("volatile-core");
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        return result.Value;
    }

    [Fact]
    public async Task AuthoredAffinityMultiHitUpgradeAndCascadePreserveIdentityAndConserveMagnitude()
    {
        var fixture = await Fixture();
        var card = new CardInstanceState { CardInstanceId = Guid.Parse("10000000-0000-8000-8000-000000000011"), DefinitionId = "core_strike" };
        var instance = card;
        foreach (var id in new[] { "core_sharpen", "core_ember", "core_multihit", "core_cascade" })
        {
            var definition = fixture.Runtime.GetDefinition<CardUpgradeDefinition>("card-upgrades", id).Value;
            var sealedDefinition = CardBundleCompiler.Seal(definition, fixture.Runtime);
            Assert.True(sealedDefinition.IsSuccess, sealedDefinition.IsFailure ? sealedDefinition.Error : null);
            instance = CardInstanceUpgradeTransitions.Apply(instance, sealedDefinition.Value, fixture.Runtime.Manifest.Revision).Value;
        }
        var compiled = new CardContentCompiler().Compile("core_strike", fixture.Runtime).Value;
        var effective = new EffectiveCardResolver().Resolve(compiled, instance);
        Assert.True(effective.IsSuccess, effective.IsFailure ? effective.Error : null);
        Assert.Equal(card.CardInstanceId, instance.CardInstanceId);
        var main = effective.Value.All<CardEffectComponentDefinition>()[0];
        Assert.Equal(13, main.Effect.Parameters[0].FlatValue);
        Assert.Equal(3, main.Effect.Repeat);
        Assert.NotNull(main.Effect.Continuation);
        var result = fixture.Executor.Execute(Request(fixture, effective.Value, main.Effect));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(15, result.Value.Steps.SelectMany(step => step.SequenceBudgets).First().Capture.Value);
        var healthApplications = result.Value.Records.Where(record => record.ResourceId == "health" && record.CalculationInfluenceId == null).ToArray();
        Assert.Equal(15d, healthApplications.Sum(record => -record.ResourceOutcome!.AppliedChange));
        Assert.Contains(result.Value.Steps, step => step.Continuation?.ToEntityId == "enemy_1");
        Assert.Equal(9, result.Value.State.GetActor("enemy_1")!.GetResource("health")!.Current);
        Assert.Empty(card.Upgrades);
    }

    [Theory]
    [InlineData("core_release", "health")]
    [InlineData("core_recover", "health")]
    [InlineData("core_empower_card", null)]
    public async Task PublishedCondensationSupportsDamageHealingAndNonResourceStacks(string cardId, string? resourceId)
    {
        var fixture = await Fixture();
        var charge = new CardContentCompiler().Compile("core_charge_card", fixture.Runtime).Value;
        var effective = new EffectiveCardResolver().Resolve(charge, new() { DefinitionId = "core_charge_card" }).Value;
        var prepared = fixture.Executor.Execute(Request(fixture, effective, effective.All<CardEffectComponentDefinition>()[0].Effect));
        Assert.True(prepared.IsSuccess, prepared.IsFailure ? prepared.Error : null);
        var compiled = new CardContentCompiler().Compile(cardId, fixture.Runtime).Value;
        var procCard = new EffectiveCardResolver().Resolve(compiled, new() { DefinitionId = cardId }).Value;
        var request = Request(fixture, procCard, procCard.All<CardEffectComponentDefinition>()[0].Effect) with
        { Combat = prepared.Value.State, Run = prepared.Value.Run, SelectedTargetEntityIds = cardId == "core_release" ? ["enemy_1"] : ["player"] };
        var result = fixture.Executor.Execute(request);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Single(result.Value.Records, record => record.Condensation != null);
        Assert.Empty(result.Value.State.StatusEffects.GetValueOrDefault("player", []));
        if (resourceId == null) Assert.Equal(2, Assert.Single(result.Value.Run!.Modifiers).Stacks);
        else Assert.Contains(result.Value.Records, record => record.ResourceId == resourceId);
        Assert.Single(result.Value.Steps.Select(step => step.Identity!.ProcId).Distinct());
    }

    [Fact]
    public async Task DefaultAndVolatileSettingsCompileToIndependentPinnedRevisions()
    {
        var root = ResourceProviderFactory.FindProjectRoot()!;
        var compiler = new SettingCompiler([new DirectoryPackageProvider("shipped", Path.Combine(root, "data", "configs"))]);
        var first = await compiler.CompileAsync("default");
        var core = await compiler.CompileAsync("volatile-core");
        var second = await compiler.CompileAsync("default");
        Assert.True(first.IsSuccess); Assert.True(core.IsSuccess); Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.Bundle.Manifest.Revision, second.Value.Bundle.Manifest.Revision);
        Assert.NotEqual(first.Value.Bundle.Manifest.Revision, core.Value.Bundle.Manifest.Revision);
        Assert.DoesNotContain("core_strike", ContentRuntime.Create(first.Value.Bundle).Value.GetDefinitions("cards").Keys);
    }

    internal sealed record Context(ContentRuntime Runtime, RunState Run, CombatState Combat, EffectTriggerExecutor Executor);
    internal static async Task<Context> Fixture()
    {
        var runtime = ContentRuntime.Create((await Compile()).Bundle).Value;
        var runtimes = new Mock<IContentRuntimeResolver>();
        runtimes.Setup(service => service.Resolve(runtime.Manifest.Revision, "volatile-core")).Returns(Result<ContentRuntime>.Success(runtime));
        var formulas = new RuntimeFormulaEvaluator(Mock.Of<IMathEngine>(), new ExpressionEvaluator(NullLogger.Instance), NullLogger.Instance, runtimes.Object);
        var executor = new EffectTriggerExecutor(formulas, new ImmutableEffectProcessor(), runtimes.Object, new CalculationEngine(formulas),
            new CompositeCalculationInfluenceProvider([new CardComponentInfluenceProvider(formulas), new EntityResourceInfluenceProvider(),
                new EntityStatInfluenceProvider(), new RunModifierInfluenceProvider(formulas)]));
        var player = PersistentPlayerTransitions.Create("player", "core_adept", runtime).Value;
        var actors = new[] { Actor("player", "core_adept", "player"), Actor("enemy_0", "core_spark", "opposition"), Actor("enemy_1", "core_echo", "opposition") };
        var context = DeterministicContext.Create(11, runtime.Manifest.Revision);
        var run = new RunState { ConfigName = "volatile-core", PlayerEntityId = "player", PlayerEntity = player, Determinism = context,
            ResolvedMode = new() { Definition = runtime.GetDefinition<GameModeDefinition>("modes", "core_volatile").Value } };
        var combat = CombatTransitions.Create(actors, context);
        combat = PersistentPlayerTransitions.Materialize(run, combat).Value;
        var enemy = combat.GetActor("enemy_0")!;
        combat = combat.ReplaceActor(enemy.ApplyResourceMutation("setup", "health", ResourceMutationOperation.Set, 4).Value);
        enemy = combat.GetActor("enemy_1")!;
        combat = combat.ReplaceActor(enemy.ApplyResourceMutation("setup", "health", ResourceMutationOperation.Set, 20).Value);
        return new(runtime, run, combat, executor);

        CombatActorState Actor(string id, string definitionId, string side)
        {
            var definition = runtime.GetDefinition<EntityDefinition>("entities", definitionId).Value;
            var resources = definition.Component<ResourceEntityComponentDefinition>()!;
            return new() { InstanceId = id, DefinitionId = definitionId, ContentRevision = runtime.Manifest.Revision,
                SideId = side, ControllerBinding = new() { Kind = id == "player" ? ControllerKind.Player : ControllerKind.AI },
                ResourceState = new() { OwnerId = id, Resources = resources.Pools.ToDictionary(pair => pair.Key, pair =>
                    ResourcePool.Materialize(runtime.GetDefinition<ResourceDefinition>("resources", pair.Key).Value, pair.Value.Current, pair.Value.Max)) } };
        }
    }
    internal static EffectTriggerExecutionRequest Request(Context fixture, EffectiveCardDefinition card, EffectDefinition effect) => new()
    { Combat = fixture.Combat, Run = fixture.Run, OwnerEntityId = "player", SourceEntityId = "player", SelectedTargetEntityIds = ["enemy_0"],
        ContentRevision = fixture.Runtime.Manifest.Revision, Card = card, Tags = card.Tags.ToImmutableHashSet(),
        Provenance = new() { Kind = EffectProvenanceKind.Card, SourceId = card.DefinitionId }, Trigger = new() { TriggerId = "main", Effects = [effect] } };
}

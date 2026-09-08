using System.Collections.Immutable;
using System.Text.Json;
using Core.Calculations;
using Core.Combat.Models;
using Core.Combat.Modifiers;
using Core.Common;
using Core.Content;
using Core.Determinism;
using Core.Effects;
using Core.Math;
using Core.Run;
using Moq;
using Xunit;

namespace Core.Tests.Effects;

public sealed class RunEffectTransactionTests
{
    [Fact]
    public void DeckEffectsAreSequentialAndFailureDiscardsTheWholeRunSnapshot()
    {
        var run = Run();
        var draw = new EffectDefinition { Type = EffectType.DRAW_CARD, Target = EffectTarget.SELF, CardCount = 2 };
        var discard = new EffectDefinition { Type = EffectType.DISCARD_CARD, Target = EffectTarget.SELF, CardCount = 1 };
        var request = EffectTransactionTests.Request(draw, discard) with { Run = run };
        var result = EffectTransactionTests.Executor().Execute(request);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Single(result.Value.Run!.Deck.HandInstanceIds);
        Assert.Single(result.Value.Run.Deck.DiscardPileInstanceIds);
        Assert.Empty(run.Deck.HandInstanceIds);
        Assert.NotEqual(result.Value.Steps[0].RunBeforeHash, result.Value.Steps[0].RunAfterHash);
        var invalid = EffectTransactionTests.Request(draw, discard, new()
            { Type = EffectType.DAMAGE, Target = EffectTarget.SELF, TargetResource = "missing", FlatValue = 1 }) with { Run = run };
        var before = CanonicalJson.ComputeHash(run);
        Assert.True(EffectTransactionTests.Executor().Execute(invalid).IsFailure);
        Assert.Equal(before, CanonicalJson.ComputeHash(run));
    }

    [Fact]
    public void PartialDrawDoesNotThrowWhenFewerCardsExist()
    {
        var result = DeckTransitions.Draw(Run().Deck, 20, Run().Determinism, false, true);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(3, result.Value.Cards.Length);
    }

    [Fact]
    public void AppliedModifierAffectsTheNextCalculationAndNotEnemyActions()
    {
        var (executor, run) = WithModifierRuntime();
        var request = EffectTransactionTests.Request(new()
        { Type = EffectType.APPLY_MODIFIER, Target = EffectTarget.SELF, ModifierId = "power", ModifierStacks = 2 },
            EffectTransactionTests.Resource(EffectType.DAMAGE, 1)) with { Run = run };
        var result = executor.Execute(request);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(5, result.Value.State.GetActor("enemy")!.GetResource("focus")!.Current);
        var instance = Assert.Single(result.Value.Run!.Modifiers);
        Assert.Equal("revision", instance.ContentRevision);
        Assert.Equal("hero", instance.Owner.Id);
        var enemy = executor.Execute(EffectTransactionTests.Request(EffectTransactionTests.Resource(EffectType.DAMAGE, 1)) with
        {
            Run = result.Value.Run, Combat = result.Value.State, OwnerEntityId = "enemy", SourceEntityId = "enemy",
            SelectedTargetEntityIds = ["hero"]
        });
        Assert.True(enemy.IsSuccess, enemy.IsFailure ? enemy.Error : null);
        Assert.Equal(9, enemy.Value.State.GetActor("hero")!.GetResource("focus")!.Current);
        Assert.Empty(run.Modifiers);
    }

    [Fact]
    public void RemovingStacksFromMultipleModifierInstancesProducesCompleteOrderedTrace()
    {
        var owner = new GameplayOwner { Kind = GameplayOwnerKind.Entity, Id = "hero" };
        var firstId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var secondId = Guid.Parse("20000000-0000-0000-0000-000000000002");
        var definition = new ScriptModifierDefinition { ModifierId = "power" };
        var run = Run() with
        {
            Modifiers =
            [
                new() { InstanceId = firstId, ModifierId = "power", Definition = definition, Owner = owner, Stacks = 3 },
                new() { InstanceId = secondId, ModifierId = "power", Definition = definition, Owner = owner, Stacks = 1 }
            ]
        };
        var request = EffectTransactionTests.Request(new EffectDefinition
        {
            Type = EffectType.REMOVE_MODIFIER,
            Target = EffectTarget.SELF,
            ModifierId = "power",
            ModifierOwner = owner,
            ModifierStacks = 2
        }) with { Run = run };

        var result = EffectTransactionTests.Executor().Execute(request);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(1, Assert.Single(result.Value.Run!.Modifiers).Stacks);
        var application = Assert.Single(result.Value.Records);
        Assert.Equal(new[] { secondId }, application.RemovedModifierInstanceIds.ToArray());
        Assert.Equal(new[] { firstId, secondId }, application.ModifierStackChanges.Select(item => item.ModifierInstanceId).ToArray());
        Assert.Equal(new[] { 1, 0 }, application.ModifierStackChanges.Select(item => item.CurrentStacks).ToArray());
        Assert.Equal(new[] { false, true }, application.ModifierStackChanges.Select(item => item.Removed).ToArray());
    }

    [Theory]
    [InlineData(ModifierDurationBoundary.Command)]
    [InlineData(ModifierDurationBoundary.Activation)]
    [InlineData(ModifierDurationBoundary.Round)]
    [InlineData(ModifierDurationBoundary.Combat)]
    [InlineData(ModifierDurationBoundary.Node)]
    [InlineData(ModifierDurationBoundary.Run)]
    public void DurationTickIsImmutableAndScoped(ModifierDurationBoundary boundary)
    {
        var state = GameplayOwnershipTests.State();
        var applied = ModifierTransitions.Apply(Run(), new() { ModifierId = "short", DefaultDuration = 1, DurationBoundary = boundary },
            new() { Kind = GameplayOwnerKind.Entity, Id = "hero" }, "test").Value.Run;
        var ticked = ModifierTransitions.Tick(applied, boundary, state, "hero");
        Assert.Empty(ticked.Modifiers);
        Assert.Single(applied.Modifiers);
        if (boundary == ModifierDurationBoundary.Activation)
            Assert.Single(ModifierTransitions.Tick(applied, boundary, state, "enemy").Modifiers);
    }

    [Fact]
    public void TenMixedTransactionsHaveIdenticalStateAndTrace()
    {
        var (executor, run) = WithModifierRuntime();
        var request = EffectTransactionTests.Request(new()
            { Type = EffectType.DRAW_CARD, Target = EffectTarget.SELF, CardCount = 1 },
            new() { Type = EffectType.APPLY_MODIFIER, Target = EffectTarget.SELF, ModifierId = "power" },
            EffectTransactionTests.Resource(EffectType.DAMAGE, 1) with { Chance = .5f, Repeat = 4 }) with { Run = run };
        var results = Enumerable.Range(0, 10).Select(_ => executor.Execute(request)).ToArray();
        Assert.All(results, result => Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null));
        Assert.Single(results.Select(result => CanonicalJson.ComputeHash(result.Value)).Distinct());
    }

    private static RunState Run()
    {
        var deck = DeckTransitions.Create(["a", "b", "c"], DeterministicContext.Create(1, "revision")).Value;
        return new() { RunId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), PlayerEntityId = "hero", Deck = deck.State, Determinism = deck.Context };
    }

    private static (EffectTriggerExecutor Executor, RunState Run) WithModifierRuntime()
    {
        var modifier = new ScriptModifierDefinition { ModifierId = "power", Influences = [new()
            { InfluenceId = "power-flat", Channel = "effect_amount", Bucket = "flat", Value = 2 }] };
        var pipeline = new CalculationPipelineDefinition { PipelineId = "amount", Channel = "effect_amount", Buckets = [new() { BucketId = "flat" }] };
        var definitions = new (string Kind, string Path, object Value)[]
        {
            ("modifiers", "modifiers/script_modifiers.json", new Dictionary<string, ScriptModifierDefinition> { ["power"] = modifier }),
            ("calculation-pipelines", "calculation-pipelines/amount.json", new Dictionary<string, CalculationPipelineDefinition> { ["amount"] = pipeline })
        };
        var runtime = ContentRuntime.Create(new()
        {
            Manifest = new() { Revision = "revision", ConfigName = "default", Artifacts = definitions.Select(item => new ContentArtifactManifest
                { Kind = item.Kind, Path = item.Path, DefinitionCount = 1 }).ToArray() },
            Artifacts = definitions.ToImmutableDictionary(item => item.Path, item => JsonSerializer.SerializeToElement(item.Value))
        });
        Assert.True(runtime.IsSuccess, runtime.IsFailure ? runtime.Error : null);
        var runtimes = new Mock<IContentRuntimeResolver>();
        runtimes.Setup(item => item.Resolve("revision", "default")).Returns(Result<ContentRuntime>.Success(runtime.Value));
        var formulas = Mock.Of<IRuntimeFormulaEvaluator>();
        return (new(formulas, new ImmutableEffectProcessor(), runtimes.Object, new CalculationEngine(), new RunModifierInfluenceProvider(formulas)),
            Run() with { ResolvedMode = new() { Definition = new() { CalculationPipelineIds = ["amount"] } } });
    }
}

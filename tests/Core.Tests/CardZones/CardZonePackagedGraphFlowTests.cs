using System.Text.Json;
using Core.CardZones;
using Core.Determinism;
using Core.Common;
using Core.Math;
using Core.Run;
using Xunit;

namespace Core.Tests.CardZones;

public sealed class CardZonePackagedGraphFlowTests
{
    [Fact]
    public void RunInitializer_UsesAuthoredZoneAndInitialBoundaryAtPinnedRevision()
    {
        var system = Load("spire_zones");
        var formulas = new CountVariableFormulas();
        var executor = new CardZoneFlowExecutor(new CardZoneRuntimeRuleEvaluator(
            formulas, new UnusedCardMetadata()));
        var definition = new RunDefinition
        {
            InitialCardZoneId = "draw", InitialCardOwner = CardZoneOwnerBinding.RunOwner
        };
        var starting = new RunStartingCard[]
        {
            new() { DefinitionId = "spark", Upgrades = [new CardUpgradeState { UpgradeId = "boost" }] },
            new() { DefinitionId = "guard" },
            new() { DefinitionId = "spark" },
            new() { DefinitionId = "heal" }
        };

        var first = CardZoneRunInitializer.Initialize(system, definition, starting,
            "run-a", "hero", [], 2, "content-a", "default",
            DeterministicContext.Create(10, "content-a"), executor);
        var repeated = CardZoneRunInitializer.Initialize(system, definition, starting,
            "run-a", "hero", [], 2, "content-a", "default",
            DeterministicContext.Create(10, "content-a"), executor);

        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        Assert.Equal(first.Value.Fingerprint, repeated.Value.Fingerprint);
        Assert.Equal("content-a", formulas.LastRevision);
        Assert.Equal(2, first.Value.State.GetZone("hand", "run-a")!.InstanceIds.Count);
        Assert.Equal(2, first.Value.State.GetZone("draw", "run-a")!.InstanceIds.Count);
        Assert.Single(first.Value.InitialFlowSteps);
        Assert.Contains(first.Value.State.Instances.Values, card =>
            card.DefinitionId == "spark" && card.Upgrades.Any(upgrade => upgrade.UpgradeId == "boost"));
    }

    [Fact]
    public void RunInitializer_CanPlaceCardsInActorZoneWithoutAHand()
    {
        var system = Load("cooldown_zones");
        var initialized = CardZoneRunInitializer.Initialize(system, new RunDefinition
        {
            InitialCardZoneId = "inventory", InitialCardOwner = CardZoneOwnerBinding.ActiveActor
        }, [new RunStartingCard { DefinitionId = "skill" }],
            "run-a", "hero", [], 0, "content-a", "default",
            DeterministicContext.Create(10, "content-a"), new CardZoneFlowExecutor());

        Assert.True(initialized.IsSuccess, initialized.IsFailure ? initialized.Error : null);
        Assert.Single(initialized.Value.State.GetZone("inventory", "hero")!.InstanceIds);
        Assert.Empty(initialized.Value.InitialFlowSteps);
    }

    [Fact]
    public void SpireGraph_DrawPlayDiscardAndRecycle_IsRepeatable()
    {
        var first = RunSpire(73);
        var repeated = RunSpire(73);

        Assert.Equal(first.Fingerprint, repeated.Fingerprint);
        Assert.True(first.State.Instances.Keys.ToHashSet().SetEquals(repeated.State.Instances.Keys));
        Assert.Equal(5, first.State.GetZone("hand", "run-a")!.InstanceIds.Count);
        Assert.Equal(["card-5", "card-6", "card-7"],
            first.State.GetZone("hand", "run-a")!.InstanceIds.Take(3)
                .Select(id => first.State.GetCard(id)!.DefinitionId));
        Assert.Equal(3, first.State.GetZone("draw", "run-a")!.InstanceIds.Count);
        Assert.Empty(first.State.GetZone("discard", "run-a")!.InstanceIds);
        Assert.Empty(first.State.GetZone("exile", "run-a")!.InstanceIds);
    }

    [Fact]
    public void CooldownGraph_PrepareUseAndReturn_DoesNotRequireDeckOrHand()
    {
        var system = Load("cooldown_zones");
        var initialized = CardZoneBootstrapper.Create(system, new CardZoneBootstrapPlan
        {
            RunOwnerId = "run-a",
            ActorIds = ["hero"],
            Batches = [new CardZoneInitialBatch
            {
                ZoneId = "inventory", OwnerId = "hero", DefinitionIds = ["ability-a", "ability-b", "ability-c"]
            }]
        }, DeterministicContext.Create(73, "content-a")).Value;
        var executor = new CardZoneFlowExecutor();
        var context = new CardZoneFlowContext { FlowOwnerId = "hero", RunOwnerId = "run-a" };

        var prepared = executor.ExecuteBoundary(system, initialized.State, initialized.Context,
            "activation.started", context).Value;
        var selected = prepared.State.GetZone("prepared", "hero")!.InstanceIds.Single();
        var spent = executor.Execute(system, prepared.State, prepared.Context, "ability.cooldown",
            context with { Invocation = CardZoneFlowInvocation.CardResolution, CardInstanceIds = [selected] }).Value;
        var returned = executor.ExecuteBoundary(system, spent.State, spent.Context,
            "round.ended", context).Value;

        Assert.Empty(returned.State.GetZone("prepared", "hero")!.InstanceIds);
        Assert.Empty(returned.State.GetZone("cooldown", "hero")!.InstanceIds);
        Assert.Equal(3, returned.State.GetZone("inventory", "hero")!.InstanceIds.Count);
        Assert.DoesNotContain("draw", system.Zones.Keys);
        Assert.DoesNotContain("hand", system.Zones.Keys);
        Assert.True(CardZoneTopologyValidator.ValidateAgainstSystem(returned.State, system, "run-a").IsSuccess);
    }

    private static CardZoneFlowResult RunSpire(ulong seed)
    {
        var system = Load("spire_zones");
        var initialized = CardZoneBootstrapper.Create(system, new CardZoneBootstrapPlan
        {
            RunOwnerId = "run-a",
            Batches = [new CardZoneInitialBatch
            {
                ZoneId = "draw", OwnerId = "run-a",
                DefinitionIds = Enumerable.Range(0, 8).Select(index => $"card-{index}").ToArray()
            }]
        }, DeterministicContext.Create(seed, "content-a")).Value;
        var executor = new CardZoneFlowExecutor();
        var context = new CardZoneFlowContext { FlowOwnerId = "run-a", RunOwnerId = "run-a" };

        var drawn = executor.ExecuteBoundary(system, initialized.State, initialized.Context,
            "activation.started", context).Value;
        var playedId = drawn.State.GetZone("hand", "run-a")!.InstanceIds[0];
        var played = executor.Execute(system, drawn.State, drawn.Context, "card.played.to-discard",
            context with { Invocation = CardZoneFlowInvocation.CardResolution, CardInstanceIds = [playedId] }).Value;
        var discarded = executor.ExecuteBoundary(system, played.State, played.Context,
            "activation.ended", context).Value;
        var recycled = executor.ExecuteBoundary(system, discarded.State, discarded.Context,
            "activation.started", context).Value;
        Assert.Equal(["draw-five.available", "return-spent", "draw-five.retry"],
            recycled.Steps.Select(step => step.StepId));
        for (var index = 1; index < recycled.Steps.Length; index++)
            Assert.Equal(recycled.Steps[index - 1].StateHash, recycled.Steps[index].PreviousStateHash);
        Assert.Contains(recycled.Steps, step => step.FlowId == "activation.recycle");
        return recycled;
    }

    private static CompiledCardZoneSystem Load(string id)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "card-zone-systems", $"{id}.json");
        var authored = JsonSerializer.Deserialize<Dictionary<string, CardZoneSystemDefinition>>(
            File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(authored);
        var compiled = CardZoneSystemCompiler.Compile(authored[id]);
        Assert.True(compiled.IsSuccess, compiled.IsFailure ? compiled.Error : null);
        return compiled.Value;
    }

    private sealed class UnusedCardMetadata : ICardZoneCardMetadataResolver
    {
        public Result<IReadOnlyList<string>> ResolveTags(CardInstanceState instance, CardZoneFlowContext context) =>
            Result<IReadOnlyList<string>>.Failure("This test does not select cards by tag");
    }

    private sealed class CountVariableFormulas : IRevisionedRuntimeFormulaEvaluator
    {
        public string? LastRevision { get; private set; }

        public Result<float> Evaluate(string expressionOrFormulaId,
            Dictionary<string, float>? variables = null, float initialValue = 0f) =>
            variables?.TryGetValue(expressionOrFormulaId, out var value) == true
                ? Result<float>.Success(value)
                : Result<float>.Failure("Variable is not available");

        public Result<float> EvaluateAtRevision(string expressionOrFormulaId, string contentRevision,
            Dictionary<string, float>? variables = null, float initialValue = 0f)
        {
            LastRevision = contentRevision;
            return Evaluate(expressionOrFormulaId, variables, initialValue);
        }
    }
}

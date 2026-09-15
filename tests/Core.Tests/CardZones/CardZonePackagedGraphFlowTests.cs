using System.Text.Json;
using Core.CardZones;
using Core.Determinism;
using Xunit;

namespace Core.Tests.CardZones;

public sealed class CardZonePackagedGraphFlowTests
{
    [Fact]
    public void SpireGraph_DrawPlayDiscardAndRecycle_IsRepeatable()
    {
        var first = RunSpire(73);
        var repeated = RunSpire(73);

        Assert.Equal(first.Fingerprint, repeated.Fingerprint);
        Assert.True(first.State.Instances.Keys.ToHashSet().SetEquals(repeated.State.Instances.Keys));
        Assert.Equal(5, first.State.GetZone("hand", "run-a")!.InstanceIds.Count);
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
}

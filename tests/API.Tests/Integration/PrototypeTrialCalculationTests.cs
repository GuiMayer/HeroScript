using System.Collections.Immutable;
using API.Tests.Helpers;
using Core.CardZones;
using Core.Combat.Models;
using Core.Determinism;
using Core.Resources;
using Core.Run;
using Core.Run.Content;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace API.Tests.Integration;

public sealed partial class PrototypeTrialTests
{
    [Fact]
    public async Task PublishedCriticalTiersUseLinearBonusAndDoNotReuseAStaleSameStepActorQuote()
    {
        using var factory = new TestWebApplicationFactory(); using var client = factory.CreateClient();
        var game = new GameEngineClientSimulator(client);
        var id = await game.StartRunAsync("prototype-test", "prototype_trial", "player", 8813, "prototype_campaign");
        await game.StartCombatAsync("player", ["enemy_0"], runId: id, heroDefinitionId: "prototype_hero", enemyDefinitionId: "prototype_target");
        var source = factory.Services.GetRequiredService<IRunQueryService>().GetRun(id).Value;
        var original = CanonicalJson.ComputeHash(source);
        var executor = factory.Services.GetRequiredService<ICardPlayExecutor>();
        var prepared = Arrange(source, "prototype_strike");
        foreach (var (chance, expected) in new[] { (0f, 8f), (100f, 16f), (200f, 24f) })
        {
            var encounter = prepared.GetActiveEncounter()!;
            var actor = encounter.Combat.GetActor("player")!;
            var stats = actor.Component<StatEntityComponentState>()!;
            actor = actor with { Components = actor.Components.ToImmutableDictionary().SetItem(stats.ComponentId,
                stats with { Values = stats.Values.ToImmutableDictionary().SetItem("critical_chance", chance).SetItem("critical_damage", 100) }) };
            var run = prepared with { Encounters = prepared.Encounters.SetItem(prepared.Encounters.Length - 1,
                encounter with { Combat = encounter.Combat with { Actors = encounter.Combat.Actors.ToImmutableDictionary().SetItem("player", actor) } }) };
            var result = executor.Execute(Request(run, "prototype_strike"));
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
            Assert.Equal(expected, encounter.Combat.GetActor("enemy_0")!.ResourceState.Current("health") - result.Value.Combat.GetActor("enemy_0")!.ResourceState.Current("health"));
            Assert.Equal(result.Value.ResolutionFingerprint, executor.Execute(Request(run, "prototype_strike")).Value.ResolutionFingerprint);
        }
        var cooled = SetResource(SetHeat(Arrange(source, "prototype_coolant"), 10), "health", 80);
        var cooling = executor.Execute(Request(cooled, "prototype_coolant", []));
        Assert.True(cooling.IsSuccess, cooling.IsFailure ? cooling.Error : null);
        Assert.Equal(0, cooling.Value.Combat.GetActor("player")!.ResourceState.Current("heat"));
        Assert.Equal(84, cooling.Value.Combat.GetActor("player")!.ResourceState.Current("health"));
        Assert.Equal(0, cooling.Value.Combat.GetActor("player")!.ResourceState.Current("block"));
        Assert.Equal(original, CanonicalJson.ComputeHash(source));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(60)]
    [InlineData(85)]
    [InlineData(100)]
    public async Task PublishedHeatCaptureAndWetReleaseUseOnePipelineWithoutRescalingStoredPotency(int heat)
    {
        using var factory = new TestWebApplicationFactory(); using var client = factory.CreateClient();
        var game = new GameEngineClientSimulator(client);
        var id = await game.StartRunAsync("prototype-test", "prototype_trial", "player", 8812, "prototype_campaign");
        await game.StartCombatAsync("player", ["enemy_0"], runId: id, heroDefinitionId: "prototype_hero", enemyDefinitionId: "prototype_target");
        var reads = factory.Services.GetRequiredService<IRunQueryService>();
        var authoritative = reads.GetRun(id).Value;
        var parentHash = CanonicalJson.ComputeHash(authoritative);
        // Controlled immutable simulation inputs, never hydrated or committed to the real store.
        var run = Arrange(authoritative, "prototype_scorch", "prototype_soak", "prototype_void_condense", "prototype_strike");
        var executor = factory.Services.GetRequiredService<ICardPlayExecutor>();
        run = Use(executor, run, "prototype_soak");
        run = SetHeat(run, heat);
        var before = CanonicalJson.ComputeHash(run);
        var result = executor.Execute(Request(run, "prototype_scorch"));
        if (heat == 100)
        {
            Assert.True(result.IsFailure);
            Assert.Equal(before, CanonicalJson.ComputeHash(run));
        }
        else
        {
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
            run = Accept(run, result.Value);
            var burn = Assert.Single(run.GetActiveEncounter()!.Combat.StatusEffects["enemy_0"], status => status.StatusId == "prototype_burn");
            var potency = Assert.Single(burn.PayloadLots).Parameters["potency"].Snapshot!.Value;
            Assert.Equal(2 * (1 + (heat + 15) * 0.005f), potency, 4);
            Assert.Equal(2, burn.Stacks);
            Assert.Contains(run.GetActiveEncounter()!.Combat.StatusEffects["enemy_0"], status => status.StatusId == "prototype_wet" && status.IsActive);
            run = SetHeat(run, 100);
            var health = run.GetActiveEncounter()!.Combat.GetActor("enemy_0")!.ResourceState.Current("health");
            var fee = run.Deck.Topology.GetZone("hand", "$run")!.InstanceIds.Single(id => run.Deck.Topology.Instances[id].DefinitionId == "prototype_strike");
            var request = Request(run, "prototype_void_condense") with { PaymentSelections = [new("cost", "fuel", [fee])] };
            var output = executor.Execute(request);
            Assert.True(output.IsSuccess, output.IsFailure ? output.Error : null);
            var expected = MathF.Round(potency * 2 * 1.25f, MidpointRounding.AwayFromZero);
            Assert.Equal(expected, health - output.Value.Combat.GetActor("enemy_0")!.ResourceState.Current("health"));
            Assert.DoesNotContain(output.Value.Combat.StatusEffects.GetValueOrDefault("enemy_0", []), status => status.StatusId == "prototype_burn" && status.IsActive && status.Stacks > 0);
            Assert.Equal(100, output.Value.Combat.GetActor("player")!.ResourceState.Current("heat"));
            Assert.Equal(output.Value.ResolutionFingerprint, executor.Execute(request).Value.ResolutionFingerprint);
        }
        Assert.Equal(parentHash, CanonicalJson.ComputeHash(reads.GetRun(id).Value));
    }

    private static RunState Arrange(RunState run, params string[] definitions)
    {
        var state = run.Deck.Topology;
        var hand = new CardZoneAddress { ZoneId = "hand", OwnerId = "$run" };
        var draw = new CardZoneAddress { ZoneId = "draw", OwnerId = "$run" };
        state = CardZoneTransitions.Move(state, hand, draw, state.GetZone(hand)!.InstanceIds, new() { Strategy = CardZoneInsertionStrategy.Bottom },
            null, CardZoneOrdering.Ordered, CardZoneOrdering.Ordered, run.Determinism).Value.State;
        foreach (var definition in definitions)
        {
            var card = state.Instances.Values.Where(card => card.DefinitionId == definition).OrderBy(card => card.CreationOrdinal).First();
            var source = state.Zones.Values.Single(zone => zone.InstanceIds.Contains(card.CardInstanceId));
            state = CardZoneTransitions.Move(state, source.Address, hand, [card.CardInstanceId], new() { Strategy = CardZoneInsertionStrategy.Bottom },
                8, CardZoneOrdering.Ordered, CardZoneOrdering.Ordered, run.Determinism).Value.State;
        }
        return run with { Deck = new() { Topology = state } };
    }
    private static CardPlayExecutionRequest Request(RunState run, string definition, string[]? targets = null) => new()
    { Run = run, Combat = run.GetActiveEncounter()!.Combat, ActorId = "player", SelectedTargetIds = targets ?? ["enemy_0"],
        CardInstanceId = run.Deck.Topology.Instances.Values.Where(card => card.DefinitionId == definition).OrderBy(card => card.CreationOrdinal).First().CardInstanceId };
    private static RunState Use(ICardPlayExecutor executor, RunState run, string definition)
    {
        var result = executor.Execute(Request(run, definition));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        return Accept(run, result.Value);
    }
    private static RunState Accept(RunState run, CardPlayExecutionResult result)
    {
        var candidate = result.Run ?? run;
        return candidate with { Encounters = candidate.Encounters.SetItem(candidate.Encounters.Length - 1,
            candidate.Encounters[^1] with { Combat = result.Combat }) };
    }
    private static RunState SetHeat(RunState run, float heat) => SetResource(run, "heat", heat);
    private static RunState SetResource(RunState run, string resource, float value)
    {
        var encounter = run.GetActiveEncounter()!;
        var actor = encounter.Combat.GetActor("player")!.ApplyResourceMutation("fixture." + resource, resource, ResourceMutationOperation.Set, value).Value;
        return run with { Encounters = run.Encounters.SetItem(run.Encounters.Length - 1, encounter with
            { Combat = encounter.Combat with { Actors = encounter.Combat.Actors.ToImmutableDictionary().SetItem("player", actor) } }) };
    }
}

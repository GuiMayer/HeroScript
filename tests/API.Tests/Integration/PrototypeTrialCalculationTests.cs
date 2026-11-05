using System.Collections.Immutable;
using API.Tests.Helpers;
using Core.CardZones;
using Core.Combat.Models;
using Core.Determinism;
using Core.Resources;
using Core.Run;
using Core.Run.Content;
using Core.Run.Loadouts;
using Core.Content;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace API.Tests.Integration;

public sealed partial class PrototypeTrialTests
{
    [Fact]
    public async Task ThinSliceCardsUsePublishedCostsAndEffectsDeterministically()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        var game = new GameEngineClientSimulator(client);
        var id = await game.StartRunAsync("prototype-test", "prototype_trial", "player", 8815, "prototype_campaign");
        await game.StartCombatAsync("player", ["enemy_0"], runId: id, heroDefinitionId: "prototype_hero", enemyDefinitionId: "prototype_target");
        var source = factory.Services.GetRequiredService<IRunQueryService>().GetRun(id).Value;
        var executor = factory.Services.GetRequiredService<ICardPlayExecutor>();
        foreach (var (definition, target, resource, expected, cost) in new[]
        {
            ("pyro_golpe_brasa", "enemy_0", "health", -14f, 2f),
            ("pyro_barreira_cinzas", "player", "block", 8f, 1f)
        })
        {
            var run = Arrange(source, definition);
            var before = CanonicalJson.ComputeHash(run);
            var request = Request(run, definition, target == "player" ? [] : ["enemy_0"]);
            var result = executor.Execute(request);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
            var original = run.GetActiveEncounter()!.Combat;
            Assert.Equal(expected, result.Value.Combat.GetActor(target)!.ResourceState.Current(resource) -
                original.GetActor(target)!.ResourceState.Current(resource));
            Assert.Equal(cost, original.GetActor("player")!.ResourceState.Current("energy") -
                result.Value.Combat.GetActor("player")!.ResourceState.Current("energy"));
            Assert.Equal(before, CanonicalJson.ComputeHash(run));
            Assert.Equal(result.Value.ResolutionFingerprint, executor.Execute(request).Value.ResolutionFingerprint);
        }
    }

    [Fact]
    public async Task EquippedStyleInscriptionsApplyOnlyToMatchingCards()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        var game = new GameEngineClientSimulator(client);
        var id = await game.StartRunAsync("prototype-test", "prototype_trial", "player", 8814, "prototype_campaign");
        var reads = factory.Services.GetRequiredService<IRunQueryService>();
        var initial = reads.GetRun(id).Value;
        var runtimes = factory.Services.GetRequiredService<IContentRuntimeResolver>();
        var loadouts = initial.Loadouts!;
        var inventory = loadouts.Inventories[loadouts.InventoryId(id, "player")];
        await game.StartCombatAsync("player", ["enemy_0"], runId: id, heroDefinitionId: "prototype_hero", enemyDefinitionId: "prototype_target");
        var combatRun = reads.GetRun(id).Value;
        var executor = factory.Services.GetRequiredService<ICardPlayExecutor>();
        var planner = new LoadoutChangePlanner(runtimes);

        foreach (var (card, style, applied, excluded) in new[]
        {
            ("prototype_scorch", "prototype_fire_mastery", "prototype.fire_focus", "prototype.void_fusion"),
            ("prototype_necro_bolt", "prototype_void_fusion", "prototype.void_fusion", "prototype.fire_focus")
        })
        {
            var draft = new[] { "prototype_fire_focus", style }.ToDictionary(
                definition => definition, definition => inventory.Items.Single(item => item.DefinitionId == definition).InstanceId);
            var equipped = planner.Plan(initial, "player", draft);
            Assert.True(equipped.IsSuccess, equipped.IsFailure ? equipped.Error : null);
            var run = Arrange(combatRun with { Loadouts = equipped.Value.Candidate.Loadouts }, card, "prototype_strike");
            Guid? payment = null;
            if (card == "prototype_necro_bolt")
            {
                var topology = run.Deck.Topology;
                var hand = topology.GetZone("hand", "$run")!;
                var discard = topology.GetZone("discard", "$run")!;
                var fuel = hand.InstanceIds.Single(
                    instance => run.Deck.Topology.Instances[instance].DefinitionId == "prototype_strike");
                var moved = CardZoneTransitions.Move(topology, hand.Address, discard.Address, [fuel],
                    new() { Strategy = CardZoneInsertionStrategy.Bottom }, null,
                    CardZoneOrdering.Ordered, CardZoneOrdering.Ordered, run.Determinism);
                Assert.True(moved.IsSuccess, moved.IsFailure ? moved.Error : null);
                run = run with { Deck = new() { Topology = moved.Value.State } };
                payment = fuel;
            }
            var request = Request(run, card);
            if (payment is { } selectedFuel)
                request = request with { PaymentSelections = [new("cost", "fuel", [selectedFuel])] };
            var before = CanonicalJson.ComputeHash(run);
            var result = executor.Execute(request);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
            var buckets = result.Value.Calculations.SelectMany(calculation => calculation.Buckets).ToArray();
            var contributions = buckets.SelectMany(bucket => bucket.Contributions).ToArray();
            Assert.Contains(contributions, contribution => contribution.InfluenceId == applied && contribution.Applied);
            Assert.DoesNotContain(contributions, contribution => contribution.InfluenceId == excluded && contribution.Applied);
            if (style == "prototype_fire_mastery")
                Assert.Contains(buckets, bucket => bucket.Groups?.Any(group => group.Key == "fire" &&
                    group.Parameters?.GetValueOrDefault("cap") == 1.5f) == true);
            Assert.Equal(before, CanonicalJson.ComputeHash(run));
            Assert.Equal(result.Value.ResolutionFingerprint, executor.Execute(request).Value.ResolutionFingerprint);
        }
    }

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
        var cooled = SetResource(Arrange(source, "prototype_coolant"), "health", 80);
        var cooling = executor.Execute(Request(cooled, "prototype_coolant", []));
        Assert.True(cooling.IsSuccess, cooling.IsFailure ? cooling.Error : null);
        Assert.False(cooling.Value.Combat.GetActor("player")!.ResourceState.Resources.ContainsKey("heat"));
        Assert.Equal(84, cooling.Value.Combat.GetActor("player")!.ResourceState.Current("health"));
        Assert.Equal(0, cooling.Value.Combat.GetActor("player")!.ResourceState.Current("block"));
        Assert.Equal(original, CanonicalJson.ComputeHash(source));
    }

    [Fact]
    public async Task PublishedWetReleaseUsesCapturedPotencyWithoutHeat()
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
        var before = CanonicalJson.ComputeHash(run);
        var result = executor.Execute(Request(run, "prototype_scorch"));
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
            Assert.Equal(before, CanonicalJson.ComputeHash(run));
            run = Accept(run, result.Value);
            var burn = Assert.Single(run.GetActiveEncounter()!.Combat.StatusEffects["enemy_0"], status => status.StatusId == "prototype_burn");
            var potency = Assert.Single(burn.PayloadLots).Parameters["potency"].Snapshot!.Value;
            Assert.Equal(2, potency, 4);
            Assert.Equal(2, burn.Stacks);
            Assert.Contains(run.GetActiveEncounter()!.Combat.StatusEffects["enemy_0"], status => status.StatusId == "prototype_wet" && status.IsActive);
            var health = run.GetActiveEncounter()!.Combat.GetActor("enemy_0")!.ResourceState.Current("health");
            var fee = run.Deck.Topology.GetZone("hand", "$run")!.InstanceIds.Single(id => run.Deck.Topology.Instances[id].DefinitionId == "prototype_strike");
            var request = Request(run, "prototype_void_condense") with { PaymentSelections = [new("cost", "fuel", [fee])] };
            var output = executor.Execute(request);
            Assert.True(output.IsSuccess, output.IsFailure ? output.Error : null);
            var expected = MathF.Round(potency * 2 * 1.25f, MidpointRounding.AwayFromZero);
            Assert.Equal(expected, health - output.Value.Combat.GetActor("enemy_0")!.ResourceState.Current("health"));
            Assert.DoesNotContain(output.Value.Combat.StatusEffects.GetValueOrDefault("enemy_0", []), status => status.StatusId == "prototype_burn" && status.IsActive && status.Stacks > 0);
            Assert.False(output.Value.Combat.GetActor("player")!.ResourceState.Resources.ContainsKey("heat"));
            Assert.Equal(output.Value.ResolutionFingerprint, executor.Execute(request).Value.ResolutionFingerprint);
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
    private static RunState SetResource(RunState run, string resource, float value)
    {
        var encounter = run.GetActiveEncounter()!;
        var actor = encounter.Combat.GetActor("player")!.ApplyResourceMutation("fixture." + resource, resource, ResourceMutationOperation.Set, value).Value;
        return run with { Encounters = run.Encounters.SetItem(run.Encounters.Length - 1, encounter with
            { Combat = encounter.Combat with { Actors = encounter.Combat.Actors.ToImmutableDictionary().SetItem("player", actor) } }) };
    }
}

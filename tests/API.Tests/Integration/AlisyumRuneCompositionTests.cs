using System.Net.Http.Json;
using System.Text.Json;
using API.Tests.Helpers;
using Core.Content;
using Core.CardZones;
using Core.Determinism;
using System.Collections.Immutable;
using Core.Run;
using Core.Run.Content;
using Core.Effects;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace API.Tests.Integration;

public sealed class AlisyumRuneCompositionTests
{
    [Fact]
    public async Task MvpRewritesOnlyTheResidualAndKeepsItsOwnPipeline()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        var revisions = await client.GetFromJsonAsync<JsonElement>("/api/v1/content/revisions?configName=prototype-test");
        var rev = revisions.GetProperty("currentRevision").GetString()!;
        var runtime = factory.Services.GetRequiredService<IContentRuntimeResolver>().Resolve(rev, "prototype-test").Value;
        var baseCard = new CardInstanceState { CardInstanceId = Guid.NewGuid(), DefinitionId = "prototype_scorch" };
        var upgrade = runtime.GetDefinition<CardUpgradeDefinition>("card-upgrades", "prototype_rune_wet").Value;
        var card = CardInstanceUpgradeTransitions.Apply(baseCard, CardBundleCompiler.Seal(upgrade, runtime).Value, rev).Value;
        var effective = new EffectiveCardResolver().Resolve(new CardContentCompiler().Compile(baseCard.DefinitionId, runtime).Value, card).Value;
        var effects = effective.All<CardEffectComponentDefinition>();
        Assert.Contains(effects, x => x.Effect.StatusId == "prototype_wet");
        Assert.DoesNotContain(effects, x => x.Effect.StatusId == "prototype_burn");
        Assert.Equal("prototype_direct_amount", effects.Single(x => x.ComponentId == "main").Effect.Parameters.Single().PipelineId);
    }

    [Fact]
    public async Task InitialForgeOffersAndBindsOneRuneBeforeFirstCombat()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        var game = new GameEngineClientSimulator(client);
        var runId = await game.StartRunAsync("alisyum", "alisyum_chapter_1", "player", 1842, "alisyum_campaign");
        var reads = factory.Services.GetRequiredService<IRunQueryService>();
        async Task Submit(string type, object payload)
        {
            var run = reads.GetRun(runId).Value;
            using var response = await client.PostAsJsonAsync($"/api/v1/runs/{runId}/commands", new { commandId = Guid.NewGuid(), type, expectedSequence = run.Sequence, expectedStep = run.Determinism.Step, payload });
            Assert.True(response.IsSuccessStatusCode, $"{type}: {await response.Content.ReadAsStringAsync()}");
        }
        await Submit("START_DIALOGUE", new { dialogueId = "alisyum_opening_pyro" });
        var dialogue = reads.GetRun(runId).Value.Dialogues.Single();
        await Submit("CHOOSE_DIALOGUE_OPTION", new { dialogueInstanceId = dialogue.DialogueInstanceId, nodeId = dialogue.CurrentNodeId, choiceId = "continue" });
        await Submit("RESOLVE_NODE", new { currentNodeId = "chapter_opening" });
        await Submit("ADVANCE_NODE", new { targetNodeId = "chapter_prepare_0" });
        await Submit("CREATE_PREPARATION", new { preparationId = "alisyum_preparation" });
        var preparation = reads.GetRun(runId).Value.Preparations.Single();
        await Submit("APPLY_PREPARATION_OPTION", new { preparationInstanceId = preparation.PreparationInstanceId, optionId = "ready" });
        await Submit("RESOLVE_NODE", new { currentNodeId = "chapter_prepare_0" });
        await Submit("ADVANCE_NODE", new { targetNodeId = "chapter_rune_0" });
        await Submit("ACQUIRE_CARD_MODIFIER_ITEM", new { itemDefinitionId = "obor_overwrite_fragment_item" });
        var owned = reads.GetRun(runId).Value;
        var item = Assert.Single(owned.CardModifierItems!.Items);
        var card = owned.Deck.Topology.Instances.Values.First(x => x.DefinitionId == "alisyum_basic_strike");
        var preview = await client.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{runId}/card-modifier-items/{item.ItemInstanceId}/preview/{card.CardInstanceId}");
        Assert.True(preview.GetProperty("after").GetProperty("permanentChanges").GetArrayLength() > 0);
        await Submit("APPLY_CARD_MODIFIER_ITEM", new { itemInstanceId = item.ItemInstanceId, cardInstanceId = card.CardInstanceId });
        var bound = reads.GetRun(runId).Value;
        Assert.Equal(CardModifierItemState.Bound, Assert.Single(bound.CardModifierItems!.Items).State);
        Assert.Single(bound.Deck.GetCard(card.CardInstanceId)!.Upgrades);
        await Submit("RESOLVE_NODE", new { currentNodeId = "chapter_rune_0" });
        await Submit("ADVANCE_NODE", new { targetNodeId = "encounter_1" });
        Assert.Equal("encounter_1", reads.GetRun(runId).Value.CurrentNodeId);
        var replay = await factory.Services.GetRequiredService<Core.Run.Replay.IRunReplayService>().VerifyAsync(runId);
        Assert.True(replay.IsValid, string.Join("; ", replay.Errors));
        await Submit("START_ENCOUNTER", new { participants = new object[] {
            new { instanceId = "player", definitionId = "pyromancer", sideId = "player", controllerBinding = new { kind = "Player", policyId = (string?)null } },
            new { instanceId = "enemy_0", definitionId = "enemy_vidaria_enforcer", sideId = "opposition", controllerBinding = new { kind = "AI", policyId = "gambit" } }
        }});
        var source = reads.GetRun(runId).Value;
        var sourceHash = CanonicalJson.ComputeHash(source);
        var runtime = factory.Services.GetRequiredService<IContentRuntimeResolver>().Resolve(source.Determinism.ContentRevision, "alisyum").Value;
        var strike = source.Deck.GetCard(card.CardInstanceId)!;
        var guard = source.Deck.Topology.Instances.Values.First(x => x.DefinitionId == "alisyum_basic_guard");
        var catalyst = source.Deck.Topology.Instances.Values.First(x => x.DefinitionId == "alisyum_basic_strike" && x.CardInstanceId != strike.CardInstanceId);
        CardInstanceState Socket(CardInstanceState target, string name) => CardInstanceUpgradeTransitions.Apply(target,
            CardBundleCompiler.Seal(runtime.GetDefinition<CardUpgradeDefinition>("card-upgrades", "obor_overwrite_" + name).Value, runtime).Value,
            source.Determinism.ContentRevision).Value;
        // Pure candidate only: exercise the full observer chain without altering the persisted test run.
        var topology = source.Deck.Topology;
        var hand = topology.Zones.Values.Single(z => z.Address.ZoneId == "hand").Address;
        foreach (var target in new[] { strike, guard, catalyst })
        {
            var zone = topology.FindZone(target.CardInstanceId)!.Address;
            if (zone.Key != hand.Key)
                topology = CardZoneTransitions.Move(topology, zone, hand, [target.CardInstanceId], new(), 10, CardZoneOrdering.Ordered, CardZoneOrdering.Ordered, source.Determinism).Value.State;
        }
        strike = Socket(strike, "ember"); guard = Socket(guard, "resonance"); catalyst = Socket(catalyst, "catalyst");
        topology = topology with { Instances = topology.Instances.ToImmutableDictionary().SetItem(strike.CardInstanceId, strike).SetItem(guard.CardInstanceId, guard).SetItem(catalyst.CardInstanceId, catalyst) };
        var candidate = source with { Deck = new() { Topology = topology } };
        var effective = new EffectiveCardResolver().Resolve(new CardContentCompiler().Compile(strike.DefinitionId, runtime).Value, strike).Value;
        var combat = source.GetActiveEncounter()!.Combat;
        var request = new EffectTriggerExecutionRequest { Combat = combat, Run = candidate, Card = effective, OwnerEntityId = "player", SourceEntityId = "player",
            ContentRevision = source.Determinism.ContentRevision, Provenance = new() { Kind = EffectProvenanceKind.Card, SourceId = strike.CardInstanceId.ToString() }, SelectedTargetEntityIds = ["enemy_0"],
            Trigger = new() { TriggerId = "rune-probe", Boundary = "Play", Effects = effective.All<CardEffectComponentDefinition>().Select(c => c.Effect).ToArray() } };
        var result = factory.Services.GetRequiredService<IEffectTriggerExecutor>().Execute(request);
        if (result.IsFailure)
        {
            var executor = factory.Services.GetRequiredService<IEffectTriggerExecutor>();
            var diagnostics = request.Trigger.Effects.Select(effect => (effect.EffectId, Execution: executor.Execute(request with { Trigger = request.Trigger with { Effects = [effect] } })))
                .Select(x => $"{x.EffectId}: {(x.Execution.IsSuccess ? "ok" : x.Execution.Error)}");
            var withoutObservers = executor.Execute(request with { Run = source });
            Assert.Fail(result.Error + " / " + string.Join("; ", diagnostics) + " / without observers: " + (withoutObservers.IsSuccess ? "ok" : withoutObservers.Error));
        }
        Assert.Contains(result.Value.Records, r => r.StatusId == "alisyum_burn");
        Assert.Contains(result.Value.Records, r => r.ResourceId == "block" && r.CurrentValue > r.PreviousValue);
        Assert.Contains(result.Value.Records, r => r.EffectType == EffectType.CARD_ZONE_FLOW && r.CardZoneSteps.Length > 0);
        Assert.Equal(sourceHash, CanonicalJson.ComputeHash(reads.GetRun(runId).Value));
    }

    [Fact]
    public async Task PublishedRunesComposeIndividualCardsWithoutReplacingTheirBudget()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        var revisions = await client.GetFromJsonAsync<JsonElement>("/api/v1/content/revisions?configName=alisyum");
        var revision = revisions.GetProperty("currentRevision").GetString()!;
        var runtime = factory.Services.GetRequiredService<IContentRuntimeResolver>().Resolve(revision, "alisyum").Value;
        var compiler = new CardContentCompiler();
        var resolver = new EffectiveCardResolver();
        CardInstanceState Apply(CardInstanceState card, string suffix)
        {
            var item = runtime.GetDefinition<CardModifierItemDefinition>("card-modifier-items", "obor_overwrite_" + suffix + "_item").Value;
            Assert.True(item.Validate(runtime).IsSuccess);
            var upgrade = runtime.GetDefinition<CardUpgradeDefinition>("card-upgrades", item.UpgradeId).Value;
            var closed = CardBundleCompiler.Seal(upgrade, runtime).Value;
            return CardInstanceUpgradeTransitions.Apply(card, closed, revision).Value;
        }
        var strike = new CardInstanceState { CardInstanceId = Guid.NewGuid(), DefinitionId = "alisyum_basic_strike" };
        var compiled = compiler.Compile(strike.DefinitionId, runtime).Value;
        var original = resolver.Resolve(compiled, strike).Value;
        var modified = resolver.Resolve(compiled, Apply(Apply(strike, "ember"), "fragment")).Value;
        var originalMain = original.All<CardEffectComponentDefinition>().Single(x => x.ComponentId == "main").Effect;
        var main = modified.All<CardEffectComponentDefinition>().Single(x => x.ComponentId == "main").Effect;
        Assert.Equal(1, originalMain.Repeat);
        Assert.Equal(3, main.Repeat);
        Assert.Equal(6f, main.Parameters.Single(x => x.Parameter == EffectNumericParameter.Amount).FlatValue);
        Assert.NotNull(main.Parameters.Single().Distribution);
        Assert.Equal("alisyum_burn", modified.All<CardEffectComponentDefinition>().Single(x => x.ComponentId == "rune.affinity").Effect.StatusId);
        Assert.Empty(strike.Upgrades);
        var magma = new CardInstanceState { CardInstanceId = Guid.NewGuid(), DefinitionId = "pyro_magma_slash" };
        var rewritten = resolver.Resolve(compiler.Compile(magma.DefinitionId, runtime).Value, Apply(magma, "frost_magma")).Value;
        Assert.DoesNotContain(rewritten.All<CardEffectComponentDefinition>(), x => x.Effect.StatusId == "alisyum_burn");
        Assert.Contains(rewritten.All<CardEffectComponentDefinition>(), x => x.Effect.StatusId == "alisyum_frostbite");
        var rupture = resolver.Resolve(compiler.Compile(magma.DefinitionId, runtime).Value, Apply(magma, "rupture")).Value;
        Assert.Contains(rupture.All<CardEffectComponentDefinition>(), x => x.Effect.CondensationRecipeId == "alisyum_detonate_burn");
        Assert.DoesNotContain(rupture.All<CardEffectComponentDefinition>(), x => x.Effect.StatusId == "alisyum_burn");
        Assert.True(resolver.Resolve(compiler.Compile(magma.DefinitionId, runtime).Value, Apply(Apply(magma, "frost_magma"), "rupture")).IsFailure);
        Assert.True(resolver.Resolve(compiler.Compile(magma.DefinitionId, runtime).Value, Apply(Apply(magma, "rupture"), "frost_magma")).IsFailure);
        var overflow = resolver.Resolve(compiled, Apply(Apply(strike, "fragment"), "overflow")).Value;
        Assert.Equal(1, overflow.All<CardEffectComponentDefinition>().Single(x => x.ComponentId == "main").Effect.Continuation!.MaximumHops);
        var guard = new CardInstanceState { CardInstanceId = Guid.NewGuid(), DefinitionId = "alisyum_basic_guard" };
        var resonance = resolver.Resolve(compiler.Compile(guard.DefinitionId, runtime).Value, Apply(guard, "resonance")).Value;
        var observer = Assert.Single(resonance.Components.OfType<Core.GameplayEvents.Responses.CardResponseComponentDefinition>()).Response!;
        Assert.Equal("hand", Assert.Single(observer.Availability.ZoneIds));
        Assert.Equal(Core.GameplayEvents.Responses.ResponseFrequencyScope.RootAction, observer.Frequency.Scope);
        Assert.Equal(Core.GameplayEvents.Responses.ResponsePropagation.AllowResponses, observer.Propagation);
        var catalyst = resolver.Resolve(compiled, Apply(strike, "catalyst")).Value;
        Assert.Contains(catalyst.Components.OfType<Core.GameplayEvents.Responses.CardResponseComponentDefinition>(), x => x.Response!.Observe.RequiredTags.Contains("rune.resonance"));
    }
}

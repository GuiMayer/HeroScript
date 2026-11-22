using System.Net.Http.Json;
using System.Text.Json;
using API.Tests.Helpers;
using Core.Content;
using Core.Run;
using Core.Run.Content;
using Core.Effects;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace API.Tests.Integration;

public sealed class AlisyumRuneCompositionTests
{
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

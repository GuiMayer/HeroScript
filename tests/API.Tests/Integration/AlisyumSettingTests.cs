using System.Net.Http.Json;
using System.Text.Json;
using API.Tests.Helpers;
using Core.Determinism;
using Core.Run;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace API.Tests.Integration;

public sealed class AlisyumSettingTests
{
    [Fact]
    public async Task AlisyumSettingCompilesAndSupportsIdentityModsGearAndCardScaling()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        // 1. Setting appears in public catalog & verifies the 4 in-game character names
        var settings = await client.GetFromJsonAsync<JsonElement>("/api/v1/content/settings");
        var alisyum = settings.GetProperty("items").EnumerateArray()
            .SingleOrDefault(item => item.GetProperty("settingId").GetString() == "alisyum");
        Assert.Equal("alisyum_campaign", alisyum.GetProperty("launch").GetProperty("modeId").GetString());

        var revisions = await client.GetFromJsonAsync<JsonElement>("/api/v1/content/revisions?configName=alisyum");
        var rev = revisions.GetProperty("currentRevision").GetString()!;
        foreach (var (id, expectedName) in new[]
        {
            ("pyromancer", "Piromante"),
            ("baron_servant", "Servo"),
            ("gambler", "Apostador"),
            ("vitreous_renegade", "Renegado")
        })
        {
            var entity = await client.GetFromJsonAsync<JsonElement>($"/api/v1/content/entities/{id}?configName=alisyum&revision={rev}");
            Assert.Equal(expectedName, entity.GetProperty("definition").GetProperty("displayName").GetString());
        }

        // 2. Start run with Pyromancer
        var game = new GameEngineClientSimulator(client);
        var runId = await game.StartRunAsync("alisyum", "alisyum_chapter_1", "player", 4201, "alisyum_campaign");
        var reads = factory.Services.GetRequiredService<IRunQueryService>();

        // 3. Complete opening dialogue node to reach Altar de Runa (Preparation)
        var run = reads.GetRun(runId).Value;
        using (var startDlg = await client.PostAsJsonAsync($"/api/v1/runs/{runId}/commands", new
        {
            commandId = Guid.NewGuid(),
            type = "START_DIALOGUE",
            expectedSequence = run.Sequence,
            expectedStep = run.Determinism.Step,
            payload = new { dialogueId = "alisyum_opening_pyro" }
        }))
        {
            Assert.True(startDlg.IsSuccessStatusCode, await startDlg.Content.ReadAsStringAsync());
        }
        run = reads.GetRun(runId).Value;
        var dialogue = run.Dialogues.Single();
        using (var advance = await client.PostAsJsonAsync($"/api/v1/runs/{runId}/commands", new
        {
            commandId = Guid.NewGuid(),
            type = "CHOOSE_DIALOGUE_OPTION",
            expectedSequence = run.Sequence,
            expectedStep = run.Determinism.Step,
            payload = new { dialogueInstanceId = dialogue.DialogueInstanceId, nodeId = dialogue.CurrentNodeId, choiceId = "continue" }
        }))
        {
            Assert.True(advance.IsSuccessStatusCode, await advance.Content.ReadAsStringAsync());
        }

        // 4. Inspect loadout: 7 ARPG slots + verify exclusive identity & starting inventory filtering
        var loadout = await client.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{runId}/loadout?actorId=player");
        var slots = loadout.GetProperty("policy").GetProperty("slots").EnumerateArray().ToArray();
        Assert.Equal(7, slots.Length);
        Assert.Equal("identity", slots[0].GetProperty("slotId").GetString());

        var items = loadout.GetProperty("items").EnumerateArray().ToDictionary(
            row => row.GetProperty("definition").GetProperty("inscriptionId").GetString()!,
            row => row.GetProperty("instance").GetProperty("instanceId").GetGuid());

        // FilterStartingByActorDefinition prevents off-class and advanced identity mods from cluttering starting backpack
        Assert.True(items.ContainsKey("pyro_identity_ember_core"));
        Assert.False(items.ContainsKey("pyro_identity_magma_veins"));
        Assert.False(items.ContainsKey("pyro_identity_cinder_warden"));
        Assert.False(items.ContainsKey("gambler_identity_rigged_die"));
        Assert.False(items.ContainsKey("gambler_identity_broken_wheel"));
        Assert.False(items.ContainsKey("baron_identity_first_oath"));
        Assert.False(items.ContainsKey("vitreous_identity_shattered_shell"));

        // Pyromancer cannot equip non-identity gear in the identity slot
        run = reads.GetRun(runId).Value;
        using (var invalidPreview = await client.PostAsJsonAsync($"/api/v1/runs/{runId}/loadout/preview", new
        {
            actorId = "player",
            assignments = new Dictionary<string, Guid> { ["identity"] = items["gear_vidaria_torchblade"] },
            expectedSequence = run.Sequence,
            expectedStep = run.Determinism.Step
        }))
        {
            Assert.False(invalidPreview.IsSuccessStatusCode);
        }

        // Pyromancer equips own identity mod + ARPG gear
        run = reads.GetRun(runId).Value;
        using (var equip = await client.PostAsJsonAsync($"/api/v1/runs/{runId}/commands", new
        {
            commandId = Guid.NewGuid(),
            type = "APPLY_LOADOUT",
            expectedSequence = run.Sequence,
            expectedStep = run.Determinism.Step,
            payload = new
            {
                actorId = "player",
                assignments = new Dictionary<string, Guid>
                {
                    ["identity"] = items["pyro_identity_ember_core"],
                    ["weapon"] = items["gear_vidaria_torchblade"],
                    ["armor"] = items["gear_calidor_vitreous_plate"],
                    ["rune_1"] = items["rune_obor_pyre"]
                }
            }
        }))
        {
            Assert.True(equip.IsSuccessStatusCode, await equip.Content.ReadAsStringAsync());
        }

        // 5. Verify attributes & critical tier cap (0 for Pyromancer)
        var attrs = await client.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{runId}/character-attributes");
        var attrList = attrs.GetProperty("attributes").EnumerateArray().ToDictionary(
            item => item.GetProperty("attributeId").GetString()!,
            item => item.GetProperty("effectiveValue").GetSingle());
        Assert.Equal(2f, attrList["fire_flat"]); // +2 flat fire damage from Lâmina-Tocha de Vidaria
        Assert.True(attrList["fire_scaling"] >= 39f); // +0.25 identity +0.15 weapon * displayScale 100 = 40%
        Assert.Equal(0f, attrList["critical_tier_cap"]);

        // 6. Advance Preparation -> Encounter 1 and verify reactive Identity Mod trigger executes in combat
        async Task PostRunCmdAsync(string type, object payload)
        {
            var r = reads.GetRun(runId).Value;
            using var resp = await client.PostAsJsonAsync($"/api/v1/runs/{runId}/commands", new
            {
                commandId = Guid.NewGuid(),
                type,
                expectedSequence = r.Sequence,
                expectedStep = r.Determinism.Step,
                payload
            });
            Assert.True(resp.IsSuccessStatusCode, $"{type} failed: {await resp.Content.ReadAsStringAsync()}");
        }

        await PostRunCmdAsync("RESOLVE_NODE", new { currentNodeId = "chapter_opening" });
        await PostRunCmdAsync("ADVANCE_NODE", new { targetNodeId = "chapter_prepare_0" });
        await PostRunCmdAsync("CREATE_PREPARATION", new { preparationId = "alisyum_preparation" });
        var prep = reads.GetRun(runId).Value.Preparations.Single();
        await PostRunCmdAsync("APPLY_PREPARATION_OPTION", new { preparationInstanceId = prep.PreparationInstanceId, optionId = "ready" });
        await PostRunCmdAsync("RESOLVE_NODE", new { currentNodeId = "chapter_prepare_0" });
        await PostRunCmdAsync("ADVANCE_NODE", new { targetNodeId = "encounter_1" });

        run = reads.GetRun(runId).Value;
        Guid combatId;
        using (var startEnc = await client.PostAsJsonAsync($"/api/v1/runs/{runId}/commands", new
        {
            commandId = Guid.NewGuid(),
            type = "START_ENCOUNTER",
            expectedSequence = run.Sequence,
            expectedStep = run.Determinism.Step,
            payload = new
            {
                participants = new object[]
                {
                    new
                    {
                        instanceId = "player",
                        definitionId = "pyromancer",
                        sideId = "player",
                        controllerBinding = new { kind = "Player", policyId = (string?)null }
                    },
                    new
                    {
                        instanceId = "enemy_0",
                        definitionId = "enemy_vidaria_enforcer",
                        sideId = "opposition",
                        controllerBinding = new { kind = "AI", policyId = "gambit" }
                    }
                }
            }
        }))
        {
            Assert.True(startEnc.IsSuccessStatusCode, await startEnc.Content.ReadAsStringAsync());
            var receipt = await startEnc.Content.ReadFromJsonAsync<JsonElement>();
            combatId = receipt.GetProperty("state").GetProperty("activeEncounterId").GetGuid();
        }

        var hand = await game.GetPlayableCardIdsAsync(runId);
        if (!hand.Contains("pyro_magma_slash"))
        {
            await game.EndTurnAsync(combatId, runId);
            hand = await game.GetPlayableCardIdsAsync(runId);
        }
        Assert.Contains("pyro_magma_slash", hand);
        var combatAfter = await game.ExecuteActionAsync(combatId, "player", "enemy_0", cardId: "pyro_magma_slash", runId: runId);
        var heat = combatAfter.GetProperty("actors").EnumerateArray()
            .First(a => a.GetProperty("instanceId").GetString() == "player")
            .GetProperty("resources")
            .GetProperty("heat_gauge")
            .GetProperty("current")
            .GetSingle();
        // +1 from pyro_magma_slash.heat + 1 from pyro_identity_ember_core reactive response = 2
        Assert.True(heat >= 2f, $"Expected reactive Identity Mod heat_gauge >= 2, got {heat}");

        var replay = await factory.Services.GetRequiredService<Core.Run.Replay.IRunReplayService>().VerifyAsync(runId);
        Assert.True(replay.IsValid, string.Join("; ", replay.Errors));
    }

    private static async Task AdvanceOpeningAsync(HttpClient client, IRunQueryService reads, Guid runId, string dialogueId)
    {
        var run = reads.GetRun(runId).Value;
        using (var startDlg = await client.PostAsJsonAsync($"/api/v1/runs/{runId}/commands", new
        {
            commandId = Guid.NewGuid(),
            type = "START_DIALOGUE",
            expectedSequence = run.Sequence,
            expectedStep = run.Determinism.Step,
            payload = new { dialogueId }
        }))
        {
            Assert.True(startDlg.IsSuccessStatusCode, await startDlg.Content.ReadAsStringAsync());
        }

        run = reads.GetRun(runId).Value;
        var dialogue = run.Dialogues.Single();
        using var advance = await client.PostAsJsonAsync($"/api/v1/runs/{runId}/commands", new
        {
            commandId = Guid.NewGuid(),
            type = "CHOOSE_DIALOGUE_OPTION",
            expectedSequence = run.Sequence,
            expectedStep = run.Determinism.Step,
            payload = new { dialogueInstanceId = dialogue.DialogueInstanceId, nodeId = dialogue.CurrentNodeId, choiceId = "continue" }
        });
        Assert.True(advance.IsSuccessStatusCode, await advance.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GamblerIdentityModsUnlockAndCapMultiTierCrit()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        var game = new GameEngineClientSimulator(client);
        var runId = await game.StartRunAsync("alisyum", "alisyum_gambler_run", "player", 4202, "alisyum_gambler_campaign");
        var reads = factory.Services.GetRequiredService<IRunQueryService>();
        await AdvanceOpeningAsync(client, reads, runId, "alisyum_opening_gambler");

        var loadout = await client.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{runId}/loadout?actorId=player");
        var items = loadout.GetProperty("items").EnumerateArray().ToDictionary(
            row => row.GetProperty("definition").GetProperty("inscriptionId").GetString()!,
            row => row.GetProperty("instance").GetProperty("instanceId").GetGuid());

        // Starting inventory only has the basic identity mod, not advanced variants
        Assert.True(items.ContainsKey("gambler_identity_rigged_die"));
        Assert.False(items.ContainsKey("gambler_identity_broken_wheel"));
        Assert.False(items.ContainsKey("gambler_identity_ace_high"));

        // Initial Gambler identity mod + starter crit gear: 10 base + 30 rigged_die + 20 dagger + 15 rune = 75% (not trivially capped at 100% on Turn 0)
        var run = reads.GetRun(runId).Value;
        using (var equipInitial = await client.PostAsJsonAsync($"/api/v1/runs/{runId}/commands", new
        {
            commandId = Guid.NewGuid(),
            type = "APPLY_LOADOUT",
            expectedSequence = run.Sequence,
            expectedStep = run.Determinism.Step,
            payload = new
            {
                actorId = "player",
                assignments = new Dictionary<string, Guid>
                {
                    ["identity"] = items["gambler_identity_rigged_die"],
                    ["weapon"] = items["gear_crolbia_ivory_dagger"],
                    ["rune_1"] = items["rune_obor_precision"]
                }
            }
        }))
        {
            Assert.True(equipInitial.IsSuccessStatusCode, await equipInitial.Content.ReadAsStringAsync());
        }

        var attrsInitial = await client.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{runId}/character-attributes");
        var attrInitialDict = attrsInitial.GetProperty("attributes").EnumerateArray().ToDictionary(
            item => item.GetProperty("attributeId").GetString()!,
            item => item.GetProperty("effectiveValue").GetSingle());
        Assert.Equal(75f, attrInitialDict["critical_chance"]);
        var guaranteedInitial = attrsInitial.GetProperty("criticalRules")[0].GetProperty("guaranteedTiers").GetSingle();
        Assert.Equal(0f, guaranteedInitial); // 75% crit < 100%, so 0 guaranteed yet

        // Advance through Preparation -> Encounter 1 -> Reward 1 -> Patrol/Elite -> Reward 2 -> Relicary (mid-run identity pivot)
        async Task PostCmdAsync(string type, object payload)
        {
            var r = reads.GetRun(runId).Value;
            using var resp = await client.PostAsJsonAsync($"/api/v1/runs/{runId}/commands", new
            {
                commandId = Guid.NewGuid(),
                type,
                expectedSequence = r.Sequence,
                expectedStep = r.Determinism.Step,
                payload
            });
            Assert.True(resp.IsSuccessStatusCode, $"{type} failed: {await resp.Content.ReadAsStringAsync()}");
        }

        async Task WinSingleTargetEncounterAsync(object[] participants)
        {
            var r = reads.GetRun(runId).Value;
            Guid cid;
            using (var startEnc = await client.PostAsJsonAsync($"/api/v1/runs/{runId}/commands", new
            {
                commandId = Guid.NewGuid(),
                type = "START_ENCOUNTER",
                expectedSequence = r.Sequence,
                expectedStep = r.Determinism.Step,
                payload = new { participants }
            }))
            {
                Assert.True(startEnc.IsSuccessStatusCode, await startEnc.Content.ReadAsStringAsync());
                var receipt = await startEnc.Content.ReadFromJsonAsync<JsonElement>();
                cid = receipt.GetProperty("state").GetProperty("activeEncounterId").GetGuid();
            }

            var cardCosts = new (string CardId, int Cost, bool SelfTarget)[]
            {
                ("gambler_pocket_knife", 0, false),
                ("gambler_rig_the_odds", 1, true),
                ("gambler_all_in", 2, false),
                ("gambler_lucky_feign", 1, false),
                ("gambler_loaded_toss", 1, false),
                ("alisyum_basic_strike", 1, false),
                ("alisyum_basic_guard", 1, true)
            };

            for (var turn = 0; turn < 25; turn++)
            {
                var combatState = await game.GetCombatStateAsync(cid);
                if (combatState.GetProperty("status").GetString() != "ACTIVE")
                    break;

                while (true)
                {
                    var currentCombat = await game.GetCombatStateAsync(cid);
                    if (currentCombat.GetProperty("status").GetString() != "ACTIVE")
                        goto Finished;
                    var energy = currentCombat.GetProperty("actors").EnumerateArray()
                        .First(a => a.GetProperty("instanceId").GetString() == "player")
                        .GetProperty("resources").GetProperty("energy").GetProperty("current").GetInt32();
                    var currentHand = await game.GetPlayableCardIdsAsync(runId);
                    var playable = cardCosts.FirstOrDefault(c => c.Cost <= energy && currentHand.Contains(c.CardId));
                    if (playable.CardId == null)
                        break;
                    var target = playable.SelfTarget ? "player" : "enemy_0";
                    var after = await game.ExecuteActionAsync(cid, "player", target, cardId: playable.CardId, runId: runId);
                    if (after.GetProperty("status").GetString() != "ACTIVE")
                        goto Finished;
                }

                await game.EndTurnAsync(cid, runId);
            }

            Finished:
            var finalCombat = await game.GetCombatStateAsync(cid);
            var runBeforeResolve = reads.GetRun(runId).Value;
            using var resolveResp = await client.PostAsJsonAsync($"/api/v1/runs/{runId}/commands", new
            {
                commandId = Guid.NewGuid(),
                type = "RESOLVE_COMBAT",
                expectedSequence = runBeforeResolve.Sequence,
                expectedStep = finalCombat.GetProperty("step").GetUInt64(),
                payload = new { combatId = cid }
            });
            Assert.True(resolveResp.IsSuccessStatusCode, await resolveResp.Content.ReadAsStringAsync());
        }

        await PostCmdAsync("RESOLVE_NODE", new { currentNodeId = "chapter_opening" });
        await PostCmdAsync("ADVANCE_NODE", new { targetNodeId = "chapter_prepare_0" });
        await PostCmdAsync("CREATE_PREPARATION", new { preparationId = "alisyum_preparation" });
        var prep = reads.GetRun(runId).Value.Preparations.Single();
        await PostCmdAsync("APPLY_PREPARATION_OPTION", new { preparationInstanceId = prep.PreparationInstanceId, optionId = "ready" });
        await PostCmdAsync("RESOLVE_NODE", new { currentNodeId = "chapter_prepare_0" });

        await PostCmdAsync("ADVANCE_NODE", new { targetNodeId = "encounter_1" });
        await WinSingleTargetEncounterAsync(new object[]
        {
            new { instanceId = "player", definitionId = "gambler", sideId = "player", controllerBinding = new { kind = "Player", policyId = (string?)null } },
            new { instanceId = "enemy_0", definitionId = "enemy_crolbia_duelist", sideId = "opposition", controllerBinding = new { kind = "AI", policyId = "gambit" } }
        });

        // Verify encounter_1 exitEffects awarded +18 gold (35 starting + 18 = 53)
        Assert.Equal(53f, reads.GetRun(runId).Value.ResourceState.Current("gold"));

        await PostCmdAsync("ADVANCE_NODE", new { targetNodeId = "reward_1" });
        var sel1 = await game.StartCardSelectionAsync(runId, "alisyum_reward_gambler");
        await game.PickCardsAsync(runId, sel1.GetProperty("selectionInstanceId").GetGuid(), new[] { sel1.GetProperty("options")[0].GetProperty("cardId").GetString()! });
        await PostCmdAsync("RESOLVE_NODE", new { currentNodeId = "reward_1" });

        // Branch to Elite 2 + Relic 2 before reaching Relicary
        await PostCmdAsync("ADVANCE_NODE", new { targetNodeId = "encounter_2_elite" });
        await WinSingleTargetEncounterAsync(new object[]
        {
            new { instanceId = "player", definitionId = "gambler", sideId = "player", controllerBinding = new { kind = "Player", policyId = (string?)null } },
            new { instanceId = "enemy_0", definitionId = "enemy_obor_runesmith", sideId = "opposition", controllerBinding = new { kind = "AI", policyId = "gambit" } }
        });
        Assert.Equal(85f, reads.GetRun(runId).Value.ResourceState.Current("gold")); // 53 + 32

        await PostCmdAsync("ADVANCE_NODE", new { targetNodeId = "reward_2_elite" });
        var sel2 = await game.StartCardSelectionAsync(runId, "alisyum_reward_gambler");
        await game.PickCardsAsync(runId, sel2.GetProperty("selectionInstanceId").GetGuid(), new[] { sel2.GetProperty("options")[0].GetProperty("cardId").GetString()! });
        await PostCmdAsync("RESOLVE_NODE", new { currentNodeId = "reward_2_elite" });

        await PostCmdAsync("ADVANCE_NODE", new { targetNodeId = "relic_2" });
        await PostCmdAsync("ACQUIRE_RELIC", new { relicId = "alisyum_relic_crolbia_die" });
        await PostCmdAsync("RESOLVE_NODE", new { currentNodeId = "relic_2" });

        await PostCmdAsync("ADVANCE_NODE", new { targetNodeId = "relicary_1" });
        await PostCmdAsync("OPEN_INSCRIPTION_OFFER", new { actorId = "player", stationId = "alisyum_relicary" });
        var offer = reads.GetRun(runId).Value.Loadouts!.Offers!.Values.Single();
        Assert.Contains("gambler_identity_broken_wheel", offer.Options);
        Assert.DoesNotContain("pyro_identity_magma_veins", offer.Options);
        await PostCmdAsync("CHOOSE_INSCRIPTION_OFFER", new { offerId = offer.OfferId, inscriptionId = "gambler_identity_broken_wheel" });

        loadout = await client.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{runId}/loadout?actorId=player");
        items = loadout.GetProperty("items").EnumerateArray().ToDictionary(
            row => row.GetProperty("definition").GetProperty("inscriptionId").GetString()!,
            row => row.GetProperty("instance").GetProperty("instanceId").GetGuid());

        // Swap to acquired advanced Gambler identity mod: 10 base + 55 identity + 20 dagger + 15 rune (+15 relic) >= 100% -> 1 guaranteed tier
        await PostCmdAsync("APPLY_LOADOUT", new
        {
            actorId = "player",
            assignments = new Dictionary<string, Guid>
            {
                ["identity"] = items["gambler_identity_broken_wheel"],
                ["weapon"] = items["gear_crolbia_ivory_dagger"],
                ["rune_1"] = items["rune_obor_precision"]
            }
        });

        var attrsAdvanced = await client.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{runId}/character-attributes");
        var rule = attrsAdvanced.GetProperty("criticalRules")[0];
        Assert.Equal(1f, rule.GetProperty("guaranteedTiers").GetSingle());
    }

    [Fact]
    public async Task ServoAndRenegadoRunsSupportExclusiveIdentitiesAndBalancedDecks()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        var game = new GameEngineClientSimulator(client);
        var reads = factory.Services.GetRequiredService<IRunQueryService>();

        // Servo run
        var servoRunId = await game.StartRunAsync("alisyum", "alisyum_servo_run", "player", 4203, "alisyum_servo_campaign");
        await AdvanceOpeningAsync(client, reads, servoRunId, "alisyum_opening_servo");
        var servoLoadout = await client.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{servoRunId}/loadout?actorId=player");
        var servoItems = servoLoadout.GetProperty("items").EnumerateArray().ToDictionary(
            row => row.GetProperty("definition").GetProperty("inscriptionId").GetString()!,
            row => row.GetProperty("instance").GetProperty("instanceId").GetGuid());
        var servoRun = reads.GetRun(servoRunId).Value;
        using (var equipServo = await client.PostAsJsonAsync($"/api/v1/runs/{servoRunId}/commands", new
        {
            commandId = Guid.NewGuid(),
            type = "APPLY_LOADOUT",
            expectedSequence = servoRun.Sequence,
            expectedStep = servoRun.Determinism.Step,
            payload = new
            {
                actorId = "player",
                assignments = new Dictionary<string, Guid>
                {
                    ["identity"] = servoItems["baron_identity_first_oath"],
                    ["weapon"] = servoItems["gear_nosirian_blood_edge"]
                }
            }
        }))
        {
            Assert.True(equipServo.IsSuccessStatusCode, await equipServo.Content.ReadAsStringAsync());
        }

        // Renegado run
        var renegadoRunId = await game.StartRunAsync("alisyum", "alisyum_renegado_run", "player", 4204, "alisyum_renegado_campaign");
        await AdvanceOpeningAsync(client, reads, renegadoRunId, "alisyum_opening_renegado");
        var renegadoLoadout = await client.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{renegadoRunId}/loadout?actorId=player");
        var renegadoItems = renegadoLoadout.GetProperty("items").EnumerateArray().ToDictionary(
            row => row.GetProperty("definition").GetProperty("inscriptionId").GetString()!,
            row => row.GetProperty("instance").GetProperty("instanceId").GetGuid());
        var renegadoRun = reads.GetRun(renegadoRunId).Value;
        using (var equipRenegado = await client.PostAsJsonAsync($"/api/v1/runs/{renegadoRunId}/commands", new
        {
            commandId = Guid.NewGuid(),
            type = "APPLY_LOADOUT",
            expectedSequence = renegadoRun.Sequence,
            expectedStep = renegadoRun.Determinism.Step,
            payload = new
            {
                actorId = "player",
                assignments = new Dictionary<string, Guid>
                {
                    ["identity"] = renegadoItems["vitreous_identity_shattered_shell"],
                    ["armor"] = renegadoItems["gear_calidor_vitreous_plate"]
                }
            }
        }))
        {
            Assert.True(equipRenegado.IsSuccessStatusCode, await equipRenegado.Content.ReadAsStringAsync());
        }

        var renegadoAttrs = await client.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{renegadoRunId}/character-attributes");
        var renegadoAttrDict = renegadoAttrs.GetProperty("attributes").EnumerateArray().ToDictionary(
            item => item.GetProperty("attributeId").GetString()!,
            item => item.GetProperty("effectiveValue").GetSingle());
        Assert.Equal(25f, renegadoAttrDict["armor"]); // 10 base + 15 Calidor plate
        Assert.True(renegadoAttrDict["block_efficiency"] >= 39f); // 0.25 identity + 0.15 plate = 40%

        // Verify all 4 archetype reward pools have 30 cards each (120 archetype cards total)
        var revs = await client.GetFromJsonAsync<JsonElement>("/api/v1/content/revisions?configName=alisyum");
        var rev = revs.GetProperty("currentRevision").GetString()!;
        foreach (var poolId in new[]
        {
            "alisyum_rewards_pyro",
            "alisyum_rewards_servo",
            "alisyum_rewards_gambler",
            "alisyum_rewards_renegado"
        })
        {
            var pool = await client.GetFromJsonAsync<JsonElement>($"/api/v1/content/card-pools/{poolId}?configName=alisyum&revision={rev}");
            Assert.Equal(30, pool.GetProperty("definition").GetProperty("explicitCardIds").GetArrayLength());
        }
    }
}

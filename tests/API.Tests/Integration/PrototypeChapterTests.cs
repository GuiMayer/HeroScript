using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
using API.Tests.Helpers;
using Core.Combat.LegalActions;
using Core.Combat.Models;
using Core.Determinism;
using Core.Run;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace API.Tests.Integration;

public sealed class PrototypeChapterTests(ITestOutputHelper output)
{
    [Fact]
    public async Task PublishedChapterCompletesWithBossRetryDurableDialogueAndNoCheats()
    {
        using var factory = new TestWebApplicationFactory();
        factory.UseKestrel(options => options.Listen(System.Net.IPAddress.Loopback, 0));
        using var client = factory.CreateClient();
        var settings = await client.GetFromJsonAsync<JsonElement>("/api/v1/content/settings");
        var setting = settings.GetProperty("items").EnumerateArray().Single(value => value.GetProperty("settingId").GetString() == "prototype-test");
        Assert.Equal("prototype_chapter", setting.GetProperty("launch").GetProperty("modeId").GetString());
        var game = new GameEngineClientSimulator(client);
        var id = await game.StartRunAsync("prototype-test", "prototype_chapter", "player", 8814, "prototype_chapter");
        var reads = factory.Services.GetRequiredService<IRunQueryService>();
        var legality = factory.Services.GetRequiredService<ILegalActionQueryService>();
        RunState Current() => reads.GetRun(id).Value;
        var original = Current();
        var originalHash = CanonicalJson.ComputeHash(original);
        var visits = new HashSet<string>();
        var retriedBoss = false;
        var hasLostBoss = false;
        var commandOrdinal = 0;
        var recorded = new List<(string Route, object Envelope, string Hash)>();
        var timings = new List<(string Type, double Post, double Session)>();
        output.WriteLine($"Runtime: {RuntimeInformation.FrameworkDescription}; OS: {RuntimeInformation.OSDescription}; logical processors: {Environment.ProcessorCount}");
        async Task Send(string type, object payload, Guid? combatId = null)
        {
            var run = Current();
            var step = combatId.HasValue || type == "RESOLVE_COMBAT" ? run.GetActiveEncounter()!.Combat.Determinism.Step : run.Determinism.Step;
            var route = combatId.HasValue ? $"/api/v1/combats/{combatId}/commands" : $"/api/v1/runs/{id}/commands";
            var envelope = new { commandId = Guid.Parse($"14000000-0000-4000-8000-{++commandOrdinal:000000000000}"), type,
                expectedSequence = run.Sequence, expectedStep = step, payload };
            using var request = new HttpRequestMessage(HttpMethod.Post, route) { Content = JsonContent.Create(envelope) };
            request.Headers.Add("Prefer", "return=minimal");
            var watch = Stopwatch.StartNew();
            using var response = await client.SendAsync(request);
            var postMs = watch.Elapsed.TotalMilliseconds;
            Assert.True(response.IsSuccessStatusCode, $"{run.CurrentNodeId}: {type}: {await response.Content.ReadAsStringAsync()}");
            var hash = CanonicalJson.ComputeHash(Current());
            recorded.Add((route, envelope, hash));
            watch.Restart();
            using var session = await client.GetAsync($"/api/v1/runs/{id}/session");
            var view = await session.Content.ReadFromJsonAsync<JsonElement>();
            var sessionMs = watch.Elapsed.TotalMilliseconds;
            Assert.True(session.IsSuccessStatusCode, view.GetRawText());
            Assert.Equal(hash, view.GetProperty("version").GetProperty("stateFingerprint").GetString());
            Assert.Equal(hash, CanonicalJson.ComputeHash(Current()));
            timings.Add((type, postMs, sessionMs));
        }
        for (var gesture = 0; gesture < 600 && Current().Lifecycle == RunLifecycleState.Active; gesture++)
        {
            var run = Current();
            visits.Add(run.CurrentNodeId!);
            // Every activity snapshot is readable without losing immutable identities.
            Assert.Equal(CanonicalJson.ComputeHash(run), CanonicalJson.ComputeHash(JsonSerializer.Deserialize<RunState>(JsonSerializer.Serialize(run))!));
            if (run.GetActiveEncounter() is { } encounter)
            {
                var combat = encounter.Combat;
                if (combat.Status != CombatStatus.ACTIVE)
                {
                    if (run.CurrentNodeId == "chapter_boss" && combat.Status == CombatStatus.DEFEAT) hasLostBoss = true;
                    else Assert.True(combat.Status == CombatStatus.VICTORY, $"{run.CurrentNodeId}: {combat.Status} after {combat.CurrentTurn} rounds");
                    await Send("RESOLVE_COMBAT", new { combatId = combat.CombatId });
                    continue;
                }
                if (run.CurrentNodeId == "chapter_boss" && !hasLostBoss)
                {
                    // Deliberately lose through normal END_TURN, never alter authoritative state.
                    await Send("END_TURN", new { actorId = "player" }, combat.CombatId);
                    continue;
                }
                var set = legality.Get(combat.CombatId, "player");
                Assert.True(set.IsSuccess, set.IsFailure ? set.Error : null);
                float Score(LegalActionCandidate candidate)
                {
                    var next = candidate.SuccessorCombat;
                    var enemies = combat.Actors.Values.Where(actor => actor.SideId == "opposition").Select(actor => actor.InstanceId).ToArray();
                    var damage = enemies.Sum(actor => combat.GetActor(actor)!.ResourceState.Current("health") - (next.GetActor(actor)?.ResourceState.Current("health") ?? 0));
                    var block = next.GetActor("player")!.ResourceState.Current("block") - combat.GetActor("player")!.ResourceState.Current("block");
                    var heal = next.GetActor("player")!.ResourceState.Current("health") - combat.GetActor("player")!.ResourceState.Current("health");
                    var statuses = enemies.Sum(actor => (next.StatusEffects.TryGetValue(actor, out var afterStatuses) ? afterStatuses.Length : 0) - (combat.StatusEffects.TryGetValue(actor, out var beforeStatuses) ? beforeStatuses.Length : 0));
                    return damage * 5 + Math.Min(12, Math.Max(0, block)) + heal * 2 + statuses * 8;
                }
                var chosen = set.Value.Candidates.Where(candidate => candidate.Source == LegalActionSource.Card)
                    .OrderByDescending(Score).ThenBy(candidate => candidate.CandidateId, StringComparer.Ordinal).FirstOrDefault();
                if (chosen != null && Score(chosen) > 0)
                {
                    var command = chosen.Command;
                    await Send("PLAY_CARD", new CombatGameplayCommand(ActorId: "player", CardInstanceId: command.CardInstanceId,
                        TargetId: command.TargetId, TargetIds: command.TargetIds, CostOptionId: command.CostOptionId, PaymentSelections: command.PaymentSelections), combat.CombatId);
                }
                else await Send("END_TURN", new { actorId = "player" }, combat.CombatId);
                continue;
            }
            var node = run.Map.Nodes.Single(value => value.NodeId == run.CurrentNodeId);
            if (run.Map.ResolvedNodeIds.Contains(node.NodeId))
            {
                Assert.Single(node.NextNodeIds);
                await Send("ADVANCE_NODE", new { targetNodeId = node.NextNodeIds[0] });
                continue;
            }
            switch (node.Activity.Type)
            {
                case RunActivityType.Dialogue:
                    var dialogue = run.Dialogues.LastOrDefault(value => value.ActivityNodeId == node.NodeId);
                    if (dialogue == null) await Send("START_DIALOGUE", new { dialogueId = node.Activity.DefinitionId });
                    else if (!dialogue.Completed) await Send("CHOOSE_DIALOGUE_OPTION", new { dialogueInstanceId = dialogue.DialogueInstanceId, nodeId = dialogue.CurrentNodeId, choiceId = "continue" });
                    else await Send("RESOLVE_NODE", new { currentNodeId = node.NodeId });
                    break;
                case RunActivityType.Encounter:
                    var start = reads.GetAvailableCommands(id).Value.Single(value => value.Type == "START_ENCOUNTER");
                    if (hasLostBoss && node.NodeId == "chapter_boss")
                    {
                        var loadout = await client.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{id}/loadout?actorId=player");
                        var edge = loadout.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("definition").GetProperty("inscriptionId").GetString() == "prototype_edge");
                        var focus = loadout.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("definition").GetProperty("inscriptionId").GetString() == "prototype_focus");
                        await Send("APPLY_LOADOUT", new { actorId = "player", assignments = new Dictionary<string, Guid> { ["trace_1"] = focus.GetProperty("instance").GetProperty("instanceId").GetGuid(), ["trace_2"] = edge.GetProperty("instance").GetProperty("instanceId").GetGuid() } });
                        retriedBoss = true;
                    }
                    await Send("START_ENCOUNTER", start.ValidPayload);
                    if (retriedBoss) Assert.Equal(100, Current().GetActiveEncounter()!.Combat.GetActor("player")!.ResourceState.Current("health"));
                    break;
                case RunActivityType.CardSelection:
                    var selection = run.CardSelections.LastOrDefault(value => value.NodeId == node.NodeId);
                    if (selection == null) await Send("CREATE_CARD_SELECTION", new { selectionId = node.Activity.DefinitionId });
                    else if (!selection.Completed) await Send("DECOMPOSE_CARD_REWARD", new { selectionInstanceId = selection.SelectionInstanceId, cardId = selection.Options[0].CardId });
                    else await Send("RESOLVE_NODE", new { currentNodeId = node.NodeId });
                    break;
                case RunActivityType.Preparation:
                    var preparation = run.Preparations.LastOrDefault(value => value.NodeId == node.NodeId);
                    if (preparation == null) await Send("CREATE_PREPARATION", new { preparationId = node.Activity.DefinitionId });
                    else if (!preparation.Options.Any(value => value.Applied)) await Send("APPLY_PREPARATION_OPTION", new { preparationInstanceId = preparation.PreparationInstanceId, optionId = "ready" });
                    else await Send("RESOLVE_NODE", new { currentNodeId = node.NodeId });
                    break;
                case RunActivityType.CardUpgrade:
                    await Send("RESOLVE_NODE", new { currentNodeId = node.NodeId }); // Valid explicit refusal.
                    break;
                default: Assert.Fail($"Unexpected activity {node.Activity.Type}"); break;
            }
        }
        var final = Current();
        output.WriteLine($"Chapter commits: {final.Sequence}; encounters: {final.Encounters.Length}; parts: {final.ResourceState.Current("parts")}");
        Assert.Equal(RunLifecycleState.Completed, final.Lifecycle);
        Assert.Equal(12, visits.Count);
        Assert.True(retriedBoss && hasLostBoss);
        Assert.InRange(final.Encounters.Length, 4, 6);
        Assert.Equal(3, final.Encounters.Count(encounter => encounter.Combat.Status == CombatStatus.VICTORY));
        Assert.Equal(3, final.NarrativeFlags.Count);
        Assert.Equal("true", final.NarrativeFlags["prototype.chapter.completed"]);
        Assert.Equal(9, final.ResourceState.Current("parts"));
        Assert.Equal(originalHash, CanonicalJson.ComputeHash(original));
        var expected = CanonicalJson.ComputeHash(final);
        static double Percentile(IEnumerable<double> samples, double quantile)
        {
            var ordered = samples.Order().ToArray();
            return ordered[(int)Math.Ceiling(ordered.Length * quantile) - 1];
        }
        foreach (var group in timings.GroupBy(sample => sample.Type))
        {
            var values = group.Select(sample => sample.Post).ToArray();
            output.WriteLine($"CHAPTER_POST type={group.Key}; n={values.Length}; first={values[0]:F2}ms; p50={Percentile(values, .5):F2}ms; p95={Percentile(values, .95):F2}ms; p99={Percentile(values, .99):F2}ms; max={values.Max():F2}ms");
            if (values.Length > 1) output.WriteLine($"CHAPTER_WARM type={group.Key}; n={values.Length - 1}; p95={Percentile(values.Skip(1), .95):F2}ms; max={values.Skip(1).Max():F2}ms");
        }
        output.WriteLine($"CHAPTER_SESSION n={timings.Count}; p95={Percentile(timings.Select(sample => sample.Session), .95):F2}ms; p99={Percentile(timings.Select(sample => sample.Session), .99):F2}ms; max={timings.Max(sample => sample.Session):F2}ms");
        var directory = factory.PersistenceRoot;
        client.Dispose(); factory.Dispose();
        using var restarted = new TestWebApplicationFactory(directory);
        using var resumed = restarted.CreateClient();
        Assert.Equal(expected, CanonicalJson.ComputeHash(restarted.Services.GetRequiredService<IRunQueryService>().GetRun(id).Value));
        var replay = await restarted.Services.GetRequiredService<Core.Run.Replay.IRunReplayService>().VerifyAsync(id);
        Assert.True(replay.IsValid, string.Join("; ", replay.Errors));
        Assert.True(replay.Reexecuted);
        Assert.Equal(expected, replay.ActualFinalHash);
        resumed.Dispose(); restarted.Dispose();
        // Fresh store, same published revision, seed and command identities: compare every commit.
        using var independent = new TestWebApplicationFactory();
        independent.UseKestrel(options => options.Listen(System.Net.IPAddress.Loopback, 0));
        using var secondClient = independent.CreateClient();
        var secondGame = new GameEngineClientSimulator(secondClient);
        Assert.Equal(id, await secondGame.StartRunAsync("prototype-test", "prototype_chapter", "player", 8814, "prototype_chapter"));
        var secondReads = independent.Services.GetRequiredService<IRunQueryService>();
        Assert.Equal(originalHash, CanonicalJson.ComputeHash(secondReads.GetRun(id).Value));
        foreach (var item in recorded)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, item.Route) { Content = JsonContent.Create(item.Envelope) };
            request.Headers.Add("Prefer", "return=minimal");
            using var response = await secondClient.SendAsync(request);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            Assert.Equal(item.Hash, CanonicalJson.ComputeHash(secondReads.GetRun(id).Value));
        }
        output.WriteLine($"CHAPTER_DETERMINISM independent commits compared={recorded.Count}; initial={originalHash}; final={expected}");
    }
}

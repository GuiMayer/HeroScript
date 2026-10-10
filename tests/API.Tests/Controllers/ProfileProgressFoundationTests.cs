using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Core.Abstractions.Persistence;
using Core.Meta;
using Core.Run;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using API.Tests.Helpers;

namespace API.Tests.Controllers;

public sealed class ProfileProgressFoundationTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task RealEncounterVictory_GrantsDependentOptionsInItsResolutionCommit_AndReplaysWithoutGrantingAgain()
    {
        using var client = factory.CreateClient();
        var game = new GameEngineClientSimulator(client);
        var player = "unlock-" + Guid.NewGuid().ToString("N");
        var runId = await game.StartRunAsync("ascendant", "ascendant_showcase_run", player, 150, "ascendant_showcase");
        await game.StartRunAsync("ascendant", "ascendant_showcase_run", player, 151, "ascendant_showcase");
        await AvailableCommand(client, runId, RunCommandTypes.StartEncounter);
        await game.DrawCardsAsync(runId, 5);
        var combatId = (await game.GetRunStateAsync(runId)).GetProperty("activeEncounterId").GetGuid();
        var status = "ACTIVE";
        for (var turn = 0; turn < 10 && status == "ACTIVE"; turn++)
        {
            for (var action = 0; action < 3 && status == "ACTIVE"; action++)
            {
                var combat = await game.GetCombatStateAsync(combatId);
                var actor = combat.GetProperty("actors").EnumerateArray().Single(item => item.GetProperty("instanceId").GetString() == player);
                var energy = actor.GetProperty("resources").GetProperty("energy").GetProperty("current").GetSingle();
                var cards = await game.GetPlayableCardIdsAsync(runId);
                var card = new[] { "ascendant_critical_lance", "ascendant_elemental_burst", "ascendant_strike", "ascendant_fracture" }
                    .FirstOrDefault(id => cards.Contains(id) && energy >= (id == "ascendant_critical_lance" ? 2 : 1));
                if (card == null) break;
                var result = await game.ExecuteActionAsync(combatId, player, "drone", cardId: card, runId: runId);
                status = result.GetProperty("status").GetString()!;
            }
            if (status == "ACTIVE") status = (await game.EndTurnAsync(combatId, runId)).GetProperty("status").GetString()!;
        }
        Assert.Equal("VICTORY", status);
        var progress = factory.Services.GetRequiredService<IProfileProgressSnapshotReader>();
        Assert.Empty((await progress.ReadAsync(player, "ascendant")).Grants); // Only the canonical resolution contributes.
        await AvailableCommand(client, runId, RunCommandTypes.ResolveCombat);
        var profile = await progress.ReadAsync(player, "ascendant");
        Assert.Equal(new[] { "critical_calibration_option", "support_link_option" }, profile.Grants.Keys);
        var run = await game.GetRunStateAsync(runId);
        var commit = await factory.Services.GetRequiredService<IRunCommitReader>().LoadCommitAsync(runId, run.GetProperty("sequence").GetInt32());
        Assert.Equal(2, commit!.ProfileProgress!.Grants.Length);
        Assert.Contains(commit.ProfileProgress.Contributions, item => item.Kind == UnlockConditionKind.EncounterCompleted && item.NodeId == "calibration");
        Assert.All(commit.ProfileProgress.Grants, grant => Assert.Equal(commit.RootCommand.CommandId, grant.CommandId));
        using var replay = await client.PostAsync($"/api/v1/runs/{runId}/verify", null);
        Assert.True(replay.IsSuccessStatusCode, await replay.Content.ReadAsStringAsync());
        Assert.True((await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isValid").GetBoolean());
        Assert.Equal(profile.Revision, (await progress.ReadAsync(player, "ascendant")).Revision);
    }

    private static async Task AvailableCommand(HttpClient client, Guid runId, string type)
    {
        var available = await client.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{runId}/available-commands");
        var command = available.GetProperty("commands").EnumerateArray().Single(item => item.GetProperty("type").GetString() == type);
        using var response = await client.PostAsJsonAsync($"/api/v1/runs/{runId}/commands", new {
            commandId = Guid.NewGuid(), type, payload = command.GetProperty("validPayload"),
            expectedSequence = command.GetProperty("expectedSequence").GetInt32(), expectedStep = command.GetProperty("expectedStep").GetUInt64() });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PublishedPolicyContributesAtomically_DuplicateCreationAndReplayNeverRepublishProgress()
    {
        using var client = factory.CreateClient();
        var catalog = await client.GetFromJsonAsync<JsonElement>("/api/v1/content/settings");
        var ascendant = catalog.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("settingId").GetString() == "ascendant");
        var player = "progress-" + Guid.NewGuid().ToString("N");
        var payload = new { settingId = "ascendant", runDefinitionId = "ascendant_showcase_run", modeId = "ascendant_showcase",
            contentRevision = ascendant.GetProperty("currentRevision").GetString(), playerEntityId = player, seed = 42UL };
        using var start = await client.PostAsJsonAsync("/api/v1/runs", payload);
        Assert.True(start.IsSuccessStatusCode, await start.Content.ReadAsStringAsync());
        var state = await start.Content.ReadFromJsonAsync<JsonElement>();
        var runId = state.GetProperty("runId").GetGuid();
        Assert.Equal("ascendant_options", state.GetProperty("resolvedMode").GetProperty("profileProgressPolicy").GetProperty("profileProgressPolicyId").GetString());
        var commits = factory.Services.GetRequiredService<IRunCommitReader>();
        var first = await commits.LoadCommitAsync(runId, 1);
        Assert.Equal(UnlockConditionKind.AttemptStarted, Assert.Single(first!.ProfileProgress!.Contributions).Kind);
        var before = await client.GetFromJsonAsync<JsonElement>($"/api/v1/profiles/{player}?settingId=ascendant");
        Assert.Equal(1, before.GetProperty("progressSequence").GetInt64());
        Assert.Empty(before.GetProperty("unlockProofs").EnumerateArray());
        using var retry = await client.PostAsJsonAsync("/api/v1/runs", payload);
        // Creation still rejects repeated deterministic inputs. Creation receipts/eligibility are stage 7.
        Assert.Equal(HttpStatusCode.BadRequest, retry.StatusCode);
        Assert.Contains("Run already exists", await retry.Content.ReadAsStringAsync());
        using var replay = await client.PostAsync($"/api/v1/runs/{runId}/verify", null);
        Assert.True(replay.IsSuccessStatusCode, await replay.Content.ReadAsStringAsync());
        Assert.True((await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isValid").GetBoolean());
        var after = await client.GetFromJsonAsync<JsonElement>($"/api/v1/profiles/{player}?settingId=ascendant");
        Assert.Equal(before.GetRawText(), after.GetRawText());
        Assert.Single(await commits.ListCommitSequencesAsync(runId));
        var progress = factory.Services.GetRequiredService<IProfileProgressSnapshotReader>();
        Assert.Empty((await progress.ReadAsync(player, "default")).Contributions);
        Assert.Empty((await progress.ReadAsync(player + "-other", "ascendant")).Contributions);
        var secondPayload = payload with { seed = 43UL };
        using var second = await client.PostAsJsonAsync("/api/v1/runs", secondPayload);
        Assert.True(second.IsSuccessStatusCode, await second.Content.ReadAsStringAsync());
        Assert.Equal(2, (await progress.ReadAsync(player, "ascendant")).Sequence);
        Assert.Empty((await progress.ReadAsync(player, "ascendant")).Grants);
    }
}

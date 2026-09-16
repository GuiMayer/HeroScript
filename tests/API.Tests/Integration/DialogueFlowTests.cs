using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using API.Tests.Helpers;
using Xunit;

namespace API.Tests.Integration;

public sealed class DialogueFlowTests
{
    [Fact]
    public async Task DialogueChoices_AreAtomicIdempotentPersistedAndReplayable()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        var game = new GameEngineClientSimulator(client);
        var runId = await game.StartRunAsync(modeId: "dialogue_demo", seed: 9132026);
        var initial = await game.GetRunStateAsync(runId);
        await Send(client, runId, "START_DIALOGUE", new { dialogueId = "ember_keeper" });
        var started = await game.GetRunStateAsync(runId);
        var dialogue = started.GetProperty("dialogues")[0];
        var instance = dialogue.GetProperty("dialogueInstanceId").GetGuid();
        var advertised = await client.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{runId}/available-commands");
        var validPayload = advertised.GetProperty("commands").EnumerateArray().Single(command => command.GetProperty("type").GetString() == "CHOOSE_DIALOGUE_OPTION").GetProperty("validPayload");
        Assert.Equal(instance, validPayload.GetProperty("dialogueInstanceId").GetGuid());
        Assert.Equal("greeting", validPayload.GetProperty("nodeId").GetString());
        Assert.DoesNotContain(dialogue.GetProperty("choices").EnumerateArray(), choice => choice.GetProperty("choiceId").GetString() == "secret");

        var choicePayload = new { dialogueInstanceId = instance, nodeId = "greeting", choiceId = "trade" };
        var identity = Guid.NewGuid();
        var envelope = new { commandId = identity, type = "CHOOSE_DIALOGUE_OPTION", expectedSequence = started.GetProperty("sequence").GetInt32(), expectedStep = started.GetProperty("step").GetUInt64(), payload = choicePayload };
        using var first = await client.PostAsJsonAsync($"/api/v1/runs/{runId}/commands", envelope);
        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());
        var receipt = await first.Content.ReadFromJsonAsync<JsonElement>();
        using var duplicate = await client.PostAsJsonAsync($"/api/v1/runs/{runId}/commands", envelope);
        var duplicateReceipt = await duplicate.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(duplicate.IsSuccessStatusCode, duplicateReceipt.GetRawText());
        Assert.True(duplicateReceipt.GetProperty("duplicate").GetBoolean());
        Assert.Equal(receipt.GetProperty("stateHash").GetString(), duplicateReceipt.GetProperty("stateHash").GetString());
        var after = await game.GetRunStateAsync(runId);
        Assert.Equal(10, after.GetProperty("resources").GetProperty("gold").GetProperty("current").GetSingle());
        Assert.Equal(VisibleCardCount(initial) + 1, VisibleCardCount(after));
        Assert.Equal("accepted", after.GetProperty("narrativeFlags").GetProperty("keeper.trade").GetString());

        using var branchResponse = await client.PostAsJsonAsync($"/api/v1/runs/{runId}/branches", new { sourceSequence = started.GetProperty("sequence").GetInt32(), branchKey = "decline-the-offer" });
        Assert.True(branchResponse.IsSuccessStatusCode, await branchResponse.Content.ReadAsStringAsync());
        var branchId = (await branchResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("runId").GetGuid();
        await Send(client, branchId, "CHOOSE_DIALOGUE_OPTION", new { dialogueInstanceId = instance, nodeId = "greeting", choiceId = "leave" });
        var branch = await game.GetRunStateAsync(branchId);
        Assert.Equal(30, branch.GetProperty("resources").GetProperty("gold").GetProperty("current").GetSingle());
        Assert.True(branch.GetProperty("dialogues")[0].GetProperty("completed").GetBoolean());
        Assert.False(branch.GetProperty("narrativeFlags").TryGetProperty("keeper.trade", out _));
        Assert.Equal(after.GetProperty("stateHash").GetString(), (await game.GetRunStateAsync(runId)).GetProperty("stateHash").GetString());
        using var branchVerify = await client.PostAsync($"/api/v1/runs/{branchId}/verify", null);
        Assert.True((await branchVerify.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isValid").GetBoolean());

        await Send(client, runId, "CHOOSE_DIALOGUE_OPTION", new { dialogueInstanceId = instance, nodeId = "gift", choiceId = "back" });
        var returned = await game.GetRunStateAsync(runId);
        var originalHash = returned.GetProperty("stateHash").GetString();
        using var rejected = await Post(client, runId, "CHOOSE_DIALOGUE_OPTION", choicePayload);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        Assert.Equal(originalHash, (await game.GetRunStateAsync(runId)).GetProperty("stateHash").GetString());
        await Send(client, runId, "CHOOSE_DIALOGUE_OPTION", new { dialogueInstanceId = instance, nodeId = "greeting", choiceId = "ask" });
        await Send(client, runId, "CHOOSE_DIALOGUE_OPTION", new { dialogueInstanceId = instance, nodeId = "warning", choiceId = "back" });
        await Send(client, runId, "CHOOSE_DIALOGUE_OPTION", new { dialogueInstanceId = instance, nodeId = "greeting", choiceId = "secret" });

        // A fresh server reads only durable state and the immutable definition captured at START_DIALOGUE.
        using var restartedFactory = new TestWebApplicationFactory(factory.PersistenceRoot);
        using var reconnected = restartedFactory.CreateClient();
        var reconnectedGame = new GameEngineClientSimulator(reconnected);
        var recovered = await reconnectedGame.GetRunStateAsync(runId);
        Assert.Equal("secret", recovered.GetProperty("dialogues")[0].GetProperty("nodeId").GetString());
        Assert.True(recovered.GetProperty("dialogues")[0].GetProperty("transcript").GetArrayLength() > 3);
        await Send(reconnected, runId, "CHOOSE_DIALOGUE_OPTION", new { dialogueInstanceId = instance, nodeId = "secret", choiceId = "leave" });
        await Send(reconnected, runId, "RESOLVE_NODE", new { currentNodeId = "keeper" });
        await Send(reconnected, runId, "ADVANCE_NODE", new { targetNodeId = "return" });
        await Send(reconnected, runId, "START_DIALOGUE", new { dialogueId = "ember_keeper" });
        var next = await reconnectedGame.GetRunStateAsync(runId);
        var nextDialogue = next.GetProperty("dialogues")[1];
        Assert.Contains(nextDialogue.GetProperty("choices").EnumerateArray(), choice => choice.GetProperty("choiceId").GetString() == "secret" && choice.GetProperty("available").GetBoolean());
        Assert.NotEqual(instance, nextDialogue.GetProperty("dialogueInstanceId").GetGuid());
        await Send(reconnected, runId, "CHOOSE_DIALOGUE_OPTION", new { dialogueInstanceId = nextDialogue.GetProperty("dialogueInstanceId").GetGuid(), nodeId = "greeting", choiceId = "leave" });
        await Send(reconnected, runId, "RESOLVE_NODE", new { currentNodeId = "return" });
        Assert.Equal("Completed", (await reconnectedGame.GetRunStateAsync(runId)).GetProperty("lifecycle").GetString());
        using var verify = await reconnected.PostAsync($"/api/v1/runs/{runId}/verify", null);
        var verification = await verify.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(verify.IsSuccessStatusCode && verification.GetProperty("isValid").GetBoolean(), verification.GetRawText());
    }

    private static int VisibleCardCount(JsonElement run) => run.GetProperty("cardZones")
        .GetProperty("zones").EnumerateArray()
        .Sum(zone => zone.GetProperty("cards").GetArrayLength());

    private static async Task<HttpResponseMessage> Post(HttpClient client, Guid runId, string type, object payload)
    {
        var run = await client.GetFromJsonAsync<JsonElement>($"/api/v1/runs/{runId}");
        return await client.PostAsJsonAsync($"/api/v1/runs/{runId}/commands", new
        {
            commandId = Guid.NewGuid(), type, payload,
            expectedSequence = run.GetProperty("sequence").GetInt32(), expectedStep = run.GetProperty("step").GetUInt64()
        });
    }

    private static async Task Send(HttpClient client, Guid runId, string type, object payload)
    {
        using var response = await Post(client, runId, type, payload);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }
}

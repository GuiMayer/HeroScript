using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace API.Tests.Controllers;

[Trait("Category", "Integration")]
public sealed class CombatSandboxIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public CombatSandboxIntegrationTests(TestWebApplicationFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task SandboxScenario_IsValidatedLaunchedAndIdempotentlyReopened()
    {
        var attemptKey = $"api-sandbox-{Guid.NewGuid():N}";
        var scenario = CreateScenario(attemptKey);

        using var validation = await _client.PostAsJsonAsync("/api/v1/sandbox/scenarios/validate", scenario);
        var validated = await validation.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, validation.StatusCode);
        Assert.Equal(64, validated.GetProperty("scenarioHash").GetString()!.Length);
        Assert.Equal("hero", validated.GetProperty("hero").GetProperty("entityId").GetString());

        using var launch = await _client.PostAsJsonAsync("/api/v1/sandbox/runs", scenario);
        var first = await launch.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, launch.StatusCode);
        Assert.False(first.GetProperty("duplicate").GetBoolean());
        var runId = first.GetProperty("run").GetProperty("runId").GetGuid();
        var combatId = first.GetProperty("combat").GetProperty("combatId").GetGuid();
        Assert.Equal("combat_sandbox", first.GetProperty("run").GetProperty("modeId").GetString());
        Assert.Equal("goblin_a", first.GetProperty("combat").GetProperty("enemies")[0].GetProperty("entityId").GetString());

        using var repeated = await _client.PostAsJsonAsync("/api/v1/sandbox/runs", scenario);
        var second = await repeated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.True(second.GetProperty("duplicate").GetBoolean());
        Assert.Equal(runId, second.GetProperty("run").GetProperty("runId").GetGuid());
        Assert.Equal(combatId, second.GetProperty("combat").GetProperty("combatId").GetGuid());

        using var nextAttempt = await _client.PostAsJsonAsync(
            "/api/v1/sandbox/runs",
            CreateScenario($"{attemptKey}-next"));
        var nextAttemptLaunch = await nextAttempt.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(nextAttempt.StatusCode == HttpStatusCode.OK, nextAttemptLaunch.GetRawText());
        Assert.NotEqual(runId, nextAttemptLaunch.GetProperty("run").GetProperty("runId").GetGuid());

        using var snapshotResponse = await _client.GetAsync($"/api/v1/sandbox/runs/{runId}/snapshot");
        var snapshot = await snapshotResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, snapshotResponse.StatusCode);
        Assert.Equal(runId, snapshot.GetProperty("run").GetProperty("runId").GetGuid());
        Assert.Equal(combatId, snapshot.GetProperty("combat").GetProperty("combatId").GetGuid());
        Assert.Equal(5, snapshot.GetProperty("hand").GetArrayLength());
        Assert.Contains(
            snapshot.GetProperty("combat").GetProperty("actors").EnumerateArray(),
            actor => actor.GetProperty("entityId").GetString() == "goblin_a");
        var intents = snapshot.GetProperty("combat")
            .GetProperty("activation")
            .GetProperty("intents")
            .EnumerateArray()
            .ToArray();
        Assert.Single(intents);
        Assert.Equal("goblin_a", intents[0].GetProperty("actorId").GetString());
        Assert.Equal("hero", intents[0].GetProperty("targetId").GetString());
        Assert.Equal("Attack", intents[0].GetProperty("telegraphType").GetString());
        var originalEnemyHealth = Health(snapshot, "goblin_a");

        var basicAttack = snapshot.GetProperty("hand").EnumerateArray()
            .First(card => card.GetProperty("definitionId").GetString() == "basic_attack");
        var simulationRequest = new
        {
            sourceRunId = runId,
            sourceSequence = first.GetProperty("run").GetProperty("sequence").GetInt32(),
            commands = new object[]
            {
                new
                {
                    type = "EXECUTE_ACTION",
                    payload = new
                    {
                        actorId = "hero",
                        actionType = 0,
                        targetId = "goblin_a",
                        cardId = basicAttack.GetProperty("cardInstanceId").GetGuid()
                    }
                },
                new { type = "END_TURN", payload = new { actorId = "hero" } }
            }
        };
        using var simulationResponse = await _client.PostAsJsonAsync("/api/v1/simulations", simulationRequest);
        var simulation = await simulationResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(simulationResponse.StatusCode == HttpStatusCode.OK, simulation.GetRawText());
        Assert.Equal(2, simulation.GetProperty("commandsExecuted").GetInt32());
        Assert.Equal(2, simulation.GetProperty("timeline").GetArrayLength());
        var simulationId = simulation.GetProperty("simulationId").GetGuid();

        using var repeatSimulationResponse = await _client.PostAsJsonAsync("/api/v1/simulations", simulationRequest);
        var repeatedSimulation = await repeatSimulationResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(
            repeatSimulationResponse.StatusCode == HttpStatusCode.OK,
            repeatedSimulation.GetRawText());
        Assert.Equal(simulationId, repeatedSimulation.GetProperty("simulationId").GetGuid());
        Assert.Equal(
            simulation.GetProperty("finalStateHash").GetString(),
            repeatedSimulation.GetProperty("finalStateHash").GetString());

        using var simulationVerifyResponse = await _client.PostAsync($"/api/v1/runs/{simulationId}/verify", null);
        var simulationReplay = await simulationVerifyResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, simulationVerifyResponse.StatusCode);
        Assert.True(simulationReplay.GetProperty("isValid").GetBoolean(), simulationReplay.GetRawText());

        using var timelineResponse = await _client.GetAsync($"/api/v1/combats/{combatId}/timeline?limit=20");
        var timeline = await timelineResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, timelineResponse.StatusCode);
        Assert.NotEmpty(timeline.GetProperty("items").EnumerateArray());
        Assert.NotEmpty(timeline.GetProperty("turnGroups").EnumerateArray());
        var historicalSequence = timeline.GetProperty("items")[0].GetProperty("runSequence").GetInt32();
        using var historicalResponse = await _client.GetAsync(
            $"/api/v1/combats/{combatId}/timeline/{historicalSequence}/state");
        var historical = await historicalResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, historicalResponse.StatusCode);
        Assert.Equal(combatId, historical.GetProperty("combatId").GetGuid());

        using var branchResponse = await _client.PostAsJsonAsync(
            $"/api/v1/combats/{combatId}/timeline/{historicalSequence}/branches",
            new { branchKey = "alternate-card-line" });
        var branch = await branchResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, branchResponse.StatusCode);
        var branchRunId = branch.GetProperty("runId").GetGuid();
        var branchCombatId = branch.GetProperty("activeEncounterId").GetGuid();
        Assert.NotEqual(runId, branchRunId);
        Assert.NotEqual(combatId, branchCombatId);

        using var treeResponse = await _client.GetAsync($"/api/v1/runs/{runId}/branch-tree");
        var tree = await treeResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, treeResponse.StatusCode);
        Assert.Contains(
            tree.GetProperty("children").EnumerateArray(),
            child => child.GetProperty("runId").GetGuid() == branchRunId);

        using var branchSnapshotResponse = await _client.GetAsync($"/api/v1/sandbox/runs/{branchRunId}/snapshot");
        var branchSnapshot = await branchSnapshotResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, branchSnapshotResponse.StatusCode);
        var branchAttack = branchSnapshot.GetProperty("hand").EnumerateArray()
            .First(card => card.GetProperty("definitionId").GetString() == "basic_attack");
        var branchEnemyHealth = Health(branchSnapshot, "goblin_a");

        using var branchActionResponse = await _client.PostAsJsonAsync(
            $"/api/v1/combats/{branchCombatId}/commands",
            new
            {
                commandId = Guid.NewGuid(),
                expectedSequence = branchSnapshot.GetProperty("run").GetProperty("sequence").GetInt32(),
                expectedStep = branchSnapshot.GetProperty("combat").GetProperty("step").GetUInt64(),
                type = "EXECUTE_ACTION",
                payload = new
                {
                    actorId = "hero",
                    actionId = branchAttack.GetProperty("actionId").GetString(),
                    cardId = branchAttack.GetProperty("cardInstanceId").GetGuid(),
                    targetId = "goblin_a"
                }
            });
        var branchAction = await branchActionResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(branchActionResponse.StatusCode == HttpStatusCode.OK, branchAction.GetRawText());

        using var updatedBranchSnapshotResponse = await _client.GetAsync($"/api/v1/sandbox/runs/{branchRunId}/snapshot");
        var updatedBranchSnapshot = await updatedBranchSnapshotResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, updatedBranchSnapshotResponse.StatusCode);
        Assert.True(Health(updatedBranchSnapshot, "goblin_a") < branchEnemyHealth);

        using var unchangedParentSnapshotResponse = await _client.GetAsync($"/api/v1/sandbox/runs/{runId}/snapshot");
        var unchangedParentSnapshot = await unchangedParentSnapshotResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, unchangedParentSnapshotResponse.StatusCode);
        Assert.Equal(originalEnemyHealth, Health(unchangedParentSnapshot, "goblin_a"));

        using var branchVerifyResponse = await _client.PostAsync($"/api/v1/runs/{branchRunId}/verify", null);
        var branchReplay = await branchVerifyResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, branchVerifyResponse.StatusCode);
        Assert.True(branchReplay.GetProperty("isValid").GetBoolean(), branchReplay.GetRawText());

        using var verification = await _client.PostAsync($"/api/v1/runs/{runId}/verify", null);
        var replay = await verification.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, verification.StatusCode);
        Assert.True(replay.GetProperty("isValid").GetBoolean(), replay.GetRawText());

        using var stored = await _client.GetAsync($"/api/v1/sandbox/runs/{runId}/scenario");
        var persisted = await stored.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, stored.StatusCode);
        Assert.Equal(attemptKey, persisted.GetProperty("attemptKey").GetString());
    }

    [Fact]
    public async Task SandboxScenario_StoresInitialStatusesInTheImmutableCombatSnapshot()
    {
        var scenario = CreateScenario(
            $"api-sandbox-status-{Guid.NewGuid():N}",
            new
            {
                heroResources = new { energy = 3 },
                effects = new[] { new { targetAlias = "goblin_a", statusId = "poison", stacks = 2 } }
            });

        using var launch = await _client.PostAsJsonAsync("/api/v1/sandbox/runs", scenario);
        var launched = await launch.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(launch.StatusCode == HttpStatusCode.OK, launched.GetRawText());
        var runId = launched.GetProperty("run").GetProperty("runId").GetGuid();

        using var snapshotResponse = await _client.GetAsync($"/api/v1/sandbox/runs/{runId}/snapshot");
        var snapshot = await snapshotResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, snapshotResponse.StatusCode);
        var status = snapshot.GetProperty("combat").GetProperty("actors").EnumerateArray()
            .First(actor => actor.GetProperty("entityId").GetString() == "goblin_a")
            .GetProperty("statuses").EnumerateArray()
            .Single(item => item.GetProperty("statusId").GetString() == "poison");
        Assert.Equal(2, status.GetProperty("stacks").GetInt32());

        using var verify = await _client.PostAsync($"/api/v1/runs/{runId}/verify", null);
        var replay = await verify.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        Assert.True(replay.GetProperty("isValid").GetBoolean(), replay.GetRawText());
    }

    [Fact]
    public async Task FixedActionSandbox_IgnoresEnergyCostsAndAdvancesAfterConfiguredActionCount()
    {
        var scenario = CreateScenario(
            $"api-fixed-actions-{Guid.NewGuid():N}",
            new { heroResources = new { energy = 0 } },
            modeId: "combat_sandbox_fixed_actions");

        using var launch = await _client.PostAsJsonAsync("/api/v1/sandbox/runs", scenario);
        var launched = await launch.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(launch.StatusCode == HttpStatusCode.OK, launched.GetRawText());

        var runId = launched.GetProperty("run").GetProperty("runId").GetGuid();
        var combatId = launched.GetProperty("combat").GetProperty("combatId").GetGuid();
        using var initialSnapshotResponse = await _client.GetAsync($"/api/v1/sandbox/runs/{runId}/snapshot");
        var initialSnapshot = await initialSnapshotResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, initialSnapshotResponse.StatusCode);
        var hand = initialSnapshot.GetProperty("hand").EnumerateArray().ToArray();
        var fireball = hand.Single(card => card.GetProperty("definitionId").GetString() == "fireball");
        var basicAttack = hand.First(card => card.GetProperty("definitionId").GetString() == "basic_attack");

        var afterFirst = await ExecuteCardCommand(runId, combatId, fireball, "goblin_a");
        Assert.Equal(0, Energy(afterFirst));
        Assert.Equal(1, afterFirst.GetProperty("activationState").GetProperty("actionsTaken").GetInt32());
        var firstActivation = afterFirst.GetProperty("activationState").GetProperty("activationNumber").GetInt32();

        var afterSecond = await ExecuteCardCommand(runId, combatId, basicAttack, "goblin_a");
        var nextActivation = afterSecond.GetProperty("activationState");
        Assert.Equal("hero", nextActivation.GetProperty("activeActorId").GetString());
        Assert.Equal(0, nextActivation.GetProperty("actionsTaken").GetInt32());
        Assert.True(nextActivation.GetProperty("activationNumber").GetInt32() > firstActivation);

        using var verify = await _client.PostAsync($"/api/v1/runs/{runId}/verify", null);
        var replay = await verify.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        Assert.True(replay.GetProperty("isValid").GetBoolean(), replay.GetRawText());
    }

    private async Task<JsonElement> ExecuteCardCommand(
        Guid runId,
        Guid combatId,
        JsonElement card,
        string targetId)
    {
        using var snapshotResponse = await _client.GetAsync($"/api/v1/sandbox/runs/{runId}/snapshot");
        var snapshot = await snapshotResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, snapshotResponse.StatusCode);

        using var response = await _client.PostAsJsonAsync(
            $"/api/v1/combats/{combatId}/commands",
            new
            {
                commandId = Guid.NewGuid(),
                expectedSequence = snapshot.GetProperty("run").GetProperty("sequence").GetInt32(),
                expectedStep = snapshot.GetProperty("combat").GetProperty("step").GetUInt64(),
                type = "EXECUTE_ACTION",
                payload = new
                {
                    actorId = "hero",
                    actionId = card.GetProperty("actionId").GetString(),
                    cardId = card.GetProperty("cardInstanceId").GetGuid(),
                    targetId
                }
            });
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(response.StatusCode == HttpStatusCode.OK, result.GetRawText());
        return result.GetProperty("state").GetProperty("combat").Clone();
    }

    private static object CreateScenario(
        string attemptKey,
        object? initialState = null,
        string modeId = "combat_sandbox") => new
    {
        schemaVersion = 1,
        modeId,
        seed = 983744UL,
        attemptKey,
        hero = new { alias = "hero", entityDefinitionId = "player_warrior" },
        deck = new[]
        {
            new { definitionId = "basic_attack" },
            new { definitionId = "basic_attack" },
            new { definitionId = "defend" },
            new { definitionId = "fireball" },
            new { definitionId = "heal" }
        },
        enemies = new[]
        {
            new { alias = "goblin_a", entityDefinitionId = "enemy_goblin" }
        },
        initialState = initialState ?? new { heroResources = new { energy = 3 } }
    };

    private static double Health(JsonElement snapshot, string entityId) => snapshot
        .GetProperty("combat")
        .GetProperty("actors")
        .EnumerateArray()
        .First(actor => actor.GetProperty("entityId").GetString() == entityId)
        .GetProperty("resources")
        .GetProperty("health")
        .GetProperty("current")
        .GetDouble();

    private static double Energy(JsonElement combat) => combat
        .GetProperty("hero")
        .GetProperty("resourceState")
        .GetProperty("resources")
        .GetProperty("energy")
        .GetProperty("current")
        .GetDouble();
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Core.Run;
using Xunit;

namespace API.Tests.Controllers;

[Trait("Category", "Integration")]
public sealed class CombatSandboxIntegrationTests : IClassFixture<TestWebApplicationFactory>, IAsyncLifetime
{
    private readonly HttpClient _client;
    private string _contentRevision = string.Empty;

    public CombatSandboxIntegrationTests(TestWebApplicationFactory factory) => _client = factory.CreateClient();

    public async Task InitializeAsync()
    {
        using var response = await _client.GetAsync("/api/v1/content/revisions?configName=default");
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        _contentRevision = content.GetProperty("currentRevision").GetString()!;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task LegacyRoleBasedScenarioShape_IsRejected()
    {
        using var response = await _client.PostAsJsonAsync("/api/v1/sandbox/runs", new
        {
            schemaVersion = 1,
            modeId = "combat_sandbox",
            contentRevision = _contentRevision,
            seed = 1UL,
            attemptKey = $"legacy-{Guid.NewGuid():N}",
            hero = new { alias = "hero", entityDefinitionId = "player_warrior" },
            enemies = new[] { new { alias = "enemy", entityDefinitionId = "enemy_goblin" } },
            deck = new[] { new { definitionId = "basic_attack" } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task IdenticalCompleteSandboxFlow_IsBitwiseStableAcrossTenFreshRuntimes()
    {
        DeterministicFlowEvidence? baseline = null;
        for (var iteration = 0; iteration < 10; iteration++)
        {
            using var factory = new TestWebApplicationFactory();
            using var client = factory.CreateClient();
            var evidence = await ExecuteDeterministicFlow(client);
            baseline ??= evidence;
            Assert.Equal(baseline, evidence);
        }
    }

    [Fact]
    public async Task CardEvaluation_ExplainsExactPreviewWithoutMutatingCombat()
    {
        using var launch = await _client.PostAsJsonAsync(
            "/api/v1/sandbox/runs",
            CreateScenario($"card-evaluation-{Guid.NewGuid():N}"));
        var launched = await launch.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(launch.StatusCode == HttpStatusCode.OK, launched.GetRawText());
        var runId = launched.GetProperty("run").GetProperty("runId").GetGuid();
        var combatId = launched.GetProperty("combat").GetProperty("combatId").GetGuid();
        var initialResolutionProperty = Assert.Single(
            launched.GetProperty("run").GetProperty("combatResolutions").EnumerateObject());
        var initialCommandId = Guid.Parse(initialResolutionProperty.Name);
        var initialResolution = initialResolutionProperty.Value;
        Assert.Equal(combatId, initialResolution.GetProperty("combatId").GetGuid());
        Assert.Equal(64, initialResolution.GetProperty("resolutionFingerprint").GetString()!.Length);
        Assert.Equal(
            "combat.initialized",
            Assert.Single(initialResolution.GetProperty("frames").EnumerateArray())
                .GetProperty("transitionType").GetString());

        using var initialResolutionResponse = await _client.GetAsync(
            $"/api/v1/combats/{combatId}/resolutions/{initialCommandId}");
        Assert.Equal(HttpStatusCode.OK, initialResolutionResponse.StatusCode);

        using var beforeResponse = await _client.GetAsync($"/api/v1/sandbox/runs/{runId}/snapshot");
        var before = await beforeResponse.Content.ReadFromJsonAsync<JsonElement>();
        var card = before.GetProperty("hand").EnumerateArray()
            .First(item => item.GetProperty("definitionId").GetString() == "basic_attack");
        var cardInstanceId = card.GetProperty("cardInstanceId").GetGuid();

        using var evaluationResponse = await _client.GetAsync(
            $"/api/v1/combats/{combatId}/cards/{cardInstanceId}/evaluation?actorId=hero&targetIds=goblin_a");
        var evaluation = await evaluationResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(evaluationResponse.StatusCode == HttpStatusCode.OK, evaluation.GetRawText());
        Assert.Equal("Full", evaluation.GetProperty("detail").GetString());
        Assert.True(evaluation.GetProperty("isPlayable").GetBoolean());
        Assert.Equal("basic_attack", evaluation.GetProperty("baseContainer").GetProperty("cardId").GetString());
        Assert.Equal(cardInstanceId, evaluation.GetProperty("effectiveBase").GetProperty("cardInstanceId").GetGuid());
        Assert.Equal("hero", evaluation.GetProperty("contextSources").GetProperty("actor").GetProperty("instanceId").GetString());
        Assert.NotEmpty(evaluation.GetProperty("calculations").EnumerateArray());
        Assert.Equal(2, evaluation.GetProperty("previewApplications").GetArrayLength());
        Assert.NotEmpty(evaluation.GetProperty("previewSteps").EnumerateArray());
        Assert.Equal(64, evaluation.GetProperty("resolutionFingerprint").GetString()!.Length);

        using var handResponse = await _client.GetAsync(
            $"/api/v1/combats/{combatId}/cards/evaluations?actorId=hero&targetIds=goblin_a");
        var hand = await handResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(handResponse.StatusCode == HttpStatusCode.OK, hand.GetRawText());
        Assert.Equal(5, hand.GetProperty("cards").GetArrayLength());

        using var afterResponse = await _client.GetAsync($"/api/v1/sandbox/runs/{runId}/snapshot");
        var after = await afterResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            before.GetProperty("run").GetProperty("stateHash").GetString(),
            after.GetProperty("run").GetProperty("stateHash").GetString());

        var previewFingerprint = evaluation.GetProperty("resolutionFingerprint").GetString();
        var commandId = Guid.Parse("40000000-0000-8000-8000-000000000001");
        var command = new
        {
            commandId,
            expectedSequence = before.GetProperty("run").GetProperty("sequence").GetInt32(),
            expectedStep = before.GetProperty("combat").GetProperty("step").GetUInt64(),
            type = "PLAY_CARD",
            payload = new
            {
                actorId = "hero",
                cardInstanceId,
                targetIds = new[] { "goblin_a" }
            }
        };
        using var commandResponse = await _client.PostAsJsonAsync(
            $"/api/v1/combats/{combatId}/commands",
            command);
        var committed = await commandResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(commandResponse.StatusCode == HttpStatusCode.OK, committed.GetRawText());
        Assert.False(committed.GetProperty("duplicate").GetBoolean());
        var committedStateHash = committed.GetProperty("stateHash").GetString();
        var embeddedResolution = committed.GetProperty("state").GetProperty("resolution");
        Assert.Equal(64, embeddedResolution.GetProperty("resolutionFingerprint").GetString()!.Length);
        var appliedFrame = embeddedResolution.GetProperty("frames").EnumerateArray()
            .Single(frame => frame.GetProperty("transitionType").GetString() == "combat.action.applied");
        Assert.Equal(
            previewFingerprint,
            appliedFrame.GetProperty("payload").GetProperty("cardResolution")
                .GetProperty("resolutionFingerprint").GetString());
        Assert.NotEmpty(appliedFrame.GetProperty("effectSteps").EnumerateArray());
        Assert.NotEmpty(appliedFrame.GetProperty("calculations").EnumerateArray());
        Assert.Equal(2, appliedFrame.GetProperty("applications").GetArrayLength());

        using var durableResponse = await _client.GetAsync(
            $"/api/v1/combats/{combatId}/resolutions/{commandId}");
        var durable = await durableResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, durableResponse.StatusCode);
        Assert.Equal(commandId, durable.GetProperty("commandId").GetGuid());
        Assert.Equal(
            previewFingerprint,
            durable.GetProperty("frames")[0].GetProperty("payload").GetProperty("cardResolution")
                .GetProperty("resolutionFingerprint").GetString());

        using var retryResponse = await _client.PostAsJsonAsync(
            $"/api/v1/combats/{combatId}/commands",
            command);
        var retried = await retryResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(retryResponse.StatusCode == HttpStatusCode.OK, retried.GetRawText());
        Assert.True(retried.GetProperty("duplicate").GetBoolean());
        Assert.Equal(committedStateHash, retried.GetProperty("stateHash").GetString());

        using var timelineResponse = await _client.GetAsync(
            $"/api/v1/combats/{combatId}/timeline?limit=20");
        var timeline = await timelineResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, timelineResponse.StatusCode);
        var timelineItem = timeline.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("commandId").ValueKind == JsonValueKind.String &&
                            item.GetProperty("commandId").GetGuid() == commandId);
        Assert.Equal(committedStateHash, timelineItem.GetProperty("stateHash").GetString());
        Assert.Equal(commandId, timelineItem.GetProperty("resolutionCommandId").GetGuid());
        Assert.Equal(
            embeddedResolution.GetProperty("resolutionFingerprint").GetString(),
            timelineItem.GetProperty("resolutionFingerprint").GetString());

        var committedSequence = timelineItem.GetProperty("runSequence").GetInt32();
        using var historicalResponse = await _client.GetAsync(
            $"/api/v1/combats/{combatId}/timeline/{committedSequence}/state");
        var historical = await historicalResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, historicalResponse.StatusCode);
        Assert.Equal(committedStateHash, historical.GetProperty("runStateHash").GetString());

        using var replayResponse = await _client.PostAsync($"/api/v1/runs/{runId}/verify", null);
        var replay = await replayResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        Assert.True(replay.GetProperty("isValid").GetBoolean(), replay.GetRawText());
        Assert.Equal(
            replay.GetProperty("expectedFinalHash").GetString(),
            replay.GetProperty("actualFinalHash").GetString());
        Assert.Equal(committedStateHash, replay.GetProperty("actualFinalHash").GetString());
    }

    [Fact]
    public async Task SandboxScenario_IsValidatedLaunchedAndIdempotentlyReopened()
    {
        var attemptKey = $"api-sandbox-{Guid.NewGuid():N}";
        var scenario = CreateScenario(attemptKey);

        using var validation = await _client.PostAsJsonAsync("/api/v1/sandbox/scenarios/validate", scenario);
        var validated = await validation.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, validation.StatusCode);
        Assert.Equal(64, validated.GetProperty("scenarioHash").GetString()!.Length);
        Assert.Equal("hero", validated.GetProperty("actors").EnumerateArray()
            .First(actor => actor.GetProperty("controllerBinding").GetProperty("kind").GetString() == "Player")
            .GetProperty("instanceId").GetString());

        using var launch = await _client.PostAsJsonAsync("/api/v1/sandbox/runs", scenario);
        var first = await launch.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, launch.StatusCode);
        Assert.False(first.GetProperty("duplicate").GetBoolean());
        var runId = first.GetProperty("run").GetProperty("runId").GetGuid();
        var combatId = first.GetProperty("combat").GetProperty("combatId").GetGuid();
        Assert.Equal("combat_sandbox", first.GetProperty("run").GetProperty("modeId").GetString());
        Assert.Equal("goblin_a", first.GetProperty("combat").GetProperty("actors")
            .GetProperty("goblin_a").GetProperty("instanceId").GetString());

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
            actor => actor.GetProperty("instanceId").GetString() == "goblin_a");
        var intents = snapshot.GetProperty("combat")
            .GetProperty("activation")
            .GetProperty("intents")
            .EnumerateArray()
            .ToArray();
        Assert.Single(intents);
        Assert.Equal("goblin_a", intents[0].GetProperty("actorId").GetString());
        Assert.Equal("hero", intents[0].GetProperty("targetIds")[0].GetString());
        Assert.Equal("Attack", intents[0].GetProperty("telegraphType").GetString());
        Assert.False(string.IsNullOrWhiteSpace(intents[0].GetProperty("previewFingerprint").GetString()));
        var originalEnemyHealth = Health(snapshot, "goblin_a");

        using var legalResponse = await _client.GetAsync(
            $"/api/v1/combats/{combatId}/legal-actions?actorId=hero");
        var legal = await legalResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, legalResponse.StatusCode);
        Assert.Equal("hero", legal.GetProperty("actorId").GetString());
        Assert.Contains(legal.GetProperty("candidates").EnumerateArray(), candidate =>
            candidate.GetProperty("command").GetProperty("actionType").GetString() == "PLAY_CARD");

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
                    type = "PLAY_CARD",
                    payload = new
                    {
                        actorId = "hero",
                        targetIds = new[] { "goblin_a" },
                        cardInstanceId = basicAttack.GetProperty("cardInstanceId").GetGuid()
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
        Assert.Equal(runId, branch.GetProperty("rootRunId").GetGuid());
        Assert.Equal(runId, branch.GetProperty("parentRunId").GetGuid());
        Assert.Equal(combatId, branch.GetProperty("sourceCombatId").GetGuid());
        Assert.Equal(64, branch.GetProperty("sourceStateHash").GetString()!.Length);

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
                type = "PLAY_CARD",
                payload = new
                {
                    actorId = "hero",
                    cardInstanceId = branchAttack.GetProperty("cardInstanceId").GetGuid(),
                    targetIds = new[] { "goblin_a" }
                }
            });
        var branchAction = await branchActionResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(branchActionResponse.StatusCode == HttpStatusCode.OK, branchAction.GetRawText());
        var fullResolution = branchAction.GetProperty("state").GetProperty("resolution");
        Assert.Equal("FullSnapshots", fullResolution.GetProperty("mode").GetString());
        Assert.All(
            fullResolution.GetProperty("frames").EnumerateArray(),
            frame => Assert.Equal(JsonValueKind.Object, frame.GetProperty("stateAfter").ValueKind));

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
    public async Task SandboxHeadRestore_AppendsACommandWithoutRewindingSequenceOrDeterminism()
    {
        using var launch = await _client.PostAsJsonAsync(
            "/api/v1/sandbox/runs",
            CreateScenario($"head-restore-{Guid.NewGuid():N}"));
        var launched = await launch.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(launch.StatusCode == HttpStatusCode.OK, launched.GetRawText());
        var run = launched.GetProperty("run");
        var runId = run.GetProperty("runId").GetGuid();
        var combatId = launched.GetProperty("combat").GetProperty("combatId").GetGuid();
        var sourceSequence = run.GetProperty("sequence").GetInt32();

        using var snapshotResponse = await _client.GetAsync($"/api/v1/sandbox/runs/{runId}/snapshot");
        var snapshot = await snapshotResponse.Content.ReadFromJsonAsync<JsonElement>();
        var initialHealth = Health(snapshot, "goblin_a");
        var attack = snapshot.GetProperty("hand").EnumerateArray()
            .First(card => card.GetProperty("definitionId").GetString() == "basic_attack");
        using var actionResponse = await _client.PostAsJsonAsync(
            $"/api/v1/combats/{combatId}/commands",
            new
            {
                commandId = Guid.NewGuid(),
                expectedSequence = sourceSequence,
                expectedStep = snapshot.GetProperty("combat").GetProperty("step").GetUInt64(),
                type = GameplayCommandTypes.PlayCard,
                payload = new
                {
                    actorId = "hero",
                    cardInstanceId = attack.GetProperty("cardInstanceId").GetGuid(),
                    targetIds = new[] { "goblin_a" }
                }
            });
        var action = await actionResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(actionResponse.StatusCode == HttpStatusCode.OK, action.GetRawText());

        using var restoreResponse = await _client.PostAsJsonAsync(
            $"/api/v1/runs/{runId}/commands",
            new
            {
                commandId = Guid.NewGuid(),
                expectedSequence = action.GetProperty("sequence").GetInt32(),
                expectedStep = action.GetProperty("state").GetProperty("run")
                    .GetProperty("determinism").GetProperty("step").GetUInt64(),
                type = RunCommandTypes.RestoreHeadFromHistory,
                payload = new { sourceSequence }
            });
        var restored = await restoreResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(restoreResponse.StatusCode == HttpStatusCode.OK, restored.GetRawText());
        Assert.Equal(action.GetProperty("sequence").GetInt32() + 1, restored.GetProperty("sequence").GetInt32());
        Assert.True(restored.GetProperty("step").GetUInt64() >
                    action.GetProperty("state").GetProperty("run")
                        .GetProperty("determinism").GetProperty("step").GetUInt64());

        using var restoredSnapshotResponse = await _client.GetAsync($"/api/v1/sandbox/runs/{runId}/snapshot");
        var restoredSnapshot = await restoredSnapshotResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(initialHealth, Health(restoredSnapshot, "goblin_a"));
        Assert.Equal(snapshot.GetProperty("hand").GetArrayLength(), restoredSnapshot.GetProperty("hand").GetArrayLength());

        using var verification = await _client.PostAsync($"/api/v1/runs/{runId}/verify", null);
        var replay = await verification.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, verification.StatusCode);
        Assert.True(replay.GetProperty("isValid").GetBoolean(), replay.GetRawText());
    }

    [Fact]
    public async Task SandboxScenario_StoresInitialStatusesInTheImmutableCombatSnapshot()
    {
        var scenario = CreateScenario(
            $"api-sandbox-status-{Guid.NewGuid():N}",
            new
            {
                resourcesByActor = new Dictionary<string, object>
                {
                    ["hero"] = new { energy = 3 }
                },
                effects = new[] { new { targetActorId = "goblin_a", statusId = "poison", stacks = 2 } }
            });

        using var launch = await _client.PostAsJsonAsync("/api/v1/sandbox/runs", scenario);
        var launched = await launch.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(launch.StatusCode == HttpStatusCode.OK, launched.GetRawText());
        var runId = launched.GetProperty("run").GetProperty("runId").GetGuid();

        using var snapshotResponse = await _client.GetAsync($"/api/v1/sandbox/runs/{runId}/snapshot");
        var snapshot = await snapshotResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, snapshotResponse.StatusCode);
        var status = snapshot.GetProperty("combat").GetProperty("actors").EnumerateArray()
            .First(actor => actor.GetProperty("instanceId").GetString() == "goblin_a")
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
            new
            {
                resourcesByActor = new Dictionary<string, object>
                {
                    ["hero"] = new { energy = 0 }
                }
            },
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

        var firstCommandId = Guid.NewGuid();
        var firstResult = await ExecuteCardCommandResult(
            runId,
            combatId,
            fireball,
            "goblin_a",
            firstCommandId);
        var afterFirst = firstResult.GetProperty("state").GetProperty("combat");
        Assert.Equal(0, Energy(afterFirst));
        Assert.Equal(1, afterFirst.GetProperty("activationState").GetProperty("actionsTaken").GetInt32());
        var compactResolution = firstResult.GetProperty("state").GetProperty("resolution");
        Assert.Equal("CompactWithSnapshotLookup", compactResolution.GetProperty("mode").GetString());
        var compactFrame = compactResolution.GetProperty("frames").EnumerateArray().Single();
        Assert.Equal(JsonValueKind.Null, compactFrame.GetProperty("stateAfter").ValueKind);
        Assert.True(compactFrame.GetProperty("snapshotSequence").GetInt32() > 0);

        using var resolutionResponse = await _client.GetAsync(
            $"/api/v1/combats/{combatId}/resolutions/{firstCommandId}");
        var durableResolution = await resolutionResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, resolutionResponse.StatusCode);
        Assert.Equal(
            compactResolution.GetProperty("finalSequence").GetInt32(),
            durableResolution.GetProperty("finalSequence").GetInt32());
        using var compactStateResponse = await _client.GetAsync(
            $"/api/v1/combats/{combatId}/timeline/{compactFrame.GetProperty("snapshotSequence").GetInt32()}/state");
        var compactState = await compactStateResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, compactStateResponse.StatusCode);
        Assert.Equal(combatId, compactState.GetProperty("combatId").GetGuid());
        using var afterFirstSnapshotResponse = await _client.GetAsync($"/api/v1/sandbox/runs/{runId}/snapshot");
        var afterFirstSnapshot = await afterFirstSnapshotResponse.Content.ReadFromJsonAsync<JsonElement>();
        var appliedBurning = afterFirstSnapshot.GetProperty("combat").GetProperty("actors")
            .EnumerateArray()
            .Single(actor => actor.GetProperty("instanceId").GetString() == "goblin_a")
            .GetProperty("statuses")[0];
        Assert.Equal(
            "EndActivation",
            appliedBurning.GetProperty("definition").GetProperty("triggers")[0]
                .GetProperty("boundary").GetString());
        var firstActivation = afterFirst.GetProperty("activationState").GetProperty("activationNumber").GetInt32();

        var afterSecond = await ExecuteCardCommand(runId, combatId, basicAttack, "goblin_a");
        var nextActivation = afterSecond.GetProperty("activationState");
        Assert.Equal("hero", nextActivation.GetProperty("activeActorId").GetString());
        Assert.Equal(0, nextActivation.GetProperty("actionsTaken").GetInt32());
        Assert.True(nextActivation.GetProperty("activationNumber").GetInt32() > firstActivation);
        Assert.Equal(2, afterSecond.GetProperty("currentTurn").GetInt32());

        using var lifecycleSnapshotResponse = await _client.GetAsync($"/api/v1/sandbox/runs/{runId}/snapshot");
        var lifecycleSnapshot = await lifecycleSnapshotResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, lifecycleSnapshotResponse.StatusCode);
        Assert.Equal(28, Health(lifecycleSnapshot, "goblin_a"));
        var burning = lifecycleSnapshot.GetProperty("combat").GetProperty("actors")
            .EnumerateArray()
            .Single(actor => actor.GetProperty("instanceId").GetString() == "goblin_a")
            .GetProperty("statuses")
            .EnumerateArray()
            .Single(status => status.GetProperty("statusId").GetString() == "burning");
        Assert.Equal(2, burning.GetProperty("duration").GetInt32());

        using var verify = await _client.PostAsync($"/api/v1/runs/{runId}/verify", null);
        var replay = await verify.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        Assert.True(replay.GetProperty("isValid").GetBoolean(), replay.GetRawText());
    }

    [Fact]
    public async Task SandboxHeal_AppliesConfiguredEffectToAuthoritativeSnapshot()
    {
        var scenario = CreateScenario(
            $"api-heal-{Guid.NewGuid():N}",
            new
            {
                resourcesByActor = new Dictionary<string, object>
                {
                    ["hero"] = new { energy = 3, health = 20 }
                }
            });
        using var launch = await _client.PostAsJsonAsync("/api/v1/sandbox/runs", scenario);
        var launched = await launch.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(launch.StatusCode == HttpStatusCode.OK, launched.GetRawText());
        var runId = launched.GetProperty("run").GetProperty("runId").GetGuid();
        var combatId = launched.GetProperty("combat").GetProperty("combatId").GetGuid();

        using var snapshotResponse = await _client.GetAsync($"/api/v1/sandbox/runs/{runId}/snapshot");
        var snapshot = await snapshotResponse.Content.ReadFromJsonAsync<JsonElement>();
        var heal = snapshot.GetProperty("hand").EnumerateArray()
            .Single(card => card.GetProperty("definitionId").GetString() == "heal");

        await ExecuteCardCommand(runId, combatId, heal, "hero");

        using var afterResponse = await _client.GetAsync($"/api/v1/sandbox/runs/{runId}/snapshot");
        var after = await afterResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(28, Health(after, "hero"));
    }

    private async Task<JsonElement> ExecuteCardCommand(
        Guid runId,
        Guid combatId,
        JsonElement card,
        string targetId)
    {
        var result = await ExecuteCardCommandResult(runId, combatId, card, targetId, Guid.NewGuid());
        return result.GetProperty("state").GetProperty("combat").Clone();
    }

    private async Task<JsonElement> ExecuteCardCommandResult(
        Guid runId,
        Guid combatId,
        JsonElement card,
        string targetId,
        Guid commandId)
    {
        using var snapshotResponse = await _client.GetAsync($"/api/v1/sandbox/runs/{runId}/snapshot");
        var snapshot = await snapshotResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, snapshotResponse.StatusCode);

        using var response = await _client.PostAsJsonAsync(
            $"/api/v1/combats/{combatId}/commands",
            new
            {
                commandId,
                expectedSequence = snapshot.GetProperty("run").GetProperty("sequence").GetInt32(),
                expectedStep = snapshot.GetProperty("combat").GetProperty("step").GetUInt64(),
                type = "PLAY_CARD",
                payload = new
                {
                    actorId = "hero",
                    cardInstanceId = card.GetProperty("cardInstanceId").GetGuid(),
                    targetIds = new[] { targetId }
                }
            });
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(response.StatusCode == HttpStatusCode.OK, result.GetRawText());
        return result.Clone();
    }

    private object CreateScenario(
        string attemptKey,
        object? initialState = null,
        string modeId = "combat_sandbox") => new
    {
        schemaVersion = 2,
        modeId,
        contentRevision = _contentRevision,
        seed = 983744UL,
        attemptKey,
        participants = new object[]
        {
            new
            {
                instanceId = "hero",
                entityDefinitionId = "player_warrior",
                sideId = "player",
                controllerBinding = new { kind = "Player" }
            },
            new
            {
                instanceId = "goblin_a",
                entityDefinitionId = "enemy_goblin",
                sideId = "opposition",
                controllerBinding = new { kind = "AI", policyId = "gambit" }
            }
        },
        deck = new[]
        {
            new { definitionId = "basic_attack" },
            new { definitionId = "basic_attack" },
            new { definitionId = "defend" },
            new { definitionId = "fireball" },
            new { definitionId = "heal" }
        },
        initialState = initialState ?? new
        {
            resourcesByActor = new Dictionary<string, object>
            {
                ["hero"] = new { energy = 3 }
            }
        }
    };

    private async Task<DeterministicFlowEvidence> ExecuteDeterministicFlow(HttpClient client)
    {
        using var launchResponse = await client.PostAsJsonAsync(
            "/api/v1/sandbox/runs",
            CreateScenario("ten-runtime-determinism"));
        var launch = await launchResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(launchResponse.StatusCode == HttpStatusCode.OK, launch.GetRawText());
        var runId = launch.GetProperty("run").GetProperty("runId").GetGuid();
        var combatId = launch.GetProperty("combat").GetProperty("combatId").GetGuid();
        var initialResolution = Assert.Single(
            launch.GetProperty("run").GetProperty("combatResolutions").EnumerateObject()).Value;

        using var snapshotResponse = await client.GetAsync($"/api/v1/sandbox/runs/{runId}/snapshot");
        var snapshot = await snapshotResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, snapshotResponse.StatusCode);
        var card = snapshot.GetProperty("hand").EnumerateArray()
            .First(item => item.GetProperty("definitionId").GetString() == "basic_attack");
        var cardInstanceId = card.GetProperty("cardInstanceId").GetGuid();

        using var previewResponse = await client.GetAsync(
            $"/api/v1/combats/{combatId}/cards/{cardInstanceId}/evaluation?actorId=hero&targetIds=goblin_a");
        var preview = await previewResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);

        using var actionResponse = await client.PostAsJsonAsync(
            $"/api/v1/combats/{combatId}/commands",
            new
            {
                commandId = Guid.Parse("70000000-0000-8000-8000-000000000001"),
                expectedSequence = snapshot.GetProperty("run").GetProperty("sequence").GetInt32(),
                expectedStep = snapshot.GetProperty("combat").GetProperty("step").GetUInt64(),
                type = "PLAY_CARD",
                payload = new
                {
                    actorId = "hero",
                    cardInstanceId,
                    targetIds = new[] { "goblin_a" }
                }
            });
        var action = await actionResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(actionResponse.StatusCode == HttpStatusCode.OK, action.GetRawText());
        var resolution = action.GetProperty("state").GetProperty("resolution");

        using var journalResponse = await client.GetAsync($"/api/v1/runs/{runId}/journal?limit=100");
        var journal = await journalResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, journalResponse.StatusCode);
        using var verifyResponse = await client.PostAsync($"/api/v1/runs/{runId}/verify", null);
        var replay = await verifyResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);
        Assert.True(replay.GetProperty("isValid").GetBoolean(), replay.GetRawText());

        return new DeterministicFlowEvidence(
            runId,
            combatId,
            cardInstanceId,
            snapshot.GetProperty("run").GetProperty("stateHash").GetString()!,
            initialResolution.GetProperty("resolutionFingerprint").GetString()!,
            preview.GetProperty("resolutionFingerprint").GetString()!,
            action.GetProperty("stateHash").GetString()!,
            resolution.GetProperty("resolutionFingerprint").GetString()!,
            resolution.GetProperty("frames").GetRawText(),
            journal.GetProperty("entries").GetRawText());
    }

    private sealed record DeterministicFlowEvidence(
        Guid RunId,
        Guid CombatId,
        Guid CardInstanceId,
        string InitialStateHash,
        string InitializationFingerprint,
        string PreviewFingerprint,
        string FinalStateHash,
        string ResolutionFingerprint,
        string Frames,
        string Journal);

    private static double Health(JsonElement snapshot, string entityId) => snapshot
        .GetProperty("combat")
        .GetProperty("actors")
        .EnumerateArray()
        .First(actor => actor.GetProperty("instanceId").GetString() == entityId)
        .GetProperty("resources")
        .GetProperty("health")
        .GetProperty("current")
        .GetDouble();

    private static double Energy(JsonElement combat) => combat
        .GetProperty("actors")
        .GetProperty("hero")
        .GetProperty("components")
        .GetProperty("resources")
        .GetProperty("state")
        .GetProperty("resources")
        .GetProperty("energy")
        .GetProperty("current")
        .GetDouble();
}

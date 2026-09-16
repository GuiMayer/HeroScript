using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using API.Contracts;
using Core.Combat;
using Core.Run;
using Core.Run.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace API.Tests.Controllers;

[Trait("Category", "Integration")]
public sealed class ApiContractFoundationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly TestWebApplicationFactory _factory;

    public ApiContractFoundationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public void LiveReplayAndSimulationRuntimeFactory_ExposeTheSameCommandComposition()
    {
        using var scope = _factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var runtimeFactory = services.GetRequiredService<IGameplayRuntimeFactory>();
        var live = services.GetRequiredService<GameplayRuntime>();
        var replay = runtimeFactory.Create(
            new GameplayRuntimeOptions(GameplayPersistenceMode.Ephemeral));

        Assert.Equal(runtimeFactory.RegisteredCommandTypes, live.RegisteredCommandTypes);
        Assert.Equal(runtimeFactory.RegisteredCommandTypes, replay.RegisteredCommandTypes);
        Assert.Same(live.Gateway, services.GetRequiredService<IGameplayCommandGateway>());
        Assert.Same(live.Runs, services.GetRequiredService<IRunManager>());
        Assert.NotSame(live.Gateway, replay.Gateway);
        Assert.NotSame(live.Runs, replay.Runs);
    }

    [Theory]
    [InlineData("/api/v1/health/live")]
    [InlineData("/api/v1/health/ready")]
    [InlineData("/api/v1/version")]
    [InlineData("/api/v1/capabilities")]
    public async Task VersionedSystemEndpoints_AreAvailable(string path)
    {
        using var response = await _client.GetAsync(path);

        Assert.True(
            response.IsSuccessStatusCode,
            $"{path} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Capabilities_AdvertiseCanonicalCommitHistory()
    {
        using var response = await _client.GetAsync("/api/v1/capabilities");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var capabilities = body.GetProperty("capabilities").EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();
        Assert.Contains("run-commits", capabilities);
        Assert.Contains("run-card-zones", capabilities);
        Assert.DoesNotContain("run-checkpoints", capabilities);
    }

    [Fact]
    public async Task InvalidModel_ReturnsProblemDetailsWithCorrelationId()
    {
        const string correlationId = "contract-test-correlation";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/runs")
        {
            Content = JsonContent.Create(new { seed = "not-an-unsigned-integer" })
        };
        request.Headers.Add("X-Correlation-ID", correlationId);

        using var response = await _client.SendAsync(request);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("INVALID_REQUEST", json.GetProperty("code").GetString());
        Assert.Equal(correlationId, json.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task ContentLookupError_IsNormalizedToProblemDetails()
    {
        const string correlationId = "content-error-contract";
        var revision = await GetCurrentRevisionAsync();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/content/actions/does_not_exist?revision={revision}");
        request.Headers.Add("X-Correlation-ID", correlationId);

        using var response = await _client.SendAsync(request);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("RESOURCE_NOT_FOUND", json.GetProperty("code").GetString());
        Assert.Equal(correlationId, json.GetProperty("correlationId").GetString());
        Assert.Contains("does_not_exist", json.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("/api/v1/no-such-endpoint", HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND")]
    [InlineData("/api/v1/admin/content/drafts/00000000-0000-0000-0000-000000000001", HttpStatusCode.Unauthorized, "UNAUTHORIZED")]
    public async Task EmptyInfrastructureError_IsNormalizedToProblemDetails(
        string path,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        using var response = await _client.GetAsync(path);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(expectedCode, json.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("correlationId").GetString()));
        Assert.Equal(path, json.GetProperty("instance").GetString());
    }

    [Theory]
    [InlineData("deck")]
    [InlineData("hand")]
    public async Task PurposeSpecificCardZoneRoutes_AreNotPublished(string route)
    {
        using var response = await _client.GetAsync(
            $"/api/v1/runs/00000000-0000-0000-0000-000000000001/{route}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ContentRevisionEndpoints_ExposeCanonicalManifest()
    {
        using var listResponse = await _client.GetAsync("/api/v1/content/revisions?configName=default");
        var listBody = await listResponse.Content.ReadAsStringAsync();

        Assert.True(listResponse.StatusCode == HttpStatusCode.OK, listBody);
        var list = JsonSerializer.Deserialize<JsonElement>(listBody);
        var revision = list.GetProperty("currentRevision").GetString();
        Assert.NotNull(revision);
        Assert.Equal(64, revision.Length);
        Assert.NotEmpty(list.GetProperty("revisions").EnumerateArray());

        using var manifestResponse = await _client.GetAsync($"/api/v1/content/revisions/{revision}");
        var manifest = await manifestResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, manifestResponse.StatusCode);
        Assert.Equal(revision, manifest.GetProperty("revision").GetString());
        Assert.NotEmpty(manifest.GetProperty("artifacts").EnumerateArray());
    }

    [Fact]
    public async Task VersionedRunReadModel_SupportsReconnectAndPersistedListing()
    {
        var revision = await GetCurrentRevisionAsync();
        var playerEntityId = $"reconnect-test-player-{Guid.NewGuid():N}";
        using var startResponse = await _client.PostAsJsonAsync("/api/v1/runs", new
        {
            settingId = "default",
            runDefinitionId = "default_run",
            playerEntityId,
            contentRevision = revision,
            modeId = "combat_sandbox"
        });
        var started = await startResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        var runId = started.GetProperty("runId").GetGuid();

        using var stateResponse = await _client.GetAsync($"/api/v1/runs/{runId}");
        var state = await stateResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, stateResponse.StatusCode);
        Assert.True(state.TryGetProperty("cardSelections", out _));
        Assert.True(state.TryGetProperty("shops", out _));
        Assert.True(state.TryGetProperty("preparations", out _));
        Assert.True(state.TryGetProperty("relics", out _));
        Assert.True(state.TryGetProperty("contentManifest", out _));
        Assert.Equal("start", state.GetProperty("map").GetProperty("currentNodeId").GetString());

        var card = state.GetProperty("cardZones").GetProperty("zones")
            .EnumerateArray()
            .SelectMany(zone => zone.GetProperty("cards").EnumerateArray())
            .First(item => item.GetProperty("definitionId").GetString() == "basic_attack");
        var cardInstanceId = card.GetProperty("cardInstanceId").GetGuid();
        using var cardResponse = await _client.GetAsync(
            $"/api/v1/runs/{runId}/cards/{cardInstanceId}");
        Assert.Equal(HttpStatusCode.OK, cardResponse.StatusCode);
        using var upgradeOptionsResponse = await _client.GetAsync(
            $"/api/v1/runs/{runId}/cards/{cardInstanceId}/upgrade-options");
        var upgradeOptions = await upgradeOptionsResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, upgradeOptionsResponse.StatusCode);
        Assert.Contains(
            upgradeOptions.GetProperty("options").EnumerateArray(),
            option => option.GetProperty("upgradeId").GetString() == "sharpened_edge");

        using var relicCatalogResponse = await _client.GetAsync($"/api/v1/content/relics?revision={revision}");
        var relicCatalog = await relicCatalogResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, relicCatalogResponse.StatusCode);
        Assert.Contains(
            relicCatalog.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("definitionId").GetString() == "ember_core");

        using var acquireResponse = await _client.PostAsJsonAsync(
            $"/api/v1/runs/{runId}/commands",
            new
            {
                commandId = Guid.NewGuid(),
                type = RunCommandTypes.AcquireRelic,
                expectedSequence = state.GetProperty("sequence").GetInt32(),
                expectedStep = state.GetProperty("step").GetUInt64(),
                payload = new { relicId = "ember_core" }
            });
        Assert.True(acquireResponse.IsSuccessStatusCode,
            await acquireResponse.Content.ReadAsStringAsync());

        using var relicsResponse = await _client.GetAsync($"/api/v1/runs/{runId}/relics");
        var relics = await relicsResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, relicsResponse.StatusCode);
        Assert.Single(relics.GetProperty("relics").EnumerateArray());

        using var p1VerifyResponse = await _client.PostAsync($"/api/v1/runs/{runId}/verify", null);
        var p1Verification = await p1VerifyResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, p1VerifyResponse.StatusCode);
        Assert.True(
            p1Verification.GetProperty("isValid").GetBoolean(),
            p1Verification.GetRawText());

        using var mapResponse = await _client.GetAsync($"/api/v1/runs/{runId}/map");
        var map = await mapResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, mapResponse.StatusCode);
        Assert.NotEmpty(map.GetProperty("nodes").EnumerateArray());

        using var commandsResponse = await _client.GetAsync(
            $"/api/v1/runs/{runId}/available-commands");
        var commands = await commandsResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, commandsResponse.StatusCode);
        Assert.Equal(
            RunCommandTypes.StartEncounter,
            commands.GetProperty("commands")[0].GetProperty("type").GetString());

        using var listResponse = await _client.GetAsync(
            $"/api/v1/runs?playerEntityId={playerEntityId}&limit=10");
        var list = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Contains(
            list.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("runId").GetGuid() == runId &&
                    item.GetProperty("recoverable").GetBoolean());

        foreach (var resource in new[] { "card-selections", "shops", "preparations" })
        {
            using var resourceResponse = await _client.GetAsync($"/api/v1/runs/{runId}/{resource}");
            Assert.Equal(HttpStatusCode.OK, resourceResponse.StatusCode);
        }
    }

    [Fact]
    public async Task RunEncounter_InheritsRunDeterminismAndIsReconnectable()
    {
        var revision = await GetCurrentRevisionAsync();
        var playerEntityId = $"encounter-player-{Guid.NewGuid():N}";
        using var startRunResponse = await _client.PostAsJsonAsync("/api/v1/runs", new
        {
            settingId = "default",
            runDefinitionId = "default_run",
            playerEntityId,
            contentRevision = revision,
            modeId = "standard"
        });
        var run = await startRunResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, startRunResponse.StatusCode);
        var runId = run.GetProperty("runId").GetGuid();

        var combat = await StartRunEncounterAsync(runId, playerEntityId, new[] { "enemy_1" });
        Assert.Equal(runId, combat.GetProperty("runId").GetGuid());
        Assert.Equal("start", combat.GetProperty("runNodeId").GetString());
        Assert.Equal(
            run.GetProperty("contentRevision").GetString(),
            combat.GetProperty("contentRevision").GetString());
        Assert.Equal(
            run.GetProperty("engineVersion").GetString(),
            combat.GetProperty("engineVersion").GetString());
        Assert.True(combat.TryGetProperty("board", out _));
        var combatId = combat.GetProperty("combatId").GetGuid();

        using var evaluationsResponse = await _client.GetAsync(
            $"/api/v1/combats/{combatId}/cards/evaluations?actorId={playerEntityId}&targetIds=enemy_1");
        var evaluations = await evaluationsResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, evaluationsResponse.StatusCode);
        var cards = evaluations.GetProperty("cards").EnumerateArray().ToArray();
        Assert.NotEmpty(cards);
        Assert.Contains(cards, card => card.GetProperty("isPlayable").GetBoolean());
        Assert.Contains(
            cards.SelectMany(card => card.GetProperty("evaluation").GetProperty("legalTargetIds").EnumerateArray()),
            target => target.GetString() == "enemy_1");

        using var currentResponse = await _client.GetAsync(
            $"/api/v1/runs/{runId}/encounters/current");
        var current = await currentResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, currentResponse.StatusCode);
        Assert.Equal(combatId, current.GetProperty("combatId").GetGuid());

        using var combatResponse = await _client.GetAsync($"/api/v1/combats/{combatId}");
        Assert.Equal(HttpStatusCode.OK, combatResponse.StatusCode);

        using var runResponse = await _client.GetAsync($"/api/v1/runs/{runId}");
        var persistedRun = await runResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, runResponse.StatusCode);
        Assert.Equal(combatId, persistedRun.GetProperty("activeEncounterId").GetGuid());
        Assert.Single(persistedRun.GetProperty("encounters").EnumerateArray());
    }

    [Fact]
    public async Task P2Contracts_ProjectProfileBranchSimulateAndVerifyDailyAttempt()
    {
        var revision = await GetCurrentRevisionAsync();
        var playerId = $"p2-player-{Guid.NewGuid():N}";
        using var startResponse = await _client.PostAsJsonAsync("/api/v1/runs", new
        {
            settingId = "default",
            runDefinitionId = "default_run",
            playerEntityId = playerId,
            seed = 778899UL,
            contentRevision = revision,
            modeId = "combat_sandbox"
        });
        var started = await startResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        var runId = started.GetProperty("runId").GetGuid();
        var sourceSequence = started.GetProperty("sequence").GetInt32();

        using var profileResponse = await _client.GetAsync($"/api/v1/profiles/{playerId}");
        var profile = await profileResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, profileResponse.StatusCode);
        Assert.Equal(1, profile.GetProperty("totalRuns").GetInt32());
        Assert.Equal(64, profile.GetProperty("revision").GetString()!.Length);

        using var branchResponse = await _client.PostAsJsonAsync(
            $"/api/v1/runs/{runId}/branches",
            new { sourceSequence, branchKey = "alternate-path" });
        var branch = await branchResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, branchResponse.StatusCode);
        var branchId = branch.GetProperty("runId").GetGuid();
        Assert.NotEqual(runId, branchId);

        using var branchVerifyResponse = await _client.PostAsync(
            $"/api/v1/runs/{branchId}/verify",
            null);
        var branchVerification = await branchVerifyResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, branchVerifyResponse.StatusCode);
        Assert.True(branchVerification.GetProperty("isValid").GetBoolean(), branchVerification.GetRawText());

        var simulationRequest = new
        {
            sourceRunId = runId,
            sourceSequence,
            commands = new[]
            {
                new { type = RunCommandTypes.InvokeCardZoneGameplayFlow,
                    payload = new { flowId = "run.draw", requestedCount = 1 } }
            }
        };
        using var simulationResponse = await _client.PostAsJsonAsync("/api/v1/simulations", simulationRequest);
        var simulation = await simulationResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, simulationResponse.StatusCode);
        var simulationId = simulation.GetProperty("simulationId").GetGuid();
        Assert.Equal(1, simulation.GetProperty("commandsExecuted").GetInt32());

        using var repeatedSimulationResponse = await _client.PostAsJsonAsync(
            "/api/v1/simulations",
            simulationRequest);
        var repeatedSimulation = await repeatedSimulationResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, repeatedSimulationResponse.StatusCode);
        Assert.Equal(simulationId, repeatedSimulation.GetProperty("simulationId").GetGuid());

        using var simulationVerifyResponse = await _client.PostAsync(
            $"/api/v1/runs/{simulationId}/verify",
            null);
        var simulationVerification = await simulationVerifyResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, simulationVerifyResponse.StatusCode);
        Assert.True(simulationVerification.GetProperty("isValid").GetBoolean(), simulationVerification.GetRawText());

        using var originalResponse = await _client.GetAsync($"/api/v1/runs/{runId}");
        var original = await originalResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(sourceSequence, original.GetProperty("sequence").GetInt32());

        using var currentChallengeResponse = await _client.GetAsync("/api/v1/challenges/daily/current");
        var currentChallenge = await currentChallengeResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, currentChallengeResponse.StatusCode);
        Assert.Equal(64, currentChallenge.GetProperty("proofHash").GetString()!.Length);

        var dailyPlayerId = $"daily-player-{Guid.NewGuid():N}";
        using var attemptResponse = await _client.PostAsJsonAsync(
            "/api/v1/challenges/daily/current/attempts",
            new { playerId = dailyPlayerId });
        var attempt = await attemptResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, attemptResponse.StatusCode);
        var attemptRunId = attempt.GetProperty("runId").GetGuid();

        using var submissionResponse = await _client.PostAsJsonAsync(
            "/api/v1/challenges/daily/current/submissions",
            new { runId = attemptRunId, playerId = dailyPlayerId });
        var submission = await submissionResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, submissionResponse.StatusCode);
        Assert.True(submission.GetProperty("accepted").GetBoolean());
    }

    [Fact]
    public void CommandEnvelope_RejectsMissingIdentityAndType()
    {
        var missingIdentity = new CommandEnvelope { Type = "TEST" };
        var identityResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();

        var identityValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            missingIdentity,
            new System.ComponentModel.DataAnnotations.ValidationContext(missingIdentity),
            identityResults,
            validateAllProperties: true);

        var missingType = new CommandEnvelope { CommandId = Guid.NewGuid() };
        var typeResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var typeValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            missingType,
            new System.ComponentModel.DataAnnotations.ValidationContext(missingType),
            typeResults,
            validateAllProperties: true);

        Assert.False(identityValid);
        Assert.Contains(identityResults, result => result.MemberNames.Contains(nameof(CommandEnvelope.CommandId)));
        Assert.False(typeValid);
        Assert.Contains(typeResults, result => result.MemberNames.Contains(nameof(CommandEnvelope.Type)));
    }

    [Fact]
    public async Task RunCommandGateway_IsIdempotentAndRejectsStaleVersion()
    {
        var revision = await GetCurrentRevisionAsync();
        var player = $"command-player-{Guid.NewGuid():N}";
        using var startResponse = await _client.PostAsJsonAsync("/api/v1/runs", new
        {
            settingId = "default",
            runDefinitionId = "default_run",
            playerEntityId = player,
            seed = 778899UL,
            contentRevision = revision,
            modeId = "standard"
        });
        var run = await startResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);

        var runId = run.GetProperty("runId").GetGuid();
        var commandId = Guid.NewGuid();
        var expectedSequence = run.GetProperty("sequence").GetInt32();
        var expectedStep = run.GetProperty("step").GetUInt64();
        var envelope = new
        {
            commandId,
            expectedSequence,
            expectedStep,
            type = RunCommandTypes.StartEncounter,
            payload = new
            {
                participants = new object[]
                {
                    new
                    {
                        instanceId = player,
                        definitionId = "player_warrior",
                        sideId = "player",
                        controllerBinding = new { kind = "Player" }
                    },
                    new
                    {
                        instanceId = "enemy_1",
                        definitionId = "enemy_goblin",
                        sideId = "opposition",
                        controllerBinding = new { kind = "AI", policyId = "gambit" }
                    }
                },
                initialResourceValues = new Dictionary<string, IReadOnlyDictionary<string, float>>
                {
                    [player] = new Dictionary<string, float> { ["energy"] = 3 }
                }
            }
        };

        using var firstResponse = await _client.PostAsJsonAsync(
            $"/api/v1/runs/{runId}/commands",
            envelope);
        var first = await firstResponse.Content.ReadFromJsonAsync<JsonElement>();
        using var retryResponse = await _client.PostAsJsonAsync(
            $"/api/v1/runs/{runId}/commands",
            envelope);
        var retry = await retryResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        Assert.False(first.GetProperty("duplicate").GetBoolean());
        Assert.True(retry.GetProperty("duplicate").GetBoolean());
        Assert.Equal(first.GetProperty("sequence").GetInt32(), retry.GetProperty("sequence").GetInt32());
        Assert.Equal(first.GetProperty("stateHash").GetString(), retry.GetProperty("stateHash").GetString());

        using var staleResponse = await _client.PostAsJsonAsync(
            $"/api/v1/runs/{runId}/commands",
            new
            {
                commandId = Guid.NewGuid(),
                expectedSequence,
                expectedStep,
                type = RunCommandTypes.AdvanceNode,
                payload = new { targetNodeId = "reward" }
            });
        var stale = await staleResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
        Assert.Equal(ApiErrorCodes.VersionConflict, stale.GetProperty("code").GetString());
        Assert.True(stale.TryGetProperty("currentSequence", out _));
        Assert.True(stale.TryGetProperty("currentStep", out _));
    }

    [Fact]
    public async Task CombatCommandGateway_CommitsActionOnceInsideRunAggregate()
    {
        var revision = await GetCurrentRevisionAsync();
        var player = $"combat-command-player-{Guid.NewGuid():N}";
        using var startRunResponse = await _client.PostAsJsonAsync("/api/v1/runs", new
        {
            settingId = "default",
            runDefinitionId = "default_run",
            playerEntityId = player,
            seed = 998877UL,
            contentRevision = revision,
            modeId = "standard"
        });
        var started = await startRunResponse.Content.ReadFromJsonAsync<JsonElement>();
        var runId = started.GetProperty("runId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, startRunResponse.StatusCode);

        var encounter = await StartRunEncounterAsync(runId, player, new[] { "enemy_1" });
        var combatId = encounter.GetProperty("combatId").GetGuid();

        using var runResponse = await _client.GetAsync($"/api/v1/runs/{runId}");
        var run = await runResponse.Content.ReadFromJsonAsync<JsonElement>();
        var expectedSequence = run.GetProperty("sequence").GetInt32();
        var expectedStep = encounter.GetProperty("step").GetUInt64();
        var envelope = new
        {
            commandId = Guid.NewGuid(),
            expectedSequence,
            expectedStep,
            type = "END_TURN",
            payload = new
            {
                actorId = encounter.GetProperty("actors").EnumerateArray()
                    .First(actor => actor.GetProperty("controllerBinding").GetProperty("kind").GetString() == "Player")
                    .GetProperty("instanceId").GetString()
            }
        };

        using var firstResponse = await _client.PostAsJsonAsync(
            $"/api/v1/combats/{combatId}/commands",
            envelope);
        var firstBody = await firstResponse.Content.ReadAsStringAsync();
        Assert.True(firstResponse.IsSuccessStatusCode, firstBody);
        var first = JsonSerializer.Deserialize<JsonElement>(firstBody);

        using var retryResponse = await _client.PostAsJsonAsync(
            $"/api/v1/combats/{combatId}/commands",
            envelope);
        var retryBody = await retryResponse.Content.ReadAsStringAsync();
        Assert.True(retryResponse.IsSuccessStatusCode, retryBody);
        var retry = JsonSerializer.Deserialize<JsonElement>(retryBody);

        Assert.False(first.GetProperty("duplicate").GetBoolean());
        Assert.True(retry.GetProperty("duplicate").GetBoolean());
        Assert.Equal(first.GetProperty("stateHash").GetString(), retry.GetProperty("stateHash").GetString());
        Assert.Equal(first.GetProperty("sequence").GetInt32(), retry.GetProperty("sequence").GetInt32());
        Assert.True(
            first.GetProperty("state").GetProperty("combat").GetProperty("step").GetUInt64() > expectedStep);
    }

    [Fact]
    public async Task DurableJournal_SurvivesSnapshotPolicyAndSemanticReplayMatches()
    {
        var revision = await GetCurrentRevisionAsync();
        var player = $"replay-player-{Guid.NewGuid():N}";
        using var startResponse = await _client.PostAsJsonAsync("/api/v1/runs", new
        {
            settingId = "default",
            runDefinitionId = "default_run",
            playerEntityId = player,
            seed = 443322UL,
            contentRevision = revision,
            modeId = "standard"
        });
        var started = await startResponse.Content.ReadFromJsonAsync<JsonElement>();
        var runId = started.GetProperty("runId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);

        var encounter = await StartRunEncounterAsync(runId, player, new[] { "enemy_1" });
        var combatId = encounter.GetProperty("combatId").GetGuid();

        using var currentRunResponse = await _client.GetAsync($"/api/v1/runs/{runId}");
        var currentRun = await currentRunResponse.Content.ReadFromJsonAsync<JsonElement>();
        using var actionResponse = await _client.PostAsJsonAsync(
            $"/api/v1/combats/{combatId}/commands",
            new
            {
                commandId = Guid.NewGuid(),
                expectedSequence = currentRun.GetProperty("sequence").GetInt32(),
                expectedStep = encounter.GetProperty("step").GetUInt64(),
                type = "END_TURN",
                payload = new
                {
                    actorId = encounter.GetProperty("actors").EnumerateArray()
                        .First(actor => actor.GetProperty("controllerBinding").GetProperty("kind").GetString() == "Player")
                        .GetProperty("instanceId").GetString()
                }
            });
        var actionBody = await actionResponse.Content.ReadAsStringAsync();
        Assert.True(actionResponse.IsSuccessStatusCode, actionBody);

        using var journalResponse = await _client.GetAsync($"/api/v1/runs/{runId}/journal?limit=100");
        var journal = await journalResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, journalResponse.StatusCode);
        var journalEntries = journal.GetProperty("entries").EnumerateArray().ToArray();
        Assert.Equal(3, journalEntries.Length);
        Assert.Equal(journalEntries.Length, journal.GetProperty("returned").GetInt32());
        Assert.Equal(RunCommandTypes.StartRun, journalEntries[0].GetProperty("commandType").GetString());
        Assert.Contains(
            journalEntries,
            entry => entry.GetProperty("commandType").GetString() == RunCommandTypes.StartEncounter);
        Assert.Contains(
            journalEntries,
            entry => entry.GetProperty("commandType").GetString() == "END_TURN");
        Assert.DoesNotContain(
            journalEntries,
            entry => entry.GetProperty("commandType").GetString()!.StartsWith("combat.", StringComparison.Ordinal));

        using var commitsResponse = await _client.GetAsync($"/api/v1/runs/{runId}/commits");
        var commits = await commitsResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, commitsResponse.StatusCode);
        Assert.Equal(journalEntries.Length, commits.GetProperty("count").GetInt32());
        Assert.Contains(
            commits.GetProperty("commits").EnumerateArray(),
            commit => commit.GetProperty("frameCount").GetInt32() > 1);

        using var verifyResponse = await _client.PostAsync($"/api/v1/runs/{runId}/verify", null);
        var verificationBody = await verifyResponse.Content.ReadAsStringAsync();
        Assert.True(verifyResponse.IsSuccessStatusCode, verificationBody);
        var verification = JsonSerializer.Deserialize<JsonElement>(verificationBody);
        Assert.True(verification.GetProperty("isValid").GetBoolean(), verificationBody);
        Assert.True(verification.GetProperty("reexecuted").GetBoolean());
        Assert.Equal(journalEntries.Length, verification.GetProperty("commandsReplayed").GetInt32());
        Assert.Equal(
            verification.GetProperty("expectedFinalHash").GetString(),
            verification.GetProperty("actualFinalHash").GetString());

        using var combatJournalResponse = await _client.GetAsync($"/api/v1/combats/{combatId}/journal");
        var combatJournal = await combatJournalResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, combatJournalResponse.StatusCode);
        Assert.Equal(2, combatJournal.GetProperty("entries").GetArrayLength());

        using var combatVerifyResponse = await _client.PostAsync($"/api/v1/combats/{combatId}/verify", null);
        var combatVerification = await combatVerifyResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, combatVerifyResponse.StatusCode);
        Assert.True(combatVerification.GetProperty("isValid").GetBoolean());

        using var eventsResponse = await _client.GetAsync(
            $"/api/v1/runs/{runId}/events?afterSequence=1&limit=100");
        var events = await eventsResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, eventsResponse.StatusCode);
        var expectedFacts = commits.GetProperty("commits").EnumerateArray()
            .Where(commit => commit.GetProperty("sequence").GetInt32() > 1)
            .Sum(commit => commit.GetProperty("factCount").GetInt32());
        Assert.Equal(expectedFacts, events.GetProperty("returned").GetInt32());
        Assert.Equal(2, events.GetProperty("events")[0].GetProperty("sequence").GetInt32());
        Assert.Equal(
            journalEntries.Length,
            events.GetProperty("events")[expectedFacts - 1].GetProperty("sequence").GetInt32());
        Assert.All(
            events.GetProperty("events").EnumerateArray(),
            item => Assert.Equal("RUN_TRANSITION_COMMITTED", item.GetProperty("eventType").GetString()));

        using var repeatedEventsResponse = await _client.GetAsync(
            $"/api/v1/runs/{runId}/events?afterSequence=1&limit=100");
        var repeatedEvents = await repeatedEventsResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            events.GetProperty("events")[0].GetProperty("eventId").GetGuid(),
            repeatedEvents.GetProperty("events")[0].GetProperty("eventId").GetGuid());

        using var combatEventsResponse = await _client.GetAsync(
            $"/api/v1/combats/{combatId}/events?afterSequence=0&limit=10");
        var combatEvents = await combatEventsResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, combatEventsResponse.StatusCode);
        Assert.True(combatEvents.GetProperty("returned").GetInt32() > 2);
        Assert.All(
            combatEvents.GetProperty("events").EnumerateArray(),
            item => Assert.Equal(combatId, item.GetProperty("combatId").GetGuid()));
        Assert.Contains(
            combatEvents.GetProperty("events").EnumerateArray().GroupBy(item => item.GetProperty("sequence").GetInt32()),
            group => group.Count() > 1);
    }

    [Theory]
    [InlineData("/api/v1/statuses/apply")]
    [InlineData("/api/v1/modifiers/apply")]
    [InlineData("/api/v1/simulations/effects/apply")]
    [InlineData("/api/v1/simulations/damage/calculate")]
    [InlineData("/api/v1/gambits/reload")]
    [InlineData("/api/v1/actions/reload")]
    public async Task ParallelRuleMutationEndpoints_AreNotExposed(string path)
    {
        using var response = await _client.PostAsJsonAsync(path, new { });

        Assert.Contains(response.StatusCode, new[]
        {
            HttpStatusCode.NotFound,
            HttpStatusCode.MethodNotAllowed
        });
    }

    [Theory]
    [InlineData("/api/v1/actions")]
    [InlineData("/api/v1/statuses/definitions")]
    [InlineData("/api/v1/modifiers")]
    [InlineData("/api/v1/gambits")]
    [InlineData("/api/v1/simulations/effects/types")]
    [InlineData("/api/v1/simulations/damage/pipeline/config")]
    public async Task ParallelRuleReadEndpoints_AreNotExposed(string path)
    {
        using var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<JsonElement> StartRunEncounterAsync(
        Guid runId,
        string heroId,
        IReadOnlyList<string> enemyIds,
        IReadOnlyDictionary<string, float>? initialHeroResourceValues = null)
    {
        using var runResponse = await _client.GetAsync($"/api/v1/runs/{runId}");
        var run = await runResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, runResponse.StatusCode);

        using var commandResponse = await _client.PostAsJsonAsync($"/api/v1/runs/{runId}/commands", new
        {
            commandId = Guid.NewGuid(),
            expectedSequence = run.GetProperty("sequence").GetInt32(),
            expectedStep = run.GetProperty("step").GetUInt64(),
            type = RunCommandTypes.StartEncounter,
            payload = new
            {
                participants = new object[]
                {
                    new
                    {
                        instanceId = heroId,
                        definitionId = "player_warrior",
                        sideId = "player",
                        controllerBinding = new { kind = "Player" }
                    }
                }.Concat(enemyIds.Select(instanceId => (object)new
                {
                    instanceId,
                    definitionId = "enemy_goblin",
                    sideId = "opposition",
                    controllerBinding = new { kind = "AI", policyId = "gambit" }
                })),
                initialResourceValues = initialHeroResourceValues == null
                    ? null
                    : new Dictionary<string, IReadOnlyDictionary<string, float>>
                    {
                        [heroId] = initialHeroResourceValues
                    }
            }
        });
        var receiptBody = await commandResponse.Content.ReadAsStringAsync();
        Assert.True(commandResponse.IsSuccessStatusCode, receiptBody);
        var receipt = JsonSerializer.Deserialize<JsonElement>(receiptBody);
        var combatId = receipt.GetProperty("state").GetProperty("activeEncounterId").GetGuid();

        using var combatResponse = await _client.GetAsync($"/api/v1/combats/{combatId}");
        var combat = await combatResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, combatResponse.StatusCode);
        return combat;
    }

    [Fact]
    public async Task ContentDraft_PublishesImmutableQueryableRevision()
    {
        using var createRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/admin/settings/default/drafts");
        createRequest.Headers.Add("X-Admin-Key", "dev-admin-key");
        using var createResponse = await _client.SendAsync(createRequest);
        var createBody = await createResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var draft = JsonSerializer.Deserialize<JsonElement>(createBody);
        var draftId = draft.GetProperty("draftId").GetGuid();
        var version = draft.GetProperty("version").GetInt32();

        using var validateRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/admin/content/drafts/{draftId}/validate");
        validateRequest.Headers.Add("X-Admin-Key", "dev-admin-key");
        using var validateResponse = await _client.SendAsync(validateRequest);
        var validation = await validateResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, validateResponse.StatusCode);
        Assert.True(validation.GetProperty("isValid").GetBoolean(), validation.ToString());

        using var publishRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/admin/content/drafts/{draftId}/publish")
        {
            Content = JsonContent.Create(new { expectedVersion = version })
        };
        publishRequest.Headers.Add("X-Admin-Key", "dev-admin-key");
        using var publishResponse = await _client.SendAsync(publishRequest);
        var publishBody = await publishResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);
        var published = JsonSerializer.Deserialize<JsonElement>(publishBody);
        var revision = published.GetProperty("revision").GetString();
        Assert.NotNull(revision);
        Assert.Equal(64, revision.Length);

        using var revisionResponse = await _client.GetAsync($"/api/v1/content/revisions/{revision}");
        Assert.Equal(HttpStatusCode.OK, revisionResponse.StatusCode);

        using var catalogResponse = await _client.GetAsync(
            $"/api/v1/content/actions?revision={revision}&limit=10");
        var catalogBody = await catalogResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, catalogResponse.StatusCode);
        var catalog = JsonSerializer.Deserialize<JsonElement>(catalogBody);
        Assert.True(catalog.GetProperty("returned").GetInt32() > 0, catalogBody);

        using var republishRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/admin/content/drafts/{draftId}/publish")
        {
            Content = JsonContent.Create(new { expectedVersion = version })
        };
        republishRequest.Headers.Add("X-Admin-Key", "dev-admin-key");
        using var republishResponse = await _client.SendAsync(republishRequest);
        var republished = await republishResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, republishResponse.StatusCode);
        Assert.Equal(revision, republished.GetProperty("revision").GetString());
    }

    [Fact]
    public async Task PackageSetting_ValidatesPublishesAndDoesNotRebindActiveRun()
    {
        var initialRevision = await GetCurrentRevisionAsync();
        using var startResponse = await _client.PostAsJsonAsync("/api/v1/runs", new
        {
            settingId = "default",
            runDefinitionId = "default_run",
            playerEntityId = $"setting-publication-{Guid.NewGuid():N}",
            contentRevision = initialRevision,
            modeId = "standard"
        });
        var startBody = await startResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        var started = JsonSerializer.Deserialize<JsonElement>(startBody);
        var runId = started.GetProperty("runId").GetGuid();
        var pinnedRevision = started.GetProperty("contentRevision").GetString();

        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/settings");
        listRequest.Headers.Add("X-Admin-Key", "dev-admin-key");
        using var listResponse = await _client.SendAsync(listRequest);
        var listBody = await listResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Contains("default", listBody, StringComparison.Ordinal);

        using var validateRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/admin/settings/default/validate");
        validateRequest.Headers.Add("X-Admin-Key", "dev-admin-key");
        using var validateResponse = await _client.SendAsync(validateRequest);
        var validateBody = await validateResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, validateResponse.StatusCode);

        using var draftRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/admin/settings/default/drafts");
        draftRequest.Headers.Add("X-Admin-Key", "dev-admin-key");
        using var draftResponse = await _client.SendAsync(draftRequest);
        var draftBody = await draftResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.Created, draftResponse.StatusCode);
        var draftId = draftBody.GetProperty("draftId").GetGuid();
        var draftVersion = draftBody.GetProperty("version").GetInt32();

        using var draftValidationRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/admin/content/drafts/{draftId}/validate");
        draftValidationRequest.Headers.Add("X-Admin-Key", "dev-admin-key");
        using var draftValidationResponse = await _client.SendAsync(draftValidationRequest);
        var draftValidation = await draftValidationResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, draftValidationResponse.StatusCode);
        Assert.True(draftValidation.GetProperty("isValid").GetBoolean());

        using var publishRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/admin/content/drafts/{draftId}/publish")
        {
            Content = JsonContent.Create(new { expectedVersion = draftVersion })
        };
        publishRequest.Headers.Add("X-Admin-Key", "dev-admin-key");
        using var publishResponse = await _client.SendAsync(publishRequest);
        var publishBody = await publishResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);
        var publication = JsonSerializer.Deserialize<JsonElement>(publishBody);
        var revision = publication.GetProperty("revision").GetString();
        Assert.NotNull(revision);
        Assert.Equal(64, revision.Length);

        using var persistedRunResponse = await _client.GetAsync($"/api/v1/runs/{runId}");
        var persistedRun = await persistedRunResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, persistedRunResponse.StatusCode);
        Assert.Equal(pinnedRevision, persistedRun.GetProperty("contentRevision").GetString());

        using var revisionResponse = await _client.GetAsync($"/api/v1/content/revisions/{revision}");
        Assert.Equal(HttpStatusCode.OK, revisionResponse.StatusCode);
    }

    private async Task<string> GetCurrentRevisionAsync()
    {
        using var response = await _client.GetAsync("/api/v1/content/revisions?configName=default");
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        return content.GetProperty("currentRevision").GetString()!;
    }
}

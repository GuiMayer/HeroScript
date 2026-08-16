using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using API.Contracts;
using Core.Combat;
using Core.Run;
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
    public async Task InvalidModel_ReturnsProblemDetailsWithCorrelationId()
    {
        const string correlationId = "contract-test-correlation";
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/status/remove")
        {
            Content = JsonContent.Create(new { targetId = "enemy_1", instanceId = "not-a-guid" })
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
        var playerEntityId = $"reconnect-test-player-{Guid.NewGuid():N}";
        using var startResponse = await _client.PostAsJsonAsync("/api/v1/runs", new
        {
            configName = "default",
            runDefinitionId = "default_run",
            playerEntityId
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
        Assert.True(state.TryGetProperty("contentManifest", out _));
        Assert.Equal("start", state.GetProperty("map").GetProperty("currentNodeId").GetString());

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
        var playerEntityId = $"encounter-player-{Guid.NewGuid():N}";
        using var startRunResponse = await _client.PostAsJsonAsync("/api/v1/runs", new
        {
            configName = "default",
            runDefinitionId = "default_run",
            playerEntityId
        });
        var run = await startRunResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, startRunResponse.StatusCode);
        var runId = run.GetProperty("runId").GetGuid();

        using var startEncounterResponse = await _client.PostAsJsonAsync(
            $"/api/v1/runs/{runId}/encounters",
            new
            {
                heroId = playerEntityId,
                enemies = new[] { "enemy_1" },
                initialEnergy = 3
            });
        var combat = await startEncounterResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, startEncounterResponse.StatusCode);
        Assert.Equal(runId, combat.GetProperty("runId").GetGuid());
        Assert.Equal("start", combat.GetProperty("runNodeId").GetString());
        Assert.Equal(
            run.GetProperty("contentRevision").GetString(),
            combat.GetProperty("contentRevision").GetString());
        Assert.Equal(
            run.GetProperty("engineVersion").GetString(),
            combat.GetProperty("engineVersion").GetString());
        var combatId = combat.GetProperty("combatId").GetGuid();

        using var currentResponse = await _client.GetAsync(
            $"/api/v1/runs/{runId}/encounters/current");
        var current = await currentResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, currentResponse.StatusCode);
        Assert.Equal(combatId, current.GetProperty("combatId").GetGuid());

        var combatSystem = _factory.Services.GetRequiredService<ICombatSystem>();
        Assert.True(combatSystem.RemoveCombatState(combatId).IsSuccess);

        using var combatResponse = await _client.GetAsync($"/api/v1/combats/{combatId}");
        Assert.Equal(HttpStatusCode.OK, combatResponse.StatusCode);

        using var runResponse = await _client.GetAsync($"/api/v1/runs/{runId}");
        var persistedRun = await runResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, runResponse.StatusCode);
        Assert.Equal(combatId, persistedRun.GetProperty("activeEncounterId").GetGuid());
        Assert.Single(persistedRun.GetProperty("encounters").EnumerateArray());
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
        var player = $"command-player-{Guid.NewGuid():N}";
        using var startResponse = await _client.PostAsJsonAsync("/api/v1/runs", new
        {
            configName = "default",
            runDefinitionId = "default_run",
            playerEntityId = player,
            seed = 778899UL
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
                heroId = player,
                enemyIds = new[] { "enemy_1" },
                initialEnergy = 3
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
        var player = $"combat-command-player-{Guid.NewGuid():N}";
        using var startRunResponse = await _client.PostAsJsonAsync("/api/v1/runs", new
        {
            configName = "default",
            runDefinitionId = "default_run",
            playerEntityId = player,
            seed = 998877UL
        });
        var started = await startRunResponse.Content.ReadFromJsonAsync<JsonElement>();
        var runId = started.GetProperty("runId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, startRunResponse.StatusCode);

        using var encounterResponse = await _client.PostAsJsonAsync(
            $"/api/v1/runs/{runId}/encounters",
            new { heroId = player, enemies = new[] { "enemy_1" }, initialEnergy = 3 });
        var encounter = await encounterResponse.Content.ReadFromJsonAsync<JsonElement>();
        var combatId = encounter.GetProperty("combatId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, encounterResponse.StatusCode);

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
            payload = new { actorId = encounter.GetProperty("hero").GetProperty("entityId").GetString() }
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
            first.GetProperty("state").GetProperty("combat").GetProperty("determinism")
                .GetProperty("step").GetUInt64() > expectedStep);
    }

    [Fact]
    public async Task DurableJournal_SurvivesSnapshotPolicyAndSemanticReplayMatches()
    {
        var player = $"replay-player-{Guid.NewGuid():N}";
        using var startResponse = await _client.PostAsJsonAsync("/api/v1/runs", new
        {
            configName = "default",
            runDefinitionId = "default_run",
            playerEntityId = player,
            seed = 443322UL
        });
        var started = await startResponse.Content.ReadFromJsonAsync<JsonElement>();
        var runId = started.GetProperty("runId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);

        using var encounterResponse = await _client.PostAsJsonAsync(
            $"/api/v1/runs/{runId}/encounters",
            new { heroId = player, enemies = new[] { "enemy_1" }, initialEnergy = 3 });
        var encounter = await encounterResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, encounterResponse.StatusCode);
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
                payload = new { actorId = encounter.GetProperty("hero").GetProperty("entityId").GetString() }
            });
        var actionBody = await actionResponse.Content.ReadAsStringAsync();
        Assert.True(actionResponse.IsSuccessStatusCode, actionBody);

        using var journalResponse = await _client.GetAsync($"/api/v1/runs/{runId}/journal?limit=10");
        var journal = await journalResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, journalResponse.StatusCode);
        Assert.Equal(3, journal.GetProperty("returned").GetInt32());
        Assert.Equal("run.start", journal.GetProperty("entries")[0].GetProperty("commandType").GetString());
        Assert.Equal(RunCommandTypes.StartEncounter, journal.GetProperty("entries")[1].GetProperty("commandType").GetString());
        Assert.Equal("END_TURN", journal.GetProperty("entries")[2].GetProperty("commandType").GetString());

        using var checkpointsResponse = await _client.GetAsync($"/api/v1/runs/{runId}/checkpoints");
        var checkpoints = await checkpointsResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, checkpointsResponse.StatusCode);
        Assert.Equal(3, checkpoints.GetProperty("count").GetInt32());

        using var verifyResponse = await _client.PostAsync($"/api/v1/runs/{runId}/verify", null);
        var verificationBody = await verifyResponse.Content.ReadAsStringAsync();
        Assert.True(verifyResponse.IsSuccessStatusCode, verificationBody);
        var verification = JsonSerializer.Deserialize<JsonElement>(verificationBody);
        Assert.True(verification.GetProperty("isValid").GetBoolean(), verificationBody);
        Assert.True(verification.GetProperty("reexecuted").GetBoolean());
        Assert.Equal(3, verification.GetProperty("commandsReplayed").GetInt32());
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
    }
}

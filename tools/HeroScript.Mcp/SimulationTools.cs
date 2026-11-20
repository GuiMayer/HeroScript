using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using ModelContextProtocol;

namespace HeroScript.Mcp;

public sealed class SandboxSession(HeroScriptApiClient? client) : IDisposable
{
    public async Task<HeroScriptApiClient> Connect(CancellationToken token)
    {
        if (client is null) throw new McpException("Simulation disabled. Set HEROSCRIPT_MCP_SANDBOX_URL to an explicitly isolated MCP sandbox API.");
        var marker = await client.Get("/api/v1/diagnostics/mcp-sandbox", token);
        if (!marker.GetProperty("isolated").GetBoolean()) throw new McpException("API is not an isolated MCP sandbox.");
        return client;
    }
    public void Dispose() => client?.Dispose();
}

[McpServerToolType]
public sealed class SimulationTools(SandboxSession sandbox)
{
    public sealed record SimulationCommand(string Type, JsonElement Payload);

    [McpServerTool(Name = "simulate_branch", ReadOnly = false), Description("Execute explicit commands on a branch of a source run in an isolated test API; never writes the source head.")]
    public async Task<object> Branch(Guid runId, int sequence, SimulationCommand[] commands, CancellationToken token)
    {
        if (sequence < 1 || commands.Length > 100) throw new ArgumentException("Positive sequence and no more than 100 commands required.");
        var api = await sandbox.Connect(token);
        var before = await api.Get($"/api/v1/runs/{runId}", token);
        var result = await api.Post("/api/v1/simulations", new { sourceRunId = runId, sourceSequence = sequence, commands }, token);
        var after = await api.Get($"/api/v1/runs/{runId}", token);
        return new { runId, sequence, contentRevision = before.GetProperty("contentRevision"), result,
            sourceUnchanged = before.GetProperty("stateHash").GetString() == after.GetProperty("stateHash").GetString() &&
                before.GetProperty("sequence").GetInt32() == after.GetProperty("sequence").GetInt32() };
    }

    [McpServerTool(Name = "simulate_encounter", ReadOnly = false), Description("Launch an isolated sandbox scenario at an explicit revision and seed, then play a bounded deterministic heuristic over legal cards/end turns. Does not modify the real campaign.")]
    public async Task<EncounterResult> Encounter(string settingId, string revision, ulong seed, JsonElement scenario, CancellationToken token, int maxRounds = 30, int maxCommands = 100)
    {
        McpSafetyPolicy.Limit(maxRounds);
        McpSafetyPolicy.Limit(maxCommands);
        if (scenario.ValueKind != JsonValueKind.Object || scenario.GetRawText().Length > 200_000) throw new ArgumentException("A bounded scenario object is required.");
        var api = await sandbox.Connect(token);
        var input = JsonNode.Parse(scenario.GetRawText())!.AsObject();
        input["contentRevision"] = revision;
        input["seed"] = seed;
        input["attemptKey"] = "mcp-" + Guid.NewGuid().ToString("N");
        await api.Post($"/api/v1/sandbox/scenarios/validate?configName={McpSafetyPolicy.Segment(settingId)}", input, token);
        var launch = await api.Post($"/api/v1/sandbox/runs?configName={McpSafetyPolicy.Segment(settingId)}", input, token);
        var runId = launch.GetProperty("run").GetProperty("runId").GetGuid();
        var combatId = launch.GetProperty("combat").GetProperty("combatId").GetGuid();
        var initial = await api.Get($"/api/v1/combats/{combatId}", token);
        var run = await api.Get($"/api/v1/runs/{runId}", token);
        var combat = initial;
        var actions = new List<ObservedAction>();
        string? failure = null;
        var combatRoute = $"/api/v1/combats/{combatId}";
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            for (var i = 0; i < maxCommands && combat.GetProperty("status").GetString() == "ACTIVE" && combat.GetProperty("currentTurn").GetInt32() <= maxRounds; i++)
            {
                budget.Token.ThrowIfCancellationRequested();
                var candidates = (await api.Get(combatRoute + "/legal-actions", budget.Token)).GetProperty("candidates").EnumerateArray().ToArray();
                var selected = candidates.Where(c => ActionType(c) is "PLAY_CARD" or "END_TURN" or "PASS_PRIORITY")
                    .OrderBy(c => ActionType(c) == "PLAY_CARD" ? 0 : 1).ThenBy(c => c.GetProperty("candidateId").GetString(), StringComparer.Ordinal).FirstOrDefault();
                if (selected.ValueKind == JsonValueKind.Undefined) { failure = "No supported legal player action"; break; }
                var command = selected.GetProperty("command");
                var type = ActionType(selected);
                var payload = new Dictionary<string, JsonElement>();
                foreach (var name in new[] { "actorId", "cardInstanceId", "targetIds", "costOptionId" })
                    if (command.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null) payload[name] = value;
                if (type != "PLAY_CARD") { payload.Remove("cardInstanceId"); payload.Remove("targetIds"); payload.Remove("costOptionId"); }
                var receipt = await api.Post(combatRoute + "/commands", new { commandId = Guid.NewGuid(), expectedSequence = run.GetProperty("sequence").GetInt32(),
                    expectedStep = combat.GetProperty("step").GetUInt64(), type, payload }, budget.Token);
                var next = receipt.GetProperty("state").GetProperty("combat");
                actions.Add(new(type, selected.TryGetProperty("cardDefinitionId", out var card) ? card.GetString() : null,
                    selected.TryGetProperty("costs", out var costs) ? costs : null, selected.TryGetProperty("applications", out var applications) ? applications : null,
                    combat.GetProperty("currentTurn").GetInt32(), Health(combat, true) - Health(next, true), Health(combat, false) - Health(next, false),
                    type == "END_TURN" ? Energy(combat) : 0));
                combat = next;
                run = await api.Get($"/api/v1/runs/{runId}", budget.Token);
            }
        }
        catch (Exception e) when (!token.IsCancellationRequested && e is McpException or InvalidOperationException or TimeoutException or OperationCanceledException)
        { failure = e.Message; }
        var status = combat.GetProperty("status").GetString()!;
        return new(settingId, revision, seed, runId, combatId, status, combat.GetProperty("currentTurn").GetInt32(),
            initial.GetProperty("actors"), combat.GetProperty("actors"), combat.GetProperty("stateHash").GetString()!, actions,
            Health(initial, true), Health(combat, true), Health(initial, false), Health(combat, false), status == "ACTIVE" || failure != null, failure);
    }

    private static string? ActionType(JsonElement candidate) => candidate.GetProperty("command").GetProperty("actionType").GetString();
    private static bool IsPlayer(JsonElement actor) => actor.GetProperty("controllerBinding").GetProperty("kind").GetString() == "Player";
    private static double Resource(JsonElement actor, string resource) => actor.GetProperty("resources").TryGetProperty(resource, out var pool) ? pool.GetProperty("current").GetDouble() : 0;
    private static double Health(JsonElement combat, bool player) => combat.GetProperty("actors").EnumerateArray().Where(a => IsPlayer(a) == player).Sum(a => Resource(a, "health"));
    private static double Energy(JsonElement combat) => combat.GetProperty("actors").EnumerateArray().Where(IsPlayer).Sum(a => Resource(a, "energy"));
}

public sealed record ObservedAction(string? Type, string? CardDefinitionId, JsonElement? Costs, JsonElement? Applications, int Round, double PlayerNetHpLost, double EnemyNetHpLost, double UnspentEnergyAtEndTurn);
public sealed record EncounterResult(string SettingId, string ContentRevision, ulong Seed, Guid RunId, Guid CombatId, string Status, int Round,
    JsonElement InitialActors, JsonElement FinalActors, string StateHash, IReadOnlyList<ObservedAction> Actions,
    double InitialPlayerHp, double FinalPlayerHp, double InitialEnemyHp, double FinalEnemyHp, bool Incomplete, string? Error);

using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace HeroScript.Mcp;

[McpServerToolType]
public sealed class EngineTools(HeroScriptApiClient api)
{
    [McpServerTool(Name = "verify_replay", ReadOnly = true), Description("Reexecute the authoritative replay, report verification and a bounded timeline page; exactly one run or combat ID.")]
    public async Task<object> VerifyReplay(CancellationToken token, Guid? runId = null, Guid? combatId = null, int afterSequence = 0, int limit = 50)
    {
        if (runId.HasValue == combatId.HasValue) throw new ArgumentException("Supply exactly one runId or combatId.");
        McpSafetyPolicy.Limit(limit);
        if (afterSequence < 0) throw new ArgumentOutOfRangeException(nameof(afterSequence));
        var route = runId.HasValue ? $"/api/v1/runs/{runId}" : $"/api/v1/combats/{combatId}";
        var before = await api.Get(route, token);
        // This POST is the existing verification query, not a gameplay command.
        var verification = await api.Post(route + "/verify", null, token);
        var timeline = await api.Get($"{route}/timeline?afterSequence={afterSequence}&limit={limit}", token);
        var after = await api.Get(route, token);
        var unchanged = before.GetProperty("stateHash").GetString() == after.GetProperty("stateHash").GetString();
        return new { runId, combatId, contentRevision = before.GetProperty("contentRevision"), verification, timeline,
            stateUnchanged = unchanged, afterSequence, limit, note = "Verification covers the full replay; the timeline page is bounded." };
    }
    [McpServerTool(Name = "inspect_save", ReadOnly = true), Description("Inspect a run's pinned and current revisions and journal head without migrating or restoring it.")]
    public async Task<object> Save(Guid runId, CancellationToken token)
    {
        var run = await api.Get($"/api/v1/runs/{runId}", token);
        var setting = run.GetProperty("settingId").GetString()!;
        var revision = run.GetProperty("contentRevision").GetString()!;
        var sequence = run.GetProperty("sequence").GetInt32();
        var revisions = await api.Get($"/api/v1/content/revisions?configName={McpSafetyPolicy.Segment(setting)}", token);
        var current = revisions.GetProperty("currentRevision").GetString();
        var capabilities = await api.Get($"/api/v1/runs/{runId}/capabilities", token);
        var journalAllowed = capabilities.GetProperty("granted").EnumerateArray().Any(item => item.GetString() == "timeline.read");
        JsonElement? journal = journalAllowed ? await api.Get($"/api/v1/runs/{runId}/journal?afterSequence={Math.Max(0, sequence - 1)}&limit=1", token) : null;
        return new { runId, settingId = setting, contentRevision = revision, currentRevision = current,
            requiresReview = revision != current, sequence, stateHash = run.GetProperty("stateHash"), journal, journalAllowed,
            note = "Revision mismatch does not imply migration or corruption. Use verify_replay for integrity." };
    }

    [McpServerTool(Name = "validate_content", ReadOnly = true), Description("Validate a published pinned revision or supplied content bundle without publishing it.")]
    public async Task<object> ValidateContent(string settingId, string revision, CancellationToken token, JsonElement? candidate = null)
    {
        var manifest = await api.Get($"/api/v1/content/revisions/{McpSafetyPolicy.Segment(revision)}?configName={McpSafetyPolicy.Segment(settingId)}", token);
        if (manifest.TryGetProperty("configName", out var configName) && configName.GetString() != settingId)
            throw new ArgumentException("Revision belongs to another setting.");
        if (candidate.HasValue && candidate.Value.GetRawText().Length > 2_000_000) throw new ArgumentException("Candidate bundle exceeds input budget.");
        var result = await api.ValidateContent(new { revision, bundle = candidate }, token);
        return new { settingId, contentRevision = revision, validation = result };
    }
    [McpServerTool(Name = "inspect_gambits", ReadOnly = true), Description("Trace the engine's actual gambit decision against a pinned combat snapshot without advancing it.")]
    public Task<JsonElement> Gambits(Guid runId, Guid combatId, string actorId, CancellationToken token, int? sequence = null)
        => api.Get($"/api/v1/diagnostics/gambits/{runId}/{combatId}/{McpSafetyPolicy.Segment(actorId)}" + (sequence.HasValue ? $"?sequence={sequence}" : ""), token);

    [McpServerTool(Name = "validate_gambits", ReadOnly = true), Description("Check configured enemy gambit bindings with bounded synthetic round and health fixtures at a pinned revision.")]
    public async Task<JsonElement> ValidateGambits(string settingId, string revision, CancellationToken token, string? runDefinitionId = null, int budget = 3000, string[]? runDefinitionIds = null)
    {
        McpSafetyPolicy.Limit(budget, 5000);
        if (runDefinitionIds?.Length > 50) throw new ArgumentException("At most 50 run definitions.");
        return await api.Get($"/api/v1/diagnostics/gambits/coverage?settingId={McpSafetyPolicy.Segment(settingId)}&revision={McpSafetyPolicy.Segment(revision)}&budget={budget}" +
            (runDefinitionId is null ? "" : $"&runDefinitionId={McpSafetyPolicy.Segment(runDefinitionId)}") + string.Concat((runDefinitionIds ?? []).Select(id => $"&runDefinitionIds={McpSafetyPolicy.Segment(id)}")), token);
    }
    [McpServerTool(Name = "get_engine_status", ReadOnly = true), Description("Check access to the local HeroScript API without changing game state.")]
    public async Task<object> Status(CancellationToken token)
    {
        var health = await api.Get("/api/v1/health/ready", token);
        var capabilities = await api.Get("/api/v1/capabilities", token);
        var settings = await api.Get("/api/v1/content/settings", token);
        return new { available = true, contract = "api/v1", health, capabilities, settings = settings.GetProperty("items").GetArrayLength() };
    }

    [McpServerTool(Name = "list_settings", ReadOnly = true), Description("List game settings and current content revisions, optionally filtered by setting ID.")]
    public async Task<object> Settings(CancellationToken token, string? settingId = null, int offset = 0, int limit = 100)
    {
        McpSafetyPolicy.Limit(limit);
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        var all = (await api.Get("/api/v1/content/settings", token)).GetProperty("items").EnumerateArray()
            .Where(item => settingId is null || item.GetProperty("settingId").GetString() == settingId).ToArray();
        return new { total = all.Length, offset, items = all.Skip(offset).Take(limit).ToArray(), incomplete = (long)offset + limit < all.Length };
    }

    [McpServerTool(Name = "inspect_run", ReadOnly = true), Description("Read the current run state, pinned revision, sequence, and tool capabilities.")]
    public async Task<object> Run(Guid runId, CancellationToken token)
    {
        var run = await api.Get($"/api/v1/runs/{runId}", token);
        var capabilities = await api.Get($"/api/v1/runs/{runId}/capabilities", token);
        return new { run, capabilities };
    }

    [McpServerTool(Name = "inspect_combat", ReadOnly = true), Description("Read live authoritative combat state and legal actions for a specified actor or the active actor.")]
    public async Task<object> Combat(Guid combatId, CancellationToken token, string? actorId = null)
    {
        var state = await api.Get($"/api/v1/combats/{combatId}", token);
        var legal = await api.Get($"/api/v1/combats/{combatId}/legal-actions" + (actorId is null ? "" : $"?actorId={McpSafetyPolicy.Segment(actorId)}"), token);
        return new { state, legalActions = legal };
    }

    [McpServerTool(Name = "inspect_entity", ReadOnly = true), Description("Read resources, statuses, controller binding and calculated attributes of a combat actor.")]
    public async Task<object> Entity(Guid combatId, string actorId, CancellationToken token)
    {
        var combat = await api.Get($"/api/v1/combats/{combatId}", token);
        var actor = combat.GetProperty("actors").EnumerateArray().FirstOrDefault(a => a.GetProperty("instanceId").GetString() == actorId);
        if (actor.ValueKind == JsonValueKind.Undefined) throw new ArgumentException("Actor not present in combat.", nameof(actorId));
        var attributes = await api.Get($"/api/v1/combats/{combatId}/actors/{McpSafetyPolicy.Segment(actorId)}/attributes", token);
        return new { combatId, actorId, contentRevision = combat.GetProperty("contentRevision"), actor, attributes };
    }

    [McpServerTool(Name = "inspect_card", ReadOnly = true), Description("Inspect a card's engine-evaluated costs, effects, targets and impediments.")]
    public async Task<JsonElement> Card(Guid combatId, Guid cardInstanceId, CancellationToken token, string? actorId = null, string[]? targetIds = null, string? costOptionId = null)
    {
        var parameters = new List<string>();
        if (actorId is not null) parameters.Add($"actorId={McpSafetyPolicy.Segment(actorId)}");
        if (targetIds?.Length > 20) throw new ArgumentException("At most 20 targets.");
        foreach (var target in targetIds ?? []) parameters.Add($"targetIds={McpSafetyPolicy.Segment(target)}");
        if (costOptionId is not null) parameters.Add($"costOptionId={McpSafetyPolicy.Segment(costOptionId)}");
        return await api.Get($"/api/v1/combats/{combatId}/cards/{cardInstanceId}/evaluation?{string.Join('&', parameters)}", token);
    }
}

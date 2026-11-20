using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace HeroScript.Mcp;

[McpServerToolType]
public sealed class EngineTools(HeroScriptApiClient api)
{
    [McpServerTool(Name = "get_engine_status", ReadOnly = true), Description("Check access to the local HeroScript API without changing game state.")]
    public async Task<object> Status(CancellationToken token)
    {
        var settings = await api.Get("/api/v1/content/settings", token);
        return new { available = true, contract = "api/v1", settings = settings.GetProperty("items").GetArrayLength() };
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

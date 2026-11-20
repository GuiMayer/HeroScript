using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using ModelContextProtocol;

namespace HeroScript.Mcp;

[McpServerToolType]
public sealed class BalanceTools(SandboxSession sandbox)
{
    [McpServerTool(Name = "analyze_cards", ReadOnly = false), Description("Compare observed card costs, effects and encounter outcomes over a bounded set of seeds in the isolated test API. Not a proof of optimal play.")]
    public async Task<object> AnalyzeCards(string settingId, string revision, JsonElement scenario, ulong[] seeds, string[] cardDefinitionIds,
        CancellationToken token, int maxRounds = 30, int maxCommands = 100)
    {
        if (seeds.Length is < 1 or > 20 || seeds.Distinct().Count() != seeds.Length || cardDefinitionIds.Length is < 1 or > 100)
            throw new ArgumentException("Use 1–20 distinct seeds and 1–100 card IDs.");
        McpSafetyPolicy.Limit(maxRounds);
        McpSafetyPolicy.Limit(maxCommands);
        await sandbox.Connect(token);
        var runs = new List<EncounterResult>();
        var failures = new List<object>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        foreach (var seed in seeds)
        {
            if (timeout.IsCancellationRequested) break;
            try { runs.Add(await new SimulationTools(sandbox).Encounter(settingId, revision, seed, scenario, timeout.Token, maxRounds, maxCommands)); }
            catch (Exception ex) when (!token.IsCancellationRequested && ex is McpException or InvalidOperationException or TimeoutException or OperationCanceledException)
            { failures.Add(new { seed, error = ex.Message }); }
        }
        token.ThrowIfCancellationRequested();
        var cards = cardDefinitionIds.Distinct(StringComparer.Ordinal).Select(id =>
        {
            var plays = runs.SelectMany(r => r.Actions).Where(a => a.CardDefinitionId == id).ToArray();
            var costs = plays.Where(a => a.Costs?.ValueKind == JsonValueKind.Array).SelectMany(a => a.Costs!.Value.EnumerateArray())
                .GroupBy(c => c.GetProperty("resourceId").GetString()).Select(g => new { resourceId = g.Key, total = g.Sum(c => c.GetProperty("amount").GetDouble()) }).ToArray();
            return new { cardDefinitionId = id, observedPlays = plays.Length, costs,
                meanEnemyNetHpLost = plays.Length == 0 ? (double?)null : plays.Average(a => a.EnemyNetHpLost),
                meanPlayerNetHpLost = plays.Length == 0 ? (double?)null : plays.Average(a => a.PlayerNetHpLost),
                predictedEffectApplications = plays.Sum(a => a.Applications?.ValueKind == JsonValueKind.Array ? a.Applications.Value.GetArrayLength() : 0) };
        }).ToArray();
        return new { settingId, contentRevision = revision, seeds, samples = runs.Count, cards,
            wins = runs.Count(r => r.Status == "VICTORY"), losses = runs.Count(r => r.Status == "DEFEAT"),
            draws = runs.Count(r => r.Status == "DRAW"), incomplete = runs.Count != seeds.Length || runs.Any(r => r.Incomplete), failures,
            results = runs.Select(r => new { r.Seed, r.RunId, r.CombatId, r.Status, r.Round, r.StateHash, r.FinalPlayerHp,
                netPlayerHpLost = r.InitialPlayerHp - r.FinalPlayerHp, r.FinalEnemyHp, commands = r.Actions.Count,
                unspentEnergyAtEndTurns = r.Actions.Sum(a => a.UnspentEnergyAtEndTurn), r.Incomplete, r.Error }),
            limitation = "Fixed legal-action heuristic, not optimal play. HP deltas are net (healing and automatic responses included); candidate applications are preview observations." };
    }
}

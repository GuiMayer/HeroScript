using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace API.Tests.Helpers;

/// <summary>
/// Simulates a game engine client (Unity/Godot) making HTTP requests to the HeroScript API.
/// Provides high-level methods for common game workflows.
/// </summary>
public class GameEngineClientSimulator
{
    private readonly HttpClient _client;
    private readonly Dictionary<Guid, Guid> _combatRuns = new();

    public GameEngineClientSimulator(HttpClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    // ==================== RUN MANAGEMENT ====================

    public async Task<Guid> StartRunAsync(
        string configName = "default",
        string runDefinitionId = "default_run",
        string playerEntityId = "player",
        ulong? seed = null,
        string modeId = "standard")
    {
        var contentRevision = await GetCurrentContentRevisionAsync(configName);
        var response = await _client.PostAsJsonAsync("/api/v1/runs", new
        {
            settingId = configName,
            runDefinitionId,
            playerEntityId,
            seed,
            contentRevision,
            modeId
        });

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("runId").GetGuid();
    }

    public async Task<string> GetCurrentContentRevisionAsync(string settingId = "default")
    {
        var response = await _client.GetAsync(
            $"/api/v1/content/revisions?configName={Uri.EscapeDataString(settingId)}");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("currentRevision").GetString()
            ?? throw new InvalidOperationException("Current content revision was not returned");
    }

    public async Task<JsonElement> GetRunStateAsync(Guid runId)
    {
        var response = await _client.GetAsync($"/api/v1/runs/{runId}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async Task<List<string>> DrawCardsAsync(Guid runId, int count = 1)
    {
        var before = await GetPlayableCardsAsync(runId);
        await ExecuteRunCommandStateAsync(runId, "INVOKE_CARD_ZONE_GAMEPLAY_FLOW",
            new { flowId = "run.draw", requestedCount = count });
        var after = await GetPlayableCardsAsync(runId);
        var previousIds = before.Select(card => card.CardInstanceId).ToHashSet();
        return after
            .Where(card => !previousIds.Contains(card.CardInstanceId))
            .Select(card => card.DefinitionId)
            .ToList();
    }

    public async Task<JsonElement> DiscardCardsAsync(Guid runId, IEnumerable<string> cardIds)
    {
        var playableCards = await GetPlayableCardsAsync(runId);
        var used = new HashSet<Guid>();
        var instanceIds = cardIds.Select(reference =>
        {
            if (Guid.TryParse(reference, out var instanceId))
                return instanceId;
            var card = playableCards.First(item =>
                !used.Contains(item.CardInstanceId) &&
                string.Equals(item.DefinitionId, reference, StringComparison.Ordinal));
            used.Add(card.CardInstanceId);
            return card.CardInstanceId;
        }).ToArray();
        return await ExecuteRunCommandStateAsync(runId, "INVOKE_CARD_ZONE_GAMEPLAY_FLOW",
            new { flowId = "run.discard", cardInstanceIds = instanceIds });
    }

    public async Task<List<string>> GetPlayableCardIdsAsync(Guid runId)
    {
        return (await GetPlayableCardsAsync(runId))
            .Select(card => card.DefinitionId)
            .ToList();
    }

    public async Task<Guid> GetPlayableCardInstanceIdAsync(Guid runId, string definitionId)
    {
        return (await GetPlayableCardsAsync(runId))
            .First(card => string.Equals(card.DefinitionId, definitionId, StringComparison.Ordinal))
            .CardInstanceId;
    }

    private async Task<List<PlayableCard>> GetPlayableCardsAsync(Guid runId)
    {
        var response = await _client.GetAsync($"/api/v1/runs/{runId}/card-zones");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("zones").EnumerateArray()
            .Where(zone => zone.GetProperty("allowsCardPlay").GetBoolean())
            .SelectMany(zone => zone.GetProperty("cards").EnumerateArray())
            .Select(card => new PlayableCard(
                card.GetProperty("cardInstanceId").GetGuid(),
                card.GetProperty("definitionId").GetString()!))
            .ToList();
    }

    // ==================== COMBAT MANAGEMENT ====================

    public async Task<Guid> StartCombatAsync(
        string heroId,
        IEnumerable<string> enemies,
        IReadOnlyDictionary<string, float>? initialHeroResourceValues = null,
        Guid? runId = null,
        string heroDefinitionId = "player_warrior",
        string enemyDefinitionId = "enemy_goblin")
    {
        var enemyIds = enemies.ToArray();
        var ownerRunId = runId ?? await StartRunAsync(playerEntityId: heroId);
        var state = await ExecuteRunCommandStateAsync(ownerRunId, "START_ENCOUNTER", new
        {
            participants = new[]
            {
                new
                {
                    instanceId = heroId,
                    definitionId = heroDefinitionId,
                    sideId = "player",
                    controllerBinding = new { kind = "Player", policyId = (string?)null }
                }
            }.Concat(enemyIds.Select(instanceId => new
            {
                instanceId,
                definitionId = enemyDefinitionId,
                sideId = "opposition",
                controllerBinding = new { kind = "AI", policyId = (string?)"gambit" }
            })),
            initialResourceValues = initialHeroResourceValues == null
                ? null
                : new Dictionary<string, IReadOnlyDictionary<string, float>>(StringComparer.Ordinal)
                {
                    [heroId] = initialHeroResourceValues
                }
        });
        var combatId = state.GetProperty("activeEncounterId").GetGuid();
        _combatRuns[combatId] = ownerRunId;
        return combatId;
    }

    public async Task<JsonElement> GetCombatStateAsync(Guid combatId)
    {
        var response = await _client.GetAsync($"/api/v1/combats/{combatId}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async Task<JsonElement> ExecuteActionAsync(Guid combatId, string actorId, string? targetId = null, 
        string? powerId = null, string? cardId = null, Guid? runId = null)
    {
        var ownerRunId = ResolveRunId(combatId, runId);
        Guid? cardInstanceId = null;
        if (!string.IsNullOrWhiteSpace(cardId) && !Guid.TryParse(cardId, out _))
        {
            cardInstanceId = await GetPlayableCardInstanceIdAsync(ownerRunId, cardId);
        }
        else if (Guid.TryParse(cardId, out var parsedCardInstanceId))
        {
            cardInstanceId = parsedCardInstanceId;
        }
        var run = await GetRunStateAsync(ownerRunId);
        var combat = await GetCombatStateAsync(combatId);
        var commandResponse = await _client.PostAsJsonAsync($"/api/v1/combats/{combatId}/commands", new
        {
            commandId = Guid.NewGuid(),
            expectedSequence = run.GetProperty("sequence").GetInt32(),
            expectedStep = combat.GetProperty("step").GetUInt64(),
            type = cardInstanceId.HasValue ? "PLAY_CARD" : "EXECUTE_ACTION",
            payload = cardInstanceId.HasValue
                ? (object)new
                {
                    actorId,
                    cardInstanceId,
                    targetIds = targetId == null ? Array.Empty<string>() : new[] { targetId }
                }
                : new
                {
                    actorId,
                    targetId,
                    powerId,
                    actionType = powerId == null ? 0 : 1
                }
        });
        if (!commandResponse.IsSuccessStatusCode)
        {
            var error = await commandResponse.Content.ReadAsStringAsync();
            throw new HttpRequestException(
                $"Combat command failed with {(int)commandResponse.StatusCode} {commandResponse.StatusCode}: {error}");
        }
        var receipt = await commandResponse.Content.ReadFromJsonAsync<JsonElement>();
        return receipt.GetProperty("state").GetProperty("combat").Clone();
    }

    private sealed record PlayableCard(Guid CardInstanceId, string DefinitionId);

    public async Task<JsonElement> EndTurnAsync(Guid combatId, Guid? runId = null)
    {
        var ownerRunId = ResolveRunId(combatId, runId);
        var run = await GetRunStateAsync(ownerRunId);
        var combat = await GetCombatStateAsync(combatId);
        var commandResponse = await _client.PostAsJsonAsync($"/api/v1/combats/{combatId}/commands", new
        {
            commandId = Guid.NewGuid(),
            expectedSequence = run.GetProperty("sequence").GetInt32(),
            expectedStep = combat.GetProperty("step").GetUInt64(),
            type = "END_TURN",
            payload = new
            {
                actorId = combat.GetProperty("actors").EnumerateArray()
                    .First(actor => actor.GetProperty("controllerBinding").GetProperty("kind").GetString() == "Player")
                    .GetProperty("instanceId").GetString()
            }
        });
        commandResponse.EnsureSuccessStatusCode();
        var receipt = await commandResponse.Content.ReadFromJsonAsync<JsonElement>();
        return receipt.GetProperty("state").GetProperty("combat").Clone();
    }

    // ==================== CARD SELECTION (REWARDS) ====================

    public async Task<JsonElement> StartCardSelectionAsync(Guid runId, string selectionId = "basic_reward")
    {
        var state = await ExecuteRunCommandStateAsync(runId, "CREATE_CARD_SELECTION", new { selectionId });
        return state.GetProperty("cardSelections").EnumerateArray().Last().Clone();
    }

    public async Task<JsonElement> PickCardsAsync(Guid runId, Guid selectionInstanceId, IEnumerable<string> cardIds)
    {
        var state = await ExecuteRunCommandStateAsync(runId, "PICK_CARD_REWARD", new { selectionInstanceId, cardIds });
        return FindByGuid(state, "cardSelections", "selectionInstanceId", selectionInstanceId);
    }

    public async Task<JsonElement> RerollCardSelectionAsync(Guid runId, Guid selectionInstanceId, IEnumerable<string>? lockedCardIds = null)
    {
        var state = await ExecuteRunCommandStateAsync(
            runId,
            "REROLL_CARD_REWARD",
            new { selectionInstanceId, lockedCardIds });
        return FindByGuid(state, "cardSelections", "selectionInstanceId", selectionInstanceId);
    }

    public async Task<JsonElement> DecomposeCardAsync(Guid runId, Guid selectionInstanceId, string cardId)
    {
        var state = await ExecuteRunCommandStateAsync(
            runId,
            "DECOMPOSE_CARD_REWARD",
            new { selectionInstanceId, cardId });
        return FindByGuid(state, "cardSelections", "selectionInstanceId", selectionInstanceId);
    }

    // ==================== SHOP ====================

    public async Task<JsonElement> OpenShopAsync(Guid runId, string shopId = "basic_shop")
    {
        var state = await ExecuteRunCommandStateAsync(runId, "CREATE_SHOP", new { shopId });
        return state.GetProperty("shops").EnumerateArray().Last().Clone();
    }

    public async Task<JsonElement> BuyShopItemAsync(Guid runId, Guid shopInstanceId, string itemId)
    {
        var state = await ExecuteRunCommandStateAsync(runId, "BUY_SHOP_ITEM", new { shopInstanceId, itemId });
        var shop = FindByGuid(state, "shops", "shopInstanceId", shopInstanceId);
        return shop.GetProperty("items").EnumerateArray()
            .Single(item => string.Equals(item.GetProperty("itemId").GetString(), itemId, StringComparison.Ordinal))
            .Clone();
    }

    public async Task<JsonElement> RerollShopAsync(Guid runId, Guid shopInstanceId)
    {
        var state = await ExecuteRunCommandStateAsync(runId, "REROLL_SHOP", new { shopInstanceId });
        return FindByGuid(state, "shops", "shopInstanceId", shopInstanceId);
    }

    // ==================== PREPARATION ====================

    public async Task<JsonElement> StartPreparationAsync(Guid runId, string preparationId = "basic_preparation")
    {
        var state = await ExecuteRunCommandStateAsync(runId, "CREATE_PREPARATION", new { preparationId });
        return state.GetProperty("preparations").EnumerateArray().Last().Clone();
    }

    public async Task<JsonElement> ApplyPreparationOptionAsync(Guid runId, Guid preparationInstanceId, string optionId)
    {
        var state = await ExecuteRunCommandStateAsync(
            runId,
            "APPLY_PREPARATION_OPTION",
            new { preparationInstanceId, optionId });
        var preparation = FindByGuid(state, "preparations", "preparationInstanceId", preparationInstanceId);
        return preparation.GetProperty("options").EnumerateArray()
            .Single(option => string.Equals(option.GetProperty("optionId").GetString(), optionId, StringComparison.Ordinal))
            .Clone();
    }

    private async Task<JsonElement> ExecuteRunCommandStateAsync(Guid runId, string type, object payload)
    {
        var current = await GetRunStateAsync(runId);
        var response = await _client.PostAsJsonAsync($"/api/v1/runs/{runId}/commands", new
        {
            commandId = Guid.NewGuid(),
            expectedSequence = current.GetProperty("sequence").GetInt32(),
            expectedStep = current.GetProperty("step").GetUInt64(),
            type,
            payload
        });
        response.EnsureSuccessStatusCode();
        var receipt = await response.Content.ReadFromJsonAsync<JsonElement>();
        return receipt.GetProperty("state").Clone();
    }

    private static JsonElement FindByGuid(JsonElement state, string collectionName, string idName, Guid id)
    {
        return state.GetProperty(collectionName).EnumerateArray()
            .Single(item => item.GetProperty(idName).GetGuid() == id)
            .Clone();
    }

    private static List<string> NewItems(IEnumerable<string> before, IEnumerable<string> after)
    {
        var remaining = before.ToList();
        var added = new List<string>();
        foreach (var item in after)
        {
            if (!remaining.Remove(item))
                added.Add(item);
        }

        return added;
    }

    private Guid ResolveRunId(Guid combatId, Guid? explicitRunId)
    {
        if (explicitRunId.HasValue)
        {
            _combatRuns[combatId] = explicitRunId.Value;
            return explicitRunId.Value;
        }

        return _combatRuns.TryGetValue(combatId, out var runId)
            ? runId
            : throw new InvalidOperationException($"Run ownership is unknown for combat {combatId}");
    }

    // ==================== EVENTS ====================

    public async Task<List<JsonElement>> GetEventsAsync(string? category = null, Guid? runId = null, 
        Guid? combatId = null, string? eventType = null, int limit = 100)
    {
        var queryParams = new List<string>();
        if (category != null) queryParams.Add($"category={category}");
        if (runId != null) queryParams.Add($"runId={runId}");
        if (combatId != null) queryParams.Add($"combatId={combatId}");
        if (eventType != null) queryParams.Add($"eventType={eventType}");
        queryParams.Add($"limit={limit}");

        var query = string.Join("&", queryParams);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/admin/telemetry?{query}");
        request.Headers.Add("X-Admin-Key", "dev-admin-key");
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var events = json.ValueKind == JsonValueKind.Array
            ? json
            : json.GetProperty("events");
        return events.EnumerateArray().ToList();
    }

    // ==================== FORMULAS ====================

    public async Task<JsonElement> EvaluateFormulaAsync(string formulaName, Dictionary<string, float> parameters)
    {
        var contentRevision = await GetCurrentContentRevisionAsync();
        var request = new
        {
            formulaName,
            inputValue = 0f,
            paramOverrides = parameters,
            contentRevision
        };

        var response = await _client.PostAsJsonAsync("/api/v1/simulations/formulas/evaluate", request);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(
                $"Formula evaluation failed with {(int)response.StatusCode} {response.StatusCode}: {error}");
        }
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    // ==================== ENTITIES ====================

    public async Task<JsonElement> GetEntityDefinitionAsync(string definitionId)
    {
        var revisionsResponse = await _client.GetAsync("/api/v1/content/revisions?configName=default");
        revisionsResponse.EnsureSuccessStatusCode();
        var revisions = await revisionsResponse.Content.ReadFromJsonAsync<JsonElement>();
        var revision = revisions.GetProperty("currentRevision").GetString();
        var response = await _client.GetAsync(
            $"/api/v1/content/entities/{definitionId}?revision={revision}&configName=default");
        response.EnsureSuccessStatusCode();
        var publication = await response.Content.ReadFromJsonAsync<JsonElement>();
        return publication.GetProperty("definition").Clone();
    }

    // ==================== HELPERS ====================

    public async Task<HttpResponseMessage> GetRawResponseAsync(string url)
    {
        return await _client.GetAsync(url);
    }

    public async Task<HttpResponseMessage> PostRawAsync(string url, object? body = null)
    {
        return await _client.PostAsJsonAsync(url, body ?? new { });
    }

    public async Task<HttpResponseMessage> PostAdminRawAsync(string url, object? body = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body ?? new { })
        };
        request.Headers.Add("X-Admin-Key", "dev-admin-key");
        return await _client.SendAsync(request);
    }
}

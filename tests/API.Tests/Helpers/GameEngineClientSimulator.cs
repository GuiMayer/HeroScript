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

    public GameEngineClientSimulator(HttpClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    // ==================== RUN MANAGEMENT ====================

    public async Task<Guid> StartRunAsync(
        string configName = "default",
        string runDefinitionId = "default_run",
        string playerEntityId = "player",
        ulong? seed = null)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/runs", new
        {
            configName,
            runDefinitionId,
            playerEntityId,
            seed
        });

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("runId").GetGuid();
    }

    public async Task<JsonElement> GetRunStateAsync(Guid runId)
    {
        var response = await _client.GetAsync($"/api/v1/runs/{runId}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async Task<List<string>> DrawCardsAsync(Guid runId, int count = 1)
    {
        var before = await GetHandAsync(runId);
        var state = await ExecuteRunCommandStateAsync(runId, "DRAW_CARDS", new { count });
        var after = state.GetProperty("deck").GetProperty("hand")
            .EnumerateArray().Select(card => card.GetString()!).ToList();
        return NewItems(before, after);
    }

    public async Task<JsonElement> DiscardCardsAsync(Guid runId, IEnumerable<string> cardIds)
    {
        return await ExecuteRunCommandStateAsync(runId, "DISCARD_CARDS", new { cardIds });
    }

    public async Task<List<string>> GetHandAsync(Guid runId)
    {
        var response = await _client.GetAsync($"/api/v1/runs/{runId}/hand");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("hand").EnumerateArray().Select(e => e.GetString()!).ToList();
    }

    // ==================== COMBAT MANAGEMENT ====================

    public async Task<Guid> StartCombatAsync(string heroId, IEnumerable<string> enemies, int initialEnergy = 3, Guid? runId = null)
    {
        if (runId.HasValue)
        {
            var state = await ExecuteRunCommandStateAsync(runId.Value, "START_ENCOUNTER", new
            {
                heroId,
                enemyIds = enemies,
                initialEnergy
            });
            return state.GetProperty("activeEncounterId").GetGuid();
        }

        var request = new
        {
            heroId,
            enemies,
            initialEnergy,
            runId
        };

        var response = await _client.PostAsJsonAsync("/api/v1/combats/start", request);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("combatId").GetGuid();
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
        if (runId.HasValue)
        {
            var run = await GetRunStateAsync(runId.Value);
            var combat = await GetCombatStateAsync(combatId);
            var commandResponse = await _client.PostAsJsonAsync($"/api/v1/combats/{combatId}/commands", new
            {
                commandId = Guid.NewGuid(),
                expectedSequence = run.GetProperty("sequence").GetInt32(),
                expectedStep = combat.GetProperty("step").GetUInt64(),
                type = "EXECUTE_ACTION",
                payload = new
                {
                    actorId,
                    targetId,
                    actionId = powerId ?? cardId,
                    powerId,
                    cardId,
                    actionType = (int?)(powerId != null || cardId != null ? null : 0)
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

        var request = new
        {
            actorId,
            targetId,
            actionId = powerId ?? cardId,
            powerId,
            cardId,
            runId,
            actionType = powerId != null ? "POWER" : (cardId != null ? "POWER" : "BASIC_ATTACK")
        };

        var response = await _client.PostAsJsonAsync($"/api/v1/combats/{combatId}/action", request);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(
                $"Combat action failed with {(int)response.StatusCode} {response.StatusCode}: {error}");
        }
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async Task<JsonElement> EndTurnAsync(Guid combatId, Guid? runId = null)
    {
        if (runId.HasValue)
        {
            var run = await GetRunStateAsync(runId.Value);
            var combat = await GetCombatStateAsync(combatId);
            var commandResponse = await _client.PostAsJsonAsync($"/api/v1/combats/{combatId}/commands", new
            {
                commandId = Guid.NewGuid(),
                expectedSequence = run.GetProperty("sequence").GetInt32(),
                expectedStep = combat.GetProperty("step").GetUInt64(),
                type = "END_TURN",
                payload = new { actorId = combat.GetProperty("hero").GetProperty("entityId").GetString() }
            });
            commandResponse.EnsureSuccessStatusCode();
            var receipt = await commandResponse.Content.ReadFromJsonAsync<JsonElement>();
            return receipt.GetProperty("state").GetProperty("combat").Clone();
        }

        var response = await _client.PostAsync($"/api/v1/combats/{combatId}/end-turn", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async Task<JsonElement> ProcessAiTurnsAsync(Guid combatId)
    {
        var response = await _client.PostAsync($"/api/v1/combats/{combatId}/process-ai-turns", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async Task<JsonElement> AutoPlayCombatAsync(Guid combatId)
    {
        var response = await _client.PostAsync($"/api/v1/combats/{combatId}/auto-play", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
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

    // ==================== STATUS EFFECTS ====================
    public async Task<List<JsonElement>> GetStatusEffectsAsync(string targetId)
    {
        var response = await _client.GetAsync($"/api/v1/statuses/{targetId}");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.EnumerateArray().ToList();
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
        var response = await _client.GetAsync($"/api/v1/admin/events?{query}");
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
        var request = new
        {
            formulaName,
            inputValue = 0f,
            paramOverrides = parameters
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

    public async Task<JsonElement> CreateEntityAsync(
        string definitionId,
        string entityId,
        string? displayName = null)
    {
        var request = new
        {
            definitionId,
            entityId,
            displayName
        };

        var response = await _client.PostAsJsonAsync("/api/v1/entities/create", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
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

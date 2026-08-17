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

    public async Task<Guid> StartRunAsync(string configName = "default", string runDefinitionId = "default_run", string playerEntityId = "player")
    {
        var response = await _client.PostAsJsonAsync("/api/run/start", new
        {
            configName,
            runDefinitionId,
            playerEntityId
        });

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("runId").GetGuid();
    }

    public async Task<JsonElement> GetRunStateAsync(Guid runId)
    {
        var response = await _client.GetAsync($"/api/run/{runId}/state");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async Task<List<string>> DrawCardsAsync(Guid runId, int count = 1)
    {
        var response = await _client.PostAsJsonAsync($"/api/run/{runId}/draw", new { count });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("drawn").EnumerateArray().Select(e => e.GetString()!).ToList();
    }

    public async Task<JsonElement> DiscardCardsAsync(Guid runId, IEnumerable<string> cardIds)
    {
        var response = await _client.PostAsJsonAsync($"/api/run/{runId}/discard", new { cardIds });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async Task<List<string>> GetHandAsync(Guid runId)
    {
        var response = await _client.GetAsync($"/api/run/{runId}/hand");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("hand").EnumerateArray().Select(e => e.GetString()!).ToList();
    }

    // ==================== COMBAT MANAGEMENT ====================

    public async Task<Guid> StartCombatAsync(string heroId, IEnumerable<string> enemies, int initialEnergy = 3, Guid? runId = null)
    {
        var request = new
        {
            heroId,
            enemies,
            initialEnergy,
            runId
        };

        var response = await _client.PostAsJsonAsync("/api/combat/start", request);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("combatId").GetGuid();
    }

    public async Task<JsonElement> GetCombatStateAsync(Guid combatId)
    {
        var response = await _client.GetAsync($"/api/combat/{combatId}/state");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async Task<JsonElement> ExecuteActionAsync(Guid combatId, string actorId, string? targetId = null, 
        string? powerId = null, string? cardId = null, Guid? runId = null)
    {
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

        var response = await _client.PostAsJsonAsync($"/api/combat/{combatId}/action", request);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(
                $"Combat action failed with {(int)response.StatusCode} {response.StatusCode}: {error}");
        }
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async Task<JsonElement> EndTurnAsync(Guid combatId)
    {
        var response = await _client.PostAsync($"/api/combat/{combatId}/end-turn", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async Task<JsonElement> ProcessAiTurnsAsync(Guid combatId)
    {
        var response = await _client.PostAsync($"/api/combat/{combatId}/process-ai-turns", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async Task<JsonElement> AutoPlayCombatAsync(Guid combatId)
    {
        var response = await _client.PostAsync($"/api/combat/{combatId}/auto-play", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    // ==================== CARD SELECTION (REWARDS) ====================

    public async Task<JsonElement> StartCardSelectionAsync(Guid runId, string selectionId = "basic_reward")
    {
        var response = await _client.PostAsJsonAsync($"/api/run/{runId}/card-selection/start", new { selectionId });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async Task<JsonElement> PickCardsAsync(Guid runId, Guid selectionInstanceId, IEnumerable<string> cardIds)
    {
        var response = await _client.PostAsJsonAsync($"/api/run/{runId}/card-selection/{selectionInstanceId}/pick", 
            new { cardIds });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async Task<JsonElement> RerollCardSelectionAsync(Guid runId, Guid selectionInstanceId, IEnumerable<string>? lockedCardIds = null)
    {
        var response = await _client.PostAsJsonAsync($"/api/run/{runId}/card-selection/{selectionInstanceId}/reroll", 
            new { lockedCardIds });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async Task<JsonElement> DecomposeCardAsync(Guid runId, Guid selectionInstanceId, string cardId)
    {
        var response = await _client.PostAsync($"/api/run/{runId}/card-selection/{selectionInstanceId}/decompose/{cardId}", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    // ==================== SHOP ====================

    public async Task<JsonElement> OpenShopAsync(Guid runId, string shopId = "basic_shop")
    {
        var response = await _client.PostAsJsonAsync($"/api/run/{runId}/shop/open", new { shopId });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async Task<JsonElement> BuyShopItemAsync(Guid runId, Guid shopInstanceId, string itemId)
    {
        var response = await _client.PostAsync($"/api/run/{runId}/shop/{shopInstanceId}/buy/{itemId}", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async Task<JsonElement> RerollShopAsync(Guid runId, Guid shopInstanceId)
    {
        var response = await _client.PostAsync($"/api/run/{runId}/shop/{shopInstanceId}/reroll", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    // ==================== PREPARATION ====================

    public async Task<JsonElement> StartPreparationAsync(Guid runId, string preparationId = "basic_preparation")
    {
        var response = await _client.PostAsJsonAsync($"/api/run/{runId}/preparation/start", new { preparationId });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async Task<JsonElement> ApplyPreparationOptionAsync(Guid runId, Guid preparationInstanceId, string optionId)
    {
        var response = await _client.PostAsync($"/api/run/{runId}/preparation/{preparationInstanceId}/apply/{optionId}", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    // ==================== STATUS EFFECTS ====================

    public async Task<JsonElement> ApplyStatusEffectAsync(string targetId, string statusId, int stacks = 1, 
        int? duration = null, string? sourceId = null)
    {
        var request = new
        {
            targetId,
            statusId,
            stacks,
            duration,
            sourceId
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/status/apply")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("X-Admin-Key", "dev-admin-key");
        var response = await _client.SendAsync(message);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async Task<List<JsonElement>> GetStatusEffectsAsync(string targetId)
    {
        var response = await _client.GetAsync($"/api/status/{targetId}");
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
        var response = await _client.GetAsync($"/api/events?{query}");
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

        var response = await _client.PostAsJsonAsync("/api/formula/evaluate", request);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(
                $"Formula evaluation failed with {(int)response.StatusCode} {response.StatusCode}: {error}");
        }
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    // ==================== ENTITIES ====================

    public async Task<JsonElement> CreateEntityAsync(string definitionId, Dictionary<string, object>? overrides = null)
    {
        var request = new
        {
            definitionId,
            overrides
        };

        var response = await _client.PostAsJsonAsync("/api/entity/create", request);
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// Integration tests for event tracking, observability, and event sourcing.
/// Tests event generation, filtering, and system observability.
/// </summary>
public sealed class GameFlowEventTrackingTests : GameEngineIntegrationTestBase
{
    public GameFlowEventTrackingTests(TestWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task GetEvents_CombatEvents_ReturnsHistory()
    {
        var (runId, combatId, playerEntityId, _) = await SetupPrototypeCombatAsync(new[] { "enemy_1" });

        await ExecuteBasicAttackAsync(runId, combatId, playerEntityId, "enemy_1");

        // Get events for combat
        var events = await Client.GetEventsAsync(combatId: combatId);

        // Verify events returned
        Assert.NotEmpty(events);

        // Verify events have required fields
        foreach (var evt in events)
        {
            AssertJsonPropertyExists(evt, "eventId");
            AssertJsonPropertyExists(evt, "eventType");
            AssertJsonPropertyExists(evt, "timestamp");
        }
    }

    [Fact]
    public async Task GetEvents_FilterByCombat_ReturnsFiltered()
    {
        var (runId, combatId, playerEntityId, _) = await SetupPrototypeCombatAsync(new[] { "enemy_1", "enemy_2" });

        await ExecuteBasicAttackAsync(runId, combatId, playerEntityId, "enemy_1");
        await ExecuteBasicAttackAsync(runId, combatId, playerEntityId, "enemy_2");

        // Get events filtered by combat
        var combatEvents = await Client.GetEventsAsync(combatId: combatId);

        // Verify filtering worked
        Assert.NotEmpty(combatEvents);
        
        // All events should be from this combat
        foreach (var evt in combatEvents)
        {
            if (evt.TryGetProperty("combatId", out var evtCombatId))
            {
                Assert.Equal(combatId.ToString(), evtCombatId.GetString());
            }
        }
    }

    [Fact]
    public async Task GetEvents_FilterByEventType_ReturnsFiltered()
    {
        var (runId, combatId, playerEntityId, _) = await SetupPrototypeCombatAsync(new[] { "enemy_1" });

        await ExecuteBasicAttackAsync(runId, combatId, playerEntityId, "enemy_1");

        // Get events filtered by type
        var actionEvents = await Client.GetEventsAsync(eventType: "ActionExecutedEvent");

        Assert.NotEmpty(actionEvents);
        // All events should be damage events
        foreach (var evt in actionEvents)
        {
            if (evt.TryGetProperty("eventType", out var eventType))
            {
                Assert.Equal("ActionExecutedEvent", eventType.GetString());
            }
        }
    }

    [Fact]
    public async Task GetEvents_MultipleFilters_ReturnsIntersection()
    {
        var (runId, combatId, playerEntityId, _) = await SetupPrototypeCombatAsync(new[] { "enemy_1" });

        await ExecuteBasicAttackAsync(runId, combatId, playerEntityId, "enemy_1");

        // Get events with multiple filters
        var filteredEvents = await Client.GetEventsAsync(
            combatId: combatId,
            eventType: "ActionExecutedEvent");

        // Verify events match all filters
        Assert.NotEmpty(filteredEvents);
        foreach (var evt in filteredEvents)
        {
            // Should be an action event.
            if (evt.TryGetProperty("eventType", out var eventType))
            {
                Assert.Equal("ActionExecutedEvent", eventType.GetString());
            }

            // Should involve the run player.
            var hasHeroInSource = evt.TryGetProperty("actorId", out var source) &&
                                 source.GetString() == playerEntityId;
            Assert.True(hasHeroInSource, $"Event should have the run player as source: {evt.GetRawText()}");

            // Should be from this combat
            if (evt.TryGetProperty("combatId", out var evtCombatId))
            {
                Assert.Equal(combatId, Guid.Parse(evtCombatId.GetString()!));
            }
        }
    }

    [Fact]
    public async Task CombatFlow_EventsRecordFullHistory_Observability()
    {
        var (runId, combatId, playerEntityId, combatState) = await SetupPrototypeCombatAsync(new[] { "enemy_1" });

        var initialTurn = GetJsonInt(combatState, "currentTurn");

        await Client.DrawCardsAsync(runId, 5);

        await ExecuteBasicAttackAsync(runId, combatId, playerEntityId, "enemy_1");

        var defendCard = (await Client.GetPlayableCardIdsAsync(runId)).First(cardId => cardId == "defend");
        await Client.ExecuteActionAsync(combatId, playerEntityId, cardId: defendCard, runId: runId);

        await Client.EndTurnAsync(combatId, runId);

        // Get all combat events
        var allEvents = await Client.GetEventsAsync(combatId: combatId);

        // Verify comprehensive event history
        Assert.NotEmpty(allEvents);

        // Verify events are chronologically ordered
        DateTimeOffset? previousTimestamp = null;
        foreach (var evt in allEvents)
        {
            if (evt.TryGetProperty("timestamp", out var timestampProp))
            {
                var timestamp = timestampProp.GetDateTimeOffset();
                if (previousTimestamp.HasValue)
                {
                    Assert.True(timestamp >= previousTimestamp.Value, 
                        "Events should be chronologically ordered");
                }
                previousTimestamp = timestamp;
            }
        }

        // Verify we have events for actions taken
        var eventTypes = allEvents
            .Where(e => e.TryGetProperty("eventType", out _))
            .Select(e => e.GetProperty("eventType").GetString())
            .ToList();

        // Should have action-related events
        Assert.NotEmpty(eventTypes);
    }

    private async Task<(Guid runId, Guid combatId, string playerEntityId, JsonElement combatState)> SetupPrototypeCombatAsync(
        string[] enemies)
    {
        var (runId, runState) = await SetupRunAsync();
        var playerEntityId = GetJsonString(runState, "playerEntityId");
        var combatId = await Client.StartCombatAsync(playerEntityId, enemies, runId: runId);
        return (runId, combatId, playerEntityId, await Client.GetCombatStateAsync(combatId));
    }

    private async Task ExecuteBasicAttackAsync(Guid runId, Guid combatId, string playerEntityId, string targetId)
    {
        var card = (await Client.GetPlayableCardIdsAsync(runId)).First(cardId => cardId == "basic_attack");
        await Client.ExecuteActionAsync(combatId, playerEntityId, targetId, cardId: card, runId: runId);
    }
}

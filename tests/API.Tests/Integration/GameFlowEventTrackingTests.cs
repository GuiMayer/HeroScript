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
        // Setup and run combat
        var (combatId, combatState) = await SetupCombatAsync("hero", new[] { "enemy_1" });

        // Execute action to generate events
        await Client.ExecuteActionAsync(combatId, "hero", targetId: "enemy_1", powerId: "basic_attack");

        // Get events for combat
        var events = await Client.GetEventsAsync(combatId: combatId);

        // Verify events returned
        Assert.NotNull(events);
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
        // Setup combat
        var (combatId, combatState) = await SetupCombatAsync("hero", new[] { "enemy_1", "enemy_2" });

        // Execute multiple actions
        await Client.ExecuteActionAsync(combatId, "hero", targetId: "enemy_1", powerId: "basic_attack");
        await Client.ExecuteActionAsync(combatId, "hero", targetId: "enemy_2", powerId: "basic_attack");

        // Get events filtered by combat
        var combatEvents = await Client.GetEventsAsync(combatId: combatId);

        // Verify filtering worked
        Assert.NotNull(combatEvents);
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
        // Setup combat
        var (combatId, combatState) = await SetupCombatAsync("hero", new[] { "enemy_1" });

        // Execute action
        await Client.ExecuteActionAsync(combatId, "hero", targetId: "enemy_1", powerId: "basic_attack");

        // Get events filtered by type
        var damageEvents = await Client.GetEventsAsync(eventType: "DAMAGE_DEALT");

        // Verify filtering worked
        Assert.NotNull(damageEvents);
        
        // All events should be damage events
        foreach (var evt in damageEvents)
        {
            if (evt.TryGetProperty("eventType", out var eventType))
            {
                Assert.Equal("DAMAGE_DEALT", eventType.GetString());
            }
        }
    }

    [Fact]
    public async Task GetEvents_MultipleFilters_ReturnsIntersection()
    {
        // Setup combat
        var (combatId, combatState) = await SetupCombatAsync("hero", new[] { "enemy_1" });

        // Execute action
        await Client.ExecuteActionAsync(combatId, "hero", targetId: "enemy_1", powerId: "basic_attack");

        // Get events with multiple filters
        var filteredEvents = await Client.GetEventsAsync(
            combatId: combatId,
            eventType: "DAMAGE_DEALT");

        // Verify events match all filters
        Assert.NotNull(filteredEvents);
        
        foreach (var evt in filteredEvents)
        {
            // Should be damage event
            if (evt.TryGetProperty("eventType", out var eventType))
            {
                Assert.Equal("DAMAGE_DEALT", eventType.GetString());
            }

            // Should involve hero
            var hasHeroInSource = evt.TryGetProperty("sourceEntityId", out var source) && 
                                 source.GetString() == "hero";
            Assert.True(hasHeroInSource, "Event should have hero as source");

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
        // Setup and run full combat flow
        var (combatId, combatState) = await SetupCombatAsync("hero", new[] { "enemy_1" });

        var initialTurn = GetJsonInt(combatState, "currentTurn");

        // Action 1: Attack
        await Client.ExecuteActionAsync(combatId, "hero", targetId: "enemy_1", powerId: "basic_attack");

        // Action 2: Defend
        await Client.ExecuteActionAsync(combatId, "hero", powerId: "defend");

        // End turn
        await Client.EndTurnAsync(combatId);

        // Get all combat events
        var allEvents = await Client.GetEventsAsync(combatId: combatId);

        // Verify comprehensive event history
        Assert.NotNull(allEvents);
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
}

# HeroScript Game Flow Documentation

This document describes the complete workflow for each game genre supported by HeroScript, including the sequence of API calls, decision points, and integration patterns.

---

## Table of Contents

1. [Roguelike Flow](#roguelike-flow)
2. [Auto-Battler Flow](#auto-battler-flow)
3. [Tactical RPG Flow](#tactical-rpg-flow)
4. [Puzzle RPG Flow](#puzzle-rpg-flow)
5. [Common Patterns](#common-patterns)
6. [Error Handling](#error-handling)

---

## Roguelike Flow

**Genre Overview:** Run-based progression with procedurally generated encounters, deck building, shops, and permadeath.

### Complete Run Lifecycle

```
1. Run Initialization
   POST /api/run/start
   ├─ configName: "default"
   ├─ runDefinitionId: "standard_roguelike"
   └─ playerEntityId: "hero"
   
   Returns: { runId, gold: 0, deck: {...} }

2. Combat Loop
   ┌─────────────────────────────────────┐
   │                                     │
   │  POST /api/combat/start             │
   │  ├─ heroId: from run state          │
   │  └─ enemyIds: procedurally selected │
   │                                     │
   │  Returns: { combatId, hero, enemies }│
   │                                     │
   ↓                                     │
   
3. Player Turn Phase                     │
   ┌─────────────────────────┐          │
   │ GET /api/run/{runId}/hand          │
   │                                     │
   │ POST /api/combat/{combatId}/action │
   │ ├─ Play card from hand              │
   │ ├─ Use power                        │
   │ └─ Basic attack                     │
   │                                     │
   │ Repeat until turn ends             │
   └─────────────────────────┘          │
                                         │
4. End Turn                              │
   POST /api/combat/{combatId}/end-turn │
   ├─ Processes end-of-turn effects     │
   └─ Resets energy                     │
                                         │
5. Enemy Turn Phase                      │
   POST /api/combat/{combatId}/process-ai-turns │
   ├─ All enemies execute gambits       │
   └─ Returns updated combat state      │
                                         │
   If combat active: goto step 3        │
   If combat complete: goto step 6      │
   └─────────────────────────────────────┘

6. Post-Combat Rewards
   POST /api/run/{runId}/card-selection/start
   ├─ selectionDefinitionId: "combat_reward"
   └─ Returns: { selectionInstanceId, options }
   
   POST /api/run/{runId}/card-selection/{id}/pick
   └─ Adds selected cards to deck
   
   Optional:
   POST /api/run/{runId}/card-selection/{id}/reroll (if rerolls available)
   POST /api/run/{runId}/card-selection/{id}/decompose/{cardId} (for resources)

7. Shop Phase (Optional)
   POST /api/run/{runId}/shop/open
   └─ Returns: { shopInstanceId, items, playerGold }
   
   POST /api/run/{runId}/shop/{id}/buy/{itemId}
   ├─ Deducts gold
   └─ Adds item to inventory/deck
   
   POST /api/run/{runId}/shop/{id}/reroll (costs gold)

8. Preparation Phase (Before next combat)
   POST /api/run/{runId}/preparation/start
   └─ Returns: { preparationInstanceId, options }
   
   POST /api/run/{runId}/preparation/{id}/apply/{optionId}
   └─ Applies buffs/effects for next combat

9. Loop
   Goto step 2 until:
   - Hero dies (run ends, permadeath)
   - Final boss defeated (victory)
   - Player exits run
```

### Key Decision Points

**After Combat Victory:**
- Take reward cards (increase deck power)
- Decompose cards (gain resources for shop)
- Skip rewards (keep deck lean)

**In Shop:**
- Buy cards (expand strategy options)
- Buy relics (permanent run bonuses)
- Reroll shop (costs gold, find better options)
- Skip shop (save gold for later)

**Preparation Phase:**
- Buff hero stats temporarily
- Apply status effects to enemies
- Gain temporary resources

### Example Integration Code

```csharp
// Roguelike client integration example
var runId = await client.StartRunAsync("default", "standard_roguelike", "hero");

while (runIsActive)
{
    // Start combat
    var combatId = await client.StartCombatAsync(heroId, enemyIds);
    
    // Combat loop
    while (combatIsActive)
    {
        // Player turn
        var hand = await client.GetHandAsync(runId);
        var selectedCard = ChooseCard(hand);
        await client.PlayCombatActionAsync(combatId, heroId, selectedCard);
        await client.EndTurnAsync(combatId);
        
        // Enemy turn
        await client.ProcessAiTurnsAsync(combatId);
        
        // Check combat status
        var state = await client.GetCombatStateAsync(combatId);
        combatIsActive = state.IsActive;
    }
    
    // Post-combat progression
    var rewardId = await client.StartCardSelectionAsync(runId, "combat_reward");
    await client.PickCardsAsync(runId, rewardId, selectedCards);
    
    // Optional: visit shop
    var shopId = await client.OpenShopAsync(runId, "standard_shop");
    await client.BuyItemAsync(runId, shopId, itemId);
}
```

---

## Auto-Battler Flow

**Genre Overview:** Automated combat with pre-configured gambits/AI. Focus on team building and synergy rather than moment-to-moment control.

### Complete Match Lifecycle

```
1. Team Setup (Pre-combat)
   POST /api/entity/create (for each unit)
   └─ Build team composition
   
   Configure Gambits (Optional):
   POST /api/gambits/configure
   └─ Set AI behavior for each unit

2. Combat Initialization
   POST /api/combat/start
   ├─ heroId: team leader or all units
   ├─ enemyIds: opponent team
   └─ initialEnergy: N/A (auto-battler typically doesn't use energy)
   
   Returns: { combatId, hero, enemies }

3. Automated Combat
   Option A: Full Auto-Play (Recommended)
   ────────────────────────────────
   POST /api/combat/{combatId}/auto-play
   
   ├─ Processes all turns automatically
   ├─ Uses gambits for all decisions
   ├─ Runs until completion (max 100 turns)
   └─ Returns: { status, totalTurns, totalActions, winner }
   
   Option B: Step-by-Step Observation
   ──────────────────────────────────
   while (combat active) {
       POST /api/combat/{combatId}/process-ai-turns
       └─ Processes one round of actions
       
       Optional: Track events
       GET /api/events?combatId={combatId}&afterSequence={lastSeq}
       
       GET /api/combat/{combatId}/state
       └─ Check if combat ended
   }

4. Post-Match Analysis
   GET /api/combat/{combatId}/history
   └─ Review action sequences
   
   GET /api/events?combatId={combatId}
   └─ Analyze combat events
   
   Calculate Synergies:
   - Parse events for combo triggers
   - Calculate damage bonuses from synergies
   - Award post-match rewards based on performance

5. Progression (Between Matches)
   - Upgrade units
   - Unlock new gambits
   - Adjust team composition
   - Configure new AI behaviors
```

### Gambit System

Gambits define AI behavior in auto-battlers:

```json
{
  "gambitId": "healer_priority",
  "priority": 10,
  "conditions": [
    {
      "type": "AllyHealthBelow",
      "threshold": 0.5
    }
  ],
  "action": {
    "actionId": "heal",
    "targetSelection": "LowestHealthAlly"
  }
}
```

**Gambit Evaluation Order:**
1. Highest priority first
2. Check conditions
3. If all conditions met, execute action
4. Continue to next unit

### Example Integration Code

```csharp
// Auto-battler client integration
var team = await BuildTeam();
var enemyTeam = await GetMatchmakingOpponent();

// Start combat
var combatId = await client.StartCombatAsync(team.LeaderId, enemyTeam.UnitIds);

// Run full auto-battle
var result = await client.AutoPlayCombatAsync(combatId);

Console.WriteLine($"Match ended in {result.TotalTurns} turns");
Console.WriteLine($"Winner: {result.Status}");
Console.WriteLine($"Total actions: {result.TotalActions}");

// Analyze for synergies
var events = await client.GetEventsAsync(combatId);
var synergies = CalculateSynergies(events);
AwardRewards(synergies);
```

---

## Tactical RPG Flow

**Genre Overview:** Grid-based positioning, turn order, movement, attack ranges, and status effects. Focus on positioning and tactical decision-making.

### Complete Battle Lifecycle

```
1. Battle Initialization
   POST /api/combat/start
   ├─ heroId: player units (comma-separated or array)
   ├─ enemyIds: enemy units
   └─ initialEnergy: N/A (tactical uses AP/movement points)
   
   Note: Entity definitions should include:
   - positioning: { x, y, facing }
   - movementRange: integer
   - attackRange: integer

2. Turn Order Calculation
   Typically based on:
   - Speed/Initiative stat
   - Status effects (Haste, Slow)
   - Custom turn phase sequence
   
   GET /api/combat/{combatId}/state
   └─ currentTurn indicates active unit

3. Active Unit Turn
   ┌────────────────────────────────────┐
   │ Movement Phase                     │
   │ POST /api/combat/{combatId}/action │
   │ ├─ actionType: "MOVE"              │
   │ ├─ targetPosition: {x, y}          │
   │ └─ Validates movement range        │
   │                                    │
   │ Action Phase                       │
   │ GET /api/combat/{combatId}/available-actions │
   │ └─ Returns actions in range        │
   │                                    │
   │ POST /api/combat/{combatId}/action │
   │ ├─ actionType: "POWER" or "BASIC_ATTACK" │
   │ ├─ targetId: enemy in range        │
   │ └─ Executes attack                 │
   │                                    │
   │ Check Status Effects               │
   │ GET /api/status/{unitId}           │
   │ └─ View active buffs/debuffs       │
   └────────────────────────────────────┘

4. Status Effect Processing
   Tactical games heavily use status effects:
   
   POST /api/status/apply
   ├─ Apply Poison, Burn, Stun, etc.
   └─ Returns instanceId
   
   Automatic processing at:
   - OnTurnStart: Regeneration, Poison tick
   - OnTurnEnd: Duration decrements
   - OnHit: Counter-attack triggers
   - OnDamaged: Thorns damage

5. Turn End
   POST /api/combat/{combatId}/end-turn
   ├─ Processes end-of-turn effects
   ├─ Advances to next unit in turn order
   └─ Returns updated state

6. Enemy Turn (If AI-controlled)
   POST /api/combat/{combatId}/process-ai-turns
   └─ Processes moves and actions for AI units
   
   Note: In tactical games, AI might process one unit at a time
   rather than all enemies in batch

7. Win Condition Check
   After each turn:
   - All enemies defeated → Victory
   - All heroes defeated → Defeat
   - Turn limit reached → Draw/Defeat
   - Objective completed → Victory

8. Post-Battle
   POST /api/combat/{combatId}/end
   └─ Returns combat statistics
   
   Award experience, loot, etc. based on:
   - Turns taken
   - Units survived
   - Bonus objectives
```

### Positioning System

In tactical games, positioning affects:
- **Attack Range:** Can only target enemies within range
- **Cover/Terrain:** Reduces damage or provides bonuses
- **Flanking:** Bonus damage from behind
- **Area Effects:** Multi-target abilities

**Positioning Data Structure:**
```json
{
  "entityId": "knight_1",
  "position": {
    "x": 3,
    "y": 5,
    "facing": "north"
  },
  "movementRange": 3,
  "attackRange": 1
}
```

### Status Effect Chains

Tactical games often feature status effect combos:

```
Wet + Lightning → Shocked (stun)
Oiled + Fire → Burning (DoT)
Frozen + Physical Damage → Shatter (massive damage)
```

**Example Chain:**
```csharp
// Apply Wet status
await client.ApplyStatusAsync(targetId, "wet", stacks: 1);

// Lightning attack triggers combo
await client.ExecuteActionAsync(combatId, casterId, "lightning_bolt", targetId);

// Game engine automatically applies Shocked based on status combination
var statuses = await client.GetStatusEffectsAsync(targetId);
// statuses now includes "shocked"
```

### Example Integration Code

```csharp
// Tactical RPG client integration
var combatId = await client.StartCombatAsync(heroUnits, enemyUnits);

while (combatActive)
{
    var state = await client.GetCombatStateAsync(combatId);
    var activeUnit = state.GetActiveUnit();
    
    if (activeUnit.IsPlayerControlled)
    {
        // Movement
        var moveTarget = SelectMovePosition(activeUnit, state.Grid);
        await client.ExecuteActionAsync(combatId, activeUnit.Id, "MOVE", moveTarget);
        
        // Action
        var availableActions = await client.GetAvailableActionsAsync(combatId, activeUnit.Id);
        var action = ChooseTacticalAction(availableActions, state);
        await client.ExecuteActionAsync(combatId, activeUnit.Id, action.Id, action.TargetId);
        
        // End turn
        await client.EndTurnAsync(combatId);
    }
    else
    {
        // AI turn
        await client.ProcessAiTurnsAsync(combatId);
    }
    
    // Check win condition
    combatActive = state.IsActive;
}
```

---

## Puzzle RPG Flow

**Genre Overview:** Match-3 or puzzle mechanics generate resources used for combat actions. Focus on puzzle-solving and resource management.

### Complete Match Lifecycle

```
1. Match Initialization
   POST /api/combat/start
   ├─ heroId: player character
   ├─ enemyIds: enemies
   └─ initialEnergy: 0 (energy from puzzle matches)

2. Puzzle Phase
   ┌─────────────────────────────────────┐
   │ Player makes puzzle moves           │
   │ (typically in game client, not API) │
   │                                     │
   │ Calculate match results:            │
   │ POST /api/formula/evaluate          │
   │ ├─ formulaName: "match_damage"     │
   │ └─ context: {                       │
   │      orbs_matched: 5,               │
   │      combo_count: 3,                │
   │      attack_stat: 120               │
   │    }                                │
   │                                     │
   │ Returns: { result: 450 }            │
   │ (calculated damage from puzzle)     │
   └─────────────────────────────────────┘

3. Resource Generation
   Each puzzle match generates:
   - Mana (for casting abilities)
   - Energy (for basic attacks)
   - Damage (applied immediately)
   
   POST /api/game-resources/add
   ├─ resourceId: "mana"
   └─ amount: 5 (from matching 5 blue orbs)

4. Action Affordability Check
   Before using abilities:
   
   GET /api/combat/{combatId}/actions/{actionId}/cost-options
   └─ Returns available payment methods
   
   POST /api/combat/{combatId}/actions/{actionId}/can-afford
   ├─ actorId: "hero"
   ├─ runId: {runId} (for checking resources)
   └─ Returns: { canAfford: true/false }

5. Execute Combat Action
   If player has enough resources:
   
   POST /api/combat/{combatId}/action
   ├─ actionType: "POWER"
   ├─ actionId: "fireball"
   ├─ targetId: enemy
   └─ costChoices: {
        "mana": 10,
        "energy": 3
      }
   
   Returns: { success, damageDealt }

6. Enemy Turn
   POST /api/combat/{combatId}/process-ai-turns
   └─ Enemies attack based on gambits

7. Turn End & Cleanup
   POST /api/combat/{combatId}/end-turn
   ├─ Resets puzzle board (client-side)
   ├─ Processes status effects
   └─ Advances turn

8. Loop
   Goto step 2 until combat ends

9. Post-Match
   Award resources based on:
   - Combo count
   - Turn efficiency
   - Perfect clears
```

### Formula System

Puzzle RPGs rely heavily on formula evaluation for dynamic calculations:

**Common Formulas:**
- **Match Damage:** `base_damage * orbs_matched * combo_multiplier * attack_stat / 100`
- **Mana Generation:** `orbs_matched + (combo_count * 2)`
- **Combo Multiplier:** `1 + (combo_count * 0.25)`

**Example Formula Configuration:**
```json
{
  "formulaId": "match_damage",
  "expression": "base * orbs * (1 + combos * 0.5) * atk / 100",
  "variables": {
    "base": "Base damage per orb",
    "orbs": "Number of orbs matched",
    "combos": "Combo count",
    "atk": "Entity attack stat"
  }
}
```

### Resource Management

Puzzle RPGs typically have multiple resource types:

```
Mana Pool:
- Generated from matching colored orbs
- Used for casting spells
- Can have color-specific mana (red mana, blue mana, etc.)

Energy:
- Generated from cascades/combos
- Used for basic attacks and skills
- Regenerates partially each turn

Health/Hearts:
- Matching heart orbs heals
- Can overheal for shields
```

**Resource API Usage:**
```csharp
// Check available resources
var resources = await client.GetGameResourcesAsync(runId);

// Validate action is affordable
var canAfford = await client.CanAffordActionAsync(combatId, "fireball", heroId, runId);

if (canAfford.CanAfford)
{
    // Execute action with resource payment
    await client.ExecuteActionAsync(combatId, heroId, "fireball", enemyId, 
        costChoices: new { mana = 10, energy = 3 });
}
```

### Example Integration Code

```csharp
// Puzzle RPG client integration
var combatId = await client.StartCombatAsync(heroId, enemyIds);
var runId = await client.GetRunIdForCombat(combatId);

while (combatActive)
{
    // Puzzle phase (client-side puzzle UI)
    var puzzleResult = await PlayPuzzle();
    
    // Calculate damage from puzzle
    var damage = await client.EvaluateFormulaAsync("match_damage", new {
        base_damage = 10,
        orbs_matched = puzzleResult.OrbsMatched,
        combo_count = puzzleResult.Combos,
        attack_stat = heroStats.Attack
    });
    
    // Generate mana
    var manaGained = puzzleResult.OrbsMatched + (puzzleResult.Combos * 2);
    await client.AddResourceAsync(runId, "mana", manaGained);
    
    // Apply puzzle damage
    await client.ExecuteActionAsync(combatId, heroId, "puzzle_damage", 
        enemyId, damageOverride: damage.Result);
    
    // Use abilities if resources available
    var canCast = await client.CanAffordActionAsync(combatId, "ultimate", heroId, runId);
    if (canCast.CanAfford && ShouldUseUltimate())
    {
        await client.ExecuteActionAsync(combatId, heroId, "ultimate", 
            enemyId, costChoices: new { mana = 15 });
    }
    
    // Enemy turn
    await client.ProcessAiTurnsAsync(combatId);
    await client.EndTurnAsync(combatId);
    
    var state = await client.GetCombatStateAsync(combatId);
    combatActive = state.IsActive;
}
```

---

## Common Patterns

### Event Streaming for Real-Time Updates

All game flows can use event streaming for live updates:

```csharp
// Server-Sent Events stream
var eventSource = client.ConnectToEventStream(combatId);

eventSource.OnMessage += (sender, e) => {
    var gameEvent = JsonSerializer.Deserialize<GameEvent>(e.Data);
    
    switch (gameEvent.EventType)
    {
        case "DamageDealt":
            UpdateHealthBars(gameEvent);
            break;
        case "StatusEffectApplied":
            ShowStatusIcon(gameEvent);
            break;
        case "ActionExecuted":
            PlayAnimation(gameEvent);
            break;
    }
};
```

### Undo/Replay System

Roguelike runs support snapshots for undo:

```csharp
// Before risky decision
var snapshot = await client.GetCurrentSnapshot(runId);

// Try decision
var result = await client.BuyShopItem(runId, shopId, itemId);

// If regret
if (UserClicksUndo())
{
    await client.UndoToSnapshot(runId, snapshot.Sequence);
}
```

### Deck State Synchronization

Keep deck state synchronized across run:

```csharp
// After any deck modification (card selection, shop, discard)
var deckState = await client.GetDeckStateAsync(runId);

// Update UI
UpdateHandDisplay(deckState.Hand);
UpdateDeckCount(deckState.DrawPile.Count);
UpdateDiscardCount(deckState.DiscardPile.Count);
```

---

## Error Handling

### Common Error Scenarios

**404 Not Found:**
- Combat/Run ID doesn't exist
- Entity not found
- Action not defined

```csharp
try {
    var state = await client.GetCombatStateAsync(combatId);
} catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound) {
    Console.WriteLine("Combat session expired or invalid");
    // Return to main menu or restart
}
```

**400 Bad Request:**
- Invalid action (not enough energy)
- Invalid target (out of range)
- Invalid state (combat already ended)

```csharp
try {
    await client.ExecuteActionAsync(combatId, heroId, "fireball", targetId);
} catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.BadRequest) {
    var error = await ParseError(ex);
    ShowUserMessage(error.Message); // "Not enough mana"
}
```

**Timeout:**
- Long-running auto-play
- Server overload

```csharp
var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
try {
    var result = await client.AutoPlayCombatAsync(combatId, cts.Token);
} catch (TaskCanceledException) {
    Console.WriteLine("Combat took too long, possible infinite loop");
    // Manually end combat or report issue
}
```

### Retry Strategy

For transient failures:

```csharp
async Task<T> RetryAsync<T>(Func<Task<T>> action, int maxRetries = 3)
{
    for (int i = 0; i < maxRetries; i++)
    {
        try
        {
            return await action();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            if (i == maxRetries - 1) throw;
            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, i))); // Exponential backoff
        }
    }
    throw new InvalidOperationException("Should not reach here");
}
```

---

## Performance Considerations

### Batch Event Queries

Instead of polling events continuously:

```csharp
// BAD: Polling every 100ms
while (combatActive) {
    var events = await client.GetEventsAsync(combatId);
    await Task.Delay(100);
}

// GOOD: Use afterSequence to only get new events
int lastSequence = 0;
while (combatActive) {
    var events = await client.GetEventsAsync(combatId, afterSequence: lastSequence);
    if (events.Any()) {
        lastSequence = events.Max(e => e.Sequence);
        ProcessEvents(events);
    }
    await Task.Delay(500); // Less frequent polling
}

// BEST: Use SSE stream
var stream = client.ConnectToEventStream(combatId);
// Real-time updates, no polling needed
```

### Minimize State Queries

Cache combat state and only refresh when needed:

```csharp
// BAD: Query state after every action
await client.ExecuteActionAsync(...);
var state1 = await client.GetCombatStateAsync(combatId); // Unnecessary

await client.ExecuteActionAsync(...);
var state2 = await client.GetCombatStateAsync(combatId); // Unnecessary

// GOOD: Action responses include updated state
var result1 = await client.ExecuteActionAsync(...);
UpdateUI(result1.Combat); // State included in response

var result2 = await client.ExecuteActionAsync(...);
UpdateUI(result2.Combat); // State included in response
```

---

## Summary

Each game flow uses the same underlying API but emphasizes different endpoints and patterns:

| Genre | Key Endpoints | Focus |
|---|---|---|
| Roguelike | run/start, card-selection, shop | Progression, deck building |
| Auto-Battler | auto-play, gambits | Team synergy, automation |
| Tactical RPG | status effects, positioning | Positioning, combos |
| Puzzle RPG | formula/evaluate, can-afford | Resource management, formulas |

All flows share:
- Combat lifecycle (start → actions → end)
- Event system for tracking
- Status effect management
- Extensible formula system

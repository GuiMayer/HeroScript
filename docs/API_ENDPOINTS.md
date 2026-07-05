# HeroScript API Endpoints

Complete reference for all HTTP endpoints in the HeroScript API. This document focuses on the **28 critical endpoints** used by integration tests, with additional endpoints documented for completeness.

**Legend:**
- ✓ = Used in integration tests
- 🔧 = Admin/diagnostic only
- 🆕 = Recently added

---

## Run Management

Endpoints for managing roguelike runs, including deck state, hand management, and undo/snapshot functionality.

### POST /api/run/start ✓

**Description:** Starts a new roguelike run with specified configuration.

**Request Body:**
```json
{
  "configName": "default",
  "runDefinitionId": "default_run",
  "playerEntityId": "player"
}
```

**Response:**
```json
{
  "runId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "configName": "default",
  "playerEntityId": "player",
  "gold": 0,
  "powerPoints": 0,
  "deck": {
    "drawPile": ["card_1", "card_2"],
    "hand": [],
    "discardPile": [],
    "exhaustPile": []
  }
}
```

**Status Codes:** 200 OK, 400 Bad Request

**Example:**
```bash
curl -X POST http://localhost:5000/api/run/start \
  -H "Content-Type: application/json" \
  -d '{"configName":"default","runDefinitionId":"default_run","playerEntityId":"player"}'
```

---

### GET /api/run/{runId}/state ✓

**Description:** Retrieves the current state of a run.

**Response:**
```json
{
  "runId": "guid",
  "configName": "default",
  "playerEntityId": "player",
  "gold": 150,
  "powerPoints": 3,
  "deck": { ... }
}
```

**Status Codes:** 200 OK, 404 Not Found

---

### POST /api/run/{runId}/draw ✓

**Description:** Draws cards from the deck into hand.

**Request Body:**
```json
{
  "count": 5
}
```

**Response:**
```json
{
  "drawnCards": ["card_1", "card_2", "card_3"],
  "hand": ["card_1", "card_2", "card_3"],
  "drawPileCount": 12
}
```

**Status Codes:** 200 OK, 400 Bad Request

---

### POST /api/run/{runId}/discard ✓

**Description:** Discards cards from hand.

**Request Body:**
```json
{
  "cardIds": ["card_1", "card_2"]
}
```

**Response:**
```json
{
  "discardedCards": ["card_1", "card_2"],
  "hand": ["card_3"],
  "discardPileCount": 5
}
```

**Status Codes:** 200 OK, 400 Bad Request

---

### GET /api/run/{runId}/hand ✓

**Description:** Retrieves current hand of cards.

**Response:**
```json
{
  "hand": ["card_1", "card_2", "card_3"],
  "handSize": 3
}
```

**Status Codes:** 200 OK, 404 Not Found

---

### POST /api/run/{runId}/shuffle

**Description:** Shuffles discard pile back into draw pile.

**Response:**
```json
{
  "drawPileCount": 15,
  "discardPileCount": 0
}
```

**Status Codes:** 200 OK

---

### GET /api/run/{runId}/deck

**Description:** Retrieves complete deck state.

**Response:**
```json
{
  "drawPile": ["card_1", "card_2"],
  "hand": ["card_3"],
  "discardPile": ["card_4"],
  "exhaustPile": [],
  "counts": {
    "drawPile": 2,
    "hand": 1,
    "discardPile": 1,
    "exhaustPile": 0
  }
}
```

**Status Codes:** 200 OK, 404 Not Found

---

### GET /api/run/{runId}/snapshots

**Description:** Lists all snapshots for undo functionality.

**Response:**
```json
{
  "snapshots": [
    {
      "sequence": 1,
      "timestamp": "2026-07-05T18:00:00Z",
      "description": "After card selection"
    }
  ]
}
```

**Status Codes:** 200 OK

---

### GET /api/run/{runId}/snapshots/{sequence}

**Description:** Retrieves specific snapshot by sequence number.

**Response:**
```json
{
  "sequence": 1,
  "runState": { ... },
  "timestamp": "2026-07-05T18:00:00Z"
}
```

**Status Codes:** 200 OK, 404 Not Found

---

### POST /api/run/{runId}/undo

**Description:** Reverts run to a previous snapshot.

**Request Body:**
```json
{
  "targetSequence": 1
}
```

**Response:**
```json
{
  "runId": "guid",
  "currentSequence": 1,
  "restoredState": { ... }
}
```

**Status Codes:** 200 OK, 400 Bad Request

---

## Combat Management

Endpoints for turn-based combat, action execution, and AI processing.

### POST /api/combat/start ✓

**Description:** Starts a new combat encounter.

**Request Body:**
```json
{
  "heroId": "hero",
  "enemyIds": ["enemy_1", "enemy_2"],
  "initialEnergy": 3
}
```

**Response:**
```json
{
  "combatId": "guid",
  "status": "Active",
  "currentTurn": 1,
  "hero": {
    "entityId": "hero",
    "name": "Hero",
    "currentHp": 100,
    "maxHp": 100,
    "isAlive": true
  },
  "enemies": [
    {
      "entityId": "enemy_1",
      "name": "Goblin",
      "currentHp": 30,
      "maxHp": 30,
      "isAlive": true
    }
  ],
  "energy": {
    "current": 3,
    "maximum": 3
  }
}
```

**Status Codes:** 200 OK, 400 Bad Request

---

### GET /api/combat/{combatId}/state ✓

**Description:** Retrieves current combat state.

**Response:** Same structure as POST /api/combat/start

**Status Codes:** 200 OK, 404 Not Found

---

### POST /api/combat/{combatId}/action ✓

**Description:** Executes a combat action.

**Request Body:**
```json
{
  "actorId": "hero",
  "actionType": "POWER",
  "actionId": "fireball",
  "targetId": "enemy_1",
  "costChoices": {
    "health": 5
  }
}
```

**Response:**
```json
{
  "success": true,
  "combat": { ... },
  "damageDealt": 25,
  "energyCost": 2,
  "message": "Fireball dealt 25 damage to Goblin"
}
```

**Status Codes:** 200 OK, 400 Bad Request

---

### POST /api/combat/{combatId}/end-turn ✓

**Description:** Ends the current turn, processes end-of-turn effects, and resets energy.

**Response:**
```json
{
  "combatId": "guid",
  "currentTurn": 2,
  "energy": {
    "current": 3,
    "maximum": 3
  }
}
```

**Status Codes:** 200 OK

---

### POST /api/combat/{combatId}/process-ai-turns ✓

**Description:** Processes AI decisions for all active enemies using gambits.

**Response:**
```json
{
  "actionsExecuted": 3,
  "combatState": { ... }
}
```

**Status Codes:** 200 OK

---

### POST /api/combat/{combatId}/auto-play 🆕 ✓

**Description:** Simulates complete combat automatically until conclusion. Processes hero and enemy turns via gambits/AI until combat ends or max turn limit reached.

**Response:**
```json
{
  "combatId": "guid",
  "status": "Victory",
  "totalTurns": 12,
  "totalActions": 45,
  "turnsProcessed": 12,
  "actionsExecuted": 45,
  "reachedMaxTurns": false,
  "damageDealt": 150,
  "damageTaken": 35,
  "duration": 2.5,
  "state": { ... }
}
```

**Status Codes:** 200 OK, 404 Not Found

**Safety Features:**
- Max 100 turns to prevent infinite loops
- Stalemate detection after 50 turns with no actions
- Automatic combat end when complete

---

### POST /api/combat/{combatId}/end

**Description:** Manually ends combat and returns final statistics.

**Response:**
```json
{
  "combatId": "guid",
  "status": "Victory",
  "totalTurns": 8,
  "totalActions": 32,
  "damageDealt": 120,
  "damageTaken": 25,
  "duration": 45.3
}
```

**Status Codes:** 200 OK, 404 Not Found

---

### GET /api/combat/{combatId}/history

**Description:** Retrieves complete action history for combat.

**Response:**
```json
{
  "actions": [
    {
      "turn": 1,
      "actorId": "hero",
      "actionType": "POWER",
      "targetId": "enemy_1",
      "damage": 15
    }
  ]
}
```

**Status Codes:** 200 OK

---

### GET /api/combat/{combatId}/available-actions

**Description:** Lists all actions available to an actor.

**Query Parameters:**
- `actorId` (required): Entity performing actions
- `runId` (optional): Run context for card actions

**Response:**
```json
{
  "actions": [
    {
      "actionId": "basic_attack",
      "name": "Attack",
      "energyCost": 1,
      "available": true
    }
  ]
}
```

**Status Codes:** 200 OK

---

### GET /api/combat/{combatId}/actions/{actionId}/cost-options

**Description:** Retrieves available cost payment options for an action.

**Response:**
```json
{
  "options": [
    {
      "optionId": "health",
      "resourceId": "health",
      "amount": 5,
      "description": "Pay 5 HP"
    }
  ]
}
```

**Status Codes:** 200 OK

---

### POST /api/combat/{combatId}/actions/{actionId}/can-afford

**Description:** Checks if actor can pay for action.

**Query Parameters:**
- `actorId` (required)
- `runId` (optional)

**Response:**
```json
{
  "canAfford": true,
  "missingResources": []
}
```

**Status Codes:** 200 OK

---

## Card Selection (Rewards)

Endpoints for post-combat card selection/rewards.

### POST /api/run/{runId}/card-selection/start ✓

**Description:** Starts card selection process (reward screen).

**Request Body:**
```json
{
  "selectionDefinitionId": "basic_reward",
  "context": "combat_victory"
}
```

**Response:**
```json
{
  "selectionInstanceId": "guid",
  "options": [
    {
      "cardId": "fireball",
      "name": "Fireball",
      "rarity": "common"
    }
  ],
  "rerollsRemaining": 1,
  "canDecompose": true
}
```

**Status Codes:** 200 OK

---

### POST /api/run/{runId}/card-selection/{selectionInstanceId}/pick ✓

**Description:** Picks selected cards and adds them to deck.

**Request Body:**
```json
{
  "cardIds": ["fireball"]
}
```

**Response:**
```json
{
  "addedCards": ["fireball"],
  "deck": { ... }
}
```

**Status Codes:** 200 OK

---

### POST /api/run/{runId}/card-selection/{selectionInstanceId}/reroll ✓

**Description:** Rerolls card options (if rerolls available).

**Response:**
```json
{
  "options": [ ... ],
  "rerollsRemaining": 0
}
```

**Status Codes:** 200 OK, 400 Bad Request

---

### POST /api/run/{runId}/card-selection/{selectionInstanceId}/decompose/{cardId} ✓

**Description:** Decomposes a card for resources instead of picking it.

**Response:**
```json
{
  "decomposedCard": "fireball",
  "resourcesGained": {
    "gold": 10,
    "powerPoints": 1
  }
}
```

**Status Codes:** 200 OK

---

## Shop

Endpoints for in-run shop interactions.

### POST /api/run/{runId}/shop/open ✓

**Description:** Opens shop with available items.

**Request Body:**
```json
{
  "shopDefinitionId": "basic_shop"
}
```

**Response:**
```json
{
  "shopInstanceId": "guid",
  "items": [
    {
      "itemId": "health_potion",
      "name": "Health Potion",
      "cost": 50,
      "available": true
    }
  ],
  "playerGold": 150,
  "rerollCost": 25
}
```

**Status Codes:** 200 OK

---

### POST /api/run/{runId}/shop/{shopInstanceId}/buy/{itemId} ✓

**Description:** Purchases item from shop.

**Response:**
```json
{
  "purchasedItem": "health_potion",
  "goldSpent": 50,
  "remainingGold": 100,
  "addedToInventory": true
}
```

**Status Codes:** 200 OK, 400 Bad Request

---

### POST /api/run/{runId}/shop/{shopInstanceId}/reroll ✓

**Description:** Rerolls shop inventory for cost.

**Response:**
```json
{
  "items": [ ... ],
  "goldSpent": 25,
  "remainingGold": 125
}
```

**Status Codes:** 200 OK, 400 Bad Request

---

## Preparation

Endpoints for pre-combat preparation phase.

### POST /api/run/{runId}/preparation/start ✓

**Description:** Starts preparation phase with available options.

**Request Body:**
```json
{
  "preparationDefinitionId": "basic_preparation"
}
```

**Response:**
```json
{
  "preparationInstanceId": "guid",
  "options": [
    {
      "optionId": "buff_attack",
      "name": "Sharpen Weapons",
      "description": "+2 Attack for next combat"
    }
  ]
}
```

**Status Codes:** 200 OK

---

### POST /api/run/{runId}/preparation/{preparationInstanceId}/apply/{optionId} ✓

**Description:** Applies selected preparation option.

**Response:**
```json
{
  "appliedOption": "buff_attack",
  "effects": [
    {
      "type": "stat_boost",
      "target": "attack",
      "value": 2
    }
  ]
}
```

**Status Codes:** 200 OK

---

## Status Effects

Endpoints for managing status effects on entities.

### POST /api/status/apply ✓

**Description:** Applies status effect to target.

**Request Body:**
```json
{
  "targetId": "hero",
  "statusEffectId": "regeneration",
  "stacks": 3,
  "duration": 5
}
```

**Response:**
```json
{
  "instanceId": "guid",
  "statusEffectId": "regeneration",
  "stacks": 3,
  "duration": 5
}
```

**Status Codes:** 200 OK

---

### GET /api/status/{targetId} 🆕 ✓

**Description:** Retrieves all active status effects on entity (short alias).

**Response:**
```json
[
  {
    "instanceId": "guid",
    "statusEffectId": "regeneration",
    "stacks": 3,
    "duration": 4,
    "timing": "OnTurnStart"
  }
]
```

**Status Codes:** 200 OK

---

### GET /api/status/{targetId}/active ✓

**Description:** Retrieves all active status effects (explicit route).

**Response:** Same as GET /api/status/{targetId}

**Status Codes:** 200 OK

---

### POST /api/status/add-stacks

**Description:** Adds stacks to existing status effect.

**Request Body:**
```json
{
  "targetId": "hero",
  "statusEffectId": "poison",
  "stacks": 2
}
```

**Status Codes:** 200 OK

---

### POST /api/status/remove-stacks

**Description:** Removes stacks from status effect.

**Status Codes:** 200 OK

---

### POST /api/status/{targetId}/tick

**Description:** Decrements duration of all status effects on entity.

**Status Codes:** 200 OK

---

### DELETE /api/status/{targetId}/status/{statusId}

**Description:** Removes all instances of a specific status effect.

**Status Codes:** 200 OK, 404 Not Found

---

## Events

Endpoints for game event tracking and streaming.

### GET /api/events ✓

**Description:** Lists events with optional filters.

**Query Parameters:**
- `category`: Filter by event category
- `severity`: Filter by severity level
- `limit`: Max events to return (default: 100)
- `afterSequence`: Events after sequence number
- `combatId`: Filter by combat
- `runId`: Filter by run
- `eventType`: Filter by event type

**Response:**
```json
{
  "events": [
    {
      "eventId": "guid",
      "eventType": "DamageDealt",
      "category": "Combat",
      "severity": "Info",
      "sequence": 42,
      "timestamp": "2026-07-05T18:30:00Z",
      "data": { ... }
    }
  ],
  "totalCount": 1,
  "hasMore": false
}
```

**Status Codes:** 200 OK

---

### GET /api/events/stream

**Description:** Server-sent events stream for real-time event monitoring.

**Query Parameters:** Same as GET /api/events, plus:
- `delayMs`: Polling delay in milliseconds

**Response:** SSE stream

**Status Codes:** 200 OK

---

### GET /api/combat/{combatId}/events

**Description:** Lists events for specific combat.

**Status Codes:** 200 OK

---

### DELETE /api/events 🔧

**Description:** Clears event history (dev mode only).

**Status Codes:** 200 OK, 403 Forbidden

---

## Formula Evaluation

Endpoints for mathematical formula evaluation.

### POST /api/formula/evaluate ✓

**Description:** Evaluates mathematical formula with provided context.

**Request Body:**
```json
{
  "formulaName": "damage_calculation",
  "context": {
    "base_damage": 10,
    "attack_stat": 15,
    "multiplier": 1.5
  }
}
```

**Response:**
```json
{
  "result": 37.5,
  "formula": "base_damage + (attack_stat * multiplier)",
  "steps": [ ... ]
}
```

**Status Codes:** 200 OK, 400 Bad Request

---

### GET /api/formula

**Description:** Lists all available formulas.

**Status Codes:** 200 OK

---

### GET /api/formula/{name}

**Description:** Retrieves formula definition.

**Status Codes:** 200 OK, 404 Not Found

---

### POST /api/formula/reload 🔧

**Description:** Reloads formula cache from config files.

**Status Codes:** 200 OK

---

## Entity Management

Endpoints for entity definitions and creation.

### POST /api/entity/create ✓

**Description:** Creates new entity instance from definition.

**Request Body:**
```json
{
  "definitionId": "hero",
  "customizations": {
    "name": "Custom Hero"
  }
}
```

**Response:**
```json
{
  "entityId": "guid",
  "definitionId": "hero",
  "name": "Custom Hero",
  "resources": { ... }
}
```

**Status Codes:** 200 OK, 400 Bad Request

---

### GET /api/entity/definitions

**Description:** Lists all entity definitions.

**Status Codes:** 200 OK

---

### GET /api/entity/definitions/{definitionId}

**Description:** Retrieves specific entity definition.

**Status Codes:** 200 OK, 404 Not Found

---

### POST /api/entity/definitions/validate

**Description:** Validates entity definition structure.

**Status Codes:** 200 OK, 400 Bad Request

---

## Additional Endpoints

**Action Management:** GET /api/action, GET /api/action/{actionId}, POST /api/action/validate, POST /api/action/reload

**Combat Activation:** GET /api/combat/{combatId}/activation/state, POST /api/combat/{combatId}/activation/start, POST /api/combat/{combatId}/activation/advance

**Configuration:** GET /api/config, GET /api/config/current, GET /api/config/{name}, POST /api/config/{name}/load

**Damage Calculation:** POST /api/damage/calculate, GET /api/damage/pipeline/config

**Diagnostics:** GET /api/diagnostics/cache/stats, POST /api/diagnostics/cache/invalidate/{cacheName}

**Effects:** POST /api/effect/apply, GET /api/effect/types

**Gambits:** GET /api/gambits, POST /api/gambits/decide, POST /api/gambits/reload

**Game Resources:** GET /api/game-resources, POST /api/game-resources/create-pool, POST /api/game-resources/validate-cost

**Health Check:** GET /api/health

**Modifiers:** GET /api/modifiers, POST /api/modifiers/apply, GET /api/modifiers/active/{ownerId}

**Operations:** GET /api/operation, GET /api/operation/categories

---

## Quick Reference

**Critical Endpoints for Integration Tests (28 total):**

| Category | Endpoint | Method |
|---|---|---|
| Run | /api/run/start | POST |
| Run | /api/run/{runId}/state | GET |
| Run | /api/run/{runId}/draw | POST |
| Run | /api/run/{runId}/discard | POST |
| Run | /api/run/{runId}/hand | GET |
| Combat | /api/combat/start | POST |
| Combat | /api/combat/{combatId}/state | GET |
| Combat | /api/combat/{combatId}/action | POST |
| Combat | /api/combat/{combatId}/end-turn | POST |
| Combat | /api/combat/{combatId}/process-ai-turns | POST |
| Combat | /api/combat/{combatId}/auto-play | POST |
| Card Selection | /api/run/{runId}/card-selection/start | POST |
| Card Selection | /api/run/{runId}/card-selection/{id}/pick | POST |
| Card Selection | /api/run/{runId}/card-selection/{id}/reroll | POST |
| Card Selection | /api/run/{runId}/card-selection/{id}/decompose/{cardId} | POST |
| Shop | /api/run/{runId}/shop/open | POST |
| Shop | /api/run/{runId}/shop/{id}/buy/{itemId} | POST |
| Shop | /api/run/{runId}/shop/{id}/reroll | POST |
| Preparation | /api/run/{runId}/preparation/start | POST |
| Preparation | /api/run/{runId}/preparation/{id}/apply/{optionId} | POST |
| Status | /api/status/apply | POST |
| Status | /api/status/{targetId} | GET |
| Events | /api/events | GET |
| Formula | /api/formula/evaluate | POST |
| Entity | /api/entity/create | POST |

**Total API Surface:** 113 endpoints across 22 controllers

# HeroScript API - Endpoints Documentation

Complete reference for all HeroScript API endpoints. This API provides access to the game engine's math formulas, combat system, actions, and resources management.

## Base URL

```
http://localhost:5000
```

## Interactive Documentation

Swagger UI is available at the root URL when running in development mode:
```
http://localhost:5000/
```

---

## Table of Contents

1. [Action Management API](#action-management-api)
2. [Resource Management API](#resource-management-api)
3. [Combat System API](#combat-system-api)
4. [Configuration API](#configuration-api)
5. [Math Engine API](#math-engine-api)

---

## Action Management API

Manage action definitions for the combat system.

### Get All Actions

List all available action definitions.

**Endpoint:** `GET /api/action`

**Response:**
```json
[
  {
    "actionId": "basic_attack",
    "displayName": "Basic Attack",
    "actionType": "ATTACK",
    "baseDamage": 10,
    "cooldown": 0,
    "tags": ["physical", "melee"]
  }
]
```

**Example (curl):**
```bash
curl http://localhost:5000/api/action
```

**Example (C#):**
```csharp
using var client = new HttpClient();
var response = await client.GetAsync("http://localhost:5000/api/action");
var actions = await response.Content.ReadFromJsonAsync<List<ActionSummaryDto>>();
```

**Example (JavaScript):**
```javascript
const response = await fetch('http://localhost:5000/api/action');
const actions = await response.json();
```

**Example (Python):**
```python
import requests
response = requests.get('http://localhost:5000/api/action')
actions = response.json()
```

---

### Get Action by ID

Get detailed information about a specific action.

**Endpoint:** `GET /api/action/{actionId}`

**Parameters:**
- `actionId` (path) - The unique identifier of the action

**Response:**
```json
{
  "actionId": "fireball",
  "displayName": "Fireball",
  "description": "Launch a ball of fire at the enemy",
  "actionType": "POWER",
  "baseDamage": 50,
  "cooldown": 3,
  "resourceCosts": [
    {
      "resourceId": "mana",
      "amount": 25
    }
  ],
  "tags": ["magic", "fire", "ranged"]
}
```

**Example (curl):**
```bash
curl http://localhost:5000/api/action/fireball
```

---

### Get Actions by Type

Filter actions by their type.

**Endpoint:** `GET /api/action/by-type/{type}`

**Parameters:**
- `type` (path) - Action type: `ATTACK`, `POWER`, `DEFENSE`, `UTILITY`, `MOVEMENT`

**Example (curl):**
```bash
curl http://localhost:5000/api/action/by-type/ATTACK
```

---

### Get Actions by Tag

Filter actions by tag.

**Endpoint:** `GET /api/action/by-tag/{tag}`

**Parameters:**
- `tag` (path) - Tag to filter by (e.g., "magic", "physical", "fire")

**Example (curl):**
```bash
curl http://localhost:5000/api/action/by-tag/magic
```

---

### Validate Action Definition

Validate an action definition before using it.

**Endpoint:** `POST /api/action/validate`

**Request Body:**
```json
{
  "definition": {
    "actionId": "custom_spell",
    "displayName": "Custom Spell",
    "actionType": "POWER",
    "baseDamage": 30,
    "cooldown": 2,
    "tags": ["magic"]
  }
}
```

**Response:**
```json
{
  "isValid": true,
  "errors": []
}
```

**Example (curl):**
```bash
curl -X POST http://localhost:5000/api/action/validate \
  -H "Content-Type: application/json" \
  -d '{"definition":{"actionId":"test","displayName":"Test","actionType":"ATTACK","baseDamage":10,"cooldown":0,"tags":[]}}'
```

---

### Reload Action Definitions

Reload action definitions from configuration files (requires `AllowConfigReload: true` in appsettings).

**Endpoint:** `POST /api/action/reload`

**Response:**
```json
{
  "success": true,
  "message": "Action definitions reloaded successfully",
  "count": 15
}
```

---

## Resource Management API

Manage gameplay resource definitions (health, mana, energy, etc.).

### Get All Resources

List all resource definitions.

**Endpoint:** `GET /api/game-resources`

**Response:**
```json
[
  {
    "resourceId": "health",
    "displayName": "Health Points",
    "shortName": "HP",
    "category": "VITAL",
    "defaultMax": 100,
    "tags": ["vital"]
  }
]
```

**Example (curl):**
```bash
curl http://localhost:5000/api/game-resources
```

---

### Get Resource by ID

Get detailed information about a specific resource.

**Endpoint:** `GET /api/game-resources/{resourceId}`

**Parameters:**
- `resourceId` (path) - The unique identifier of the resource

**Response:**
```json
{
  "resourceId": "mana",
  "displayName": "Mana",
  "shortName": "MP",
  "description": "Magical energy used to cast spells",
  "category": "TACTICAL",
  "defaultCurrent": 100,
  "defaultMax": 100,
  "defaultMin": 0,
  "regenerationRate": 5,
  "tags": ["magic", "tactical"]
}
```

**Example (curl):**
```bash
curl http://localhost:5000/api/game-resources/mana
```

---

### Get Resources by Category

Filter resources by category.

**Endpoint:** `GET /api/game-resources/by-category/{category}`

**Parameters:**
- `category` (path) - Resource category: `VITAL`, `TACTICAL`, `STRATEGIC`, `CURRENCY`, `CONSUMABLE`

**Example (curl):**
```bash
curl http://localhost:5000/api/game-resources/by-category/VITAL
```

---

### Get Resources by Tag

Filter resources by tag.

**Endpoint:** `GET /api/game-resources/by-tag/{tag}`

**Parameters:**
- `tag` (path) - Tag to filter by

**Example (curl):**
```bash
curl http://localhost:5000/api/game-resources/by-tag/magic
```

---

### Create Resource Pool

Create a resource pool instance with initial values.

**Endpoint:** `POST /api/game-resources/create-pool`

**Request Body:**
```json
{
  "resourceId": "health",
  "initialCurrent": 75
}
```

**Response:**
```json
{
  "resourceId": "health",
  "current": 75,
  "maximum": 100,
  "minimum": 0
}
```

**Example (curl):**
```bash
curl -X POST http://localhost:5000/api/game-resources/create-pool \
  -H "Content-Type: application/json" \
  -d '{"resourceId":"health","initialCurrent":75}'
```

---

### Validate Resource Cost

Check if a resource pool can afford a specific cost.

**Endpoint:** `POST /api/game-resources/validate-cost`

**Request Body:**
```json
{
  "resourceId": "mana",
  "cost": 25,
  "currentAmount": 100
}
```

**Response:**
```json
{
  "canAfford": true,
  "remainingAmount": 75,
  "error": null
}
```

**Example (curl):**
```bash
curl -X POST http://localhost:5000/api/game-resources/validate-cost \
  -H "Content-Type: application/json" \
  -d '{"resourceId":"mana","cost":25,"currentAmount":100}'
```

---

### Validate Resource Definition

Validate a resource definition before using it.

**Endpoint:** `POST /api/game-resources/validate`

**Request Body:**
```json
{
  "definition": {
    "resourceId": "custom_resource",
    "displayName": "Custom Resource",
    "shortName": "CR",
    "category": "TACTICAL",
    "defaultMax": 50,
    "tags": ["custom"]
  }
}
```

**Response:**
```json
{
  "isValid": true,
  "errors": []
}
```

---

### Reload Resource Definitions

Reload resource definitions from configuration files (requires `AllowConfigReload: true`).

**Endpoint:** `POST /api/game-resources/reload`

**Response:**
```json
{
  "success": true,
  "message": "Resource definitions reloaded successfully",
  "count": 8
}
```

---

## Combat System API

Extended combat endpoints that integrate with ActionManager.

### Get Available Actions for Combat

Get all actions available in a specific combat context.

**Endpoint:** `GET /api/combat/{combatId}/available-actions`

**Parameters:**
- `combatId` (path) - The combat instance ID

**Response:**
```json
[
  {
    "actionId": "basic_attack",
    "displayName": "Basic Attack",
    "actionType": "ATTACK",
    "isAvailable": true,
    "cooldownRemaining": 0
  }
]
```

**Example (curl):**
```bash
curl http://localhost:5000/api/combat/combat_001/available-actions
```

---

### Get Cost Options for Action

Get all possible resource cost options for an action.

**Endpoint:** `GET /api/combat/{combatId}/cost-options`

**Parameters:**
- `combatId` (path) - The combat instance ID

**Query Parameters:**
- `actionId` (optional) - Filter by specific action

**Response:**
```json
[
  {
    "actionId": "fireball",
    "costOptions": [
      {
        "optionId": "mana_cost",
        "resources": [
          {
            "resourceId": "mana",
            "amount": 25
          }
        ]
      }
    ]
  }
]
```

---

### Check if Action Can Be Afforded

Check if a combatant can afford to use a specific action.

**Endpoint:** `POST /api/combat/{combatId}/actions/{actionId}/can-afford`

**Parameters:**
- `combatId` (path) - The combat instance ID
- `actionId` (path) - The action to check

**Request Body:**
```json
{
  "combatantId": "player_001",
  "costOptionId": "mana_cost"
}
```

**Response:**
```json
{
  "canAfford": true,
  "missingResources": []
}
```

---

## Common Use Cases

### 1. Creating a Combat Turn System

```csharp
// Get available actions for the player
var actionsResponse = await client.GetAsync($"http://localhost:5000/api/combat/{combatId}/available-actions");
var actions = await actionsResponse.Content.ReadFromJsonAsync<List<ActionSummaryDto>>();

// Check if player can afford an action
var canAffordRequest = new { combatantId = "player_001", costOptionId = "default" };
var canAffordResponse = await client.PostAsJsonAsync(
    $"http://localhost:5000/api/combat/{combatId}/actions/fireball/can-afford",
    canAffordRequest
);
var affordability = await canAffordResponse.Content.ReadFromJsonAsync<AffordabilityDto>();

if (affordability.CanAfford)
{
    // Execute the action
    // ...
}
```

---

### 2. Building a Character Sheet

```javascript
// Fetch all vital resources
const vitalsResponse = await fetch('http://localhost:5000/api/game-resources/by-category/VITAL');
const vitals = await vitalsResponse.json();

// Create resource pools for the character
const characterResources = {};
for (const vital of vitals) {
    const poolResponse = await fetch('http://localhost:5000/api/game-resources/create-pool', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            resourceId: vital.resourceId,
            initialCurrent: vital.defaultMax
        })
    });
    characterResources[vital.resourceId] = await poolResponse.json();
}
```

---

### 3. Modding: Adding Custom Actions

```python
# Define a custom action
custom_action = {
    "definition": {
        "actionId": "lightning_bolt",
        "displayName": "Lightning Bolt",
        "description": "Strike enemies with lightning",
        "actionType": "POWER",
        "baseDamage": 40,
        "cooldown": 2,
        "resourceCosts": [
            {"resourceId": "mana", "amount": 30}
        ],
        "tags": ["magic", "lightning", "ranged"]
    }
}

# Validate the action
response = requests.post(
    'http://localhost:5000/api/action/validate',
    json=custom_action
)
validation = response.json()

if validation['isValid']:
    print("Action is valid and ready to use!")
else:
    print(f"Validation errors: {validation['errors']}")
```

---

## Error Responses

All endpoints return standard HTTP status codes:

- `200 OK` - Request successful
- `400 Bad Request` - Invalid request parameters
- `404 Not Found` - Resource not found
- `500 Internal Server Error` - Server error

Error response format:
```json
{
  "error": "Error message",
  "details": "Detailed error information"
}
```

---

## Configuration

### Enable Config Reload

To enable runtime configuration reloading, add to `appsettings.json`:

```json
{
  "AllowConfigReload": true
}
```

**⚠️ Warning:** Only enable this in development environments. Production systems should not allow runtime config changes.

---

## Rate Limiting

Currently, there are no rate limits on the API. For production deployments, consider implementing rate limiting middleware.

---

## Authentication

The current API does not require authentication. For production use, implement authentication middleware appropriate for your deployment scenario.

---

## Support

For issues, questions, or contributions, please refer to the main project repository.

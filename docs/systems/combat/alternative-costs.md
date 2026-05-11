# Alternative Costs System

## Overview

The Alternative Costs system allows actions to have multiple payment options, giving players strategic choices about how to pay for powerful abilities. Instead of a single fixed cost, an action can offer several alternatives (e.g., "pay 8 mana OR pay 20 health OR pay 3 mana + 10 health").

## Key Concepts

### ActionCosts
The main container for action costs. Contains:
- `Costs`: Normal/default costs (list of ResourceCost)
- `AlternativeCosts`: List of alternative payment options (list of AlternativeCostOption)

### AlternativeCostOption
Represents one alternative way to pay for an action:
- `OptionId`: Unique identifier (e.g., "mana_cost", "health_cost")
- `Description`: Human-readable description (e.g., "Pay 20 health (blood magic)")
- `Costs`: List of ResourceCost required for this option

### ResourceCost
A single resource requirement:
- `ResourceId`: The resource to spend (e.g., "mana", "health")
- `Amount`: How much to spend
- `AllowOverdraft`: Whether spending can go below minimum (default: false)

## Usage Examples

### Example 1: Powerful Spell with Multiple Options

```json
{
  "actionId": "powerful_spell",
  "name": "Powerful Spell",
  "costs": {
    "costs": [],
    "alternativeCosts": [
      {
        "optionId": "mana_cost",
        "description": "Pay 8 mana",
        "costs": [
          { "resourceId": "mana", "amount": 8, "allowOverdraft": false }
        ]
      },
      {
        "optionId": "health_cost",
        "description": "Pay 20 health (blood magic)",
        "costs": [
          { "resourceId": "health", "amount": 20, "allowOverdraft": false }
        ]
      },
      {
        "optionId": "hybrid_cost",
        "description": "Pay 3 mana + 10 health",
        "costs": [
          { "resourceId": "mana", "amount": 3, "allowOverdraft": false },
          { "resourceId": "health", "amount": 10, "allowOverdraft": false }
        ]
      }
    ]
  }
}
```

### Example 2: Traditional Action (Backward Compatible)

```json
{
  "actionId": "fireball",
  "name": "Fireball",
  "costs": {
    "costs": [
      { "resourceId": "mana", "amount": 3, "allowOverdraft": false }
    ],
    "alternativeCosts": []
  }
}
```

## API Usage

### Execute Action with Cost Option

**Endpoint:** `POST /api/combat/{combatId}/action`

**Request Body:**
```json
{
  "actionType": "POWER",
  "powerId": "powerful_spell",
  "targetId": "enemy-1",
  "costOptionId": "health_cost"
}
```

If `costOptionId` is omitted, normal costs are used (backward compatible).

### Get Available Cost Options

**Endpoint:** `GET /api/combat/{combatId}/actions/{actionId}/cost-options`

**Response:**
```json
{
  "actionId": "powerful_spell",
  "normalCosts": [],
  "alternativeCosts": [
    {
      "optionId": "mana_cost",
      "description": "Pay 8 mana",
      "costs": [
        { "resourceId": "mana", "amount": 8 }
      ],
      "affordable": false
    },
    {
      "optionId": "health_cost",
      "description": "Pay 20 health (blood magic)",
      "costs": [
        { "resourceId": "health", "amount": 20 }
      ],
      "affordable": true
    },
    {
      "optionId": "hybrid_cost",
      "description": "Pay 3 mana + 10 health",
      "costs": [
        { "resourceId": "mana", "amount": 3 },
        { "resourceId": "health", "amount": 10 }
      ],
      "affordable": true
    }
  ]
}
```

**Note:** This endpoint currently returns a placeholder response. Full implementation requires ActionManager integration.

## Code Usage

### Check Affordability

```csharp
var heroResources = combatState.Hero.ResourceState.Resources;

// Check if hero can afford any option
bool canAfford = actionCosts.CanAfford(heroResources);

// Get all affordable options
var affordableOptions = actionCosts.GetAffordableOptions(heroResources);

// Check specific option
var option = actionCosts.GetOption("health_cost");
bool canAffordOption = option?.CanAfford(heroResources) ?? false;
```

### Get Error Messages

```csharp
// Get descriptive error if hero cannot afford
string? error = actionCosts.GetAffordabilityError(heroResources);
// Example: "Cannot afford any alternative. Options: Pay 8 mana OR Pay 20 health"
```

### Apply Costs (CombatSystem)

```csharp
// In CombatSystem.ApplyCosts() method
var updatedHero = ApplyCosts(hero, actionCosts, costOptionId: "health_cost");
```

## Design Patterns

### Pattern 1: Resource Conversion
Allow players to convert one resource into another:
- Option A: Pay 5 mana
- Option B: Pay 15 health (convert health to magical power)

### Pattern 2: Risk vs Safety
Give players a choice between safe and risky options:
- Option A: Pay 8 mana (safe, but expensive)
- Option B: Pay 3 mana + 20 health (risky, cheaper mana cost)

### Pattern 3: Situational Flexibility
Adapt to different combat situations:
- Option A: Pay 10 energy (good when energy is abundant)
- Option B: Pay 5 energy + 1 card from hand (good when low on energy)

### Pattern 4: Thematic Choices
Reflect character themes or moral choices:
- Option A: Pay 6 mana (pure magic)
- Option B: Pay 25 health (blood magic, dark power)
- Option C: Sacrifice 1 ally (forbidden ritual)

## Integration Status

### ✅ Completed
- Core data structures (AlternativeCostOption, ActionCosts)
- Unit tests (20 tests covering all functionality)
- Integration tests (6 scenarios demonstrating usage)
- CombatSystem support (costOptionId parameter, ApplyCosts method)
- API support (ExecuteActionRequest.CostOptionId field)
- API endpoint placeholder (GET /cost-options)

### 🔄 Pending (ActionManager Integration)
- Load action definitions from JSON
- Validate costOptionId against action definitions
- Populate /cost-options endpoint with real data
- Apply costs using ApplyCosts() method in ExecuteAction

### Integration Points

When ActionManager is integrated, update these locations:

1. **CombatSystem.ValidateAction()** (line ~217)
   - Uncomment TODO section
   - Validate costOptionId against action definition
   - Check affordability before execution

2. **CombatSystem.ExecutePower()** (future)
   - Replace hardcoded DEFAULT_POWER_COST
   - Use ApplyCosts() with action definition and costOptionId

3. **CombatController.GetCostOptions()** (line ~125)
   - Uncomment TODO section
   - Return actual cost options from ActionManager
   - Include affordability status for each option

## Testing

Run all tests:
```bash
dotnet test
```

Run alternative costs tests only:
```bash
dotnet test --filter "FullyQualifiedName~AlternativeCost"
```

Current test coverage:
- 20 unit tests (AlternativeCostOptionTests, ActionCostsTests)
- 6 integration tests (AlternativeCostsIntegrationTests)
- All 168 tests passing (162 existing + 6 new)

## Backward Compatibility

The system is fully backward compatible:
- `costOptionId` parameter is optional everywhere
- Actions without alternative costs work exactly as before
- Existing tests continue to pass (142 tests)
- API clients can ignore the new field

## Future Enhancements

Potential future features:
- **Conditional costs**: Costs that change based on game state
- **Discount modifiers**: Reduce costs based on buffs/items
- **Cost refunds**: Return resources if action fails
- **Dynamic costs**: Costs that scale with power level
- **Cost previews**: Show exact costs before committing

## References

- Core implementation: `src/Core/Combat/ActionCosts.cs`
- Unit tests: `tests/Core.Tests/Combat/AlternativeCostOptionTests.cs`
- Integration tests: `tests/Core.Tests/Combat/AlternativeCostsIntegrationTests.cs`
- API controller: `src/API/Controllers/CombatController.cs`
- Combat system: `src/Core/Combat/CombatSystem.cs`

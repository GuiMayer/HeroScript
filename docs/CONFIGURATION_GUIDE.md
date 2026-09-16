# HeroScript Configuration Guide

Complete guide to configuring HeroScript for different game genres, including file structure, required definitions, inheritance chains, and practical examples.

---

## Table of Contents

1. [Configuration Architecture](#configuration-architecture)
2. [Required Files by Genre](#required-files-by-genre)
3. [Configuration Examples](#configuration-examples)
4. [Inheritance and Overrides](#inheritance-and-overrides)
5. [Validation and Troubleshooting](#validation-and-troubleshooting)

---

## Configuration Architecture

### Directory Structure

```
configs/
├── default/                    # Base configuration
│   ├── Entities.json
│   ├── Actions.json
│   ├── StatusEffects.json
│   ├── MathFormulas.json
│   ├── GameResources.json
│   └── ...
├── roguelike/                  # Genre-specific overrides
│   ├── RunDefinitions.json
│   ├── CardSelections.json
│   ├── Shops.json
│   └── Preparations.json
├── autobattler/
│   ├── Gambits.json
│   └── Synergies.json
├── tactical/
│   ├── TurnPhases.json
│   ├── Grid.json
│   └── Positioning.json
└── puzzle/
    ├── PuzzleBoard.json
    └── ResourceConversion.json
```

### Configuration Chain

HeroScript uses a hierarchical configuration system:

```
default → genre → custom → runtime overrides
```

**Loading Priority:**
1. **Default config**: Base definitions loaded first
2. **Genre config**: Overrides/extends default
3. **Custom config**: User-specific modifications
4. **Runtime**: Dynamic changes during gameplay

**Example:** Loading `roguelike` config:
```
1. Load configs/default/Entities.json
2. Merge configs/roguelike/Entities.json (adds roguelike-specific entities)
3. Result: Combined entity definitions available
```

### File Formats

All configuration files use JSON format with the following structure:

```json
{
  "configName": "default",
  "version": "1.0.0",
  "definitions": [
    {
      "id": "unique_identifier",
      "name": "Display Name",
      // ... definition-specific fields
    }
  ]
}
```

---

## Required Files by Genre

### Roguelike Minimum Configuration

**Core Files (Required):**

1. **Entities.json** - Hero and enemy definitions
2. **Actions.json** - Combat actions and abilities
3. **RunDefinitions.json** - Run parameters and progression
4. **CardSelections.json** - Reward screen definitions
5. **Shops.json** - Shop configurations
6. **GameResources.json** - Gold, power points, etc.

**Optional but Recommended:**
- **StatusEffects.json** - Buffs/debuffs
- **Preparations.json** - Pre-combat preparation options
- **MathFormulas.json** - Damage calculations

**Minimal Example:**

```json
// Entities.json
{
  "configName": "roguelike",
  "definitions": [
    {
      "id": "hero",
      "name": "Hero",
      "baseResources": {
        "health": { "current": 100, "maximum": 100 },
        "energy": { "current": 3, "maximum": 3 }
      },
      "powers": ["basic_attack"]
    },
    {
      "id": "enemy_goblin",
      "name": "Goblin",
      "baseResources": {
        "health": { "current": 30, "maximum": 30 }
      },
      "aiType": "aggressive"
    }
  ]
}
```

```json
// RunDefinitions.json
{
  "configName": "roguelike",
  "definitions": [
    {
      "id": "default_run",
      "startingGold": 100,
      "startingCards": [
        "strike", "strike", "strike", "strike", "strike",
        "defend", "defend", "defend", "defend"
      ],
      "encounterPool": ["goblin_fight", "bandit_fight"],
      "shopAppearanceRate": 0.3
    }
  ]
}
```

```json
// CardSelections.json
{
  "configName": "roguelike",
  "definitions": [
    {
      "id": "basic_reward",
      "cardCount": 3,
      "rarity": {
        "common": 0.7,
        "uncommon": 0.25,
        "rare": 0.05
      },
      "rerollsAllowed": 1,
      "canDecompose": true,
      "decomposeValue": { "gold": 10 }
    }
  ]
}
```

```json
// Shops.json
{
  "configName": "roguelike",
  "definitions": [
    {
      "id": "basic_shop",
      "itemCount": 5,
      "itemTypes": ["card", "relic", "potion"],
      "priceRange": { "min": 50, "max": 200 },
      "rerollCost": 25
    }
  ]
}
```

---

### Auto-Battler Minimum Configuration

**Core Files (Required):**

1. **Entities.json** - Units with gambit support
2. **Actions.json** - Unit abilities
3. **Gambits.json** - AI behavior rules
4. **GameResources.json** - Any resources used

**Optional:**
- **Synergies.json** - Team composition bonuses
- **StatusEffects.json** - Buffs applied by synergies

**Minimal Example:**

```json
// Entities.json
{
  "configName": "autobattler",
  "definitions": [
    {
      "id": "warrior",
      "name": "Warrior",
      "controller": "gambit",
      "baseResources": {
        "health": { "current": 150, "maximum": 150 },
        "attack": { "current": 15, "maximum": 15 }
      },
      "powers": ["slash", "cleave"],
      "gambitSet": "warrior_default"
    },
    {
      "id": "healer",
      "name": "Healer",
      "controller": "gambit",
      "baseResources": {
        "health": { "current": 80, "maximum": 80 },
        "mana": { "current": 50, "maximum": 50 }
      },
      "powers": ["heal", "protect"],
      "gambitSet": "healer_default"
    }
  ]
}
```

```json
// Gambits.json
{
  "configName": "autobattler",
  "definitions": [
    {
      "id": "warrior_default",
      "gambits": [
        {
          "priority": 10,
          "conditions": [
            { "type": "EnemyHealthBelow", "threshold": 0.3 }
          ],
          "action": {
            "actionId": "cleave",
            "targetSelection": "LowestHealthEnemy"
          }
        },
        {
          "priority": 5,
          "conditions": [],
          "action": {
            "actionId": "slash",
            "targetSelection": "NearestEnemy"
          }
        }
      ]
    },
    {
      "id": "healer_default",
      "gambits": [
        {
          "priority": 10,
          "conditions": [
            { "type": "AllyHealthBelow", "threshold": 0.5 }
          ],
          "action": {
            "actionId": "heal",
            "targetSelection": "LowestHealthAlly"
          }
        },
        {
          "priority": 7,
          "conditions": [
            { "type": "AllyHealthBelow", "threshold": 0.7 }
          ],
          "action": {
            "actionId": "protect",
            "targetSelection": "LowestHealthAlly"
          }
        }
      ]
    }
  ]
}
```

---

### Tactical RPG Minimum Configuration

**Core Files (Required):**

1. **Entities.json** - Units with positioning data
2. **Actions.json** - Abilities with range/AoE
3. **TurnPhases.json** - Phase sequence definitions
4. **StatusEffects.json** - Buffs, debuffs, conditions
5. **GameResources.json** - AP, movement points

**Optional:**
- **Grid.json** - Terrain and obstacles
- **Positioning.json** - Cover and flanking rules

**Minimal Example:**

```json
// Entities.json
{
  "configName": "tactical",
  "definitions": [
    {
      "id": "knight",
      "name": "Knight",
      "baseResources": {
        "health": { "current": 120, "maximum": 120 },
        "actionPoints": { "current": 2, "maximum": 2 }
      },
      "positioning": {
        "movementRange": 3,
        "attackRange": 1,
        "size": "medium"
      },
      "powers": ["sword_strike", "shield_bash"]
    },
    {
      "id": "archer",
      "name": "Archer",
      "baseResources": {
        "health": { "current": 80, "maximum": 80 },
        "actionPoints": { "current": 2, "maximum": 2 }
      },
      "positioning": {
        "movementRange": 4,
        "attackRange": 5,
        "size": "medium"
      },
      "powers": ["arrow_shot", "multi_shot"]
    }
  ]
}
```

```json
// TurnPhases.json
{
  "configName": "tactical",
  "definitions": [
    {
      "id": "standard_tactical",
      "phases": [
        {
          "phaseId": "player_turn",
          "name": "Player Phase",
          "actorFilter": "PlayerControlled",
          "allowMovement": true,
          "allowActions": true
        },
        {
          "phaseId": "enemy_turn",
          "name": "Enemy Phase",
          "actorFilter": "AIControlled",
          "allowMovement": true,
          "allowActions": true
        }
      ],
      "turnEndConditions": ["AllUnitsActed"]
    }
  ]
}
```

```json
// StatusEffects.json
{
  "configName": "tactical",
  "definitions": [
    {
      "id": "poison",
      "name": "Poison",
      "type": "Debuff",
      "maxStacks": 5,
      "duration": 3,
      "timing": "OnTurnStart",
      "effect": {
        "type": "Damage",
        "formula": "stacks * 5"
      }
    },
    {
      "id": "haste",
      "name": "Haste",
      "type": "Buff",
      "maxStacks": 1,
      "duration": 2,
      "effect": {
        "type": "ResourceModifier",
        "resourceId": "actionPoints",
        "modifier": "+1"
      }
    }
  ]
}
```

---

### Puzzle RPG Minimum Configuration

**Core Files (Required):**

1. **Entities.json** - Hero and enemies
2. **Actions.json** - Abilities with costs
3. **MathFormulas.json** - Match damage calculations
4. **GameResources.json** - Mana, energy, orbs
5. **StatusEffects.json** - Buffs from combos

**Optional:**
- **PuzzleBoard.json** - Board configuration
- **ResourceConversion.json** - Orb → resource mapping

**Minimal Example:**

```json
// MathFormulas.json
{
  "configName": "puzzle",
  "definitions": [
    {
      "id": "match_damage",
      "name": "Match Damage Calculation",
      "formula": "base_damage * orbs_matched * (1 + combo_count * 0.5) * attack_stat / 100",
      "variables": {
        "base_damage": { "type": "number", "default": 10 },
        "orbs_matched": { "type": "number" },
        "combo_count": { "type": "number", "default": 0 },
        "attack_stat": { "type": "number" }
      }
    },
    {
      "id": "mana_generation",
      "name": "Mana from Orbs",
      "formula": "orbs_matched + (combo_count * 2)",
      "variables": {
        "orbs_matched": { "type": "number" },
        "combo_count": { "type": "number", "default": 0 }
      }
    }
  ]
}
```

```json
// GameResources.json
{
  "configName": "puzzle",
  "definitions": [
    {
      "id": "mana",
      "name": "Mana",
      "category": "Expendable",
      "maximum": 100,
      "regeneration": 0
    },
    {
      "id": "energy",
      "name": "Energy",
      "category": "Expendable",
      "maximum": 10,
      "regeneration": 0
    },
    {
      "id": "red_orbs",
      "name": "Red Orbs",
      "category": "Currency",
      "generatesResource": "mana"
    },
    {
      "id": "blue_orbs",
      "name": "Blue Orbs",
      "category": "Currency",
      "generatesResource": "mana"
    }
  ]
}
```

```json
// Actions.json
{
  "configName": "puzzle",
  "definitions": [
    {
      "id": "fireball",
      "name": "Fireball",
      "actionType": "POWER",
      "costs": [
        {
          "resourceId": "mana",
          "amount": 10
        },
        {
          "resourceId": "energy",
          "amount": 3
        }
      ],
      "effects": [
        {
          "type": "Damage",
          "formula": "25 * (1 + attack_stat / 100)",
          "target": "SingleEnemy"
        }
      ]
    }
  ]
}
```

---

## Configuration Examples

### Complete Roguelike Configuration

Full working example for a minimal roguelike game:

**configs/roguelike/Entities.json:**
```json
{
  "configName": "roguelike",
  "version": "1.0.0",
  "definitions": [
    {
      "id": "player",
      "name": "Adventurer",
      "controller": "player",
      "baseResources": {
        "health": { "current": 100, "maximum": 100 },
        "energy": { "current": 3, "maximum": 3 },
        "gold": { "current": 100, "maximum": 9999 }
      },
      "stats": {
        "attack": 10,
        "defense": 5
      },
      "powers": ["basic_attack", "defend"]
    },
    {
      "id": "enemy_1",
      "name": "Slime",
      "controller": "ai",
      "baseResources": {
        "health": { "current": 25, "maximum": 25 }
      },
      "stats": {
        "attack": 5,
        "defense": 2
      },
      "powers": ["basic_attack"],
      "gambitSet": "simple_aggressive"
    },
    {
      "id": "enemy_2",
      "name": "Goblin Scout",
      "controller": "ai",
      "baseResources": {
        "health": { "current": 40, "maximum": 40 }
      },
      "stats": {
        "attack": 8,
        "defense": 3
      },
      "powers": ["basic_attack", "quick_strike"],
      "gambitSet": "simple_aggressive"
    }
  ]
}
```

**configs/roguelike/Actions.json:**
```json
{
  "configName": "roguelike",
  "definitions": [
    {
      "id": "basic_attack",
      "name": "Attack",
      "actionType": "BASIC_ATTACK",
      "costs": [
        { "resourceId": "energy", "amount": 1 }
      ],
      "effects": [
        {
          "type": "Damage",
          "formula": "attack_stat",
          "target": "SingleEnemy"
        }
      ]
    },
    {
      "id": "defend",
      "name": "Defend",
      "actionType": "POWER",
      "costs": [
        { "resourceId": "energy", "amount": 1 }
      ],
      "effects": [
        {
          "type": "ApplyStatus",
          "statusEffectId": "block",
          "stacks": 5,
          "target": "Self"
        }
      ]
    },
    {
      "id": "quick_strike",
      "name": "Quick Strike",
      "actionType": "POWER",
      "costs": [
        { "resourceId": "energy", "amount": 1 }
      ],
      "effects": [
        {
          "type": "Damage",
          "formula": "attack_stat * 0.75",
          "target": "SingleEnemy"
        }
      ]
    }
  ]
}
```

---

## Inheritance and Overrides

### How Inheritance Works

When loading a config like `roguelike`, HeroScript:

1. **Loads base definitions** from `configs/default/`
2. **Merges genre definitions** from `configs/roguelike/`
3. **Overrides by ID**: If `roguelike/Entities.json` defines `"id": "hero"` and `default/Entities.json` also has `"id": "hero"`, the roguelike version wins

### Override Example

**configs/default/Entities.json:**
```json
{
  "definitions": [
    {
      "id": "hero",
      "name": "Generic Hero",
      "baseResources": {
        "health": { "current": 100, "maximum": 100 }
      }
    }
  ]
}
```

**configs/tactical/Entities.json:**
```json
{
  "definitions": [
    {
      "id": "hero",
      "name": "Tactical Hero",
      "baseResources": {
        "health": { "current": 120, "maximum": 120 },
        "actionPoints": { "current": 2, "maximum": 2 }
      },
      "positioning": {
        "movementRange": 3,
        "attackRange": 1
      }
    }
  ]
}
```

**Result when loading `tactical` config:**
```json
{
  "id": "hero",
  "name": "Tactical Hero",  // Overridden
  "baseResources": {
    "health": { "current": 120, "maximum": 120 },  // Overridden
    "actionPoints": { "current": 2, "maximum": 2 }  // Added
  },
  "positioning": {  // Added
    "movementRange": 3,
    "attackRange": 1
  }
}
```

### Partial Overrides

You can override specific fields while inheriting others:

**default/Actions.json:**
```json
{
  "id": "fireball",
  "name": "Fireball",
  "costs": [{ "resourceId": "mana", "amount": 10 }],
  "effects": [{ "type": "Damage", "formula": "25" }]
}
```

**puzzle/Actions.json:**
```json
{
  "id": "fireball",
  "costs": [
    { "resourceId": "mana", "amount": 15 },
    { "resourceId": "energy", "amount": 3 }
  ]
}
```

**Result:** Fireball in puzzle mode costs more, but keeps the same name and effects.

---

## Validation and Troubleshooting

### Common Configuration Errors

**1. Missing Required Definition**

```
Error: Run definition 'default_run' not found
```

**Fix:** Ensure `RunDefinitions.json` contains the required ID:
```json
{
  "definitions": [
    {
      "id": "default_run",
      // ... definition
    }
  ]
}
```

**2. Invalid Resource Reference**

```
Error: Resource 'mana' not defined in GameResources.json
```

**Fix:** Add resource definition:
```json
// GameResources.json
{
  "definitions": [
    {
      "id": "mana",
      "name": "Mana",
      "category": "Expendable",
      "maximum": 100
    }
  ]
}
```

**3. Circular Dependencies**

```
Error: Circular dependency detected: A → B → A
```

**Fix:** Review entity/action references and remove cycles.

**4. Invalid Formula Syntax**

```
Error: Formula 'base * atk +' is invalid
```

**Fix:** Correct formula syntax:
```json
{
  "formula": "base * atk"  // Removed trailing '+'
}
```

### Validation and publication endpoints

Gameplay definitions are read from immutable content revisions. Validate a
complete candidate bundle (or an already published revision) through the
content API:

```bash
# Validate one candidate bundle
POST /api/v1/content/validate
{
  "bundle": { ... }
}

# Validate one published revision
POST /api/v1/content/validate
{
  "revision": "<sha256>"
}

# Query definitions pinned to that revision
GET /api/v1/content/entities?revision=<sha256>
GET /api/v1/content/actions?revision=<sha256>
GET /api/v1/content/formulas?revision=<sha256>
```

### Configuration Debugging

**Enable verbose logging:**
```json
// appsettings.json
{
  "Logging": {
    "LogLevel": {
      "Core.Configuration": "Debug"
    }
  }
}
```

**Check loaded config:**
```bash
GET /api/v1/admin/config/current
# Returns currently active configuration name and loaded files
```

**Change content without restarting in development:**
```bash
POST /api/v1/admin/content/drafts
PUT  /api/v1/admin/content/drafts/{draftId}/artifacts/{kind}/{definitionId}
POST /api/v1/admin/content/drafts/{draftId}/validate
POST /api/v1/admin/content/drafts/{draftId}/publish
```

Publishing creates a new immutable revision. A development run only adopts it
through `ACTIVATE_CONTENT_REVISION`; the engine rejects activation when a
pinned card upgrade is incompatible with the new base container.

---

## Configuration Checklist

Before starting a game session, verify:

- [ ] All required definition files exist for your genre
- [ ] Entity IDs referenced in code match definition IDs
- [ ] All action costs reference valid resources
- [ ] Formula syntax is valid (use `/api/v1/simulations/formulas/{name}` to test)
- [ ] Status effects reference valid timings
- [ ] Gambit conditions reference valid entity properties
- [ ] No circular dependencies between definitions

**Quick Test:**
```bash
# Start a minimal run to verify config
POST /api/v1/runs
{
  "configName": "your_genre",
  "runDefinitionId": "test_run",
  "playerEntityId": "hero"
}
```

If this succeeds, your core configuration is valid.

---

## Best Practices

1. **Start with `default` config** - Build base definitions in `default/`, override in genre configs
2. **Use descriptive IDs** - `enemy_goblin_scout` not `e1`
3. **Keep formulas simple** - Complex logic belongs in code, not config
4. **Version your configs** - Include `version` field for migration tracking
5. **Document custom fields** - Add comments (as JSON doesn't support comments, use separate docs)
6. **Test incrementally** - Add one definition at a time, test with API
7. **Use validation endpoints** - Catch errors before runtime

---

## Additional Resources

- **API Endpoints Reference:** `docs/API_ENDPOINTS.md`
- **Game Flow Documentation:** `docs/GAME_FLOW.md`
- **Example Configurations:** `configs/examples/`
- **Schema Definitions:** `schemas/` (if available)

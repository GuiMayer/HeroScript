# Status Effects Configuration Guide

This directory contains all status effect definitions for the HeroScript game engine.

## Quick Start

Status effects are defined in `status_effects.json` and loaded automatically at startup. The system supports:

- **Damage Over Time (DoT)** - Burning, Poison, Bleeding
- **Buffs** - Strength, Dexterity, Regeneration, Shield
- **Debuffs** - Weakness, Vulnerable, Frail, Stunned, Silenced
- **Special Effects** - Artifact, Intangible, Buffer, Thorns

## File Structure

```
StatusEffects/
├── status_effects.json          # Main configuration file (loaded by game)
├── README.md                    # This file
└── examples/                    # Commented examples for learning
    ├── dot_effects.json         # Damage over time examples
    ├── buff_effects.json        # Buff examples
    ├── debuff_effects.json      # Debuff examples
    └── special_effects.json     # Special mechanics examples
```

## Status Effect Definition Schema

```json
{
  "statusId": "UNIQUE_ID",           // Unique identifier (UPPERCASE recommended)
  "displayName": "Display Name",     // Human-readable name
  "description": "What it does",     // Tooltip text
  "type": "BUFF|DEBUFF|NEUTRAL",     // Effect category
  "isStackable": true|false,         // Can multiple instances stack?
  "maxStacks": 10,                   // Maximum stacks (if stackable)
  "defaultDuration": 3,              // Turns (-1 = permanent)
  "refreshDuration": true|false,     // Reset duration when reapplied?
  "behaviors": [                     // What the effect actually does
    {
      "timing": "WHEN_IT_TRIGGERS",  // See Timing section
      "type": "WHAT_IT_DOES",        // See Behavior Types section
      "value": 5.0,                  // Numeric value (if applicable)
      "scalesWithStacks": true,      // Multiply by stack count?
      "modifierKey": "stat_name",    // For STAT_MODIFIER type
      "formulaValue": "stacks * 0.25", // For STAT_MODIFIER type
      "consumesStack": true          // Remove stack when triggered?
    }
  ]
}
```

## Timing Options

When does the behavior trigger?

| Timing | Description | Use Cases |
|--------|-------------|-----------|
| `START_OF_TURN` | Beginning of entity's turn | Regeneration, Stunned check, Energy modification |
| `END_OF_TURN` | End of entity's turn | Burning, Poison, Bleeding |
| `ON_DAMAGE_TAKEN` | When entity takes damage | Shield absorption, Thorns reflection, Intangible |
| `ON_DEATH` | When HP would reach 0 | Buffer (death prevention) |
| `ON_DEBUFF_APPLIED` | When debuff is about to be applied | Artifact (debuff blocking) |
| `PASSIVE` | Always active | Strength, Weakness, stat modifiers |

## Behavior Types

What does the effect do?

### Damage & Healing

| Type | Description | Required Fields |
|------|-------------|-----------------|
| `DAMAGE_OVER_TIME` | Deals damage each turn | `value`, `scalesWithStacks` |
| `HEAL_OVER_TIME` | Restores HP each turn | `value`, `scalesWithStacks` |

### Stat Modification

| Type | Description | Required Fields |
|------|-------------|-----------------|
| `STAT_MODIFIER` | Modifies damage/stats | `modifierKey`, `formulaValue` |

**Available Modifier Keys:**
- `increased_damage_total` - Outgoing damage multiplier
- `increased_damage_taken` - Incoming damage multiplier
- `crit_chance` - Critical hit probability
- `block_effectiveness` - Block gain multiplier

### Damage Interaction

| Type | Description | Required Fields |
|------|-------------|-----------------|
| `ABSORB_DAMAGE` | Reduces incoming damage | `value`, `scalesWithStacks` |
| `REFLECT_DAMAGE` | Deals damage back to attacker | `value`, `scalesWithStacks` |
| `DAMAGE_CAP` | Limits damage to maximum | `value` |

### Control Effects

| Type | Description | Required Fields |
|------|-------------|-----------------|
| `SKIP_TURN` | Entity cannot act this turn | None |
| `DISABLE_POWERS` | Cannot use abilities | None |

### Special Mechanics

| Type | Description | Required Fields | Status |
|------|-------------|-----------------|--------|
| `PREVENT_NEXT_DEBUFF` | Blocks next debuff | `consumesStack` | TODO |
| `DEATH_PREVENTION` | Survive fatal damage | `consumesStack` | TODO |
| `MODIFY_ENERGY` | Change energy gain | `value`, `scalesWithStacks` | TODO |

## Formula Syntax

For `STAT_MODIFIER` behaviors, use `formulaValue` with these variables:

- `stacks` - Current stack count
- Basic math operators: `+`, `-`, `*`, `/`

**Examples:**
```json
"formulaValue": "stacks * 0.25"      // +25% per stack
"formulaValue": "stacks * -0.50"     // -50% per stack
"formulaValue": "stacks * 0.10"      // +10% per stack
```

## Integration with Actions

Status effects are applied through Actions using the `APPLY_STATUS` effect type:

```json
{
  "effects": [
    {
      "type": "APPLY_STATUS",
      "statusId": "BURNING",
      "stacks": 3,
      "duration": 3,
      "target": "SINGLE_ENEMY"
    }
  ]
}
```

See `data/configs/default/Actions/poison_strike.json` for a working example.

## Creating Custom Status Effects

### Step 1: Define the Effect

Add your definition to `status_effects.json`:

```json
{
  "statusId": "MY_CUSTOM_EFFECT",
  "displayName": "My Effect",
  "description": "Does something cool",
  "type": "BUFF",
  "isStackable": true,
  "maxStacks": 5,
  "defaultDuration": 3,
  "refreshDuration": true,
  "behaviors": [
    {
      "timing": "END_OF_TURN",
      "type": "DAMAGE_OVER_TIME",
      "value": 10.0,
      "scalesWithStacks": true
    }
  ]
}
```

### Step 2: Create an Action

Create an action that applies your effect:

```json
{
  "actionId": "apply_my_effect",
  "displayName": "Apply My Effect",
  "energyCost": 1,
  "effects": [
    {
      "type": "APPLY_STATUS",
      "statusId": "MY_CUSTOM_EFFECT",
      "stacks": 2,
      "duration": 3,
      "target": "SINGLE_ENEMY"
    }
  ]
}
```

### Step 3: Test

Use the API to test your effect:

```bash
# Apply the status effect
curl -X POST http://localhost:5000/api/statuseffects/apply \
  -H "Content-Type: application/json" \
  -d '{
    "targetId": "entity-guid",
    "statusId": "MY_CUSTOM_EFFECT",
    "stacks": 2,
    "duration": 3
  }'

# Check active effects
curl http://localhost:5000/api/statuseffects/entity-guid/active
```

## Design Guidelines

### Balance Considerations

1. **Stacking Limits**: Higher damage/impact = lower max stacks
   - DoT: 10 stacks max (3-5 damage per stack)
   - Damage modifiers: 3-5 stacks max (25-50% per stack)
   - Control effects: Usually not stackable

2. **Duration**: More powerful = shorter duration
   - Intangible: 1 turn (extremely powerful)
   - Stunned: 1 turn (loses entire turn)
   - Strength/Weakness: Permanent (but removable)
   - DoTs: 2-3 turns (sustained damage)

3. **Refresh Behavior**:
   - DoTs: Usually refresh (extend duration)
   - Buffs: Usually don't refresh (add stacks instead)
   - Control: Usually don't refresh (binary state)

### Mod Compatibility

The system is designed for mod extensibility:

- **Multiple Config Sources**: `LoadStatusDefinitions()` can be called multiple times
- **ID Conflicts**: Later definitions override earlier ones (with warning)
- **Validation**: Invalid definitions are skipped with error logs
- **No Hardcoding**: All effects are data-driven

**For Modders:**
1. Create your own `status_effects.json`
2. Call `StatusEffectManager.LoadStatusDefinitions("your_mod_name")`
3. Use unique `statusId` prefixes (e.g., `MYMOD_BURNING`)

## Troubleshooting

### Effect Not Loading

Check logs for:
```
[StatusEffectManager] Failed to load definition: EFFECT_ID
```

Common causes:
- Missing required fields (`statusId`, `type`, `behaviors`)
- Invalid JSON syntax
- Invalid enum values (`type`, `timing`, `behaviorType`)

### Effect Not Triggering

1. Verify timing is correct for your use case
2. Check that behavior type is implemented (see TODO list)
3. Ensure entity has the status effect active
4. Check duration hasn't expired

### Stacks Not Working

- Verify `isStackable: true`
- Check `maxStacks` isn't being exceeded
- Confirm `scalesWithStacks: true` in behavior

## Implementation Status

### Fully Implemented
- ✅ DAMAGE_OVER_TIME (END_OF_TURN)
- ✅ STAT_MODIFIER (PASSIVE)
- ✅ Basic stacking and duration
- ✅ Status effect manager API

### Partially Implemented
- ⚠️ HEAL_OVER_TIME (needs START_OF_TURN processing)
- ⚠️ ABSORB_DAMAGE (needs ON_DAMAGE_TAKEN integration)
- ⚠️ SKIP_TURN (needs START_OF_TURN processing)

### TODO
- ❌ PREVENT_NEXT_DEBUFF (Artifact)
- ❌ DEATH_PREVENTION (Buffer)
- ❌ DAMAGE_CAP (Intangible)
- ❌ REFLECT_DAMAGE (Thorns)
- ❌ MODIFY_ENERGY (Haste/Slow)
- ❌ DISABLE_POWERS (Silenced)
- ❌ ON_DEATH timing
- ❌ ON_DEBUFF_APPLIED timing
- ❌ consumesStack logic

See `examples/special_effects.json` for detailed TODO notes on special behaviors.

## API Reference

See the StatusEffectController for REST endpoints:

- `POST /api/statuseffects/apply` - Apply status effect
- `GET /api/statuseffects/{targetId}/active` - Get active effects
- `DELETE /api/statuseffects/{targetId}/{instanceId}` - Remove effect
- `POST /api/statuseffects/{targetId}/stacks` - Modify stacks
- `POST /api/statuseffects/{targetId}/tick` - Process turn

Full API documentation: `/swagger`

## Examples

See the `examples/` directory for fully commented examples of each effect type.

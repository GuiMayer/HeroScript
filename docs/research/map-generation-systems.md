# Roguelike Card Game Map Generation & Progression Systems

**Research Document**
**Project:** HeroScript
**Date:** 2026-07-08
**Purpose:** Document map topology types, node systems, progression mechanics, and configuration approaches for roguelike card games

---

## Table of Contents

1. [Industry Map Generation Patterns](#industry-map-generation-patterns)
2. [HeroScript Current Implementation](#heroscript-current-implementation)
3. [Gap Analysis & Recommendations](#gap-analysis--recommendations)
4. [Data-Driven Configuration Structure](#data-driven-configuration-structure)
5. [Implementation Roadmap](#implementation-roadmap)

---

## 1. Industry Map Generation Patterns

### 1.1 Map Topology Types

#### **A. Branching/Layered (Slay the Spire)**

**Structure:**
- Vertical progression through layers (typically 15-17 layers per act)
- 3-7 nodes per layer
- Connections only between adjacent layers (no backtracking)
- Player chooses path from available connections

**Characteristics:**
- Visual: Directed acyclic graph (DAG) rendered vertically
- Path flexibility: High (multiple routes to same destination)
- Replayability: Excellent (different paths each run)
- Player agency: Moderate (choose between 2-4 options per layer)

**Generation Algorithm:**
```
1. Generate start node (layer 0)
2. For each layer L (1 to N):
   a. Generate 3-7 nodes with random types
   b. Connect each node to 1-3 nodes in layer L-1
   c. Ensure all nodes in L-1 have at least 1 connection forward
   d. Ensure all nodes in L have at least 1 connection backward
3. Generate boss node (layer N+1) connected to all layer N nodes
4. Apply constraints (e.g., shops only in certain layers)
```

**Advantages:**
- Clear visual progression
- No dead ends or impossible states
- Easy to balance (control node type distribution per layer)
- Supports "look ahead" gameplay (plan route 2-3 layers ahead)

**Disadvantages:**
- Can feel formulaic after many runs
- Limited backtracking or exploration

**Configuration Parameters:**
```json
{
  "layerCount": 15,
  "nodesPerLayer": { "min": 3, "max": 7 },
  "connectionsPerNode": { "min": 1, "max": 3 },
  "nodeTypeWeights": {
    "layers_1-5": { "combat": 0.6, "event": 0.3, "treasure": 0.1 },
    "layers_6-10": { "combat": 0.5, "elite": 0.2, "shop": 0.2, "rest": 0.1 },
    "layers_11-15": { "combat": 0.4, "elite": 0.3, "shop": 0.2, "rest": 0.1 }
  },
  "constraintRules": {
    "shop": { "minLayer": 3, "maxLayer": 14, "frequency": "every_5_layers" },
    "rest": { "minLayer": 4, "maxLayer": 14, "frequency": "every_6_layers" }
  }
}
```

---

#### **B. Ring/Linear (Monster Train)**

**Structure:**
- Fixed sequence of rings/stations
- Linear progression with no branching
- Each ring has predefined or semi-random encounter type
- Player makes choices within encounters, not between paths

**Characteristics:**
- Visual: Circular or linear track
- Path flexibility: None (fixed order)
- Replayability: Moderate (variety from encounter content, not path)
- Player agency: Low for map, high within encounters

**Generation Algorithm:**
```
1. Define ring count (typically 8 per run)
2. For each ring:
   a. Assign encounter type based on position rules
   b. Position 1-2: Easy combat
   c. Position 3-4: Shop or event
   d. Position 5-7: Elite combat or blessing
   e. Position 8: Boss
3. Add random events within constraints
```

**Advantages:**
- Simple, predictable structure
- Easy to tune difficulty curve
- Fast generation (no graph solving)
- Works well for shorter runs

**Disadvantages:**
- Low replayability from map structure
- No strategic path planning
- Requires high encounter variety to stay interesting

**Configuration Parameters:**
```json
{
  "ringCount": 8,
  "ringTemplates": [
    { "position": 1, "type": "combat", "difficulty": "easy" },
    { "position": 2, "type": "combat", "difficulty": "easy" },
    { "position": 3, "type": "random", "pool": ["shop", "event", "treasure"] },
    { "position": 4, "type": "combat", "difficulty": "medium" },
    { "position": 5, "type": "random", "pool": ["elite", "event"] },
    { "position": 6, "type": "shop" },
    { "position": 7, "type": "elite", "difficulty": "hard" },
    { "position": 8, "type": "boss" }
  ],
  "randomEventChance": 0.15,
  "difficultyProgression": "linear"
}
```

---

#### **C. Hub & Spoke (Griftlands)**

**Structure:**
- Central hub with multiple mission branches
- Complete missions to unlock new areas
- Some non-linear progression (choose mission order)
- Story-driven node selection

**Characteristics:**
- Visual: Hub with radiating mission paths
- Path flexibility: Moderate (mission order flexibility)
- Replayability: High (different mission combinations)
- Player agency: High (choose missions, negotiate outcomes)

**Configuration Parameters:**
```json
{
  "hubCount": 4,
  "missionsPerHub": 4,
  "missionLength": { "min": 2, "max": 4 },
  "unlockRequirements": { "missionsCompleted": 2 },
  "missionTypes": ["combat", "negotiation", "exploration", "story"]
}
```

---

#### **D. Grid/Free Roam (FTL-inspired, rarely used in card games)**

**Structure:**
- 2D grid or network of interconnected nodes
- Player can move in multiple directions
- Fog of war (can't see all nodes initially)
- More exploration-focused

**Characteristics:**
- Visual: 2D map with fog of war
- Path flexibility: Very high (explore freely)
- Replayability: Excellent (different exploration paths)
- Player agency: Very high (full freedom)

**Advantages:**
- Maximum exploration freedom
- Highest replayability
- Supports backtracking and route optimization
- Can hide secrets and optional areas

**Disadvantages:**
- Can be confusing or overwhelming
- Harder to balance progression
- Risk of getting lost or missing content
- More UI complexity

**Configuration Parameters:**
```json
{
  "gridSize": { "width": 15, "height": 10 },
  "nodeDensity": 0.4,
  "connectionRules": "adjacent_and_diagonal",
  "fogOfWarRadius": 2,
  "deadEndPenalty": 0.7,
  "secretNodeChance": 0.05
}
```

---

### 1.2 Node Types & Purposes

#### **Combat (Standard)**
**Purpose:** Core challenge node where player fights enemies
**Rewards:** Gold, card rewards (via CardSelection)
**Frequency:** 60-70% of nodes
**Variants:**
- Easy: 1-2 weak enemies
- Medium: 2-3 balanced enemies
- Hard: 3+ enemies or strong single enemy

**Configuration:**
```json
{
  "nodeType": "combat",
  "difficulty": "medium",
  "enemyPool": "act1_standard",
  "enemyCount": { "min": 2, "max": 3 },
  "goldReward": { "min": 10, "max": 20 },
  "cardRewardCount": 3
}
```

---

#### **Elite Combat**
**Purpose:** Harder optional fights with better rewards
**Rewards:** More gold, rare cards, relics
**Frequency:** 10-15% of nodes
**Characteristics:**
- Significantly harder than standard combat
- Higher risk/reward
- Often skippable (alternate path available)

**Configuration:**
```json
{
  "nodeType": "elite",
  "difficulty": "hard",
  "enemyPool": "act1_elite",
  "goldReward": { "min": 25, "max": 40 },
  "cardRewardCount": 3,
  "cardRewardRarity": "rare_boosted",
  "relicChance": 0.5
}
```

---

#### **Boss**
**Purpose:** Act-ending major challenge
**Rewards:** Special relic, large gold bonus
**Frequency:** 1 per act (fixed)
**Characteristics:**
- Always at end of act
- Unique mechanics per boss
- Run-defining rewards

**Configuration:**
```json
{
  "nodeType": "boss",
  "bossPool": "act1_bosses",
  "goldReward": { "min": 100, "max": 150 },
  "relicReward": "boss_relic",
  "healAfter": 1.0
}
```

---

#### **Shop**
**Purpose:** Spend gold on cards, relics, potions, removals
**Services:** Buy cards, remove cards, buy relics, buy potions
**Frequency:** Every 5-7 layers
**Characteristics:**
- Strategic resource management
- Prices scale with progression
- Can reroll inventory for cost

**Configuration:**
```json
{
  "nodeType": "shop",
  "shopId": "standard_shop",
  "inventorySize": 7,
  "cardCount": 5,
  "relicCount": 2,
  "rerollCost": 50,
  "cardRemovalCost": 75
}
```

---

#### **Rest Site (Campfire)**
**Purpose:** Heal or upgrade cards
**Options:**
- Rest: Heal 30% max HP
- Smith: Upgrade a card
- (Optional) Dig: Gamble for relic (low HP risk)
**Frequency:** Every 6-8 layers
**Characteristics:**
- Safe space (no combat)
- Strategic decision (heal vs power)

**Configuration:**
```json
{
  "nodeType": "rest",
  "healAmount": 0.3,
  "upgradeSlots": 1,
  "specialOptions": [
    {
      "optionId": "dig",
      "unlockCondition": "has_shovel_relic",
      "hpCost": 0.1,
      "relicChance": 0.35
    }
  ]
}
```

---

#### **Treasure/Chest**
**Purpose:** Free rewards with no combat
**Rewards:** Relic, gold, or rare card
**Frequency:** 5-10% of nodes (rare)
**Characteristics:**
- Pure reward node
- Sometimes has minor cost (lose HP, curse)
- High value

**Configuration:**
```json
{
  "nodeType": "treasure",
  "rewardType": "random",
  "rewardPool": {
    "relic": 0.6,
    "gold": 0.3,
    "card": 0.1
  },
  "goldAmount": { "min": 50, "max": 100 },
  "cursedChestChance": 0.15
}
```

---

#### **Unknown/Event (Question Mark)**
**Purpose:** Random events with choices
**Outcomes:** Variable (can be good, bad, or neutral)
**Frequency:** 15-20% of nodes
**Characteristics:**
- High variance
- Story/flavor moments
- Strategic risk/reward decisions

**Event Types:**
- Combat event: Fight with twist
- Merchant: Special deals
- Shrine: Lose something, gain something
- NPC: Story choices
- Puzzle: Solve for reward

**Configuration:**
```json
{
  "nodeType": "event",
  "eventPool": "act1_events",
  "eventWeights": {
    "combat_event": 0.3,
    "shrine": 0.25,
    "merchant": 0.2,
    "npc_story": 0.15,
    "puzzle": 0.1
  }
}
```

---

### 1.3 Progression Mechanics

#### **Path Selection**
- **Player Choice:** Player selects next node from available connections
- **Visibility:** Can see 1-3 layers ahead (depending on game)
- **Locked Paths:** Some paths may be locked until conditions met
- **One-Way:** Most systems prevent backtracking

#### **Node Reveal**
- **Full Reveal:** All nodes visible from start (Slay the Spire)
- **Progressive Reveal:** Only nearby nodes visible (FTL-style)
- **Fog of War:** Nodes revealed as player progresses

#### **Path Restrictions**
- **Minimum Path Length:** Ensure players can't skip too many layers
- **Required Nodes:** Force certain node types (shops, rest sites)
- **Exclusion Zones:** Prevent certain node types in specific areas
- **Distance Rules:** Shops/rest sites must be X nodes apart

#### **Risk/Reward Balance**
- **High Risk Paths:** More elites, harder combats, better rewards
- **Safe Paths:** More rest sites, easier combats, modest rewards
- **Optimal Path Problem:** Skilled players identify best risk/reward routes

---

### 1.4 Visual Representation

#### **Vertical Spire (Slay the Spire)**
```
                  [BOSS]
                    |
         [E] [C] [?] [R] [C]     Layer 15
          |   |   |   |   |
         [C] [?] [S] [C] [E]     Layer 14
          |   |   |   |   |
         .....................
          |   |   |   |   |
         [C] [C] [?] [C] [C]     Layer 2
          |   |   |   |   |
              [START]            Layer 1
```

**Visual Elements:**
- Nodes represented as icons (combat, shop, etc.)
- Lines show available connections
- Current node highlighted
- Completed nodes greyed out
- Path history shows route taken

#### **Circular Ring (Monster Train)**
```
     [Ring 8: BOSS]
            |
    [Ring 7: Elite]
            |
    [Ring 6: Shop]
            |
    [Ring 5: Event]
            |
     ...continues...
```

**Visual Elements:**
- Circular track or linear progression bar
- Current ring highlighted
- Next ring previewed
- Progress indicator (ring X of 8)

#### **Hub & Spoke (Griftlands)**
```
    [Mission A] -- [Hub 1] -- [Mission B]
                      |
                  [Mission C]
```

**Visual Elements:**
- Central hub with radiating missions
- Mission status (available, completed, locked)
- Story progression indicators
- Character/faction relationships

---

## 2. HeroScript Current Implementation

### 2.1 Existing Code Analysis

#### **Current Structure (`src/Core/Run/`)**

**RunDefinition.cs:**
```csharp
public sealed record RunDefinition
{
    public string RunId { get; init; } = "default_run";
    public int StartingGold { get; init; }
    public int StartingPowerPoints { get; init; }
    public int StartingHandSize { get; init; } = 5;
    public string CombatActivationRulesId { get; init; } = "default_activation";
    public List<string> StartingDeck { get; init; } = new();
    public List<RunMapNodeDefinition> MapNodes { get; init; } = new();
    public Dictionary<string, object> Metadata { get; init; } = new();
}
```

**RunMapNodeDefinition.cs:**
```csharp
public sealed record RunMapNodeDefinition
{
    public string NodeId { get; init; } = string.Empty;
    public string NodeType { get; init; } = "combat";
    public List<string> NextNodeIds { get; init; } = new();
    public Dictionary<string, object> Metadata { get; init; } = new();
}
```

**Current JSON Example (`data/configs/default/Resources/runs/default_run.json`):**
```json
{
  "default_run": {
    "runId": "default_run",
    "startingGold": 50,
    "startingPowerPoints": 0,
    "startingHandSize": 5,
    "combatActivationRulesId": "default_activation",
    "startingDeck": [
      "basic_attack", "basic_attack", "basic_attack", "basic_attack", "basic_attack",
      "defend", "defend", "defend", "fireball", "heal"
    ],
    "mapNodes": [
      {
        "nodeId": "start",
        "nodeType": "combat",
        "nextNodeIds": ["reward_1"]
      },
      {
        "nodeId": "reward_1",
        "nodeType": "card_selection",
        "nextNodeIds": ["shop_1"]
      },
      {
        "nodeId": "shop_1",
        "nodeType": "shop",
        "nextNodeIds": ["boss_1"]
      },
      {
        "nodeId": "boss_1",
        "nodeType": "combat",
        "nextNodeIds": []
      }
    ],
    "metadata": {
      "description": "Default data-driven run definition for MVP loop"
    }
  }
}
```

### 2.2 Current Capabilities

**What Exists:**
- ✅ Basic run state management (`RunState`, `DeckState`)
- ✅ Linear node progression (simple linked list)
- ✅ Node type definitions (combat, shop, card_selection, preparation)
- ✅ Run lifecycle (start, state query, deck management)
- ✅ Integration with combat, shops, card selection, preparation
- ✅ Gold and Power Points economy
- ✅ Data-driven run definitions via JSON
- ✅ Snapshot/undo system for time-travel

**What's Missing:**
- ❌ Map generation algorithms (all maps are hand-authored)
- ❌ Layered/branching topology support
- ❌ Path selection mechanics (player can't choose between paths)
- ❌ Node type distribution configuration
- ❌ Visual map representation data
- ❌ Map validation (dead ends, reachability)
- ❌ Procedural generation parameters
- ❌ Seeded generation for reproducibility
- ❌ Layer-based constraints
- ❌ Path history tracking
- ❌ Map query API (get available next nodes, preview paths)

---

## 3. Gap Analysis & Recommendations

### 3.1 Critical Missing Features

#### **1. Map Generation Algorithm**
**Current State:** All maps are hand-authored JSON
**Needed:** Procedural generation system
**Priority:** HIGH

**Recommendation:** Implement Slay the Spire-style layered generation first (most popular, well-balanced)

**Benefits:**
- High replayability
- Easy to tune difficulty progression
- Clear visual representation
- Supports strategic path planning

#### **2. Path Selection Mechanics**
**Current State:** Linear progression only (no branching)
**Needed:** Player chooses between multiple available next nodes
**Priority:** HIGH

**Recommendation:** Add `availableNextNodes` calculation and `/advance` endpoint

#### **3. Map Visualization Data**
**Current State:** No coordinate or layer information
**Needed:** X/Y coordinates or layer numbers for UI rendering
**Priority:** MEDIUM

**Recommendation:** Add `layer` and `position` fields to `RunMapNodeDefinition`

#### **4. Node Distribution Configuration**
**Current State:** Manual node placement
**Needed:** Data-driven node type weights and constraints
**Priority:** HIGH

**Recommendation:** Add map generation configuration schema

#### **5. Map Validation**
**Current State:** No validation of hand-authored maps
**Needed:** Detect dead ends, unreachable nodes, invalid connections
**Priority:** MEDIUM

**Recommendation:** Add validation service that runs on map load

---

### 3.2 Recommended Architecture

#### **New Components Needed**

```
src/Core/Run/
├── Map/
│   ├── IMapGenerator.cs              # Interface for generation strategies
│   ├── LayeredMapGenerator.cs        # Slay the Spire-style generator
│   ├── LinearMapGenerator.cs         # Monster Train-style generator
│   ├── MapGenerationConfig.cs        # Configuration model
│   ├── MapValidator.cs               # Validation logic
│   ├── NodeDistributor.cs            # Node type distribution
│   └── PathfindingService.cs         # Reachability & path queries
```

#### **Enhanced Models**

```csharp
// Enhanced RunMapNodeDefinition
public sealed record RunMapNodeDefinition
{
    public string NodeId { get; init; } = string.Empty;
    public string NodeType { get; init; } = "combat";
    public int Layer { get; init; }  // NEW: Layer number (0-based)
    public int Position { get; init; }  // NEW: Position within layer
    public List<string> NextNodeIds { get; init; } = new();
    public List<string> PreviousNodeIds { get; init; } = new();  // NEW: Backward references
    public NodeStatus Status { get; init; } = NodeStatus.Locked;  // NEW: Available, Completed, Locked
    public Dictionary<string, object> Metadata { get; init; } = new();
}

// NEW: Map generation configuration
public sealed record MapGenerationConfig
{
    public string GeneratorType { get; init; } = "layered";  // "layered", "linear", "hub"
    public int LayerCount { get; init; } = 15;
    public int NodesPerLayerMin { get; init; } = 3;
    public int NodesPerLayerMax { get; init; } = 7;
    public int ConnectionsPerNodeMin { get; init; } = 1;
    public int ConnectionsPerNodeMax { get; init; } = 3;
    public Dictionary<string, LayerNodeWeights> NodeWeightsByLayer { get; init; } = new();
    public List<NodeConstraint> Constraints { get; init; } = new();
    public int? Seed { get; init; }  // For reproducible generation
}

public sealed record LayerNodeWeights
{
    public float Combat { get; init; } = 0.6f;
    public float Elite { get; init; } = 0.1f;
    public float Shop { get; init; } = 0.1f;
    public float Rest { get; init; } = 0.1f;
    public float Event { get; init; } = 0.1f;
    public float Treasure { get; init; } = 0.0f;
}

public sealed record NodeConstraint
{
    public string NodeType { get; init; } = string.Empty;
    public int? MinLayer { get; init; }
    public int? MaxLayer { get; init; }
    public int? MinSpacing { get; init; }  // Minimum nodes between occurrences
    public int? MaxPerRun { get; init; }
}
```

---

### 3.3 Implementation Phases

#### **Phase 1: Foundation (Week 1-2)**
- Add `Layer` and `Position` fields to `RunMapNodeDefinition`
- Implement `MapValidator` for existing hand-authored maps
- Add `GET /api/run/{runId}/map` endpoint
- Add `GET /api/run/{runId}/available-nodes` endpoint
- Add `POST /api/run/{runId}/advance` endpoint with node selection

#### **Phase 2: Linear Generator (Week 2-3)**
- Implement `LinearMapGenerator` (Monster Train style)
- Add `MapGenerationConfig` model
- Load generation config from JSON
- Generate maps on run start based on config

#### **Phase 3: Layered Generator (Week 3-5)**
- Implement `LayeredMapGenerator` (Slay the Spire style)
- Node type distribution by layer
- Connection generation with constraints
- Ensure all nodes are reachable

#### **Phase 4: Advanced Features (Week 5-6)**
- Seeded generation for reproducibility
- Node constraint system (spacing, layer restrictions)
- Path preview (show N layers ahead)
- Map difficulty tuning

---

## 4. Data-Driven Configuration Structure

### 4.1 Recommended JSON Schema

#### **Map Generation Config Schema**

```json
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "title": "MapGenerationConfig",
  "type": "object",
  "properties": {
    "mapId": { "type": "string" },
    "generatorType": {
      "type": "string",
      "enum": ["layered", "linear", "hub", "grid"]
    },
    "seed": { "type": "integer", "nullable": true },
    "layeredConfig": { "$ref": "#/definitions/LayeredConfig" },
    "linearConfig": { "$ref": "#/definitions/LinearConfig" },
    "hubConfig": { "$ref": "#/definitions/HubConfig" }
  },
  "definitions": {
    "LayeredConfig": {
      "type": "object",
      "properties": {
        "layerCount": { "type": "integer", "minimum": 5, "maximum": 30 },
        "nodesPerLayer": {
          "type": "object",
          "properties": {
            "min": { "type": "integer", "minimum": 1 },
            "max": { "type": "integer", "maximum": 10 }
          }
        },
        "connectionsPerNode": {
          "type": "object",
          "properties": {
            "min": { "type": "integer", "minimum": 1 },
            "max": { "type": "integer", "maximum": 5 }
          }
        },
        "nodeWeightsByLayer": {
          "type": "object",
          "additionalProperties": {
            "$ref": "#/definitions/NodeWeights"
          }
        },
        "constraints": {
          "type": "array",
          "items": { "$ref": "#/definitions/NodeConstraint" }
        }
      }
    },
    "NodeWeights": {
      "type": "object",
      "properties": {
        "combat": { "type": "number", "minimum": 0, "maximum": 1 },
        "elite": { "type": "number", "minimum": 0, "maximum": 1 },
        "shop": { "type": "number", "minimum": 0, "maximum": 1 },
        "rest": { "type": "number", "minimum": 0, "maximum": 1 },
        "event": { "type": "number", "minimum": 0, "maximum": 1 },
        "treasure": { "type": "number", "minimum": 0, "maximum": 1 }
      }
    },
    "NodeConstraint": {
      "type": "object",
      "properties": {
        "nodeType": { "type": "string" },
        "minLayer": { "type": "integer", "nullable": true },
        "maxLayer": { "type": "integer", "nullable": true },
        "minSpacing": { "type": "integer", "nullable": true },
        "maxPerRun": { "type": "integer", "nullable": true },
        "requiredLayers": {
          "type": "array",
          "items": { "type": "integer" }
        }
      }
    }
  }
}
```

---

### 4.2 Example Configuration Files

#### **Example 1: Slay the Spire-style (Act 1)**

**File:** `data/configs/default/Resources/maps/act1_layered.json`

```json
{
  "act1_layered": {
    "mapId": "act1_layered",
    "generatorType": "layered",
    "seed": null,
    "layeredConfig": {
      "layerCount": 15,
      "nodesPerLayer": {
        "min": 4,
        "max": 7
      },
      "connectionsPerNode": {
        "min": 1,
        "max": 3
      },
      "nodeWeightsByLayer": {
        "layers_1-5": {
          "combat": 0.65,
          "event": 0.25,
          "treasure": 0.10,
          "elite": 0.0,
          "shop": 0.0,
          "rest": 0.0
        },
        "layers_6-10": {
          "combat": 0.50,
          "elite": 0.15,
          "event": 0.15,
          "shop": 0.10,
          "rest": 0.10,
          "treasure": 0.0
        },
        "layers_11-15": {
          "combat": 0.40,
          "elite": 0.25,
          "event": 0.15,
          "shop": 0.10,
          "rest": 0.10,
          "treasure": 0.0
        }
      },
      "constraints": [
        {
          "nodeType": "shop",
          "minLayer": 5,
          "maxLayer": 14,
          "requiredLayers": [6, 12],
          "minSpacing": 5
        },
        {
          "nodeType": "rest",
          "minLayer": 5,
          "maxLayer": 14,
          "requiredLayers": [8, 14],
          "minSpacing": 6
        },
        {
          "nodeType": "treasure",
          "minLayer": 1,
          "maxLayer": 6,
          "maxPerRun": 2
        },
        {
          "nodeType": "elite",
          "minLayer": 6,
          "maxPerRun": 10
        }
      ],
      "bossNode": {
        "nodeType": "boss",
        "layer": 16,
        "bossPool": "act1_bosses"
      }
    }
  }
}
```

---

#### **Example 2: Monster Train-style (Linear)**

**File:** `data/configs/default/Resources/maps/standard_linear.json`

```json
{
  "standard_linear": {
    "mapId": "standard_linear",
    "generatorType": "linear",
    "seed": null,
    "linearConfig": {
      "ringCount": 8,
      "ringTemplates": [
        {
          "position": 1,
          "type": "combat",
          "difficulty": "easy",
          "enemyPool": "ring1_enemies"
        },
        {
          "position": 2,
          "type": "random",
          "pool": ["combat", "event"],
          "weights": { "combat": 0.7, "event": 0.3 }
        },
        {
          "position": 3,
          "type": "shop",
          "shopId": "standard_shop"
        },
        {
          "position": 4,
          "type": "combat",
          "difficulty": "medium",
          "enemyPool": "ring4_enemies"
        },
        {
          "position": 5,
          "type": "event",
          "eventPool": "mid_run_events"
        },
        {
          "position": 6,
          "type": "elite",
          "difficulty": "hard",
          "enemyPool": "elite_enemies"
        },
        {
          "position": 7,
          "type": "rest"
        },
        {
          "position": 8,
          "type": "boss",
          "bossPool": "final_bosses"
        }
      ],
      "randomEventChance": 0.1,
      "difficultyProgression": "linear"
    }
  }
}
```

---

#### **Example 3: Short Tutorial Run**

**File:** `data/configs/default/Resources/maps/tutorial_run.json`

```json
{
  "tutorial_run": {
    "mapId": "tutorial_run",
    "generatorType": "linear",
    "seed": null,
    "linearConfig": {
      "ringCount": 5,
      "ringTemplates": [
        {
          "position": 1,
          "type": "combat",
          "difficulty": "tutorial",
          "enemyPool": "tutorial_enemy1",
          "metadata": { "tutorialStep": "basic_combat" }
        },
        {
          "position": 2,
          "type": "card_selection",
          "metadata": { "tutorialStep": "learn_card" }
        },
        {
          "position": 3,
          "type": "shop",
          "shopId": "tutorial_shop",
          "metadata": { "tutorialStep": "shop_intro", "freeGold": 100 }
        },
        {
          "position": 4,
          "type": "combat",
          "difficulty": "tutorial",
          "enemyPool": "tutorial_enemy2",
          "metadata": { "tutorialStep": "advanced_combat" }
        },
        {
          "position": 5,
          "type": "boss",
          "bossPool": "tutorial_boss",
          "metadata": { "tutorialStep": "boss_fight" }
        }
      ]
    }
  }
}
```

---

### 4.3 Integration with Existing Systems

#### **RunDefinition.json Enhancement**

Current structure remains compatible. Add optional `mapGenerationConfig` field:

```json
{
  "default_run": {
    "runId": "default_run",
    "startingGold": 50,
    "startingPowerPoints": 0,
    "startingHandSize": 5,
    "combatActivationRulesId": "default_activation",
    "startingDeck": ["basic_attack", "defend", "fireball", "heal"],

    "mapGenerationConfig": "act1_layered",

    "mapNodes": [],

    "metadata": {
      "description": "Uses procedural generation instead of hand-authored nodes"
    }
  }
}
```

**Migration Path:**
- If `mapGenerationConfig` is null → use hand-authored `mapNodes` (backward compatible)
- If `mapGenerationConfig` is set → generate map procedurally, ignore `mapNodes`

---

## 5. Implementation Roadmap

### 5.1 Milestone 1: Foundation (2 weeks)

**Goal:** Add map query capabilities to existing system

**Tasks:**
1. Add `Layer` and `Position` to `RunMapNodeDefinition`
2. Add `Status` enum (Available, Locked, Completed, Current)
3. Implement `MapValidator.ValidateMap()`
4. Add `GET /api/run/{runId}/map` endpoint
5. Add `GET /api/run/{runId}/available-nodes` endpoint
6. Add `POST /api/run/{runId}/advance` with node selection
7. Update `RunState` to track completed nodes

**Acceptance Criteria:**
- Hand-authored maps work with new fields
- Player can query available next nodes
- Player can choose between multiple paths
- API returns validation errors for broken maps

**Files to Create/Modify:**
```
src/Core/Run/RunMapNodeDefinition.cs (modify)
src/Core/Run/Map/MapValidator.cs (new)
src/Core/Run/Map/IMapQuery.cs (new)
src/Core/Run/Map/MapQueryService.cs (new)
src/API/Controllers/RunController.cs (modify)
tests/Core.Tests/Run/MapValidatorTests.cs (new)
tests/API.Tests/Integration/MapNavigationTests.cs (new)
```

---

### 5.2 Milestone 2: Linear Generator (2 weeks)

**Goal:** Implement Monster Train-style linear map generation

**Tasks:**
1. Create `MapGenerationConfig` model
2. Create `IMapGenerator` interface
3. Implement `LinearMapGenerator`
4. Load generation config from JSON
5. Generate map on run start if config specified
6. Add unit tests for generation logic

**Acceptance Criteria:**
- Can generate 8-ring linear maps
- Node types match template configuration
- Generated maps pass validation
- Reproducible with seed

**Files to Create:**
```
src/Core/Run/Map/MapGenerationConfig.cs (new)
src/Core/Run/Map/IMapGenerator.cs (new)
src/Core/Run/Map/LinearMapGenerator.cs (new)
src/Core/Run/Map/MapGenerationService.cs (new)
data/configs/default/Resources/maps/standard_linear.json (new)
tests/Core.Tests/Run/Map/LinearMapGeneratorTests.cs (new)
```

---

### 5.3 Milestone 3: Layered Generator (3 weeks)

**Goal:** Implement Slay the Spire-style branching maps

**Tasks:**
1. Implement `LayeredMapGenerator`
2. Implement node type distribution by layer
3. Implement connection generation algorithm
4. Add constraint system (spacing, required layers)
5. Ensure graph connectivity (no orphaned nodes)
6. Add extensive testing

**Acceptance Criteria:**
- Can generate 15-layer branching maps
- All nodes are reachable from start
- Boss node reachable from all final layer nodes
- Node type distribution matches config
- Constraints respected (shops at correct layers, etc.)
- No dead ends or impossible states

**Algorithm Pseudocode:**
```csharp
public List<RunMapNodeDefinition> Generate(MapGenerationConfig config)
{
    var nodes = new List<RunMapNodeDefinition>();
    var rng = new Random(config.Seed ?? Environment.TickCount);

    // Layer 0: Start node
    nodes.Add(CreateNode("start", 0, 0, "combat"));

    // Layers 1 to N
    for (int layer = 1; layer <= config.LayerCount; layer++)
    {
        int nodeCount = rng.Next(config.NodesPerLayerMin, config.NodesPerLayerMax + 1);
        var layerWeights = GetWeightsForLayer(layer, config);

        for (int pos = 0; pos < nodeCount; pos++)
        {
            string nodeType = SelectNodeType(layerWeights, layer, config.Constraints, rng);
            var node = CreateNode($"L{layer}_N{pos}", layer, pos, nodeType);
            nodes.Add(node);
        }
    }

    // Boss node
    nodes.Add(CreateNode("boss", config.LayerCount + 1, 0, "boss"));

    // Generate connections
    ConnectLayers(nodes, config, rng);

    // Validate
    ValidateConnectivity(nodes);

    return nodes;
}

private void ConnectLayers(List<RunMapNodeDefinition> nodes, Config config, Random rng)
{
    for (int layer = 1; layer <= config.LayerCount + 1; layer++)
    {
        var currentLayer = nodes.Where(n => n.Layer == layer).ToList();
        var previousLayer = nodes.Where(n => n.Layer == layer - 1).ToList();

        // Each node in current layer connects to 1-3 nodes in previous layer
        foreach (var node in currentLayer)
        {
            int connectionCount = rng.Next(config.ConnectionsPerNodeMin,
                                          config.ConnectionsPerNodeMax + 1);
            var targets = previousLayer.OrderBy(_ => rng.Next()).Take(connectionCount);

            foreach (var target in targets)
            {
                node.PreviousNodeIds.Add(target.NodeId);
                target.NextNodeIds.Add(node.NodeId);
            }
        }

        // Ensure all previous layer nodes have at least 1 forward connection
        foreach (var prevNode in previousLayer)
        {
            if (!prevNode.NextNodeIds.Any())
            {
                var randomNext = currentLayer[rng.Next(currentLayer.Count)];
                prevNode.NextNodeIds.Add(randomNext.NodeId);
                randomNext.PreviousNodeIds.Add(prevNode.NodeId);
            }
        }
    }
}
```

**Files to Create:**
```
src/Core/Run/Map/LayeredMapGenerator.cs (new)
src/Core/Run/Map/NodeDistributor.cs (new)
src/Core/Run/Map/GraphConnector.cs (new)
data/configs/default/Resources/maps/act1_layered.json (new)
tests/Core.Tests/Run/Map/LayeredMapGeneratorTests.cs (new)
tests/Core.Tests/Run/Map/GraphConnectivityTests.cs (new)
```

---

### 5.4 Milestone 4: Advanced Features (2 weeks)

**Goal:** Polish and advanced capabilities

**Tasks:**
1. Seeded generation (reproducible maps)
2. Constraint system refinement
3. Path preview API (show N layers ahead)
4. Map difficulty tuning
5. Performance optimization
6. Documentation and examples

**Files to Create:**
```
docs/systems/run/map-generation-guide.md (new)
data/configs/default/Resources/maps/hard_mode.json (new)
data/configs/default/Resources/maps/endless_mode.json (new)
```

---

## 6. Summary & Next Steps

### What We Learned

**Industry Patterns:**
1. **Layered/Branching** (Slay the Spire) is most popular for strategic depth
2. **Linear** (Monster Train) works for shorter, faster runs
3. **Hub & Spoke** (Griftlands) best for story integration
4. **Grid/Free Roam** rarely used in card games (too complex)

**Node Types:**
- Combat (60-70% of nodes)
- Elite (10-15%, optional high-risk)
- Boss (act endings)
- Shop (every 5-7 layers)
- Rest (every 6-8 layers)
- Event/Unknown (15-20%, high variance)
- Treasure (5-10%, pure reward)

**Configuration Principles:**
- All generation parameters should be JSON-driven
- Support both hand-authored and procedural maps
- Validate connectivity (no dead ends)
- Balance distribution by layer
- Seed for reproducibility

### HeroScript Current State

**Strengths:**
- Solid foundation with `RunDefinition` and `RunMapNodeDefinition`
- Good separation of concerns (Run, Combat, Shop, CardSelection)
- Data-driven philosophy already established
- JSON configuration pipeline ready

**Gaps:**
- No map generation (only hand-authored)
- No path selection (linear only)
- No map visualization data (layers, positions)
- No validation

### Recommended Next Actions

**Priority 1 (Do First):**
1. Add map query/navigation API (Milestone 1)
2. Implement linear generator (Milestone 2)
3. Test with existing systems

**Priority 2 (After MVP validated):**
1. Implement layered generator (Milestone 3)
2. Add constraint system
3. Performance testing

**Priority 3 (Polish):**
1. Advanced features (Milestone 4)
2. Multiple acts with different configs
3. Daily challenge / seeded runs

---

**Document Version:** 1.0
**Last Updated:** 2026-07-08
**Authors:** HeroScript Development Team
**Status:** Research Complete - Ready for Implementation Planning

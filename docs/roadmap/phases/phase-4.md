# Fase 4 - Conteúdo MVP

**Status:** 📋 Planejado  
**Dependências:** Fase 1 (Combat), Fase 2 (Status, Modifiers), Fase 3 (Run, Shop)

---

## Visão Geral

A Fase 4 adiciona o conteúdo jogável necessário para o MVP: raças, poderes, companions, e inimigos. Esta fase transforma os sistemas abstratos em experiências concretas de jogo, fornecendo os dados que alimentam todos os sistemas anteriores.

## APIs Planejadas

### 1. Race API

Raças jogáveis com bônus e características únicas.

**Endpoints:**
- `GET /api/races` - Lista raças disponíveis
- `GET /api/races/{name}` - Obtém detalhes de raça específica
- `GET /api/races/{name}/bonuses` - Obtém bônus raciais
- `GET /api/races/{name}/starting-deck` - Obtém deck inicial da raça

**Raças MVP (3 raças):**
- **Human** - Versátil, bônus em ouro
- **Orc** - Focado em dano físico e crítico
- **Elf** - Focado em magia e energia

**Recursos:**
- Bônus raciais (stats, descontos, energia inicial)
- Deck inicial customizado por raça
- Desbloqueio de poderes específicos
- Lore e descrição

### 2. Power API

Poderes (root e derived) com árvores de derivação.

**Endpoints:**
- `GET /api/powers` - Lista poderes disponíveis
- `GET /api/powers/{name}` - Obtém detalhes de poder
- `GET /api/powers/{name}/tree` - Obtém árvore de derivados
- `GET /api/powers/{name}/combos` - Verifica combos com outros poderes
- `POST /api/powers/validate-combo` - Valida combo entre múltiplos poderes

**Estrutura de Poderes:**
- **Root Powers** - Poderes base (15+ raízes planejadas)
- **Derived Powers** - Variações de root (5 derivados por raiz)
- **Tags** - fire, ice, lightning, physical, crit, dot, aoe, single-target
- **Raridade** - Comum, Incomum, Raro, Lendário

**Poderes MVP (5 raízes × 5 derivados = 25 poderes):**
- Fireball (fire, aoe)
- Lightning Strike (lightning, single-target)
- Ice Shard (ice, control)
- Power Strike (physical, single-target)
- Whirlwind (physical, aoe)

**Recursos:**
- Sistema de combos (sinergia entre poderes)
- Scaling powers (crescem com uso)
- Árvore de derivação visual (futuro)
- Filtros por tag e raridade

### 3. Companion API

Companions com gambits e poderes únicos.

**Endpoints:**
- `GET /api/companions` - Lista companions disponíveis
- `GET /api/companions/{name}` - Obtém detalhes de companion
- `GET /api/companions/{name}/gambits` - Obtém gambits padrão
- `GET /api/companions/{name}/powers` - Obtém poderes do companion

**Companions MVP (2 companions):**
- **Wolf** - Focado em dano físico, gambit agressivo
- **Owl** - Focado em suporte, gambit defensivo

**Recursos:**
- 2 slots de poderes por companion
- Cooldowns individuais
- Gambits pré-configurados (customizáveis)
- Sincronia com tags de ações do herói

### 4. Enemy API

Inimigos com intents e comportamento.

**Endpoints:**
- `GET /api/enemies` - Lista inimigos disponíveis
- `GET /api/enemies/{name}` - Obtém detalhes de inimigo
- `GET /api/enemies/{name}/intent` - Obtém intent atual do inimigo
- `GET /api/enemies/by-tier` - Agrupa inimigos por tier (normal, elite, boss)

**Tipos de Inimigo:**
- **Normal** - Inimigos comuns
- **Elite** - Inimigos mais fortes
- **Boss** - Chefes de bioma

**Inimigos MVP:**
- 5 inimigos normais (Goblin, Skeleton, Bandit, Spider, Slime)
- 2 inimigos elite (Orc Warrior, Dark Mage)
- 1 boss (Goblin King)

**Recursos:**
- Sistema de intent (telegrafar próxima ação)
- Padrões de comportamento (agressivo, defensivo, suporte)
- Loot tables
- Scaling por progresso da run

---

## Estrutura de Dados

### Power Definition (JSON)

```json
{
  "name": "FIREBALL",
  "displayName": "Fireball",
  "type": "ROOT",
  "tags": ["fire", "aoe", "magic"],
  "rarity": "COMMON",
  "energyCost": 2,
  "baseDamage": 10,
  "damageFormula": "FIRE_DAMAGE",
  "targets": "AOE_SMALL",
  "description": "Launch a ball of fire that explodes on impact",
  "derivedPowers": [
    "FIREBALL_INFERNO",
    "FIREBALL_PRECISION",
    "FIREBALL_CHAIN",
    "FIREBALL_METEOR",
    "FIREBALL_PHOENIX"
  ],
  "combos": {
    "ICE_SHARD": "STEAM_EXPLOSION",
    "LIGHTNING_STRIKE": "PLASMA_BURST"
  }
}
```

### Race Definition (JSON)

```json
{
  "name": "HUMAN",
  "displayName": "Human",
  "description": "Versatile and adaptable",
  "bonuses": {
    "startingGold": 50,
    "shopDiscount": 0.1,
    "startingEnergy": 3
  },
  "startingDeck": [
    "BASIC_ATTACK",
    "BASIC_ATTACK",
    "BASIC_ATTACK",
    "POWER_STRIKE",
    "FIREBALL"
  ],
  "unlockedPowers": ["POWER_STRIKE", "FIREBALL", "ICE_SHARD"]
}
```

### Companion Definition (JSON)

```json
{
  "name": "WOLF",
  "displayName": "Wolf Companion",
  "description": "Fierce and loyal",
  "powers": [
    {
      "name": "BITE",
      "energyCost": 0,
      "baseDamage": 5,
      "cooldown": 2
    },
    {
      "name": "HOWL",
      "energyCost": 0,
      "effect": "BUFF_HERO_DAMAGE",
      "cooldown": 4
    }
  ],
  "defaultGambits": [
    {
      "priority": 1,
      "condition": {
        "type": "HERO_HP_BELOW",
        "threshold": 0.3
      },
      "action": {
        "type": "USE_POWER",
        "powerId": "HOWL"
      }
    },
    {
      "priority": 2,
      "condition": {
        "type": "ALWAYS"
      },
      "action": {
        "type": "USE_POWER",
        "powerId": "BITE"
      }
    }
  ]
}
```

### Enemy Definition (JSON)

```json
{
  "name": "GOBLIN",
  "displayName": "Goblin",
  "tier": "NORMAL",
  "hp": 20,
  "armor": 2,
  "behavior": "AGGRESSIVE",
  "intents": [
    {
      "type": "ATTACK",
      "damage": 5,
      "weight": 0.6
    },
    {
      "type": "DEFEND",
      "armor": 3,
      "weight": 0.3
    },
    {
      "type": "BUFF",
      "effect": "STRENGTH",
      "weight": 0.1
    }
  ],
  "loot": {
    "gold": [10, 20],
    "cardOffers": 1
  }
}
```

---

## Integração com Sistemas Anteriores

### Powers + Combat

Poderes são executados através do CombatSystem:
- Validação de custo de energia
- Resolução de alvos (single, aoe)
- Aplicação de dano via BucketPipeline
- Trigger de eventos para sincronia

### Powers + ScriptModifiers

Modificadores alteram comportamento de poderes:
- Go Again → permite usar outro poder
- Multi-Hit → executa poder múltiplas vezes
- Explosivo → transforma single-target em aoe

### Companions + Gambits

Companions reagem a eventos via gambits:
- Subscribe a eventos de combate
- Avaliam condições
- Executam ações automaticamente

### Enemies + Combat

Inimigos participam do combate:
- Intent system (telegrafar ação)
- Padrões de comportamento
- Loot após derrota

---

## Dependências

### Sistemas Core Necessários

- ✅ CombatSystem (Fase 1)
- ✅ BucketPipeline (Fase 1)
- ✅ StatusSystem (Fase 2)
- ✅ ScriptModifierSystem (Fase 2)
- ✅ GambitEngine (Fase 2)
- ✅ RunManager (Fase 3)
- ⏳ ContentLoader (implementar)
- ⏳ ComboValidator (implementar)

### Ordem de Implementação

1. **Content Core** (src/Core/Content/)
   - ContentDefinition, ContentLoader
   - Validation e schema

2. **Race System** (src/Core/Content/Races/)
   - RaceDefinition, RaceBonus
   - Starting deck logic

3. **Race API** (src/API/Controllers/RaceController.cs)
   - List races
   - Get details/bonuses

4. **Power System** (src/Core/Content/Powers/)
   - PowerDefinition, PowerTree
   - ComboValidator

5. **Power API** (src/API/Controllers/PowerController.cs)
   - List powers
   - Get tree/combos

6. **Companion System** (src/Core/Content/Companions/)
   - CompanionDefinition
   - Default gambits

7. **Companion API** (src/API/Controllers/CompanionController.cs)
   - List companions
   - Get details/gambits

8. **Enemy System** (src/Core/Content/Enemies/)
   - EnemyDefinition, IntentSystem
   - Behavior patterns

9. **Enemy API** (src/API/Controllers/EnemyController.cs)
   - List enemies
   - Get intent/details

---

## Exemplos de Uso

### Listar Raças

```http
GET /api/races
```

### Obter Árvore de Poder

```http
GET /api/powers/FIREBALL/tree
```

### Validar Combo

```http
POST /api/powers/validate-combo
Content-Type: application/json

{
  "powers": ["FIREBALL", "ICE_SHARD"]
}
```

### Obter Intent de Inimigo

```http
GET /api/enemies/GOBLIN/intent?combatId=combat-123&enemyId=goblin-1
```

---

## Próximos Passos

Após completar a Fase 4, a Fase 5 implementará persistência:
- **Save API** - Salvar/carregar runs
- **MetaProgression API** - Estatísticas e desbloqueios

Ver: [PHASE_5.md](PHASE_5.md)

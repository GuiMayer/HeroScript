# Effect System

## Visão Geral

O **Effect System** é a unidade fundamental de todas as ações em combate no HeroScript. Tudo que causa mudanças de estado em combate é representado como um **Effect**: dano, cura, modificadores, status, economia, cartas, e muito mais.

## Conceitos Fundamentais

### Effect como Unidade Fundamental

No HeroScript, **Effect** é a abstração central que unifica todas as ações:

- **Cartas** são compostas por Effects
- **Status** executam Effects a cada tick
- **Eventos especiais** aplicam Effects
- **Custos de ações** são Effects (MODIFY_RESOURCE com valor negativo)
- **Modificadores de run** (relíquias, poderes) alteram Effects

### Arquitetura

```
ActionDefinition (Carta)
  └─> Effects[] (lista de efeitos inline)
       ├─> EffectDefinition (tipo, valores, condições)
       └─> EffectModifiers[] (modificações aplicadas durante run)

StatusDefinition (Buff/Debuff/DoT)
  └─> Effects[] (efeitos executados a cada tick)

Event (Slay the Spire style)
  └─> Effects[] (efeitos do evento)
```

### Fluxo de Execução

```
1. Carta é jogada
   ↓
2. ActionDefinition.Effects[] é carregado
   ↓
3. Modificadores de run são aplicados (relíquias, poderes)
   ↓
4. Para cada Effect:
   ↓
5. EffectResolver determina pipeline apropriado
   ↓
6. Pipeline processa Effect (DamagePipeline, HealPipeline, etc.)
   ↓
7. Resultado é aplicado ao CombatState
   ↓
8. Eventos são publicados
```

## Tipos de Effects

### Recursos

- **DAMAGE**: Causa dano a um recurso (geralmente health)
- **HEAL**: Cura/restaura um recurso
- **MODIFY_RESOURCE**: Modifica qualquer recurso (energia, mana, stamina)

### Economia

- **GAIN_GOLD**: Ganha ouro
- **LOSE_GOLD**: Perde ouro
- **GAIN_PP**: Ganha Power Points
- **LOSE_PP**: Perde Power Points

### Status

- **APPLY_STATUS**: Aplica status (buff/debuff/DoT/HoT)
- **REMOVE_STATUS**: Remove status específico
- **DISPEL_STATUS**: Dispela tipos de status

### Cartas/Deck

- **DRAW_CARD**: Compra carta do deck
- **DISCARD_CARD**: Descarta carta da mão
- **EXHAUST_CARD**: Exausta carta (remove da run)
- **ADD_CARD_TO_HAND**: Adiciona carta específica à mão

### Modificadores

- **MODIFY_DAMAGE_DEALT**: Modifica dano causado
- **MODIFY_DAMAGE_TAKEN**: Modifica dano recebido
- **MODIFY_CRIT_CHANCE**: Modifica chance de crítico
- **MODIFY_CRIT_MULT**: Modifica multiplicador de crítico
- **MODIFY_COOLDOWNS**: Modifica cooldowns

### Controle

- **PREVENT_ACTIONS**: Impede ações (stun, silence)
- **FORCE_TARGET**: Força alvo específico (taunt)
- **SKIP_TURN**: Pula turno

### Utilidade

- **REFLECT_DAMAGE**: Reflete dano
- **ABSORB_DAMAGE**: Absorve dano (shield)
- **TRIGGER_EFFECT**: Dispara outro effect
- **CONDITIONAL_EFFECT**: Effect condicional

### Meta

- **MODIFY_EFFECT**: Modifica outro effect
- **COPY_EFFECT**: Copia effect de outra fonte

## Estrutura JSON

### EffectDefinition

```json
{
  "type": "DAMAGE",
  "target": "TARGET",
  "timing": "IMMEDIATE",
  "flatValue": 10,
  "formulaValue": "source_attack * 1.5",
  "isPercentage": false,
  "targetResource": "health",
  "condition": "target_hp < target_max_hp * 0.5",
  "requiredTags": ["fire"],
  "excludedTags": ["water"],
  "chance": 1.0,
  "repeat": 1,
  "tags": ["physical", "attack"],
  "chainedEffects": [],
  "conditionalEffects": []
}
```

### Campos Principais

- **type**: Tipo do efeito (ver lista acima)
- **target**: Alvo do efeito (SELF, TARGET, ALL_ENEMIES, etc.)
- **timing**: Quando executar (IMMEDIATE, DELAYED, ON_TURN_START, etc.)
- **flatValue**: Valor fixo
- **formulaValue**: Fórmula dinâmica (usa MathEngine)
- **isPercentage**: Se o valor é percentual
- **targetResource**: Recurso alvo (health, energy, mana)
- **condition**: Condição para executar (expressão booleana)
- **requiredTags**: Tags requeridas para executar
- **excludedTags**: Tags que impedem execução
- **chance**: Probabilidade de executar (0.0 a 1.0)
- **repeat**: Número de repetições
- **tags**: Tags para categorização
- **chainedEffects**: Efeitos disparados após este
- **conditionalEffects**: Efeitos condicionais

## Exemplos de Uso

### Carta Básica (Strike)

```json
{
  "actionId": "BASIC_ATTACK",
  "displayName": "Strike",
  "description": "Deal 6 damage",
  "actionType": "ATTACK",
  "costs": {
    "resources": [{"resourceId": "energy", "amount": 1}]
  },
  "effects": [
    {
      "type": "DAMAGE",
      "target": "TARGET",
      "timing": "IMMEDIATE",
      "flatValue": 6,
      "targetResource": "health",
      "tags": ["physical", "attack"]
    }
  ],
  "tags": ["attack", "common"]
}
```

### Carta com Múltiplos Effects (Fireball)

```json
{
  "actionId": "FIREBALL",
  "displayName": "Fireball",
  "description": "Deal 10 fire damage. Apply 2 stacks of Burn.",
  "effects": [
    {
      "type": "DAMAGE",
      "target": "TARGET",
      "flatValue": 10,
      "tags": ["fire", "magic"]
    },
    {
      "type": "APPLY_STATUS",
      "target": "TARGET",
      "statusId": "BURN",
      "statusStacks": 2
    }
  ]
}
```

### Carta Condicional

```json
{
  "actionId": "CONDITIONAL_HEAL",
  "displayName": "Emergency Heal",
  "description": "If HP < 50%, heal 15. Otherwise, heal 5.",
  "effects": [
    {
      "type": "HEAL",
      "target": "SELF",
      "flatValue": 15,
      "condition": "target_hp < target_max_hp * 0.5"
    },
    {
      "type": "HEAL",
      "target": "SELF",
      "flatValue": 5,
      "condition": "target_hp >= target_max_hp * 0.5"
    }
  ]
}
```

### Carta AoE (Whirlwind)

```json
{
  "actionId": "WHIRLWIND",
  "displayName": "Whirlwind",
  "description": "Deal 5 damage to ALL enemies",
  "effects": [
    {
      "type": "DAMAGE",
      "target": "ALL_ENEMIES",
      "flatValue": 5,
      "tags": ["physical", "aoe"]
    }
  ]
}
```

### Carta com Repetição

```json
{
  "actionId": "DRAW_CARDS",
  "displayName": "Preparation",
  "description": "Draw 2 cards",
  "effects": [
    {
      "type": "DRAW_CARD",
      "target": "SELF",
      "repeat": 2
    }
  ]
}
```

## Effect Modifiers

Modificadores alteram Effects durante a run (relíquias, poderes, status).

### Tipos de Modificadores

- **MULTIPLY_VALUE**: Multiplica valor do effect
- **ADD_VALUE**: Adiciona valor ao effect
- **CHANGE_TYPE**: Muda tipo do effect (dano → cura)
- **CHANGE_TARGET**: Muda alvo do effect
- **ADD_TAGS**: Adiciona tags ao effect
- **REMOVE_TAGS**: Remove tags do effect
- **MULTIPLY_CHANCE**: Multiplica chance de execução
- **ADD_REPEAT**: Adiciona repetições
- **CHAIN_EFFECT**: Adiciona effect encadeado

### Exemplo: Relíquia "Pen Nib" (Slay the Spire)

```json
{
  "relicId": "PEN_NIB",
  "displayName": "Pen Nib",
  "description": "Every 10th attack deals double damage",
  "effectModifiers": [
    {
      "type": "MULTIPLY_VALUE",
      "valueMultiplier": 2.0,
      "requiredTags": ["attack"],
      "condition": "attack_count % 10 == 0"
    }
  ]
}
```

### Exemplo: Poder "Demon Form"

```json
{
  "powerId": "DEMON_FORM",
  "displayName": "Demon Form",
  "description": "Increases attack damage by 50%",
  "effectModifiers": [
    {
      "type": "MULTIPLY_VALUE",
      "valueMultiplier": 1.5,
      "requiredTags": ["attack"],
      "isPermanent": false,
      "duration": 3
    }
  ]
}
```

## Alvos (EffectTarget)

- **SELF**: Quem executou a ação
- **TARGET**: Alvo selecionado
- **ALL_ENEMIES**: Todos os inimigos
- **ALL_ALLIES**: Todos os aliados
- **RANDOM_ENEMY**: Inimigo aleatório
- **LOWEST_HP_ENEMY**: Inimigo com menor HP
- **HIGHEST_HP_ENEMY**: Inimigo com maior HP

## Timing (EffectTiming)

- **IMMEDIATE**: Executa imediatamente
- **DELAYED**: Executa após X turnos
- **ON_TURN_START**: Executa no início do turno
- **ON_TURN_END**: Executa no fim do turno
- **ON_DAMAGE_DEALT**: Executa ao causar dano
- **ON_DAMAGE_TAKEN**: Executa ao receber dano

## Condições e Fórmulas

Effects suportam condições e fórmulas dinâmicas via MathEngine.

### Variáveis Disponíveis

- **source_hp**: HP atual da fonte
- **source_max_hp**: HP máximo da fonte
- **source_energy**: Energia atual da fonte
- **target_hp**: HP atual do alvo
- **target_max_hp**: HP máximo do alvo
- **target_energy**: Energia atual do alvo
- **stacks**: Número de stacks (para status)

### Exemplos de Condições

```json
"condition": "target_hp < target_max_hp * 0.5"  // HP < 50%
"condition": "source_energy >= 3"                // Energia >= 3
"condition": "stacks >= 5"                       // 5+ stacks
```

### Exemplos de Fórmulas

```json
"formulaValue": "source_attack * 1.5"           // 150% do ataque
"formulaValue": "target_max_hp * 0.1"           // 10% do HP máximo
"formulaValue": "stacks * 3"                    // 3 por stack
```

## Integração com Status System

Status executam Effects a cada tick. O StatusManager delega a execução ao EffectResolver.

### Exemplo: Poison (DoT)

```json
{
  "statusId": "POISON",
  "displayName": "Poison",
  "description": "Lose 3 HP at the start of each turn",
  "type": "DOT",
  "baseDuration": 3,
  "maxStacks": 10,
  "timings": ["TURN_START"],
  "effects": [
    {
      "type": "DAMAGE",
      "target": "SELF",
      "flatValue": 3,
      "targetResource": "health",
      "tags": ["poison", "dot"]
    }
  ]
}
```

### Exemplo: Regeneration (HoT)

```json
{
  "statusId": "REGENERATION",
  "displayName": "Regeneration",
  "description": "Heal 5 HP at the start of each turn",
  "type": "HOT",
  "baseDuration": 3,
  "timings": ["TURN_START"],
  "effects": [
    {
      "type": "HEAL",
      "target": "SELF",
      "flatValue": 5,
      "targetResource": "health",
      "tags": ["heal", "hot"]
    }
  ]
}
```

### Exemplo: Strength (Modificador)

```json
{
  "statusId": "STRENGTH",
  "displayName": "Strength",
  "description": "Increases damage dealt by 25% per stack",
  "type": "BUFF",
  "baseDuration": 3,
  "maxStacks": 5,
  "effects": [
    {
      "type": "MODIFY_DAMAGE_DEALT",
      "target": "SELF",
      "modifierKey": "damage_multiplier",
      "modifierValue": 1.25,
      "tags": ["buff", "damage"]
    }
  ]
}
```

## Eventos

O EffectResolver publica eventos para todos os sistemas:

- **EffectExecutedEvent**: Quando um effect é executado
- **EffectModifiedEvent**: Quando modificadores são aplicados
- **EffectChainedEvent**: Quando effects encadeados são disparados

## API Programática

### IEffectResolver

```csharp
public interface IEffectResolver
{
    // Execução
    Result<EffectResult> ResolveEffect(EffectInstance effect, CombatState state);
    Result<List<EffectResult>> ResolveEffects(List<EffectInstance> effects, CombatState state);
    
    // Modificação
    EffectDefinition ApplyModifiers(EffectDefinition definition, List<EffectModifier> modifiers);
    EffectInstance CreateEffectInstance(EffectDefinition definition, string sourceId, string targetId);
    
    // Validação
    Result<bool> CanExecuteEffect(EffectInstance effect, CombatState state);
    Result<bool> ValidateDefinition(EffectDefinition definition);
    
    // Query
    List<EffectModifier> GetActiveModifiers(string entityId, EffectType? filterType = null);
}
```

### Exemplo de Uso

```csharp
// Criar effect
var effectDef = new EffectDefinition
{
    Type = EffectType.DAMAGE,
    Target = EffectTarget.TARGET,
    FlatValue = 10,
    Tags = new List<string> { "physical", "attack" }
};

// Criar instância
var effect = effectResolver.CreateEffectInstance(effectDef, heroId, enemyId);

// Aplicar modificadores (relíquias, poderes)
var modifiers = effectResolver.GetActiveModifiers(heroId, EffectType.DAMAGE);
effect = effect with { AppliedModifiers = modifiers };

// Executar
var result = effectResolver.ResolveEffect(effect, combatState);
```

## Boas Práticas

### 1. Use Tags para Filtros

Tags permitem que modificadores sejam aplicados seletivamente:

```json
{
  "type": "DAMAGE",
  "flatValue": 10,
  "tags": ["physical", "attack", "fire"]
}
```

Modificador que afeta apenas ataques físicos:

```json
{
  "type": "MULTIPLY_VALUE",
  "valueMultiplier": 1.5,
  "requiredTags": ["physical", "attack"]
}
```

### 2. Use Condições para Lógica Complexa

Em vez de criar múltiplas cartas, use condições:

```json
{
  "type": "DAMAGE",
  "formulaValue": "target_hp < target_max_hp * 0.5 ? 20 : 10"
}
```

### 3. Use Effects Encadeados para Combos

```json
{
  "type": "DAMAGE",
  "flatValue": 10,
  "chainedEffects": [
    {
      "type": "APPLY_STATUS",
      "statusId": "VULNERABLE",
      "statusStacks": 1
    }
  ]
}
```

### 4. Use Repeat para Múltiplos Hits

```json
{
  "type": "DAMAGE",
  "flatValue": 3,
  "repeat": 5,
  "tags": ["multi-hit"]
}
```

## Próximos Passos

Com o Effect System implementado, os próximos sistemas são:

1. **Status System** - Gerencia duração, stacks, timing; usa Effects para DoT/HoT
2. **Modifier System** - Go Again, Multi-Hit, etc.; usa EffectModifiers
3. **Card System** - Cartas são ActionDefinitions com Effects
4. **Event System** - Eventos especiais (Slay the Spire style) com Effects
5. **Relic System** - Relíquias modificam Effects

## Referências

- `src/Core/Effects/` - Código fonte do sistema
- `data/configs/default/Actions/` - Exemplos de cartas
- `docs/DAMAGE_PIPELINE.md` - Pipeline de dano (integrado com Effects)
- `docs/roadmap/PHASE_2.md` - Roadmap de implementação

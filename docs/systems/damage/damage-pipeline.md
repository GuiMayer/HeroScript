# Damage Pipeline System

Sistema de cálculo de dano configurável via JSON com arquitetura de buckets e operações genéricas.

## Visão Geral

O Damage Pipeline é um sistema modular que processa dano através de uma sequência de **buckets**, onde cada bucket aplica operações matemáticas configuráveis. O sistema suporta:

- **Configuração JSON**: Pipeline completamente configurável sem código
- **Filtros condicionais**: Buckets executam apenas quando condições são atendidas
- **Operações genéricas**: Multiplicação, adição, crítico, mitigação, etc.
- **Eventos observáveis**: Rastreamento completo do pipeline via eventos
- **Hot-reload**: Recarregar configuração sem reiniciar o servidor
- **API REST**: Endpoints para cálculo e inspeção do pipeline

## Arquitetura

### Fluxo de Dados

```
ActionDefinition + Attacker + Target
           ↓
    DamageCalculator (facade)
           ↓
    DamageContext (estado inicial)
           ↓
    PipelineManager.ExecutePipeline()
           ↓
    [Bucket 1] → [Bucket 2] → [Bucket 3] → ...
           ↓
    DamageResult (dano final + metadata)
```

### Componentes Principais

#### 1. DamageContext
Estado imutável que flui pelo pipeline:
- `CurrentDamage`: Dano atual
- `BaseDamage`: Dano original (referência)
- `Tags`: Tags da ação (physical, spell, fire, can_crit, etc.)
- `Modifiers`: Modificadores das entidades (increased_damage_total, etc.)
- `Metadata`: Dados adicionais (crit_tier, armor_mitigation, etc.)

#### 2. BucketDefinition
Configuração de um bucket:
- `BucketId`: Identificador único
- `Description`: Descrição do bucket
- `FilterConditions`: Condições para executar o bucket
- `Operations`: Lista de operações a executar
- `EmitEvents`: Se deve emitir eventos de processamento

#### 3. GenericBucketProcessor
Processador que executa operações de um bucket:
- Avalia filtros (tags, modifiers, metadata)
- Executa operações sequencialmente
- Emite eventos (opcional)
- Retorna novo contexto imutável

#### 4. PipelineManager
Gerenciador do pipeline:
- Carrega configuração JSON
- Instancia processadores
- Executa pipeline completo
- Suporta hot-reload

## Configuração JSON

### Estrutura do DamagePipeline.json

```json
{
  "buckets": [
    {
      "bucket_id": "base_damage",
      "description": "Aplica dano base da ação",
      "filter_conditions": [],
      "operations": [
        {
          "operation_type": "set_base",
          "value_source": "base_damage"
        }
      ],
      "emit_events": true
    }
  ]
}
```

### Tipos de Filtros

#### has_tag
Verifica se a ação tem uma tag específica:
```json
{
  "type": "has_tag",
  "tag": "physical"
}
```

#### has_modifier
Verifica se o atacante tem um modifier:
```json
{
  "type": "has_modifier",
  "modifier_key": "increased_damage_total"
}
```

#### metadata_exists
Verifica se existe uma chave no metadata:
```json
{
  "type": "metadata_exists",
  "metadata_key": "crit_tier"
}
```

#### metadata_compare
Compara valor no metadata:
```json
{
  "type": "metadata_compare",
  "metadata_key": "crit_tier",
  "operator": "greater_than",
  "compare_value": 0
}
```

### Tipos de Operações

#### set_base
Define dano como valor base:
```json
{
  "operation_type": "set_base",
  "value_source": "base_damage"
}
```

#### multiply
Multiplica dano por um valor:
```json
{
  "operation_type": "multiply",
  "value_source": "constant",
  "constant_value": 1.5
}
```

#### add
Adiciona valor ao dano:
```json
{
  "operation_type": "add",
  "value_source": "modifier",
  "modifier_key": "flat_damage_bonus"
}
```

#### multiply_modifier
Multiplica por (1 + modifier/100):
```json
{
  "operation_type": "multiply_modifier",
  "modifier_key": "increased_damage_total"
}
```

#### roll_crit
Rola crítico e armazena tier no metadata:
```json
{
  "operation_type": "roll_crit",
  "crit_chance_source": "attacker_stat",
  "crit_multiplier_source": "attacker_stat"
}
```

#### multiply_crit
Multiplica dano baseado no tier de crítico:
```json
{
  "operation_type": "multiply_crit",
  "metadata_key": "crit_tier",
  "base_multiplier": 2.0,
  "tier_bonus": 0.5
}
```

#### apply_mitigation
Aplica mitigação de armadura:
```json
{
  "operation_type": "apply_mitigation",
  "armor_source": "target_stat",
  "formula_id": "armor_mitigation"
}
```

#### eval_formula
Avalia fórmula matemática do MathEngine:
```json
{
  "operation_type": "eval_formula",
  "formula_id": "custom_damage_scaling",
  "result_action": "multiply"
}
```

### Value Sources

- `constant`: Valor fixo (`constant_value`)
- `base_damage`: Dano base original
- `modifier`: Modifier do atacante (`modifier_key`)
- `metadata`: Valor do metadata (`metadata_key`)
- `attacker_stat`: Stat do atacante (armor, crit_chance, crit_multiplier)
- `target_stat`: Stat do alvo

## Exemplos de Uso

### Exemplo 1: Ataque Básico Físico

```csharp
var actionDef = new ActionDefinition
{
    ActionId = "basic_attack",
    BaseDamage = 10,
    Tags = new List<string> { "physical", "melee", "can_crit" }
};

var result = damageCalculator.CalculateDamage(actionDef, hero, enemy);
// result.FinalDamage = dano calculado
// result.CritTier = 0 (normal) ou 1+ (crítico)
```

**Pipeline executado:**
1. `base_damage`: Define dano = 10
2. `increased_damage`: Multiplica por (1 + increased_damage_total/100)
3. `crit_roll`: Rola crítico (se can_crit)
4. `crit_multiply`: Multiplica por 2.0 se crítico
5. `armor_mitigation`: Aplica redução de armadura

### Exemplo 2: Spell de Fogo

```csharp
var actionDef = new ActionDefinition
{
    ActionId = "fireball",
    BaseDamage = 30,
    Tags = new List<string> { "spell", "fire", "can_crit" }
};

var result = damageCalculator.CalculateDamage(actionDef, hero, enemy);
```

**Pipeline executado:**
1. `base_damage`: Define dano = 30
2. `increased_damage`: Multiplica por modifiers gerais
3. `spell_damage`: Multiplica por (1 + increased_spell_damage/100) se spell
4. `fire_damage`: Multiplica por (1 + increased_fire_damage/100) se fire
5. `crit_roll`: Rola crítico
6. `crit_multiply`: Multiplica se crítico
7. `armor_mitigation`: Aplica mitigação

### Exemplo 3: Ataque Sem Crítico

```csharp
var actionDef = new ActionDefinition
{
    ActionId = "poison_dot",
    BaseDamage = 5,
    Tags = new List<string> { "spell", "poison" } // Sem can_crit
};

var result = damageCalculator.CalculateDamage(actionDef, hero, enemy);
```

**Pipeline executado:**
1. `base_damage`: Define dano = 5
2. `increased_damage`: Multiplica por modifiers
3. `spell_damage`: Multiplica por spell modifiers
4. `crit_roll`: **SKIPPED** (sem tag can_crit)
5. `crit_multiply`: **SKIPPED** (sem crit_tier no metadata)
6. `armor_mitigation`: Aplica mitigação

## API REST

### POST /api/damage/calculate

Calcula dano de uma ação.

**Request:**
```json
{
  "actionId": "fireball",
  "baseDamage": 30,
  "tags": ["spell", "fire", "can_crit"],
  "attacker": {
    "entityId": "hero_1",
    "armor": 0,
    "critChance": 25,
    "critMultiplier": 2.0,
    "modifiers": {
      "increased_damage_total": 50,
      "increased_spell_damage": 30,
      "increased_fire_damage": 20
    }
  },
  "target": {
    "entityId": "enemy_1",
    "armor": 10,
    "critChance": 0,
    "critMultiplier": 1.0,
    "modifiers": {}
  }
}
```

**Response:**
```json
{
  "finalDamage": 87.5,
  "critTier": 1,
  "baseDamage": 30,
  "breakdown": [],
  "metadata": {
    "crit_tier": 1,
    "armor_mitigation": 0.09
  }
}
```

### GET /api/damage/pipeline/config

Obtém configuração atual do pipeline.

**Response:**
```json
{
  "bucketCount": 7,
  "buckets": [
    {
      "bucketId": "base_damage",
      "description": "Aplica dano base da ação",
      "operationCount": 1,
      "emitEvents": true
    }
  ]
}
```

### POST /api/damage/pipeline/reload

Recarrega configuração do pipeline.

**Response:**
```json
{
  "bucketCount": 7,
  "buckets": [...]
}
```

## Eventos

### DamageCalculatedEvent

Emitido quando o cálculo de dano é finalizado.

```csharp
public class DamageCalculatedEvent : IEvent
{
    public string ActionId { get; init; }
    public string AttackerId { get; init; }
    public string TargetId { get; init; }
    public float BaseDamage { get; init; }
    public float FinalDamage { get; init; }
    public int CritTier { get; init; }
    public HashSet<string> Tags { get; init; }
    public Dictionary<string, object> Metadata { get; init; }
    public DateTime Timestamp { get; init; }
}
```

### BucketProcessedEvent

Emitido quando um bucket é processado (se `emit_events: true`).

```csharp
public class BucketProcessedEvent : IEvent
{
    public string BucketId { get; init; }
    public float DamageBefore { get; init; }
    public float DamageAfter { get; init; }
    public float DamageDelta { get; }
    public Dictionary<string, object> Metadata { get; init; }
    public DateTime Timestamp { get; init; }
}
```

### PipelineReloadedEvent

Emitido quando o pipeline é recarregado.

```csharp
public class PipelineReloadedEvent : IEvent
{
    public int BucketCount { get; init; }
    public List<string> BucketIds { get; init; }
    public string Reason { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime Timestamp { get; init; }
}
```

## Integração com Combat System

O `CombatSystem` usa o `DamageCalculator` opcionalmente:

```csharp
public CombatSystem(
    ILogger logger,
    IResourceManager resourceManager,
    IEventBus? eventBus = null,
    IDamageCalculator? damageCalculator = null)
{
    _damageCalculator = damageCalculator;
}
```

Se `damageCalculator` for `null`, o sistema usa dano fixo (fallback).

## Extensibilidade

### Adicionar Novo Tipo de Operação

1. Adicionar enum em `BucketOperation.OperationType`
2. Implementar lógica em `GenericBucketProcessor.ExecuteOperation()`
3. Atualizar JSON com nova operação

### Adicionar Novo Tipo de Filtro

1. Adicionar enum em `FilterCondition.FilterType`
2. Implementar lógica em `GenericBucketProcessor.EvaluateFilter()`
3. Atualizar JSON com novo filtro

### Adicionar Nova Fórmula

1. Adicionar fórmula em `MathFormulas.json`
2. Usar operação `eval_formula` no pipeline

## Performance

- **Imutabilidade**: Contexto é imutável, evita side-effects
- **Cache**: PipelineManager cacheia processadores
- **Thread-safe**: Random.Shared para rolls de crítico
- **Lazy loading**: Configuração carregada sob demanda

## Debugging

### Logs

O sistema emite logs detalhados:

```
[DEBUG] Pipeline start: 30.00 damage, 3 tags, 2 modifiers
[DEBUG] Bucket 'base_damage': 0.00 → 30.00
[DEBUG] Bucket 'increased_damage': 30.00 → 45.00
[DEBUG] Bucket 'crit_roll': 45.00 → 45.00
[DEBUG] Bucket 'crit_multiply': 45.00 → 90.00
[DEBUG] Bucket 'armor_mitigation': 90.00 → 81.90
[DEBUG] Pipeline end: 81.90 damage (delta: +51.90)
[DEBUG] Damage calculated: 81.90 (crit tier: 1)
```

### Eventos

Subscrever eventos para observabilidade:

```csharp
eventBus.Subscribe<BucketProcessedEvent>(evt =>
{
    Console.WriteLine($"{evt.BucketId}: {evt.DamageBefore:F2} → {evt.DamageAfter:F2}");
});
```

## Roadmap

- [ ] Breakdown detalhado na API (capturar eventos)
- [ ] Sistema de stats/modifiers completo
- [ ] Suporte a múltiplos alvos (AoE)
- [ ] Damage over time (DoT) tracking
- [ ] Resistências elementais
- [ ] Penetração de armadura
- [ ] Damage reflection
- [ ] Shields e absorção

## Referências

- `src/Core/Damage/` - Código fonte
- `config/DamagePipeline.json` - Configuração do pipeline
- `config/MathFormulas.json` - Fórmulas matemáticas
- `src/API/Controllers/DamageController.cs` - API REST

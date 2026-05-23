# Damage Pipeline - Exemplos Práticos

Este documento contém exemplos práticos de uso do sistema de pipeline de dano.

## Exemplo 1: Configuração Básica

### Cenário
Herói com 50% de aumento de dano total ataca inimigo com 10 de armadura.

### Código
```csharp
// Setup
var hero = new CombatEntity("hero_1", "Hero", healthPool);
var enemy = new CombatEntity("enemy_1", "Enemy", healthPool);

// Herói tem +50% increased damage
hero = hero.UpdateModifier("increased_damage_total", 50);

// Inimigo tem 10 de armadura
enemy = enemy.UpdateModifier("armor", 10);

// Ação: ataque básico
var action = new ActionDefinition
{
    ActionId = "basic_attack",
    BaseDamage = 10,
    Tags = new List<string> { "physical", "melee", "can_crit" }
};

// Calcular dano
var result = damageCalculator.CalculateDamage(action, hero, enemy);

Console.WriteLine($"Dano final: {result.FinalDamage:F2}");
// Output: Dano final: 13.64
```

### Pipeline Executado
1. **base_damage**: 0 → 10 (define dano base)
2. **increased_damage**: 10 → 15 (multiplica por 1.5 devido ao +50%)
3. **crit_roll**: 15 → 15 (não crítico, assume 0% crit chance)
4. **crit_multiply**: SKIPPED (crit_tier = 0)
5. **armor_mitigation**: 15 → 13.64 (reduz ~9% devido a 10 de armadura)

### Fórmula de Armadura
```
mitigation = armor / (armor + 100)
mitigation = 10 / (10 + 100) = 0.0909 (9.09%)
final_damage = 15 * (1 - 0.0909) = 13.64
```

## Exemplo 2: Crítico Simples

### Cenário
Herói com 100% de chance de crítico (garantido) e 2.0x de multiplicador.

### Código
```csharp
// Herói com 100% crit chance
var hero = new CombatEntity("hero_1", "Hero", healthPool);
hero = hero.UpdateModifier("crit_chance", 100);
hero = hero.UpdateModifier("crit_multiplier", 2.0f);

var action = new ActionDefinition
{
    ActionId = "basic_attack",
    BaseDamage = 10,
    Tags = new List<string> { "physical", "melee", "can_crit" }
};

var result = damageCalculator.CalculateDamage(action, hero, enemy);

Console.WriteLine($"Dano final: {result.FinalDamage:F2}");
Console.WriteLine($"Crit tier: {result.CritTier}");
// Output: Dano final: 20.00
// Output: Crit tier: 1
```

### Pipeline Executado
1. **base_damage**: 0 → 10
2. **increased_damage**: 10 → 10 (sem modifiers)
3. **crit_roll**: 10 → 10 (rola crítico, crit_tier = 1)
4. **crit_multiply**: 10 → 20 (multiplica por 2.0)
5. **armor_mitigation**: 20 → 20 (sem armadura)

## Exemplo 3: Spell com Modifiers Específicos

### Cenário
Spell de fogo com modifiers gerais, de spell e de fogo.

### Código
```csharp
var hero = new CombatEntity("hero_1", "Hero", healthPool);
hero = hero.UpdateModifier("increased_damage_total", 20);      // +20% geral
hero = hero.UpdateModifier("increased_spell_damage", 30);      // +30% spell
hero = hero.UpdateModifier("increased_fire_damage", 50);       // +50% fire

var action = new ActionDefinition
{
    ActionId = "fireball",
    BaseDamage = 30,
    Tags = new List<string> { "spell", "fire", "can_crit" }
};

var result = damageCalculator.CalculateDamage(action, hero, enemy);

Console.WriteLine($"Dano final: {result.FinalDamage:F2}");
// Output: Dano final: 70.20
```

### Pipeline Executado
1. **base_damage**: 0 → 30
2. **increased_damage**: 30 → 36 (multiplica por 1.20)
3. **spell_damage**: 36 → 46.8 (multiplica por 1.30)
4. **fire_damage**: 46.8 → 70.2 (multiplica por 1.50)
5. **crit_roll**: 70.2 → 70.2 (não crítico)
6. **crit_multiply**: SKIPPED
7. **armor_mitigation**: 70.2 → 70.2 (sem armadura)

### Cálculo Manual
```
base = 30
after_general = 30 * 1.20 = 36
after_spell = 36 * 1.30 = 46.8
after_fire = 46.8 * 1.50 = 70.2
```

## Exemplo 4: Ataque Sem Crítico (DoT)

### Cenário
Damage over time (poison) que não pode dar crítico.

### Código
```csharp
var action = new ActionDefinition
{
    ActionId = "poison_dot",
    BaseDamage = 5,
    Tags = new List<string> { "spell", "poison" } // SEM can_crit
};

var result = damageCalculator.CalculateDamage(action, hero, enemy);

Console.WriteLine($"Dano final: {result.FinalDamage:F2}");
Console.WriteLine($"Crit tier: {result.CritTier}");
// Output: Dano final: 5.00
// Output: Crit tier: 0
```

### Pipeline Executado
1. **base_damage**: 0 → 5
2. **increased_damage**: 5 → 5 (sem modifiers)
3. **spell_damage**: 5 → 5 (sem modifiers)
4. **crit_roll**: SKIPPED (sem tag can_crit)
5. **crit_multiply**: SKIPPED
6. **armor_mitigation**: 5 → 5 (sem armadura)

## Exemplo 5: Múltiplos Tiers de Crítico

### Cenário
Sistema com múltiplos tiers de crítico (tier 1 = 2x, tier 2 = 2.5x, tier 3 = 3x).

### Configuração JSON
```json
{
  "bucket_id": "crit_multiply",
  "operations": [
    {
      "operation_type": "multiply_crit",
      "metadata_key": "crit_tier",
      "base_multiplier": 2.0,
      "tier_bonus": 0.5
    }
  ]
}
```

### Código
```csharp
// Simular tier 2 de crítico (modificando metadata manualmente para teste)
var context = new DamageContext
{
    CurrentDamage = 10,
    BaseDamage = 10,
    Tags = new HashSet<string> { "physical", "can_crit" },
    Modifiers = new Dictionary<string, float>(),
    Metadata = new Dictionary<string, object> { ["crit_tier"] = 2 }
};

var result = pipelineManager.ExecutePipeline(context);

Console.WriteLine($"Dano final: {result.CurrentDamage:F2}");
// Output: Dano final: 25.00
```

### Cálculo
```
multiplier = base_multiplier + (tier * tier_bonus)
multiplier = 2.0 + (2 * 0.5) = 3.0
final_damage = 10 * 3.0 = 30.0

Mas na configuração atual, tier_bonus = 0.5:
tier 1: 2.0 + (1 * 0.5) = 2.5x
tier 2: 2.0 + (2 * 0.5) = 3.0x
```

## Exemplo 6: Uso via API REST

### Request: Calcular Dano
```bash
curl -X POST http://localhost:5000/api/damage/calculate \
  -H "Content-Type: application/json" \
  -d '{
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
  }'
```

### Response
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

### Cálculo Detalhado
```
base = 30
after_general = 30 * 1.50 = 45
after_spell = 45 * 1.30 = 58.5
after_fire = 58.5 * 1.20 = 70.2
after_crit = 70.2 * 2.0 = 140.4 (se crítico)
after_armor = 140.4 * (1 - 0.09) = 127.76

Sem crítico:
after_armor = 70.2 * (1 - 0.09) = 63.88
```

## Exemplo 7: Observando Eventos

### Código
```csharp
// Subscrever eventos
eventBus.Subscribe<BucketProcessedEvent>(evt =>
{
    Console.WriteLine($"[{evt.BucketId}] {evt.DamageBefore:F2} → {evt.DamageAfter:F2} (Δ {evt.DamageDelta:+F2;-F2;0})");
});

eventBus.Subscribe<DamageCalculatedEvent>(evt =>
{
    Console.WriteLine($"Damage calculated: {evt.ActionId}");
    Console.WriteLine($"  Base: {evt.BaseDamage:F2}");
    Console.WriteLine($"  Final: {evt.FinalDamage:F2}");
    Console.WriteLine($"  Crit tier: {evt.CritTier}");
});

// Calcular dano
var result = damageCalculator.CalculateDamage(action, hero, enemy);
```

### Output
```
[base_damage] 0.00 → 10.00 (Δ +10.00)
[increased_damage] 10.00 → 15.00 (Δ +5.00)
[crit_roll] 15.00 → 15.00 (Δ +0.00)
[crit_multiply] 15.00 → 30.00 (Δ +15.00)
[armor_mitigation] 30.00 → 27.27 (Δ -2.73)
Damage calculated: basic_attack
  Base: 10.00
  Final: 27.27
  Crit tier: 1
```

## Exemplo 8: Hot-Reload do Pipeline

### Código
```csharp
// Recarregar pipeline
pipelineManager.ReloadConfiguration(new[] { "DamagePipeline.json" });

// Evento emitido
eventBus.Subscribe<PipelineReloadedEvent>(evt =>
{
    if (evt.Success)
    {
        Console.WriteLine($"Pipeline reloaded: {evt.BucketCount} buckets");
        foreach (var bucketId in evt.BucketIds)
        {
            Console.WriteLine($"  - {bucketId}");
        }
    }
    else
    {
        Console.WriteLine($"Pipeline reload failed: {evt.ErrorMessage}");
    }
});
```

### Output
```
Pipeline reloaded: 7 buckets
  - base_damage
  - increased_damage
  - spell_damage
  - fire_damage
  - crit_roll
  - crit_multiply
  - armor_mitigation
```

## Exemplo 9: Criando Bucket Customizado

### Cenário
Adicionar bucket que aplica bônus de dano contra inimigos com baixa vida.

### JSON (adicionar em DamagePipeline.json)
```json
{
  "bucket_id": "execute_bonus",
  "description": "Bônus de dano contra inimigos com baixa vida",
  "filter_conditions": [
    {
      "type": "metadata_exists",
      "metadata_key": "target_health_percent"
    },
    {
      "type": "metadata_compare",
      "metadata_key": "target_health_percent",
      "operator": "less_than",
      "compare_value": 30
    }
  ],
  "operations": [
    {
      "operation_type": "multiply",
      "value_source": "constant",
      "constant_value": 1.5
    }
  ],
  "emit_events": true
}
```

### Código
```csharp
// Criar contexto com metadata de vida do alvo
var context = new DamageContext
{
    CurrentDamage = 10,
    BaseDamage = 10,
    Tags = new HashSet<string> { "physical" },
    Modifiers = new Dictionary<string, float>(),
    Metadata = new Dictionary<string, object>
    {
        ["target_health_percent"] = 25 // Alvo com 25% de vida
    }
};

var result = pipelineManager.ExecutePipeline(context);

Console.WriteLine($"Dano final: {result.CurrentDamage:F2}");
// Output: Dano final: 15.00 (10 * 1.5)
```

## Exemplo 10: Fórmula Customizada

### Cenário
Criar fórmula de scaling baseado em nível do personagem.

### MathFormulas.json
```json
{
  "formula_id": "level_scaling",
  "expression": "1 + (level * 0.05)",
  "description": "Scaling de dano por nível (+5% por nível)",
  "variables": ["level"]
}
```

### DamagePipeline.json
```json
{
  "bucket_id": "level_scaling",
  "description": "Aplica scaling de nível",
  "filter_conditions": [
    {
      "type": "metadata_exists",
      "metadata_key": "attacker_level"
    }
  ],
  "operations": [
    {
      "operation_type": "eval_formula",
      "formula_id": "level_scaling",
      "result_action": "multiply"
    }
  ],
  "emit_events": true
}
```

### Código
```csharp
var context = new DamageContext
{
    CurrentDamage = 10,
    BaseDamage = 10,
    Tags = new HashSet<string> { "physical" },
    Modifiers = new Dictionary<string, float>(),
    Metadata = new Dictionary<string, object>
    {
        ["attacker_level"] = 20 // Nível 20
    }
};

var result = pipelineManager.ExecutePipeline(context);

Console.WriteLine($"Dano final: {result.CurrentDamage:F2}");
// Output: Dano final: 20.00 (10 * (1 + 20 * 0.05) = 10 * 2.0)
```

## Dicas de Debugging

### 1. Ativar Logs Detalhados
```csharp
logger.SetLogLevel(LogLevel.Debug);
```

### 2. Ativar Eventos em Todos os Buckets
```json
{
  "emit_events": true
}
```

### 3. Inspecionar Metadata
```csharp
var result = damageCalculator.CalculateDamage(action, hero, enemy);
foreach (var kvp in result.Metadata)
{
    Console.WriteLine($"{kvp.Key}: {kvp.Value}");
}
```

### 4. Testar Buckets Isoladamente
```csharp
var processor = new GenericBucketProcessor(bucketDef, mathEngine, eventBus, logger);
var result = processor.Process(context);
```

## Referências

- Documentação completa: [`damage-pipeline.md`](damage-pipeline.md)
- Configuração: `config/DamagePipeline.json`
- Fórmulas: `config/MathFormulas.json`
- Código fonte: `src/Core/Damage/`

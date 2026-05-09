# Core.Damage - Sistema de Cálculo de Dano Data-Driven

## Visão Geral

O módulo `Core.Damage` implementa um sistema de cálculo de dano completamente configurável via JSON, baseado no conceito de **pipeline de transformações**. O dano flui através de uma sequência de "buckets" (baldes), cada um aplicando transformações específicas ao contexto de dano.

### Arquitetura

```
ActionDefinition + CombatEntity (attacker/target)
    ↓
DamageCalculator (constrói contexto inicial)
    ↓
PipelineManager (carrega configuração JSON)
    ↓
GenericBucketProcessor (executa cada bucket)
    ↓
MathEngine (avalia fórmulas)
    ↓
DamageResult (dano final + metadata)
```

### Características Principais

- **100% Data-Driven**: Todo comportamento definido em JSON, zero código hardcoded
- **Imutável**: `DamageContext` é um record imutável, cada bucket retorna novo contexto
- **Observável**: Sistema de eventos rastreia cada transformação
- **Extensível**: Novos tipos de dano via JSON, sem modificar código C#
- **Testável**: Interfaces injetáveis, fácil mock e teste isolado

---

## Estrutura do Pipeline JSON

### Schema Básico

```json
{
  "buckets": [
    {
      "bucketId": "unique_id",
      "order": 1,
      "filterConditions": [...],
      "operations": [...],
      "emitEvents": true
    }
  ]
}
```

### Campos Obrigatórios

| Campo | Tipo | Descrição |
|-------|------|-----------|
| `bucketId` | string | Identificador único do bucket |
| `order` | int | Ordem de execução (1, 2, 3, ...) sem gaps |
| `filterConditions` | array | Condições para executar o bucket (vazio = sempre executa) |
| `operations` | array | Lista de operações a executar sequencialmente |
| `emitEvents` | bool | Se deve emitir `BucketProcessedEvent` |

### Validação

O pipeline é validado automaticamente ao carregar:
- IDs únicos (sem duplicatas)
- Ordem sequencial (1, 2, 3, ... sem gaps)
- Pelo menos um bucket presente

---

## Tipos de Operação

### ADD_FLAT
Adiciona valor flat ao dano atual.

**Formato**:
```json
{
  "type": "ADD_FLAT",
  "source": "modifier:base_damage",
  "parameters": {}
}
```

**Source aceita**:
- `"modifier:key"` - Valor de um modifier
- `"constant:123.45"` - Valor literal com prefixo
- `"123.45"` - Valor literal sem prefixo
- `"current_damage"` - Dano atual

**Exemplo**: Adicionar dano base + dano flat de equipamento

---

### MULTIPLY
Multiplica dano atual por um valor.

**Formato**:
```json
{
  "type": "MULTIPLY",
  "source": "modifier:more_multiplier_1",
  "parameters": {}
}
```

**Source**: Mesmo formato que `ADD_FLAT`

**Exemplo**: Aplicar multiplicador "more damage" de 1.3x

---

### APPLY_FORMULA
Aplica fórmula do MathEngine ao dano.

**Formato**:
```json
{
  "type": "APPLY_FORMULA",
  "source": "formula:LINEAR_ADDITIVE",
  "parameters": {
    "FLAT_BONUS": "modifier:increased_damage_total"
  }
}
```

**Source**: `"formula:FORMULA_NAME"` (nome registrado no MathEngine)

**Parameters**: Mapeamento de parâmetros da fórmula
- Valores podem ser `"modifier:key"`, `"current_damage"`, ou literais

**Fórmulas Disponíveis**:
- `LINEAR_ADDITIVE`: `damage * (1 + bonus)` - Para "increased damage"
- `ARMOR_REDUCTION`: `damage * (1 - armor/(armor+K))` - Redução por armadura
- `CRIT_GUARANTEED_TIER`: `floor(crit_chance / 100)` - Tier garantido de crítico
- `CRIT_EXTRA_CHANCE`: `crit_chance % 100` - Chance de tier extra
- `CRIT_DAMAGE_MULTIPLIER`: `1 + tier * (mult - 1)` - Multiplicador de dano crítico

---

### ROLL_CRIT_TIER
Operação especial para sistema de crítico multi-tier.

**Formato**:
```json
{
  "type": "ROLL_CRIT_TIER",
  "source": "",
  "parameters": {}
}
```

**Mecânica**:
1. Tier garantido = `floor(crit_chance / 100)`
2. Chance extra = `crit_chance % 100`
3. Roll para +1 tier baseado na chance extra
4. Aplica multiplicador: `damage * (1 + tier * (crit_mult - 1))`

**Exemplo**: 
- 150% crit chance → Tier 1 garantido + 50% chance de Tier 2
- Tier 2 com 2.0x multiplier → `damage * (1 + 2 * (2.0 - 1)) = damage * 3.0`

---

### SET_TAG
Adiciona tag ao contexto de dano.

**Formato**:
```json
{
  "type": "SET_TAG",
  "source": "fire_damage",
  "parameters": {}
}
```

**Source**: Nome da tag (com ou sem prefixo `"tag:"`)

**Uso**: Marcar tipo de dano para filtros posteriores

---

### REMOVE_TAG
Remove tag do contexto de dano.

**Formato**:
```json
{
  "type": "REMOVE_TAG",
  "source": "can_crit",
  "parameters": {}
}
```

**Uso**: Remover tags condicionalmente

---

### SET_MODIFIER
Define valor de um modifier.

**Formato**:
```json
{
  "type": "SET_MODIFIER",
  "source": "1.5",
  "parameters": {
    "key": "fire_resistance"
  }
}
```

**Uso**: Criar ou sobrescrever modifiers dinamicamente

---

### ADD_TO_MODIFIER
Adiciona valor a modifier existente.

**Formato**:
```json
{
  "type": "ADD_TO_MODIFIER",
  "source": "0.2",
  "parameters": {
    "key": "increased_damage_total"
  }
}
```

**Uso**: Acumular bônus aditivos

---

## Tipos de Filtro

Filtros determinam se um bucket deve ser executado.

### TAG_PRESENT
Executa apenas se tag existe.

**Formato**:
```json
{
  "type": "TAG_PRESENT",
  "parameter": "can_crit",
  "value": null
}
```

**Uso**: Crítico apenas para ações que podem criticar

---

### TAG_ABSENT
Executa apenas se tag NÃO existe.

**Formato**:
```json
{
  "type": "TAG_ABSENT",
  "parameter": "spell",
  "value": null
}
```

**Uso**: Aplicar bônus apenas para ataques físicos

---

### MODIFIER_PRESENT
Executa apenas se modifier existe.

**Formato**:
```json
{
  "type": "MODIFIER_PRESENT",
  "parameter": "fire_resistance",
  "value": null
}
```

---

### MODIFIER_ABOVE
Executa apenas se modifier > threshold.

**Formato**:
```json
{
  "type": "MODIFIER_ABOVE",
  "parameter": "target_health_percent",
  "value": 0.5
}
```

**Uso**: Bônus quando alvo tem mais de 50% HP

---

### MODIFIER_BELOW
Executa apenas se modifier < threshold.

**Formato**:
```json
{
  "type": "MODIFIER_BELOW",
  "parameter": "target_health_percent",
  "value": 0.3
}
```

**Uso**: Execute damage quando alvo tem menos de 30% HP

---

## Exemplos Práticos

### Pipeline Simples (apenas dano base)

```json
{
  "buckets": [
    {
      "bucketId": "base",
      "order": 1,
      "filterConditions": [],
      "operations": [
        {
          "type": "ADD_FLAT",
          "source": "modifier:base_damage",
          "parameters": {}
        }
      ],
      "emitEvents": true
    }
  ]
}
```

---

### Pipeline com Crítico

```json
{
  "buckets": [
    {
      "bucketId": "base",
      "order": 1,
      "filterConditions": [],
      "operations": [
        {"type": "ADD_FLAT", "source": "modifier:base_damage", "parameters": {}}
      ],
      "emitEvents": true
    },
    {
      "bucketId": "critical",
      "order": 2,
      "filterConditions": [
        {"type": "TAG_PRESENT", "parameter": "can_crit", "value": null}
      ],
      "operations": [
        {"type": "ROLL_CRIT_TIER", "source": "", "parameters": {}}
      ],
      "emitEvents": true
    }
  ]
}
```

---

### Pipeline com Resistências Elementais

```json
{
  "buckets": [
    {
      "bucketId": "base",
      "order": 1,
      "filterConditions": [],
      "operations": [
        {"type": "ADD_FLAT", "source": "modifier:base_damage", "parameters": {}}
      ],
      "emitEvents": true
    },
    {
      "bucketId": "fire_resistance",
      "order": 2,
      "filterConditions": [
        {"type": "TAG_PRESENT", "parameter": "fire", "value": null},
        {"type": "MODIFIER_PRESENT", "parameter": "fire_resistance", "value": null}
      ],
      "operations": [
        {"type": "MULTIPLY", "source": "modifier:fire_resistance", "parameters": {}}
      ],
      "emitEvents": true
    }
  ]
}
```

**Uso**: Se dano tem tag "fire" E alvo tem fire_resistance, aplica multiplicador

---

### Pipeline com Buffs Temporários

```json
{
  "buckets": [
    {
      "bucketId": "base",
      "order": 1,
      "filterConditions": [],
      "operations": [
        {"type": "ADD_FLAT", "source": "modifier:base_damage", "parameters": {}}
      ],
      "emitEvents": true
    },
    {
      "bucketId": "enraged_bonus",
      "order": 2,
      "filterConditions": [
        {"type": "TAG_PRESENT", "parameter": "enraged", "value": null}
      ],
      "operations": [
        {"type": "MULTIPLY", "source": "1.5", "parameters": {}}
      ],
      "emitEvents": true
    }
  ]
}
```

**Uso**: Se atacante tem tag "enraged", aplica 50% more damage

---

## Integração com MathEngine

O `MathEngine` permite criar fórmulas customizadas reutilizáveis.

### Registrar Nova Fórmula

```csharp
// Em MathEngine ou módulo de inicialização
mathEngine.RegisterFormula("POISON_DAMAGE_OVER_TIME", 
    (baseDamage, parameters) => {
        var duration = parameters["DURATION"];
        var tickRate = parameters["TICK_RATE"];
        return baseDamage * duration / tickRate;
    });
```

### Usar no Pipeline

```json
{
  "type": "APPLY_FORMULA",
  "source": "formula:POISON_DAMAGE_OVER_TIME",
  "parameters": {
    "DURATION": "modifier:poison_duration",
    "TICK_RATE": "constant:1.0"
  }
}
```

---

## Debugging e Observabilidade

### Eventos Disponíveis

#### BucketProcessedEvent
Emitido após cada bucket (se `emitEvents: true`).

**Campos**:
- `BucketId`: ID do bucket processado
- `DamageBefore`: Dano antes do bucket
- `DamageAfter`: Dano depois do bucket
- `DamageDelta`: Diferença (after - before)
- `Metadata`: Contexto completo

**Uso**: Rastrear transformações passo a passo

---

#### DamageCalculatedEvent
Emitido ao final do pipeline.

**Campos**:
- `ActionId`, `AttackerId`, `TargetId`: Identificadores
- `BaseDamage`: Dano inicial
- `FinalDamage`: Dano final
- `CritTier`: Tier de crítico alcançado
- `Tags`: Tags da ação
- `Metadata`: Dados adicionais

**Uso**: Logging, analytics, UI feedback

---

#### PipelineReloadedEvent
Emitido quando configuração é recarregada.

**Campos**:
- `BucketCount`: Número de buckets
- `BucketIds`: Lista de IDs na ordem
- `Success`: Se reload foi bem-sucedido
- `ErrorMessage`: Mensagem de erro (se houver)

**Uso**: Hot-reload em desenvolvimento

---

### Logs

O sistema emite logs detalhados em cada nível:

```
[DEBUG] Calculating damage: basic_attack from hero-1 to enemy-1
[DEBUG] Initial context: base=50.00, crit_chance=25.0%, armor=30.0
[DEBUG] Pipeline start: 50.00 damage, 3 tags, 8 modifiers
[DEBUG] Bucket 'base': 0.00 → 50.00
[DEBUG] Bucket 'increased': 50.00 → 75.00
[DEBUG] Bucket 'more': 75.00 → 97.50
[DEBUG] Bucket 'critical' skipped (filters failed)
[DEBUG] Bucket 'mitigation': 97.50 → 73.13
[DEBUG] Pipeline end: 73.13 damage (delta: +23.13)
[DEBUG] Damage calculated: 73.13 (crit tier: 0)
```

---

### Dicas de Debugging

1. **Ativar eventos**: `emitEvents: true` em todos os buckets
2. **Verificar logs**: Procurar por "Pipeline start/end" e deltas de dano
3. **Inspecionar metadata**: `DamageCalculatedEvent.Metadata` contém estado completo
4. **Validar JSON**: `PipelineConfiguration.Validate()` detecta erros estruturais
5. **Testar isoladamente**: Usar `PipelineManager.CreateWithConfig()` em testes

---

## Customização Avançada

### Adicionar Novo Tipo de Dano Elemental

1. **Criar bucket com filtro de tag**:
```json
{
  "bucketId": "lightning_bonus",
  "order": 3,
  "filterConditions": [
    {"type": "TAG_PRESENT", "parameter": "lightning", "value": null}
  ],
  "operations": [
    {"type": "MULTIPLY", "source": "modifier:lightning_multiplier", "parameters": {}}
  ],
  "emitEvents": true
}
```

2. **Adicionar tag à ação**:
```csharp
var action = new ActionDefinition {
    ActionId = "lightning_bolt",
    Tags = new List<string> { "spell", "lightning", "can_crit" },
    // ...
};
```

3. **Configurar modifier no atacante**:
```csharp
context.Modifiers["lightning_multiplier"] = 1.5f; // 50% more lightning damage
```

---

### Modificar Ordem de Cálculo

Altere o campo `order` dos buckets:

```json
// Crítico ANTES de "more" multipliers
{"bucketId": "critical", "order": 3},
{"bucketId": "more", "order": 4}
```

**Impacto**: Crítico será calculado sobre dano base+increased, depois "more" amplifica o crítico.

---

### Adicionar Dano Condicional

```json
{
  "bucketId": "low_health_execute",
  "order": 6,
  "filterConditions": [
    {"type": "MODIFIER_BELOW", "parameter": "target_health_percent", "value": 0.2}
  ],
  "operations": [
    {"type": "MULTIPLY", "source": "2.0", "parameters": {}}
  ],
  "emitEvents": true
}
```

**Efeito**: Dobra dano quando alvo tem <20% HP (execute mechanic)

---

## Referências

- **Exemplo Comentado**: `data/configs/default/Resources/Pipelines/DamagePipeline.example.json`
- **Pipeline Alternativo**: `data/configs/default/Resources/Pipelines/DamagePipeline.high-crit.json`
- **Código Fonte**: `src/Core/Damage/`
- **Testes**: `tests/Core.Tests/Damage/`
- **Análise de Arquitetura**: Documento de análise (9.0/10)

---

## FAQ

**P: Como adiciono um novo tipo de operação?**  
R: Adicione ao enum `OperationType` e implemente método `Execute*` em `GenericBucketProcessor`.

**P: Posso ter buckets condicionais aninhados?**  
R: Não diretamente. Use múltiplos filtros no mesmo bucket (AND lógico) ou crie buckets separados.

**P: Como faço hot-reload do pipeline?**  
R: Chame `PipelineManager.ReloadConfiguration(configChain)`. Emite `PipelineReloadedEvent`.

**P: O pipeline é thread-safe?**  
R: Sim. `DamageContext` é imutável e `PipelineManager` usa lock no cache.

**P: Posso ter mais de 3 multiplicadores "more"?**  
R: Atualmente limitado a 3 slots. Versão futura terá lista dinâmica.

**P: Como testo meu pipeline customizado?**  
R: Use `PipelineManager.CreateWithConfig()` com configuração inline em testes unitários.

# Math Expression API - Três Modos de Operação

O endpoint `/api/math/expression/evaluate` suporta **três modos** de operação para máxima flexibilidade:

## Modo 1: Implícito (Values) - Acumulador Implícito

**Quando usar:** Expressões simples onde cada operação modifica o acumulador atual.

**Características:**
- Usa o campo `values` em cada step
- Acumulador implícito (`currentValue`) é modificado por cada operação
- Retrocompatível com versão anterior da API

**Exemplo:**

```json
POST /api/math/expression/evaluate
{
  "initialValue": 10,
  "steps": [
    { "operation": "ADD", "values": [5, 3] },
    { "operation": "MULTIPLY", "values": [2] }
  ]
}
```

**Resultado:** `(10 + 5 + 3) * 2 = 36`

---

## Modo 2: Explícito Literal (Operands Numéricos)

**Quando usar:** Operações explícitas com valores fixos conhecidos.

**Características:**
- Usa o campo `operands` com strings numéricas
- Operações ainda modificam o acumulador
- Útil quando valores são gerados dinamicamente como strings

**Exemplo:**

```json
POST /api/math/expression/evaluate
{
  "initialValue": 0,
  "steps": [
    { "operation": "ADD", "operands": ["10", "20"] },
    { "operation": "MULTIPLY", "operands": ["2"] }
  ]
}
```

**Resultado:** `(0 + 10 + 20) * 2 = 60`

---

## Modo 3: Explícito Simbólico (Operands Dinâmicos)

**Quando usar:** Expressões dinâmicas que dependem de parâmetros ou do valor atual.

**Características:**
- Usa o campo `operands` com referências simbólicas
- Requer campo `parameters` quando usa `params.X`
- Suporta: `$current`, `$initial`, `params.NAME`, literais numéricos

**Referências Especiais:**
- `$current` - Valor atual do acumulador
- `$initial` - Valor inicial da expressão
- `params.NAME` - Parâmetro fornecido no dicionário `parameters`
- `"123.45"` - Literal numérico

### Exemplo 1: Usando Parâmetros

```json
POST /api/math/expression/evaluate
{
  "initialValue": 0,
  "parameters": {
    "BONUS": 50,
    "MULTIPLIER": 2
  },
  "steps": [
    { "operation": "ADD", "operands": ["params.BONUS"] },
    { "operation": "MULTIPLY", "operands": ["params.MULTIPLIER"] }
  ]
}
```

**Resultado:** `(0 + 50) * 2 = 100`

### Exemplo 2: LERP (Linear Interpolation)

```json
POST /api/math/expression/evaluate
{
  "initialValue": 0,
  "parameters": {
    "TARGET": 100,
    "START": 0,
    "T": 0.5
  },
  "steps": [
    { "operation": "ADD", "operands": ["params.TARGET"] },
    { "operation": "SUBTRACT", "operands": ["params.START"] },
    { "operation": "MULTIPLY", "operands": ["params.T"] },
    { "operation": "ADD", "operands": ["params.START"] }
  ]
}
```

**Fórmula:** `START + (TARGET - START) * T`  
**Resultado:** `0 + (100 - 0) * 0.5 = 50`

### Exemplo 3: Misturando Simbólico e Literal

```json
POST /api/math/expression/evaluate
{
  "initialValue": 10,
  "parameters": {
    "BONUS": 20
  },
  "steps": [
    { "operation": "ADD", "operands": ["params.BONUS"] },
    { "operation": "MULTIPLY", "operands": ["2"] },
    { "operation": "ADD", "operands": ["$current", "5"] }
  ]
}
```

**Resultado:** 
1. `10 + 20 = 30`
2. `30 * 2 = 60`
3. `60 + 60 + 5 = 125`

---

## Validações

### ❌ Erro: Misturar Values e Operands

```json
{
  "operation": "ADD",
  "values": [10],
  "operands": ["20"]  // ❌ ERRO: Não pode usar ambos
}
```

**Resposta:** `400 Bad Request - "Step 'ADD' cannot have both Values and Operands"`

### ❌ Erro: Parâmetro Não Fornecido

```json
{
  "initialValue": 0,
  "steps": [
    { "operation": "ADD", "operands": ["params.MISSING"] }
  ]
  // ❌ ERRO: Falta campo "parameters"
}
```

**Resposta:** `400 Bad Request - "Operand 'params.MISSING' requires parameters dictionary"`

### ❌ Erro: Operando Inválido

```json
{
  "initialValue": 0,
  "parameters": {},
  "steps": [
    { "operation": "ADD", "operands": ["invalid_operand"] }
  ]
}
```

**Resposta:** `400 Bad Request - "Invalid operand: 'invalid_operand'"`

---

## Comparação dos Modos

| Aspecto | Modo 1 (Values) | Modo 2 (Operands Literal) | Modo 3 (Operands Simbólico) |
|---------|-----------------|---------------------------|------------------------------|
| **Campo usado** | `values` | `operands` | `operands` |
| **Tipo de valores** | Array de floats | Array de strings numéricas | Array de strings (simbólicas ou literais) |
| **Requer `parameters`** | Não | Não | Sim (se usar `params.X`) |
| **Suporta `$current`** | Implícito | Não | Sim |
| **Suporta `params.X`** | Não | Não | Sim |
| **Complexidade** | Baixa | Baixa | Média |
| **Flexibilidade** | Baixa | Média | Alta |

---

## Quando Usar Cada Modo

### Use Modo 1 (Values) quando:
- Expressão é simples e valores são conhecidos
- Não precisa de parâmetros dinâmicos
- Quer retrocompatibilidade com API antiga

### Use Modo 2 (Operands Literal) quando:
- Valores são gerados como strings
- Não precisa de parâmetros dinâmicos
- Quer operações explícitas sem símbolos

### Use Modo 3 (Operands Simbólico) quando:
- Precisa de parâmetros dinâmicos
- Quer referenciar `$current` ou `$initial`
- Expressão depende de valores calculados em runtime
- Implementando fórmulas complexas (LERP, interpolação, etc.)

---

## Operações Suportadas

Todas as operações do `MathExpression` são suportadas nos três modos:

- **Aritméticas:** `ADD`, `SUBTRACT`, `MULTIPLY`, `DIVIDE`
- **Potência:** `POW`, `SQRT`
- **Arredondamento:** `ROUND`, `FLOOR`, `CEIL`
- **Comparação:** `MIN`, `MAX`, `CLAMP`
- **Outras:** `ABS`, `NEGATE`, `SET`

---

## Performance

- **Modo 1 e 2:** Overhead mínimo (~0.1ms por step)
- **Modo 3:** Overhead de resolução de operandos (~0.2ms por step)
- Para expressões típicas (< 20 steps), diferença é imperceptível

---

## Exemplos Práticos

### Cálculo de Dano com Bônus

```json
{
  "initialValue": 0,
  "parameters": {
    "BASE_DAMAGE": 50,
    "STRENGTH_BONUS": 10,
    "CRIT_MULTIPLIER": 2
  },
  "steps": [
    { "operation": "ADD", "operands": ["params.BASE_DAMAGE", "params.STRENGTH_BONUS"] },
    { "operation": "MULTIPLY", "operands": ["params.CRIT_MULTIPLIER"] }
  ]
}
```

**Resultado:** `(50 + 10) * 2 = 120`

### Cálculo de Experiência com Cap

```json
{
  "initialValue": 0,
  "parameters": {
    "BASE_XP": 100,
    "BONUS_XP": 50,
    "MAX_XP": 120
  },
  "steps": [
    { "operation": "ADD", "operands": ["params.BASE_XP", "params.BONUS_XP"] },
    { "operation": "MIN", "operands": ["params.MAX_XP"] }
  ]
}
```

**Resultado:** `min(100 + 50, 120) = 120`

### Interpolação de Cor (LERP RGB)

```json
{
  "initialValue": 0,
  "parameters": {
    "COLOR_START": 0,
    "COLOR_END": 255,
    "T": 0.75
  },
  "steps": [
    { "operation": "ADD", "operands": ["params.COLOR_END"] },
    { "operation": "SUBTRACT", "operands": ["params.COLOR_START"] },
    { "operation": "MULTIPLY", "operands": ["params.T"] },
    { "operation": "ADD", "operands": ["params.COLOR_START"] },
    { "operation": "ROUND", "operands": ["0"] }
  ]
}
```

**Resultado:** `round(0 + (255 - 0) * 0.75) = 191`

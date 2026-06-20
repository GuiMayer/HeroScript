# Math Expression API - Dois Modos de Operacao

O endpoint `/api/math/expression/evaluate` suporta **dois modos** para definir operacoes:

1. **Modo simples (`values`)** - aplica valores numericos ao acumulador implicito.
2. **Modo explicito (`operands`)** - declara operandos como literais numericos ou referencias simbolicas.

Cada step deve usar apenas um desses campos. Nao misture `values` e `operands` na mesma operacao.

---

## Modo 1: Simples / Acumulador Implicito (`values`)

**Quando usar:** expressoes simples onde cada operacao modifica o valor atual.

**Caracteristicas:**

- Usa o campo `values` em cada step.
- O acumulador atual (`currentValue`) e usado implicitamente.
- E a forma mais compacta para operacoes lineares e calculos diretos.
- Nao requer `parameters`.

**Limitacoes:**

- Nao resolve `params.NAME`.
- Nao aceita `$current` ou `$initial` como tokens declarativos.
- Nao permite declarar operandos independentes do acumulador.
- E menos flexivel para formulas dinamicas, interpolacoes e composicoes que precisam referenciar valores externos.

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

## Modo 2: Explicito (`operands`)

**Quando usar:** operacoes que precisam declarar os operandos explicitamente, seja com valores fixos ou referencias dinamicas.

**Caracteristicas:**

- Usa o campo `operands` em cada step.
- Aceita literais numericos como strings.
- Aceita referencias simbolicas quando a expressao depende do contexto.
- Suporta `parameters` quando usa `params.NAME`.

**Referencias especiais:**

- `$current` - valor atual do acumulador.
- `$initial` - valor inicial da expressao.
- `params.NAME` - parametro fornecido no dicionario `parameters`.
- `"123.45"` - literal numerico.

### Exemplo com literais numericos

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

### Exemplo com parametros

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

### Exemplo LERP (Linear Interpolation)

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

**Formula:** `START + (TARGET - START) * T`  
**Resultado:** `0 + (100 - 0) * 0.5 = 50`

### Exemplo misturando simbolos e literais

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

## Validacoes

### Erro: misturar `values` e `operands`

```json
{
  "operation": "ADD",
  "values": [10],
  "operands": ["20"]
}
```

**Resposta:** `400 Bad Request - "Step 'ADD' cannot have both Values and Operands"`

### Erro: parametro nao fornecido

```json
{
  "initialValue": 0,
  "steps": [
    { "operation": "ADD", "operands": ["params.MISSING"] }
  ]
}
```

**Resposta:** `400 Bad Request - "Operand 'params.MISSING' requires parameters dictionary"`

### Erro: operando invalido

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

## Comparacao dos Modos

| Aspecto | Modo simples (`values`) | Modo explicito (`operands`) |
|---------|--------------------------|------------------------------|
| **Campo usado** | `values` | `operands` |
| **Tipo de valores** | Array de numeros | Array de strings |
| **Acumulador** | Implicito | Pode ser implicito ou referenciado com `$current` |
| **Requer `parameters`** | Nao | Somente se usar `params.NAME` |
| **Suporta `$current`** | Nao como token | Sim |
| **Suporta `$initial`** | Nao como token | Sim |
| **Suporta `params.NAME`** | Nao | Sim |
| **Complexidade** | Baixa | Media |
| **Flexibilidade** | Baixa a media | Alta |

---

## Quando Usar Cada Modo

### Use `values` quando:

- A expressao e simples e linear.
- Os valores sao numericos e conhecidos no request.
- Voce nao precisa de parametros dinamicos.
- A operacao deve apenas modificar o acumulador atual.

### Use `operands` quando:

- Voce precisa declarar argumentos explicitamente.
- Os valores chegam como strings numericas.
- A expressao usa `params.NAME`, `$current` ou `$initial`.
- A formula depende de valores calculados em runtime.
- Voce esta implementando formulas mais complexas, como LERP ou interpolacao.

---

## Operacoes Suportadas

Todas as operacoes do `MathExpression` sao suportadas pelos dois modos, respeitando os argumentos exigidos por cada operacao:

- **Aritmeticas:** `ADD`, `SUBTRACT`, `MULTIPLY`, `DIVIDE`
- **Potencia:** `POW`, `SQRT`
- **Arredondamento:** `ROUND`, `FLOOR`, `CEIL`
- **Comparacao:** `MIN`, `MAX`, `CLAMP`
- **Outras:** `ABS`, `NEGATE`, `SET`

---

## Performance

- **Modo simples (`values`):** overhead minimo (~0.1ms por step).
- **Modo explicito com literais:** overhead minimo (~0.1ms por step).
- **Modo explicito com simbolos:** adiciona custo de resolucao de operandos (~0.2ms por step).
- Para expressoes tipicas (< 20 steps), a diferenca e imperceptivel.

---

## Exemplos Praticos

### Calculo de dano com bonus

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

### Calculo de experiencia com cap

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

### Interpolacao de cor (LERP RGB)

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

# Core.Math - Sistema de Fórmulas Matemáticas

**Versão:** 1.0.0  
**Data:** 2026-05-08  
**Público-alvo:** Desenvolvedores e Modders

---

## Visão Geral

O **Core.Math** é o sistema de fórmulas matemáticas do HeroScript, responsável por carregar, construir e executar expressões matemáticas definidas em JSON. É completamente **data-driven** - todas as fórmulas são dados, não código.

### Características Principais

- ✅ **Data-driven** - Fórmulas são JSON, não código compilado
- ✅ **Herança delta** - Mods podem sobrescrever apenas as fórmulas que precisam
- ✅ **18 operações matemáticas** - De básicas (ADD, MULTIPLY) a avançadas (POW, LOG)
- ✅ **2 modos de operação** - Simples com acumulador implícito e explícito com operandos
- ✅ **Parâmetros customizáveis** - Cada fórmula pode ter parâmetros com defaults
- ✅ **Rastreamento de origem** - Sabe qual config forneceu cada fórmula
- ✅ **Cache inteligente** - Lazy loading com invalidação automática
- ✅ **Validação extensiva** - Divisão por zero, overflow, underflow, etc.

---

## Arquitetura

### Componentes Principais

```
Core.Math/
├── IMathEngine.cs           - Interface principal (11 métodos)
├── MathEngine.cs            - Implementação do engine
├── MathExpression.cs        - Classe de expressões matemáticas
├── FormulaLoader.cs         - Carregador de fórmulas JSON
└── Operations/
    ├── IOperationStrategy.cs      - Interface para operações
    ├── OperationRegistry.cs       - Registro de operações
    └── [Add|Subtract|Multiply|Divide]Operation.cs
```

### Fluxo de Execução

```
1. MathEngine carrega fórmulas de JSON (com herança delta)
2. MathEngine.BuildFromFormula() cria uma MathExpression
3. MathExpression registra operações como comandos (MathStep)
4. MathExpression.Build() executa todos os comandos e retorna resultado
```

**Padrão de Design:** Command Pattern - Operações são registradas como comandos imutáveis e executadas apenas no final.

---

## Estrutura de Fórmulas JSON

### Localização

Fórmulas são armazenadas em:
```
user://[config-name]/Resources/Pipelines/MathFormulas.json
```

Exemplo: `user://alisyum/Resources/Pipelines/MathFormulas.json`

### Estrutura Básica

```json
{
  "FORMULA_NAME": {
    "description": "Descrição legível da fórmula",
    "params": {
      "PARAM_NAME": 100.0,
      "ANOTHER_PARAM": 2.5
    },
    "operations": [
      { "op": "MULTIPLY", "value": "params.PARAM_NAME" },
      { "op": "ADD", "value": "params.ANOTHER_PARAM" }
    ]
  }
}
```

### Exemplo Real: Curva Hiperbólica

```json
{
  "HYPERBOLIC_CURVE": {
    "description": "Curva hiperbólica para retornos decrescentes",
    "params": {
      "base": 100.0,
      "scale": 100.0
    },
    "operations": [
      { "op": "MULTIPLY", "value": "params.scale" },
      { "op": "DIVIDE_INVERSE", "value": "params.base" }
    ]
  }
}
```

**Uso:**
```csharp
var engine = serviceProvider.GetService<IMathEngine>();
var expr = engine.BuildFromFormula("HYPERBOLIC_CURVE", inputValue: 50);
float result = expr.Build();
// Resultado: (50 * 100) / (100 + 50) = 33.33
```

---

## Dois Modos de Operação

O sistema suporta **2 modos** para definir operações, cada um com casos de uso específicos.

### Modo 1: Simples / Acumulador Implícito (`value` ou `values`)

**Quando usar:** expressões simples onde cada operação modifica o acumulador atual.

**Características:**
- Usa `value` no formato de fórmulas ou `values` no endpoint `/api/math/expression/evaluate`
- Acumulador implícito (`currentValue`) é modificado por cada operação
- É a forma mais compacta para cálculos lineares e diretos
- Não exige `operands`

**Limitações:**
- No endpoint de expressão, `values` aceita apenas números diretos
- Não resolve `params.NAME`, `$current` ou `$initial` como tokens declarativos no campo `values`
- É menos flexível para fórmulas dinâmicas e interpolações

**Exemplo:**
```json
{
  "SIMPLE_DAMAGE": {
    "description": "Dano base + bônus",
    "params": {
      "BASE": 50,
      "BONUS": 10
    },
    "operations": [
      { "op": "ADD", "value": "params.BASE" },
      { "op": "ADD", "value": "params.BONUS" }
    ]
  }
}
```

**Comportamento:** `currentValue = currentValue + BASE + BONUS`

### Modo 2: Explícito (`operands`)

**Quando usar:** operações que precisam declarar argumentos explicitamente, com literais numéricos ou referências dinâmicas.

**Características:**
- Usa o campo `operands`
- Aceita strings numéricas como literais
- Aceita referências simbólicas: `$current`, `$initial`, `params.NAME`
- É o modo mais flexível para expressões parametrizadas

**Referências Especiais:**
- `$current` - Valor atual do acumulador
- `$initial` - Valor inicial da expressão
- `params.NAME` - Parâmetro fornecido no dicionário
- `"123.45"` - Literal numérico

**Exemplo com literais:**
```json
{
  "initialValue": 10,
  "steps": [
    { "operation": "ADD", "operands": ["5", "3"] },
    { "operation": "MULTIPLY", "operands": ["2"] }
  ]
}
```

**Exemplo com símbolos: LERP (Linear Interpolation)**
```json
{
  "LERP": {
    "description": "Interpolação linear: START + (TARGET - START) * T",
    "params": {
      "START": 0,
      "TARGET": 100,
      "T": 0.5
    },
    "operations": [
      { "op": "ADD", "operands": ["params.TARGET"] },
      { "op": "SUBTRACT", "operands": ["params.START"] },
      { "op": "MULTIPLY", "operands": ["params.T"] },
      { "op": "ADD", "operands": ["params.START"] }
    ]
  }
}
```

**Resultado:** `0 + (100 - 0) * 0.5 = 50`

### Regras de Validação

❌ **ERRO:** Não pode misturar `value` e `operands` na mesma operação
```json
{
  "op": "ADD",
  "value": "10",
  "operands": ["20"]  // ❌ ERRO
}
```

✅ **CORRETO:** Use um ou outro
```json
{ "op": "ADD", "value": "10" }
// OU
{ "op": "ADD", "operands": ["10", "20"] }
```

---

## Operações Disponíveis

### Operações Básicas

| Operação | Símbolo | Descrição | Exemplo |
|----------|---------|-----------|---------|
| `ADD` | `+` | Adiciona valores ao acumulador | `{ "op": "ADD", "value": "10" }` |
| `SUBTRACT` | `-` | Subtrai valores do acumulador | `{ "op": "SUBTRACT", "value": "5" }` |
| `MULTIPLY` | `*` | Multiplica o acumulador | `{ "op": "MULTIPLY", "value": "2" }` |
| `DIVIDE` | `/` | Divide o acumulador | `{ "op": "DIVIDE", "value": "2" }` |
| `NEGATE` | `-x` | Inverte o sinal | `{ "op": "NEGATE" }` |
| `ABS` | `\|x\|` | Valor absoluto | `{ "op": "ABS" }` |
| `ROUND` | `~` | Arredonda para N decimais | `{ "op": "ROUND", "value": "2" }` |
| `FLOOR` | `⌊x⌋` | Arredonda para baixo | `{ "op": "FLOOR" }` |
| `CEIL` | `⌈x⌉` | Arredonda para cima | `{ "op": "CEIL" }` |

### Operações Avançadas

| Operação | Descrição | Exemplo |
|----------|-----------|---------|
| `DIVIDE_INVERSE` | Divisão inversa: `numerator / currentValue` | `{ "op": "DIVIDE_INVERSE", "value": "100" }` |
| `POW` | Potência: `currentValue ^ exponent` | `{ "op": "POW", "value": "2" }` |
| `POW_BASE` | Potência inversa: `base ^ currentValue` | `{ "op": "POW_BASE", "value": "2" }` |
| `SQRT` | Raiz quadrada | `{ "op": "SQRT" }` |
| `LOG` | Logaritmo (natural ou base customizada) | `{ "op": "LOG", "value": "10" }` |

### Operações Multi-Valor

| Operação | Descrição | Exemplo |
|----------|-----------|---------|
| `MIN` | Retorna o menor valor entre acumulador e valores fornecidos | `{ "op": "MIN", "value": "100" }` |
| `MAX` | Retorna o maior valor entre acumulador e valores fornecidos | `{ "op": "MAX", "value": "0" }` |
| `CLAMP` | Limita valor entre min e max | `{ "op": "CLAMP", "min": "0", "max": "100" }` |

### Operação Especial

| Operação | Descrição | Exemplo |
|----------|-----------|---------|
| `SET` | Define valor absoluto, ignorando acumulador | `{ "op": "SET", "value": "100" }` |

**Caso de uso do SET:**
```json
{
  "CONSTANT_VALUE": {
    "description": "Sempre retorna 100, independente do input",
    "params": {},
    "operations": [
      { "op": "SET", "value": "100" }
    ]
  }
}
```

---

## Herança Delta de Fórmulas

### Como Funciona

Mods podem **sobrescrever** ou **adicionar** fórmulas sem duplicar todas as fórmulas base.

**Exemplo:**

**Base (alisyum):**
```json
{
  "DAMAGE": {
    "description": "Dano base",
    "params": { "MULTIPLIER": 1.0 },
    "operations": [
      { "op": "MULTIPLY", "value": "params.MULTIPLIER" }
    ]
  }
}
```

**Mod (orc-mod):**
```json
{
  "DAMAGE": {
    "description": "Dano base - VERSÃO ORC BUFFADA",
    "params": { "MULTIPLIER": 1.5 },
    "operations": [
      { "op": "MULTIPLY", "value": "params.MULTIPLIER" }
    ]
  },
  "ORC_RAGE": {
    "description": "Nova fórmula exclusiva de orcs",
    "params": { "RAGE_BONUS": 10 },
    "operations": [
      { "op": "ADD", "value": "params.RAGE_BONUS" }
    ]
  }
}
```

**Resultado ao carregar orc-mod:**
- Fórmula `DAMAGE` é sobrescrita (multiplier 1.5 em vez de 1.0)
- Fórmula `ORC_RAGE` é adicionada
- Todas as outras fórmulas de `alisyum` são herdadas

### Rastreamento de Origem

O MathEngine rastreia qual config forneceu cada fórmula:

```csharp
var origins = engine.GetFormulaOrigins();
// {
//   "DAMAGE": "orc-mod",
//   "ORC_RAGE": "orc-mod",
//   "ARMOR": "alisyum",
//   ...
// }
```

---

## Uso Programático

### Injeção de Dependência

```csharp
// Registrar no DI container
services.AddSingleton<IMathEngine, MathEngine>();

// Injetar no construtor
public class CombatSystem
{
    private readonly IMathEngine _mathEngine;
    
    public CombatSystem(IMathEngine mathEngine)
    {
        _mathEngine = mathEngine;
    }
}
```

### Construir e Executar Fórmula

```csharp
// Construir expressão a partir de fórmula
var expression = _mathEngine.BuildFromFormula(
    formulaName: "HYPERBOLIC_CURVE",
    inputValue: 50
);

// Executar e obter resultado
float result = expression.Build();
```

### Sobrescrever Parâmetros

```csharp
// Usar parâmetros customizados
var paramOverrides = new Dictionary<string, float>
{
    { "scale", 150.0f },  // Sobrescreve default de 100.0
    { "base", 80.0f }     // Sobrescreve default de 100.0
};

var expression = _mathEngine.BuildFromFormula(
    formulaName: "HYPERBOLIC_CURVE",
    inputValue: 50,
    paramOverrides: paramOverrides
);

float result = expression.Build();
```

### Listar Fórmulas Disponíveis

```csharp
// Obter todas as fórmulas
var formulas = _mathEngine.GetAvailableFormulas();
foreach (var name in formulas)
{
    Console.WriteLine(name);
}

// Verificar se fórmula existe
if (_mathEngine.FormulaExists("DAMAGE"))
{
    // ...
}
```

### Introspecção de Fórmulas

```csharp
// Obter descrição
string? description = _mathEngine.GetFormulaDescription("HYPERBOLIC_CURVE");

// Obter parâmetros default
var defaultParams = _mathEngine.GetFormulaDefaultParams("HYPERBOLIC_CURVE");
// { "base": 100.0, "scale": 100.0 }

// Obter parâmetros merged (defaults + overrides)
var mergedParams = _mathEngine.GetMergedParams(
    "HYPERBOLIC_CURVE",
    new Dictionary<string, float> { { "scale", 150.0f } }
);
// { "base": 100.0, "scale": 150.0 }
```

### Construir Expressão Manualmente

```csharp
// Construir expressão sem usar fórmula JSON
var expression = new MathExpression(initialValue: 10);
expression
    .Add(5, 3)           // 10 + 5 + 3 = 18
    .Multiply(2)         // 18 * 2 = 36
    .Clamp(0, 30);       // min(36, 30) = 30

float result = expression.Build();  // 30
```

---

## API REST

### Endpoint: Avaliar Expressão

**POST** `/api/math/expression/evaluate`

Avalia uma expressão matemática customizada.

**Request:**
```json
{
  "initialValue": 10,
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

**Response:**
```json
{
  "result": 120,
  "initialValue": 10,
  "steps": [...],
  "executionTimeMs": 0.234
}
```

### Endpoint: Listar Operações

**GET** `/api/operation`

Lista todas as 18 operações disponíveis com metadados.

**Response:**
```json
[
  {
    "name": "ADD",
    "symbol": "+",
    "description": "Add values to the current result",
    "minValues": 1,
    "maxValues": -1,
    "category": "basic",
    "behavior": "accumulator",
    "isUnary": false
  }
]
```

### Endpoint: Obter Origens de Fórmulas

**GET** `/api/resource/origins?path=Pipelines/MathFormulas.json`

Retorna qual config forneceu cada fórmula.

**Response:**
```json
{
  "resourcePath": "Pipelines/MathFormulas.json",
  "origins": {
    "HYPERBOLIC_CURVE": "alisyum",
    "ORC_RAGE_SCALING": "test-orc-mod"
  }
}
```

---

## Exemplos Práticos

### Exemplo 1: Dano com Crítico

```json
{
  "CRITICAL_DAMAGE": {
    "description": "Dano base com chance de crítico",
    "params": {
      "BASE_DAMAGE": 50,
      "CRIT_MULTIPLIER": 2.0,
      "IS_CRIT": 0
    },
    "operations": [
      { "op": "SET", "value": "params.BASE_DAMAGE" },
      { "op": "MULTIPLY", "operands": ["1", "params.IS_CRIT"] },
      { "op": "MULTIPLY", "operands": ["params.CRIT_MULTIPLIER"] },
      { "op": "ADD", "operands": ["params.BASE_DAMAGE"] }
    ]
  }
}
```

**Uso:**
```csharp
// Dano normal
var expr = engine.BuildFromFormula("CRITICAL_DAMAGE", 0, 
    new Dictionary<string, float> { { "IS_CRIT", 0 } });
float damage = expr.Build();  // 50

// Dano crítico
var exprCrit = engine.BuildFromFormula("CRITICAL_DAMAGE", 0,
    new Dictionary<string, float> { { "IS_CRIT", 1 } });
float critDamage = exprCrit.Build();  // 100
```

### Exemplo 2: Escala com Soft Cap

```json
{
  "SOFT_CAP_SCALING": {
    "description": "Escala linear até o cap, depois hiperbólica",
    "params": {
      "CAP": 100,
      "SCALE_AFTER_CAP": 0.5
    },
    "operations": [
      { "op": "MIN", "value": "params.CAP" },
      { "op": "SUBTRACT", "operands": ["$current", "params.CAP"] },
      { "op": "MAX", "value": "0" },
      { "op": "MULTIPLY", "value": "params.SCALE_AFTER_CAP" },
      { "op": "ADD", "value": "params.CAP" }
    ]
  }
}
```

**Comportamento:**
- Input ≤ 100: retorna input
- Input > 100: retorna 100 + (input - 100) * 0.5

### Exemplo 3: Interpolação de Cor (LERP)

```json
{
  "COLOR_LERP": {
    "description": "Interpolação linear entre duas cores",
    "params": {
      "COLOR_START": 0,
      "COLOR_END": 255,
      "T": 0.5
    },
    "operations": [
      { "op": "SET", "value": "params.COLOR_END" },
      { "op": "SUBTRACT", "value": "params.COLOR_START" },
      { "op": "MULTIPLY", "value": "params.T" },
      { "op": "ADD", "value": "params.COLOR_START" },
      { "op": "ROUND", "value": "0" }
    ]
  }
}
```

**Fórmula:** `START + (END - START) * T`

---

## Validações e Tratamento de Erros

### Validações Automáticas

O sistema valida automaticamente:

- ✅ **Divisão por zero** - Lança `DivideByZeroException`
- ✅ **Logaritmo de número não-positivo** - Lança `InvalidOperationException`
- ✅ **Raiz quadrada de número negativo** - Lança `InvalidOperationException`
- ✅ **Overflow/Underflow** - Valida `float.IsInfinity` e `float.IsNaN`
- ✅ **Operandos inválidos** - Valida referências simbólicas
- ✅ **Parâmetros faltando** - Valida `params.X` quando `parameters` não fornecido

### Exemplo de Tratamento

```csharp
try
{
    var expr = engine.BuildFromFormula("DAMAGE", inputValue: 50);
    float result = expr.Build();
}
catch (InvalidOperationException ex)
{
    // Fórmula não encontrada ou operação inválida
    Console.WriteLine($"Erro: {ex.Message}");
}
catch (DivideByZeroException ex)
{
    // Divisão por zero detectada
    Console.WriteLine($"Erro: {ex.Message}");
}
catch (ArgumentException ex)
{
    // Parâmetro inválido ou faltando
    Console.WriteLine($"Erro: {ex.Message}");
}
```

---

## Performance

### Cache de Fórmulas

O MathEngine usa cache inteligente:

- ✅ **Lazy loading** - Fórmulas são carregadas apenas quando necessário
- ✅ **Cache por config** - Cada config tem seu próprio cache
- ✅ **Invalidação automática** - Cache é invalidado quando config muda

### Invalidar Cache Manualmente

```csharp
// Forçar reload de todas as fórmulas
_mathEngine.InvalidateCache();
```

### Estatísticas de Cache

```csharp
var stats = _mathEngine.GetCacheStats();
// {
//   "formulasLoaded": 18,
//   "cacheHits": 1234,
//   "cacheMisses": 56
// }
```

### Overhead de Execução

- **Modo simples (`values`):** ~0.1ms por step
- **Modo explícito com literais:** ~0.1ms por step
- **Modo explícito com símbolos:** ~0.2ms por step

Para expressões típicas (< 20 steps), diferença é imperceptível.

---

## Boas Práticas

### Para Modders

1. **Minimize overrides** - Sobrescreva apenas as fórmulas que realmente precisa modificar
2. **Use descrições claras** - Documente o que cada fórmula faz
3. **Teste com diferentes inputs** - Valide edge cases (0, negativo, muito grande)
4. **Prefira parâmetros** - Use `params` em vez de valores hardcoded
5. **Nomeie fórmulas semanticamente** - `ORC_RAGE_SCALING` é melhor que `FORMULA_42`

### Para Desenvolvedores

1. **Injete IMathEngine via DI** - Não instancie diretamente
2. **Cache resultados quando possível** - Se input não muda, resultado não muda
3. **Use parâmetros para variações** - Evite criar múltiplas fórmulas similares
4. **Valide fórmulas no load** - Use `FormulaExists()` antes de usar
5. **Trate exceções apropriadamente** - Divisão por zero pode acontecer

### Exemplo de Validação

```csharp
public float CalculateDamage(string formulaName, float input)
{
    if (!_mathEngine.FormulaExists(formulaName))
    {
        _logger.LogWarning($"Formula '{formulaName}' not found, using default");
        formulaName = "DEFAULT_DAMAGE";
    }
    
    try
    {
        var expr = _mathEngine.BuildFromFormula(formulaName, input);
        return expr.Build();
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, $"Error evaluating formula '{formulaName}'");
        return input;  // Fallback para input original
    }
}
```

---

## Troubleshooting

### Problema: Fórmula não encontrada

**Erro:** `InvalidOperationException: Formula 'X' not found`

**Solução:**
1. Verifique se o nome está correto (case-sensitive)
2. Verifique se a config está carregada corretamente
3. Use `GetAvailableFormulas()` para listar fórmulas disponíveis

### Problema: Parâmetro não encontrado

**Erro:** `ArgumentException: Parameter 'X' not found in formula`

**Solução:**
1. Verifique se o parâmetro existe na definição da fórmula
2. Use `GetFormulaDefaultParams()` para ver parâmetros disponíveis
3. Forneça o parâmetro via `paramOverrides`

### Problema: Divisão por zero

**Erro:** `DivideByZeroException: Cannot divide by zero`

**Solução:**
1. Valide input antes de passar para a fórmula
2. Use `CLAMP` para garantir valores mínimos
3. Adicione validação na fórmula com `MAX`

### Problema: Resultado é NaN ou Infinity

**Erro:** `InvalidOperationException: Result is NaN or Infinity`

**Solução:**
1. Verifique se há overflow (valores muito grandes)
2. Use `CLAMP` para limitar valores
3. Valide operações de potência e logaritmo

---

## Roadmap Futuro

### Planejado para Fase 2

- [ ] **EventBus integration** - Publicar eventos quando fórmulas são avaliadas
- [ ] **Formula validation tool** - Validar fórmulas JSON antes de carregar
- [ ] **Formula debugger** - Step-by-step execution com breakpoints
- [ ] **Formula profiler** - Medir performance de cada operação

### Planejado para Fase 4

- [ ] **Visual formula editor** - Editor gráfico no dashboard web
- [ ] **Formula templates** - Templates prontos para casos comuns
- [ ] **Formula testing framework** - Testes automatizados de fórmulas
- [ ] **Formula documentation generator** - Gerar docs a partir de JSON

---

## Referências

- **Código-fonte:** `src/Core/Math/`
- **Testes:** `tests/Core.Tests/Math/`
- **API Endpoints:** [`endpoints.md`](../../api/endpoints.md)
- **Modos de Operação:** [`expression-modes.md`](expression-modes.md)
- **Sistema de Config:** [`config-system.md`](../config/config-system.md)
- **Arquitetura Geral:** [`overview.md`](../../architecture/overview.md)

---

## Suporte

Para dúvidas ou problemas:
1. Consulte a documentação completa em `docs/`
2. Verifique os testes em `tests/Core.Tests/Math/` para exemplos
3. Abra uma issue no repositório do projeto

---

**Última atualização:** 2026-05-08  
**Versão do documento:** 1.0.0

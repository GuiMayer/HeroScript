# Delta Operations - Quick Reference

Referência rápida para o sistema de operações delta do Hero-Engine.

## Estrutura Básica

```json
{
  "RESOURCE_NAME": {
    "$delta": {
      "$op": "OPERATION_TYPE",
      "$data": { ... },
      "$value": ...,
      "$target": "path.to.field",
      "$index": 0
    }
  }
}
```

## Operações

### REPLACE
Substitui o recurso inteiro.

**Campos obrigatórios:** `$data`

```json
{
  "MY_RESOURCE": {
    "$delta": {
      "$op": "REPLACE",
      "$data": {
        "field1": "value1",
        "field2": "value2"
      }
    }
  }
}
```

---

### MERGE_SHALLOW
Mescla campos no nível superior (sobrescreve objetos nested).

**Campos obrigatórios:** `$data`

```json
{
  "MY_RESOURCE": {
    "$delta": {
      "$op": "MERGE_SHALLOW",
      "$data": {
        "newField": "newValue",
        "params": { "x": 10 }
      }
    }
  }
}
```

**Comportamento:**
- Campos em `$data` sobrescrevem campos existentes
- Objetos nested são substituídos inteiros
- Campos não mencionados permanecem inalterados

---

### MERGE_DEEP
Mescla recursivamente (preserva campos nested não mencionados).

**Campos obrigatórios:** `$data`

```json
{
  "MY_RESOURCE": {
    "$delta": {
      "$op": "MERGE_DEEP",
      "$data": {
        "params": {
          "newParam": 100
        }
      }
    }
  }
}
```

**Comportamento:**
- Mescla recursivamente em todos os níveis
- Preserva campos nested não mencionados
- Ideal para adicionar/modificar parâmetros sem afetar outros

---

### ARRAY_APPEND
Adiciona elementos ao final de um array.

**Campos obrigatórios:** `$value` (array)  
**Campos opcionais:** `$target` (path para o array, se não for o recurso inteiro)

```json
{
  "MY_RESOURCE": {
    "$delta": {
      "$op": "ARRAY_APPEND",
      "$target": "operations",
      "$value": [
        { "op": "ADD", "value": 10 },
        { "op": "MULTIPLY", "value": 2 }
      ]
    }
  }
}
```

**Comportamento:**
- Adiciona todos os elementos de `$value` ao final do array
- Se `$target` não for especificado, o recurso inteiro deve ser um array

---

### ARRAY_PREPEND
Adiciona elementos ao início de um array.

**Campos obrigatórios:** `$value` (array)  
**Campos opcionais:** `$target` (path para o array)

```json
{
  "MY_RESOURCE": {
    "$delta": {
      "$op": "ARRAY_PREPEND",
      "$target": "effects",
      "$value": [
        { "type": "BUFF", "duration": 5 }
      ]
    }
  }
}
```

---

### ARRAY_REMOVE_INDEX
Remove elemento de um array por índice.

**Campos obrigatórios:** `$index` (≥ 0)  
**Campos opcionais:** `$target` (path para o array)

```json
{
  "MY_RESOURCE": {
    "$delta": {
      "$op": "ARRAY_REMOVE_INDEX",
      "$target": "effects",
      "$index": 0
    }
  }
}
```

**Comportamento:**
- Remove o elemento no índice especificado
- Índices começam em 0
- Erro se índice for negativo ou fora dos limites

---

### ARRAY_REPLACE_INDEX
Substitui elemento de um array por índice.

**Campos obrigatórios:** `$index` (≥ 0), `$value`  
**Campos opcionais:** `$target` (path para o array)

```json
{
  "MY_RESOURCE": {
    "$delta": {
      "$op": "ARRAY_REPLACE_INDEX",
      "$target": "operations",
      "$index": 1,
      "$value": { "op": "MULTIPLY", "value": 3 }
    }
  }
}
```

---

### FIELD_DELETE
Remove um campo específico (suporta paths nested com `.`).

**Campos obrigatórios:** `$target` (path do campo)

```json
{
  "MY_RESOURCE": {
    "$delta": {
      "$op": "FIELD_DELETE",
      "$target": "params.OLD_PARAM"
    }
  }
}
```

**Comportamento:**
- Remove o campo especificado
- Suporta paths nested: `"params.subfield.value"`
- Não gera erro se o campo não existir

---

### DELETE
Remove o recurso inteiro da herança.

**Campos obrigatórios:** nenhum

```json
{
  "UNWANTED_RESOURCE": {
    "$delta": {
      "$op": "DELETE"
    }
  }
}
```

**Comportamento:**
- Remove o recurso completamente
- Útil para desabilitar recursos herdados

---

## Exemplos Práticos

### Exemplo 1: Modificar Parâmetro Específico

**Base:**
```json
{
  "FIREBALL": {
    "damage": { "base": 100, "scaling": 1.5 },
    "manaCost": 50
  }
}
```

**Delta:**
```json
{
  "FIREBALL": {
    "$delta": {
      "$op": "MERGE_DEEP",
      "$data": {
        "damage": { "base": 120 }
      }
    }
  }
}
```

**Resultado:**
```json
{
  "FIREBALL": {
    "damage": { "base": 120, "scaling": 1.5 },
    "manaCost": 50
  }
}
```

---

### Exemplo 2: Adicionar Efeito a Habilidade

**Base:**
```json
{
  "SHIELD_BASH": {
    "effects": [
      { "type": "STUN", "duration": 2 }
    ]
  }
}
```

**Delta:**
```json
{
  "SHIELD_BASH": {
    "$delta": {
      "$op": "ARRAY_APPEND",
      "$target": "effects",
      "$value": [
        { "type": "KNOCKBACK", "distance": 5 }
      ]
    }
  }
}
```

**Resultado:**
```json
{
  "SHIELD_BASH": {
    "effects": [
      { "type": "STUN", "duration": 2 },
      { "type": "KNOCKBACK", "distance": 5 }
    ]
  }
}
```

---

### Exemplo 3: Remover Parâmetro Obsoleto

**Base:**
```json
{
  "HYPERBOLIC_CURVE": {
    "params": {
      "SCALING_VALUE": 100,
      "OLD_PARAM": 50,
      "BASE_VALUE": 10
    }
  }
}
```

**Delta:**
```json
{
  "HYPERBOLIC_CURVE": {
    "$delta": {
      "$op": "FIELD_DELETE",
      "$target": "params.OLD_PARAM"
    }
  }
}
```

**Resultado:**
```json
{
  "HYPERBOLIC_CURVE": {
    "params": {
      "SCALING_VALUE": 100,
      "BASE_VALUE": 10
    }
  }
}
```

---

### Exemplo 4: Cadeia de Herança (3 Níveis)

**Base (alisyum):**
```json
{
  "FIREBALL": {
    "damage": { "base": 100 },
    "effects": [{ "type": "BURN", "duration": 3 }]
  }
}
```

**Nível 1 (test-orc-mod):**
```json
{
  "FIREBALL": {
    "$delta": {
      "$op": "MERGE_DEEP",
      "$data": {
        "damage": { "base": 120 }
      }
    }
  }
}
```

**Nível 2 (test-orc-mod-hardcore):**
```json
{
  "FIREBALL": {
    "$delta": {
      "$op": "ARRAY_APPEND",
      "$target": "effects",
      "$value": [
        { "type": "EXPLOSION", "radius": 2, "damage": 50 }
      ]
    }
  }
}
```

**Resultado final (test-orc-mod-hardcore):**
```json
{
  "FIREBALL": {
    "damage": { "base": 120 },
    "effects": [
      { "type": "BURN", "duration": 3 },
      { "type": "EXPLOSION", "radius": 2, "damage": 50 }
    ]
  }
}
```

---

## Validação

O sistema valida automaticamente:

| Operação | Validações |
|----------|-----------|
| `REPLACE` | Requer `$data` não vazio |
| `MERGE_SHALLOW` | Requer `$data` não vazio |
| `MERGE_DEEP` | Requer `$data` não vazio |
| `ARRAY_APPEND` | Requer `$value` (array) |
| `ARRAY_PREPEND` | Requer `$value` (array) |
| `ARRAY_REMOVE_INDEX` | Requer `$index` ≥ 0 |
| `ARRAY_REPLACE_INDEX` | Requer `$index` ≥ 0 e `$value` |
| `FIELD_DELETE` | Requer `$target` não vazio |
| `DELETE` | Nenhuma validação adicional |

**Erros comuns:**
- `$index` negativo → Erro de validação
- `$value` não é array em operações de array → Erro de validação
- `$target` vazio em `FIELD_DELETE` → Erro de validação
- `$data` vazio em operações de merge → Erro de validação

---

## Compatibilidade

**Modo legado:** Recursos sem `$delta` são tratados como `REPLACE` implícito.

**Exemplo legado (ainda funciona):**
```json
{
  "MY_RESOURCE": {
    "field1": "value1",
    "field2": "value2"
  }
}
```

É equivalente a:
```json
{
  "MY_RESOURCE": {
    "$delta": {
      "$op": "REPLACE",
      "$data": {
        "field1": "value1",
        "field2": "value2"
      }
    }
  }
}
```

---

## Dicas

1. **Use MERGE_DEEP para modificações cirúrgicas** - Preserva campos não mencionados
2. **Use REPLACE apenas quando necessário** - Substitui o recurso inteiro
3. **Combine operações em múltiplos deltas** - Aplique o mesmo recurso várias vezes na cadeia
4. **Teste cadeias longas** - Verifique o resultado final com 3+ níveis de herança
5. **Documente suas mudanças** - Use campos `description` para explicar modificações

---

## Arquitetura

**Classes principais:**
- `Core/Config/Delta/DeltaOperationType.cs` - Enum de operações
- `Core/Config/Delta/DeltaDefinition.cs` - Estrutura de delta
- `Core/Config/Delta/DeltaValidator.cs` - Validação
- `Core/Config/Delta/DeltaMerger.cs` - Aplicação de deltas
- `Core/Config/ResourceLoader.cs` - Carregador genérico

**Uso:**
```csharp
var loader = new ResourceLoader<Dictionary<string, JsonElement>>(
    configManager,
    "Pipelines/MathFormulas.json"
);
var resources = loader.LoadResources();
```

---

**Versão:** 2.0.0  
**Data:** 2026-05-07  
**Autor:** Hero-Engine Team

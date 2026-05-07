# Sistema de Configuração - Hero-Engine

## Visão Geral

O Hero-Engine implementa um sistema de configuração modular baseado em **herança delta**, permitindo que mods e distribuições customizadas sobrescrevam apenas as partes necessárias da configuração base, mantendo compatibilidade e facilitando atualizações.

## Conceitos Fundamentais

### 1. Configurações (Configs)

Uma **config** é um conjunto completo de recursos do jogo (fórmulas matemáticas, assets, scripts, etc.) armazenado em uma pasta específica.

**Localização das configs:**
- `user://` → `%APPDATA%\HeroScript\` (Windows)
- Cada config tem sua própria pasta: `user://alisyum/`, `user://my-mod/`, etc.

### 2. Herança Delta

Configs podem **herdar** de outras configs, sobrescrevendo apenas os arquivos que precisam ser modificados. Isso permite:

- **Mods leves**: Um mod pode ter apenas 2-3 fórmulas modificadas em vez de duplicar todas as 100+ fórmulas base
- **Atualizações fáceis**: Quando a config base é atualizada, os mods herdam automaticamente as melhorias
- **Compatibilidade**: Mods que não conflitam podem ser combinados

**Exemplo de cadeia de herança:**
```
alisyum (base oficial)
  └─> my-orc-mod (aumenta força de orcs)
       └─> my-orc-mod-hardcore (aumenta ainda mais + adiciona dificuldade)
```

### 3. Estrutura de uma Config

```
user://my-config/
├── config.json                    # Metadados da config
├── Resources/
│   └── Pipelines/
│       └── MathFormulas.json      # Fórmulas matemáticas (delta)
├── runs/                          # Logs de execução
└── saves/                         # Saves do jogo
```

## Arquivo config.json

O arquivo `config.json` contém os metadados da configuração:

```json
{
  "name": "Orc Warrior Mod",
  "version": "0.1.0",
  "author": "modder-example",
  "description": "Mod que aumenta força de orcs",
  "parent": "alisyum",
  "git_repo": null,
  "git_hash": null,
  "created_at": "2026-05-07"
}
```

**Campos:**
- `name`: Nome legível da config
- `version`: Versão semântica (major.minor.patch)
- `author`: Autor da config
- `description`: Descrição curta
- `parent`: Config pai (null = config base)
- `git_repo`: URL do repositório Git (opcional)
- `git_hash`: Hash do commit Git (opcional)
- `created_at`: Data de criação

## MathFormulas.json (Delta)

O arquivo `MathFormulas.json` em uma config filha contém **apenas as fórmulas que você quer modificar ou adicionar**.

**Exemplo - Config base (alisyum):**
```json
{
  "HYPERBOLIC_CURVE": {
    "description": "Curva hiperbólica para retornos decrescentes",
    "params": { "base": 100.0, "scale": 100.0 },
    "operations": [
      { "op": "MULTIPLY", "value": "params.scale" },
      { "op": "DIVIDE_INVERSE", "value": "params.base" }
    ]
  },
  "LINEAR_ADDITIVE": { ... },
  "EXPONENTIAL_SCALING": { ... }
  // ... mais 14 fórmulas
}
```

**Exemplo - Mod (test-orc-mod):**
```json
{
  "HYPERBOLIC_CURVE": {
    "description": "Curva hiperbólica - VERSÃO ORC BUFFADA",
    "params": { "base": 100.0, "scale": 150.0 },
    "operations": [
      { "op": "MULTIPLY", "value": "params.scale" },
      { "op": "DIVIDE_INVERSE", "value": "params.base" }
    ]
  },
  "ORC_RAGE_SCALING": {
    "description": "Escala de dano de fúria específica para orcs",
    "params": { "base_multiplier": 1.5, "rage_exponent": 2.0 },
    "operations": [
      { "op": "MULTIPLY", "value": "params.base_multiplier" },
      { "op": "POW", "value": "params.rage_exponent" }
    ]
  }
}
```

**Resultado final ao carregar test-orc-mod:**
- 16 fórmulas herdadas de alisyum (inalteradas)
- 1 fórmula sobrescrita: `HYPERBOLIC_CURVE` (versão buffada)
- 1 fórmula nova: `ORC_RAGE_SCALING`
- **Total: 18 fórmulas**

## Como Funciona a Herança

### 1. Resolução da Cadeia de Herança

Quando você carrega uma config, o sistema:

1. Resolve a cadeia completa: `test-orc-mod` → `alisyum` → (fim)
2. Detecta ciclos: Se A herda de B e B herda de A, lança erro
3. Valida que todos os pais existem

### 2. Carregamento de Fórmulas

O `MathEngine` carrega as fórmulas na ordem:

1. **Base primeiro**: Carrega todas as fórmulas de `alisyum`
2. **Sobrescreve**: Carrega fórmulas de `test-orc-mod`, sobrescrevendo as que já existem
3. **Rastreia origem**: Mantém registro de qual config forneceu cada fórmula

**Logs de exemplo:**
```
[MathEngine] Loading formulas from chain: alisyum -> test-orc-mod
[MathEngine] Loaded 17 formulas from MathFormulas.json (alisyum)
[MathEngine] Override: HYPERBOLIC_CURVE (from test-orc-mod)
[MathEngine] Loaded 2 formulas from MathFormulas.json (test-orc-mod)
[MathEngine] Loaded 18 formulas total
```

### 3. Validação

O `ConfigValidator` verifica:

- ✓ Pasta da config existe
- ✓ `config.json` existe e é válido
- ✓ `Resources/Pipelines/MathFormulas.json` existe
- ✓ Config pai existe (se especificado)
- ✓ Não há ciclos de herança

## Uso via CLI

### Comandos Disponíveis

```bash
# Listar configs disponíveis
dotnet run --project Core -- --list

# Carregar config específica
dotnet run --project Core -- --config alisyum

# Carregar config e executar testes
dotnet run --project Core -- --config test-orc-mod --test

# Mostrar ajuda
dotnet run --project Core -- --help
```

### Arquivos .bat de Exemplo

O projeto inclui scripts .bat para facilitar o uso:

- `list-configs.bat` - Lista todas as configs
- `run-alisyum.bat` - Executa com config padrão
- `run-orc-mod.bat` - Executa com mod de teste
- `run-tests.bat` - Executa todos os testes

## Criando Sua Própria Config

### Passo 1: Criar Estrutura de Pastas

```bash
mkdir %APPDATA%\HeroScript\my-mod
mkdir %APPDATA%\HeroScript\my-mod\Resources\Pipelines
mkdir %APPDATA%\HeroScript\my-mod\runs
mkdir %APPDATA%\HeroScript\my-mod\saves
```

### Passo 2: Criar config.json

```json
{
  "name": "My Awesome Mod",
  "version": "1.0.0",
  "author": "seu-nome",
  "description": "Descrição do seu mod",
  "parent": "alisyum",
  "git_repo": null,
  "git_hash": null,
  "created_at": "2026-05-07"
}
```

### Passo 3: Criar MathFormulas.json (Delta)

Inclua **apenas** as fórmulas que você quer modificar ou adicionar:

```json
{
  "MY_CUSTOM_FORMULA": {
    "description": "Minha fórmula customizada",
    "params": {
      "multiplier": 2.0,
      "bonus": 10.0
    },
    "operations": [
      { "op": "MULTIPLY", "value": "params.multiplier" },
      { "op": "ADD", "value": "params.bonus" }
    ]
  }
}
```

### Passo 4: Testar

```bash
dotnet run --project Core -- --config my-mod --test
```

## Arquitetura do Sistema

### Classes Principais

#### ConfigMetadata
- Representa os metadados de uma config
- Serializado/deserializado de `config.json`

#### ConfigValidator
- Valida estrutura de configs
- Detecta ciclos de herança
- Verifica arquivos obrigatórios

#### ConfigManager
- Gerencia carregamento de configs
- Resolve cadeias de herança
- Fornece API para listar configs disponíveis

#### MathEngine
- Carrega fórmulas com suporte a herança
- Mantém cache de fórmulas
- Rastreia origem de cada fórmula

### Fluxo de Carregamento

```
1. Main.cs
   └─> ConfigManager.LoadConfig("test-orc-mod")
       ├─> ConfigValidator.ValidateConfig("test-orc-mod")
       ├─> ConfigManager.ResolveInheritanceChain("test-orc-mod")
       │   └─> ["alisyum", "test-orc-mod"]
       └─> MathEngine.InvalidateCache()
           └─> MathEngine.LoadFormulasFromChain(["alisyum", "test-orc-mod"])
               ├─> LoadFromConfig("alisyum") → 17 fórmulas
               └─> LoadFromConfig("test-orc-mod") → 2 fórmulas (1 override + 1 nova)
```

## Modo Dev (Fallback)

Quando uma config não existe em `user://`, o sistema usa o **modo dev**:

- Carrega recursos de `Core/Resources/` (pasta do executável)
- Útil para desenvolvimento e testes
- Não requer instalação de configs em `user://`

**Exemplo:**
```bash
# Se "alisyum" não existir em user://, usa dev fallback
dotnet run --project Core -- --config alisyum
```

## Testes

O sistema inclui testes abrangentes:

### MathEngineTests
- Testa todas as 17 fórmulas base
- Testa parâmetros customizados
- Testa casos de erro

### ConfigTests
- Testa validação de configs
- Testa herança delta
- Testa detecção de ciclos
- Testa rastreamento de origens

**Executar testes:**
```bash
dotnet run --project Core -- --test
```

## Boas Práticas

### Para Modders

1. **Minimize deltas**: Inclua apenas o que você realmente modifica
2. **Documente mudanças**: Use descrições claras nas fórmulas
3. **Teste herança**: Verifique se seu mod funciona com a base atualizada
4. **Versione corretamente**: Use versionamento semântico

### Para Desenvolvedores

1. **Mantenha compatibilidade**: Não remova fórmulas da base sem aviso
2. **Documente breaking changes**: Avise quando mudanças quebram mods
3. **Teste cadeias longas**: Teste herança com 3+ níveis
4. **Valide sempre**: Use `ConfigValidator` antes de carregar

## Sistema Delta Estruturado

A partir da versão 2.0, o Hero-Engine implementa um **sistema delta estruturado** que permite operações granulares sobre recursos JSON, indo além da simples substituição de arquivos inteiros.

### Motivação

O sistema anterior tinha limitações:
- **Substituição total**: Modificar um único parâmetro exigia duplicar o recurso inteiro
- **Conflitos**: Mods que modificavam o mesmo recurso não podiam coexistir
- **Manutenção**: Atualizações na base quebravam mods que duplicavam recursos

O sistema delta estruturado resolve isso permitindo operações cirúrgicas sobre recursos JSON.

### Estrutura Delta

Um delta é identificado pela chave especial `$delta` dentro de um recurso:

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

### Operações Disponíveis

#### 1. REPLACE
Substitui o recurso inteiro (comportamento legado).

```json
{
  "ORC_RAGE_SCALING": {
    "$delta": {
      "$op": "REPLACE",
      "$data": {
        "description": "Nova fórmula completamente diferente",
        "params": { "BASE": 2.0 },
        "operations": [...]
      }
    }
  }
}
```

#### 2. MERGE_SHALLOW
Mescla campos no nível superior, sobrescrevendo objetos nested.

```json
{
  "HYPERBOLIC_CURVE": {
    "$delta": {
      "$op": "MERGE_SHALLOW",
      "$data": {
        "description": "Nova descrição",
        "params": { "SCALING_VALUE": 80 }
      }
    }
  }
}
```

**Resultado**: `description` atualizada, `params` substituído inteiro.

#### 3. MERGE_DEEP
Mescla recursivamente, preservando campos não mencionados.

```json
{
  "EXPONENTIAL_SCALING": {
    "$delta": {
      "$op": "MERGE_DEEP",
      "$data": {
        "params": {
          "EXPONENT_VALUE": 2.5,
          "ORC_BONUS": 10
        }
      }
    }
  }
}
```

**Resultado**: Adiciona `ORC_BONUS` sem remover outros parâmetros existentes.

#### 4. ARRAY_APPEND
Adiciona elementos ao final de um array.

```json
{
  "EXPONENTIAL_SCALING": {
    "$delta": {
      "$op": "ARRAY_APPEND",
      "$target": "operations",
      "$value": [
        { "op": "MULTIPLY", "value": 1.2 },
        { "op": "ADD", "value": 5 }
      ]
    }
  }
}
```

#### 5. ARRAY_PREPEND
Adiciona elementos ao início de um array.

```json
{
  "SKILL_EFFECTS": {
    "$delta": {
      "$op": "ARRAY_PREPEND",
      "$target": "effects",
      "$value": [
        { "type": "BUFF", "duration": 10 }
      ]
    }
  }
}
```

#### 6. ARRAY_REMOVE_INDEX
Remove elemento de um array por índice.

```json
{
  "SKILL_EFFECTS": {
    "$delta": {
      "$op": "ARRAY_REMOVE_INDEX",
      "$target": "effects",
      "$index": 0
    }
  }
}
```

#### 7. ARRAY_REPLACE_INDEX
Substitui elemento de um array por índice.

```json
{
  "SKILL_EFFECTS": {
    "$delta": {
      "$op": "ARRAY_REPLACE_INDEX",
      "$target": "effects",
      "$index": 1,
      "$value": { "type": "STUN", "duration": 3 }
    }
  }
}
```

#### 8. FIELD_DELETE
Remove um campo específico (suporta paths nested com `.`).

```json
{
  "HYPERBOLIC_CURVE": {
    "$delta": {
      "$op": "FIELD_DELETE",
      "$target": "params.SCALING_VALUE"
    }
  }
}
```

#### 9. DELETE
Remove o recurso inteiro da herança.

```json
{
  "LOGARITHMIC_SCALING": {
    "$delta": {
      "$op": "DELETE"
    }
  }
}
```

### Exemplo Completo: Cadeia de 3 Níveis

**Base (alisyum):**
```json
{
  "FIREBALL": {
    "name": "Fireball",
    "manaCost": 50,
    "damage": { "base": 100 },
    "effects": [
      { "type": "BURN", "duration": 3 }
    ]
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
        { "type": "EXPLOSION", "radius": 2 }
      ]
    }
  }
}
```

**Resultado final ao carregar test-orc-mod-hardcore:**
```json
{
  "FIREBALL": {
    "name": "Fireball",
    "manaCost": 50,
    "damage": { "base": 120 },
    "effects": [
      { "type": "BURN", "duration": 3 },
      { "type": "EXPLOSION", "radius": 2 }
    ]
  }
}
```

### Validação

O sistema valida deltas automaticamente:
- `REPLACE`, `MERGE_SHALLOW`, `MERGE_DEEP` requerem `$data`
- `ARRAY_APPEND`, `ARRAY_PREPEND` requerem `$value` (array)
- `ARRAY_REMOVE_INDEX`, `ARRAY_REPLACE_INDEX` requerem `$index` (≥ 0)
- `FIELD_DELETE` requer `$target` (string não vazia)
- `DELETE` não requer campos adicionais

Erros de validação são reportados com mensagens claras indicando o problema.

### Genericidade

O sistema delta funciona com **qualquer tipo de recurso JSON**:
- `MathFormulas.json` - Fórmulas matemáticas
- `Skills.json` - Habilidades de personagens
- `Items.json` - Itens do jogo
- `Quests.json` - Missões
- Qualquer outro recurso JSON que você criar

### Arquitetura

**Classes principais:**
- `DeltaOperationType` - Enum com todas as operações
- `DeltaDefinition` - Estrutura de um delta
- `DeltaValidator` - Validação de deltas
- `DeltaMerger` - Aplicação de deltas
- `ResourceLoader<T>` - Carregador genérico com suporte a delta
- `FormulaLoader` - Implementação específica para fórmulas

### Modo de Compatibilidade

Recursos sem `$delta` são tratados como `REPLACE` implícito, mantendo compatibilidade com o formato legado.

## Roadmap Futuro

- [x] Sistema delta estruturado com 9 operações
- [x] Validação automática de deltas
- [x] Suporte genérico para qualquer tipo de recurso JSON
- [ ] Sistema de dependências entre mods
- [ ] Validação de compatibilidade de versões
- [ ] Hot-reload de configs em runtime
- [ ] Interface gráfica para gerenciar configs
- [ ] Marketplace de mods

## Referências

- `Core/Config/ConfigMetadata.cs` - Estrutura de metadados
- `Core/Config/ConfigValidator.cs` - Validação de configs
- `Core/Config/ConfigManager.cs` - Gerenciamento de configs
- `Core/Math/MathEngine.cs` - Engine de fórmulas com herança
- `Core/Tests/ConfigTests.cs` - Testes do sistema

---

**Versão:** 2.0.0  
**Data:** 2026-05-07  
**Autor:** Hero-Engine Team

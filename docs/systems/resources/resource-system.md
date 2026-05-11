# Plano de Implementação - Sistema de Recursos

**Data:** 2026-05-09  
**Autor:** Análise técnica do sistema de recursos  
**Objetivo:** Implementar regeneração de recursos e melhorias para suportar múltiplos gêneros de card games

---

## Sumário Executivo

Este documento detalha o plano completo de implementação para resolver os gaps críticos identificados no sistema de recursos do HeroScript. O sistema atual está **80% pronto**, mas falta o processamento de regeneração - um bloqueador crítico para gameplay funcional.

### Escopo Total
- **6 Fases de implementação**
- **5-9 dias de trabalho** (dependendo do escopo escolhido)
- **80+ testes novos**
- **25+ recursos de exemplo** para diferentes jogos

### Impacto
Desbloqueia suporte completo para:
- Slay the Spire (energy regenera, block zera)
- Balatro (hands/discards regeneram)
- Magic: The Gathering (mana baseado em terrenos)
- Yu-Gi-Oh (normal summons regeneram)
- Hearthstone (mana crystals crescem por turno)
- Inscryption (energy incremental)

---

## 1. CONTEXT - Estado Atual do Sistema

### 1.1 Arquitetura Existente

**Componentes implementados:**

```
src/Core/Resources/
├── IResourceManager.cs          ✅ Interface completa
├── ResourceManager.cs           ✅ Implementação funcional
├── ResourceDefinition.cs        ✅ Metadados de recursos
├── ResourcePool.cs              ✅ Pool imutável com operações
├── ResourceCategory.cs          ✅ Enum de categorias
└── RegenerationConfig.cs        ✅ Config de regeneração (não processada)

src/Core/Combat/
└── EntityResourceState.cs       ✅ Estado de recursos por entidade

data/configs/default/Resources/resources/
├── health.json                  ✅ HP (VITAL)
├── energy.json                  ✅ Energy (TACTICAL)
├── mana.json                    ✅ Mana (TACTICAL, regenera)
└── shield.json                  ✅ Shield (TEMPORARY, pode exceder max)
```

### 1.2 Funcionalidades Existentes

1. **Carregamento de definições**
   - Carrega de JSON via IResourceLoader
   - Suporta herança de configs
   - **Gap:** Lista hardcoded de recursos conhecidos

2. **Criação de pools**
   - Cria instância imutável de recurso
   - Valida definição antes de criar

3. **Operações imutáveis**
   - Spend(amount) - Gasta recurso
   - Gain(amount) - Ganha recurso (respeita max)
   - Set(value) - Define valor direto
   - Reset() - Reseta para máximo
   - CanAfford(cost) - Valida custo

4. **Flags especiais:**
   - CanBeNegative - Permite valores negativos
   - CanExceedMax - Permite ultrapassar máximo

5. **Regeneração (não implementada):**
   - RegenerationConfig existe
   - **Gap:** Nenhum código processa regeneração

6. **Categorias:**
   - VITAL - Morte se chegar a 0
   - TACTICAL - Consumo em combate
   - SPECIAL - Mecânicas únicas
   - TEMPORARY - Buffs temporários


### 1.3 Gaps Críticos Identificados

#### Gap 1: Regeneração não é processada ⭐ CRÍTICO
**Impacto:** Bloqueia Slay the Spire, Balatro, Hearthstone, Inscryption

**Problema:**
- RegenerationConfig existe no JSON
- Mas nenhum código processa AmountPerTurn ou Formula
- Energy não regenera, mana não regenera, block não zera

**Exemplo quebrado:**
```json
// energy.json
"regeneration": { "enabled": true, "amountPerTurn": 3, "timing": "START_TURN" }
```
→ Energy nunca regenera 3 no início do turno

---

#### Gap 2: Lista hardcoded de recursos ⚠️ IMPORTANTE
**Impacto:** Limita extensibilidade para jogos customizados

**Problema:**
```csharp
// ResourceManager.cs linha 39
var resourceNames = new[] { "health", "energy", "mana", "shield", "stamina", "rage" };
```
→ Adicionar novo recurso requer modificar código

**Solução esperada:**
- Escanear diretório resources/ automaticamente
- Carregar todos os .json encontrados

---

#### Gap 3: Fórmulas dinâmicas sem contexto ⚠️ IMPORTANTE
**Impacto:** Bloqueia MTG (mana = terrenos), Hearthstone (mana = turno)

**Problema:**
```json
"regeneration": { "formula": "plains_count" }
```
→ Não há contexto disponível para avaliar plains_count

**Solução esperada:**
- Passar contexto para IMathEngine.Evaluate()
- Contexto inclui: turn_number, land_count, mana_crystals, etc.

---

#### Gap 4: Recursos temporários não zeram ⚠️ IMPORTANTE
**Impacto:** Bloqueia Slay the Spire (block deve zerar)

**Problema:**
```json
// block.json
"regeneration": { "enabled": true, "amountPerTurn": -999, "timing": "END_TURN" }
```
→ Não há lógica para "zerar" recurso (amountPerTurn negativo não funciona)

**Solução esperada:**
- Adicionar flag ResetToZero ou ResetToMin
- Processar no fim do turno

---

#### Gap 5: Sem integração com Turn Management ⭐ CRÍTICO
**Impacto:** Regeneração precisa de turnos para funcionar

**Problema:**
- Não existe TurnManager ainda
- Não há conceito de "início do turno" ou "fim do turno"
- Regeneração não pode ser processada

**Solução esperada:**
- Criar TurnManager (Fase 2.2 do roadmap)
- TurnManager chama ResourceManager.ProcessRegeneration()

---

#### Gap 6: Sem suporte a recursos compostos ⚠️ MÉDIA
**Impacto:** Limita mecânicas avançadas

**Problema:**
- Hearthstone: current_mana regenera baseado em mana_crystals
- MTG: white_mana regenera baseado em plains_count
- Não há forma de referenciar outro recurso na fórmula

**Solução esperada:**
- Contexto de fórmula inclui todos os recursos da entidade
- Exemplo: formula: "mana_crystals" acessa valor de outro recurso

---

#### Gap 7: Sem eventos de mudança de recurso ⚠️ MÉDIA
**Impacto:** UI não pode reagir a mudanças

**Problema:**
- ResourcePool.Spend() / Gain() não publicam eventos
- Frontend não sabe quando recurso mudou
- Não pode animar mudanças

**Solução esperada:**
- Publicar ResourceChangedEvent via IEventBus
- Incluir: EntityId, ResourceId, OldValue, NewValue, ChangeType

---

## 2. Análise de Múltiplos Gêneros de Card Games

### 2.1 Padrões Comuns Identificados

| Padrão | Jogos | Suporte Atual | Gap |
|--------|-------|---------------|-----|
| **Regeneração fixa por turno** | Slay the Spire (energy), Balatro (hands), Yu-Gi-Oh (summons) | ✅ Config existe | ❌ Não processado |
| **Regeneração com fórmula** | MTG (mana = terrenos), Hearthstone (mana = turno) | ✅ Campo existe | ❌ Não processado |
| **Recursos que excedem max** | MTG (life), Hearthstone (armor), Balatro (chips) | ✅ CanExceedMax | ✅ Funciona |
| **Recursos temporários** | Slay the Spire (block), Shield | ✅ TEMPORARY | ⚠️ Precisa zerar |
| **Múltiplos recursos do mesmo tipo** | MTG (5 cores de mana) | ✅ Suporta | ⚠️ Lista hardcoded |
| **Recursos negativos** | Debt, Corruption | ✅ CanBeNegative | ✅ Funciona |
| **Recursos baseados em contexto** | MTG (mana = terrenos), Hearthstone (mana = turno) | ⚠️ Fórmula existe | ❌ Sem contexto |
| **Win conditions alternativas** | MTG (poison = 10) | ❌ Não existe | ❌ Precisa lógica |


### 2.2 Exemplos de Recursos por Jogo

#### Slay the Spire
```json
// energy.json - Regenera 3 no início do turno
{
  "resourceId": "energy",
  "category": "TACTICAL",
  "defaultMax": 3,
  "regeneration": {
    "enabled": true,
    "amountPerTurn": 3,
    "timing": "START_TURN"
  }
}

// block.json - Zera no fim do turno
{
  "resourceId": "block",
  "category": "TEMPORARY",
  "defaultMax": 999,
  "canExceedMax": true,
  "regeneration": {
    "enabled": true,
    "amountPerTurn": -999,
    "timing": "END_TURN"
  }
}
```

#### Balatro
```json
// hands.json - Reseta para 4 no início da rodada
{
  "resourceId": "hands",
  "category": "TACTICAL",
  "defaultMax": 4,
  "regeneration": {
    "enabled": true,
    "amountPerTurn": 4,
    "timing": "START_TURN"
  }
}

// money.json - Ganha  por rodada
{
  "resourceId": "money",
  "category": "SPECIAL",
  "defaultMax": 999,
  "regeneration": {
    "enabled": true,
    "amountPerTurn": 4,
    "timing": "END_TURN"
  }
}
```

#### Hearthstone
```json
// mana_crystals.json - Cresce com turno
{
  "resourceId": "mana_crystals",
  "category": "TACTICAL",
  "defaultMax": 10,
  "regeneration": {
    "enabled": true,
    "formula": "min(turn_number, 10)",
    "timing": "START_TURN"
  }
}

// current_mana.json - Regenera baseado em cristais
{
  "resourceId": "current_mana",
  "category": "TACTICAL",
  "defaultMax": 10,
  "regeneration": {
    "enabled": true,
    "formula": "mana_crystals",
    "timing": "START_TURN"
  }
}
```

#### Magic: The Gathering
```json
// white_mana.json - Baseado em terrenos
{
  "resourceId": "white_mana",
  "category": "TACTICAL",
  "defaultMax": 10,
  "regeneration": {
    "enabled": true,
    "formula": "plains_count",
    "timing": "START_TURN"
  },
  "tags": ["mana", "white"]
}
```

---

## 3. PLANO DE IMPLEMENTAÇÃO

### FASE 1: Regeneração Básica (2-3 dias) ⭐ CRÍTICO

#### Step 1.1: Criar IResourceRegenerationProcessor (1-2h)

**Arquivo a criar:** src/Core/Resources/IResourceRegenerationProcessor.cs

**Interface:**
```csharp
public interface IResourceRegenerationProcessor
{
    Result<EntityResourceState> ProcessRegeneration(
        EntityResourceState entityResourceState,
        RegenerationTiming timing,
        Dictionary<string, float>? context = null);
    
    float CalculateRegenerationAmount(
        ResourceDefinition definition,
        ResourcePool currentPool,
        Dictionary<string, float>? context = null);
}
```

**Responsabilidades:**
- Processar regeneração de todos os recursos de uma entidade
- Calcular quantidade baseado em AmountPerTurn ou Formula
- Filtrar por Timing (START_TURN, END_TURN, OUT_OF_COMBAT)
- Retornar novo EntityResourceState imutável

**Testes:**
- Interface compila
- Pode ser injetada via DI

---

#### Step 1.2: Implementar ResourceRegenerationProcessor (3-4h)

**Arquivo a criar:** src/Core/Resources/ResourceRegenerationProcessor.cs

**Lógica principal:**
1. Iterar sobre todos os recursos da entidade
2. Para cada recurso:
   - Verificar se Regeneration.Enabled == true
   - Verificar se Regeneration.Timing corresponde ao timing atual
   - Calcular quantidade via CalculateRegenerationAmount()
   - Aplicar via pool.Gain(amount) ou pool.Spend(-amount)
   - Publicar ResourceRegeneratedEvent via IEventBus
3. Retornar novo EntityResourceState com recursos atualizados

**Método CalculateRegenerationAmount():**
1. Se Formula não é nulo:
   - Criar contexto com: current, max, min, percent
   - Merge com contexto externo (turn_number, land_count, etc.)
   - Avaliar via IMathEngine.BuildFromFormula()
   - Fallback para AmountPerTurn se fórmula falhar
2. Senão, retornar AmountPerTurn

**Dependências:**
- IMathEngine (já existe)
- ILogger (já existe)
- IEventBus (já existe)

**Testes (20+ testes):**
- Regeneração fixa positiva (energy +3)
- Regeneração fixa negativa (block -999)
- Regeneração com fórmula simples (max * 0.1)
- Regeneração com fórmula e contexto (turn_number)
- Timing START_TURN processa apenas START_TURN
- Timing END_TURN processa apenas END_TURN
- Recursos sem regeneração não mudam
- Recursos com Enabled=false não mudam
- Eventos são publicados corretamente
- Fallback funciona se fórmula falhar
- Múltiplos recursos regeneram simultaneamente
- Regeneração respeita min/max do pool

---

#### Step 1.3: Criar ResourceRegeneratedEvent (30min)

**Arquivo a criar:** src/Core/Events/Domain/ResourceRegeneratedEvent.cs

**Campos:**
```csharp
public record ResourceRegeneratedEvent : DomainEvent
{
    public string EntityId { get; init; }
    public string ResourceId { get; init; }
    public float OldValue { get; init; }
    public float NewValue { get; init; }
    public float Amount { get; init; }
    public RegenerationTiming Timing { get; init; }
}
```

**Testes:**
- Serialização JSON
- Campos obrigatórios preenchidos
- Herda de DomainEvent

---

#### Step 1.4: Adicionar ProcessRegeneration ao IResourceManager (1h)

**Modificar:** src/Core/Resources/IResourceManager.cs

**Adicionar método:**
```csharp
Result<EntityResourceState> ProcessRegeneration(
    EntityResourceState entityResourceState,
    RegenerationTiming timing,
    Dictionary<string, float>? context = null);
```

**Modificar:** src/Core/Resources/ResourceManager.cs

**Adicionar:**
- Campo private readonly IResourceRegenerationProcessor _regenerationProcessor;
- Injetar no construtor
- Implementar método delegando para _regenerationProcessor

**Testes:**
- Método delega corretamente
- Retorna resultado do processor

---

#### Step 1.5: Integrar com DI (30min)

**Modificar:** src/API/Program.cs

**Adicionar:**
```csharp
builder.Services.AddSingleton<IResourceRegenerationProcessor, ResourceRegenerationProcessor>();
```

**Modificar construtor de ResourceManager para receber IResourceRegenerationProcessor**

**Testes:**
- DI resolve IResourceRegenerationProcessor
- ResourceManager recebe injeção corretamente
- API inicia sem erros

---

#### Step 1.6: Atualizar JSONs de recursos (1h)

**Modificar:** data/configs/default/Resources/resources/energy.json
- Mudar regeneration.enabled para true
- Mudar regeneration.amountPerTurn para 3

**Criar:** data/configs/default/Resources/resources/block.json
```json
{
  "resourceId": "block",
  "displayName": "Block",
  "shortName": "BL",
  "category": "TEMPORARY",
  "defaultMin": 0,
  "defaultMax": 999,
  "defaultCurrent": 0,
  "canBeNegative": false,
  "canExceedMax": true,
  "regeneration": {
    "enabled": true,
    "amountPerTurn": -999,
    "timing": "END_TURN"
  },
  "tags": ["temporary", "defensive"]
}
```

**Adicionar "block" à lista hardcoded em ResourceManager.cs linha 39**

**Testes:**
- JSONs carregam sem erro
- Validação passa
- ResourceManager.GetDefinition("block") retorna definição

---

#### Step 1.7: Criar testes de integração (2-3h)

**Arquivo a criar:** tests/Core.Tests/Resources/ResourceRegenerationIntegrationTests.cs

**Casos de teste (10+ testes):**
1. Energy regenera 3 no START_TURN
2. Mana regenera 5 no START_TURN
3. Block zera no END_TURN (regeneração negativa)
4. Recursos sem regeneração não mudam
5. Timing incorreto não regenera
6. Fórmula dinâmica funciona (max * 0.1)
7. Contexto externo é passado corretamente
8. Eventos são publicados
9. Múltiplos recursos regeneram simultaneamente
10. Regeneração respeita min/max
11. Fallback funciona se fórmula inválida


---

### FASE 2: Carregamento Dinâmico de Recursos (1 dia)

#### Step 2.1: Modificar LoadResourceDefinitions para escanear diretório (2-3h)

**Modificar:** src/Core/Resources/ResourceManager.cs

**Substituir lista hardcoded por escaneamento:**

**Antes (linha 39):**
```csharp
var resourceNames = new[] { "health", "energy", "mana", "shield", "stamina", "rage" };
```

**Depois:**
```csharp
// Escanear diretório resources/ para encontrar todos os JSONs
var resourceFiles = new HashSet<string>();

foreach (var config in configChain)
{
    var configPath = _configManager.GetConfigPath(config);
    var resourcesPath = Path.Combine(configPath, "Resources", "resources");
    
    if (Directory.Exists(resourcesPath))
    {
        var files = Directory.GetFiles(resourcesPath, "*.json", SearchOption.TopDirectoryOnly);
        foreach (var file in files)
        {
            var resourceName = Path.GetFileNameWithoutExtension(file);
            resourceFiles.Add(resourceName);
        }
    }
}

// Carregar cada recurso encontrado
foreach (var resourceName in resourceFiles)
{
    // ... resto do código de carregamento
}
```

**Testes (10+ testes):**
- Carrega recursos existentes (health, energy, mana, shield)
- Carrega recursos novos automaticamente
- Ignora arquivos não-JSON
- Herança de configs funciona (child sobrescreve parent)
- Diretório inexistente não causa erro
- Recursos duplicados usam herança
- Log mostra quantos recursos foram carregados

---

#### Step 2.2: Criar recursos de exemplo para diferentes jogos (1-2h)

**Criar arquivos JSON:**

1. data/configs/default/Resources/resources/chips.json (Balatro)
2. data/configs/default/Resources/resources/mult.json (Balatro)
3. data/configs/default/Resources/resources/hands.json (Balatro)
4. data/configs/default/Resources/resources/discards.json (Balatro)
5. data/configs/default/Resources/resources/white_mana.json (MTG)
6. data/configs/default/Resources/resources/life_points.json (Yu-Gi-Oh)
7. data/configs/default/Resources/resources/normal_summons.json (Yu-Gi-Oh)
8. data/configs/default/Resources/resources/bones.json (Inscryption)

**Testes:**
- Todos os recursos carregam automaticamente
- GetAllDefinitions() retorna 12+ recursos
- GetDefinitionsByTag("balatro") retorna 4 recursos

---

#### Step 2.3: Documentar como adicionar novos recursos (30min)

**Criar:** docs/RESOURCE_SYSTEM.md

**Conteúdo:**
- Arquitetura do sistema
- Como criar novo recurso (apenas criar JSON)
- Campos obrigatórios vs opcionais
- Exemplos de recursos para diferentes jogos
- Como usar regeneração (fixa vs fórmula)
- Como usar flags (CanBeNegative, CanExceedMax)
- Categorias disponíveis

---

### FASE 3: Contexto para Fórmulas Dinâmicas (1-2 dias)

#### Step 3.1: Criar ResourceContext record (1h)

**Arquivo a criar:** src/Core/Resources/ResourceContext.cs

```csharp
/// <summary>
/// Contexto disponível para fórmulas de regeneração.
/// </summary>
public record ResourceContext
{
    // Contexto de turno
    public int TurnNumber { get; init; }
    public string TurnPhase { get; init; } = string.Empty;
    
    // Contexto de entidade
    public string EntityId { get; init; } = string.Empty;
    public Dictionary<string, float> EntityResources { get; init; } = new();
    
    // Contexto de jogo (extensível)
    public Dictionary<string, float> GameState { get; init; } = new();
    
    /// <summary>
    /// Converte para dicionário para uso com IMathEngine.
    /// </summary>
    public Dictionary<string, float> ToDictionary()
    {
        var dict = new Dictionary<string, float>
        {
            ["turn_number"] = TurnNumber
        };
        
        // Adicionar recursos da entidade
        foreach (var (key, value) in EntityResources)
            dict[key] = value;
        
        // Adicionar game state
        foreach (var (key, value) in GameState)
            dict[key] = value;
        
        return dict;
    }
}
```

**Testes:**
- ToDictionary() inclui turn_number
- ToDictionary() inclui recursos da entidade
- ToDictionary() inclui game state

---

#### Step 3.2: Modificar ProcessRegeneration para aceitar ResourceContext (1-2h)

**Modificar:** src/Core/Resources/IResourceRegenerationProcessor.cs

**Substituir:**
```csharp
Result<EntityResourceState> ProcessRegeneration(
    EntityResourceState entityResourceState,
    RegenerationTiming timing,
    ResourceContext? context = null);  // ← Mudou de Dictionary para ResourceContext
```

**Modificar:** src/Core/Resources/ResourceRegenerationProcessor.cs

**Atualizar CalculateRegenerationAmount() para:**
1. Criar contexto base com current, max, min, percent
2. Se ResourceContext fornecido, merge com context.ToDictionary()
3. Passar para IMathEngine

**Testes:**
- Contexto é passado corretamente
- Fórmula pode acessar turn_number
- Fórmula pode acessar outros recursos (mana_crystals)
- Fórmula pode acessar game state (land_count)

---

#### Step 3.3: Criar recursos com fórmulas dinâmicas (1h)

**Criar:** data/configs/default/Resources/resources/mana_crystals.json (Hearthstone)

```json
{
  "resourceId": "mana_crystals",
  "displayName": "Mana Crystals",
  "shortName": "MC",
  "category": "TACTICAL",
  "defaultMin": 0,
  "defaultMax": 10,
  "defaultCurrent": 1,
  "canBeNegative": false,
  "canExceedMax": false,
  "regeneration": {
    "enabled": true,
    "formula": "min(turn_number, 10)",
    "timing": "START_TURN"
  },
  "tags": ["hearthstone", "mana"]
}
```

**Testes:**
- mana_crystals cresce baseado em turn_number
- current_mana regenera baseado em mana_crystals
- Fórmula min(turn_number, 10) funciona

---

#### Step 3.4: Criar testes de integração com contexto (2-3h)

**Arquivo a criar:** tests/Core.Tests/Resources/ResourceContextIntegrationTests.cs

**Casos de teste (8+ testes):**
1. Mana crystals crescem com turn_number
2. Current mana regenera baseado em mana_crystals
3. Fórmula max * 0.1 funciona
4. Fórmula min(turn_number, 10) funciona
5. Contexto inclui todos os recursos da entidade
6. Game state é acessível em fórmulas
7. Fórmula inválida usa fallback
8. Contexto nulo não causa erro

---

### FASE 4: Eventos de Mudança de Recurso (1 dia)

#### Step 4.1: Criar ResourceChangedEvent (30min)

**Arquivo a criar:** src/Core/Events/Domain/ResourceChangedEvent.cs

```csharp
public record ResourceChangedEvent : DomainEvent
{
    public string EntityId { get; init; } = string.Empty;
    public string ResourceId { get; init; } = string.Empty;
    public float OldValue { get; init; }
    public float NewValue { get; init; }
    public float Delta { get; init; }
    public ResourceChangeType ChangeType { get; init; }
}

public enum ResourceChangeType
{
    SPENT,
    GAINED,
    SET,
    REGENERATED
}
```

**Testes:**
- Serialização JSON
- Campos obrigatórios

---

#### Step 4.2: Modificar ResourcePool para publicar eventos (2-3h)

**Problema:** ResourcePool é record imutável, não tem acesso a IEventBus.

**Solução:** Criar wrapper ResourcePoolManager que publica eventos.

**Arquivo a criar:** src/Core/Resources/IResourcePoolManager.cs

```csharp
public interface IResourcePoolManager
{
    ResourcePool Spend(ResourcePool pool, float amount, string entityId);
    ResourcePool Gain(ResourcePool pool, float amount, string entityId);
    ResourcePool Set(ResourcePool pool, float value, string entityId);
    ResourcePool Reset(ResourcePool pool, string entityId);
}
```

**Lógica:**
1. Chamar método do ResourcePool (ex: pool.Spend(amount))
2. Publicar ResourceChangedEvent via IEventBus
3. Retornar novo pool

**Testes (12+ testes):**
- Spend publica evento SPENT
- Gain publica evento GAINED
- Set publica evento SET
- Reset publica evento SET
- Delta é calculado corretamente
- Eventos incluem EntityId

---

#### Step 4.3: Integrar ResourcePoolManager com CombatSystem (1-2h)

**Modificar:** src/Core/Combat/CombatSystem.cs

**Substituir chamadas diretas:**
```csharp
// Antes
var newPool = pool.Spend(cost);

// Depois
var newPool = _resourcePoolManager.Spend(pool, cost, entityId);
```

**Testes:**
- Eventos são publicados durante combate
- Spend de energy publica evento
- Gain de health publica evento

---

#### Step 4.4: Criar testes de integração de eventos (1-2h)

**Arquivo a criar:** tests/Core.Tests/Resources/ResourceEventsIntegrationTests.cs

**Casos de teste (8+ testes):**
1. Spend publica ResourceChangedEvent
2. Gain publica ResourceChangedEvent
3. Regeneração publica ResourceRegeneratedEvent
4. Múltiplas mudanças publicam múltiplos eventos
5. Eventos incluem valores corretos
6. Eventos incluem EntityId correto
7. Subscribers recebem eventos
8. Eventos são persistidos no EventStore

---

#### Step 4.5: Criar documentação de eventos (30min)

**Arquivo a criar:** docs/RESOURCE_EVENTS.md

**Conteúdo:**
- Tipos de eventos disponíveis
- Quando cada evento é publicado
- Como subscribir a eventos
- Exemplos de uso (UI reagindo a mudanças)
- Performance considerations


---

### FASE 5: Melhorias e Polish (1-2 dias) - OPCIONAL

#### Step 5.1: Adicionar validação de fórmulas no carregamento (2-3h)

**Objetivo:** Detectar fórmulas inválidas ao carregar recursos, não em runtime.

**Modificar:** src/Core/Resources/ResourceManager.cs

**Adicionar validação:**
```csharp
private Result ValidateRegenerationFormula(ResourceDefinition definition)
{
    if (definition.Regeneration?.Formula == null)
        return Result.Success();
    
    try
    {
        // Tentar compilar fórmula com contexto vazio
        var testContext = new Dictionary<string, float>
        {
            ["current"] = 0,
            ["max"] = 100,
            ["min"] = 0,
            ["percent"] = 0
        };
        
        var expr = _mathEngine.BuildFromFormula(definition.Regeneration.Formula, 0, testContext);
        expr.Evaluate();
        
        return Result.Success();
    }
    catch (Exception ex)
    {
        return Result.Failure("Invalid regeneration formula '{definition.Regeneration.Formula}': {ex.Message}");
    }
}
```

**Testes:**
- Fórmula válida passa validação
- Fórmula inválida falha no carregamento
- Warning é logado para fórmulas inválidas
- Recurso com fórmula inválida não é carregado

---

#### Step 5.2: Adicionar suporte a recursos "resetáveis" (1-2h)

**Objetivo:** Recursos que resetam para valor específico ao invés de regenerar.

**Modificar:** src/Core/Resources/RegenerationConfig.cs

**Adicionar campos:**
```csharp
public record RegenerationConfig
{
    // ... campos existentes
    
    /// <summary>
    /// Se true, reseta para ResetValue ao invés de adicionar AmountPerTurn.
    /// </summary>
    public bool ResetToValue { get; init; }
    
    /// <summary>
    /// Valor para resetar (usado se ResetToValue = true).
    /// </summary>
    public float? ResetValue { get; init; }
}
```

**Exemplo de uso:** block.json
```json
{
  "regeneration": {
    "enabled": true,
    "resetToValue": true,
    "resetValue": 0,
    "timing": "END_TURN"
  }
}
```

**Testes:**
- Block reseta para 0 no END_TURN
- Hands resetam para 4 no START_TURN
- ResetValue null usa Minimum

---

#### Step 5.3: Criar 25+ recursos de exemplo organizados (2-3h)

**Criar recursos completos para todos os jogos analisados:**

**Slay the Spire:**
- block.json (já criado)
- energy.json (já existe, atualizar)

**Balatro:**
- chips.json
- mult.json
- hands.json
- discards.json
- money.json

**Magic: The Gathering:**
- life.json
- white_mana.json
- blue_mana.json
- black_mana.json
- red_mana.json
- green_mana.json
- colorless_mana.json
- poison_counters.json

**Yu-Gi-Oh:**
- life_points.json
- normal_summons.json

**Hearthstone:**
- mana_crystals.json
- current_mana.json
- armor.json

**Inscryption:**
- bones.json
- blood.json

**Testes:**
- Todos os recursos carregam automaticamente
- GetAllDefinitions() retorna 25+ recursos
- Recursos podem ser filtrados por tag

---

### FASE 6: Integração com Turn Management (quando implementado)

**Nota:** Esta fase depende de TurnManager (Fase 2.2 do roadmap principal).

#### Step 6.1: Criar interface ITurnAware (30min)

**Arquivo a criar:** src/Core/Combat/ITurnAware.cs

```csharp
/// <summary>
/// Interface para sistemas que precisam processar eventos de turno.
/// </summary>
public interface ITurnAware
{
    Result ProcessStartOfTurn(CombatState state, ResourceContext context);
    Result ProcessEndOfTurn(CombatState state, ResourceContext context);
}
```

**Implementar em ResourceManager:**
```csharp
public class ResourceManager : IResourceManager, ITurnAware
{
    public Result ProcessStartOfTurn(CombatState state, ResourceContext context)
    {
        foreach (var entity in state.Entities)
        {
            var result = ProcessRegeneration(
                entity.ResourceState, 
                RegenerationTiming.START_TURN, 
                context);
            
            if (result.IsFailure)
                return result;
            
            // Atualizar entidade no estado
            entity = entity with { ResourceState = result.Value };
        }
        
        return Result.Success();
    }
}
```

**Testes:**
- TurnManager chama ProcessStartOfTurn() corretamente
- Recursos regeneram no timing correto
- Estado é atualizado após regeneração

---

## 4. RISCOS E MITIGAÇÕES

### Risco 1: Dependência circular ResourceManager ↔ TurnManager
**Problema:** ResourceManager precisa processar regeneração, TurnManager precisa chamar ResourceManager

**Mitigação:**
- ResourceManager não depende de TurnManager
- TurnManager depende de IResourceManager (interface)
- Injeção via DI resolve ciclo em runtime
- ProcessRegeneration() é método público

**Validação:**
- Testes unitários não precisam de TurnManager
- Testes de integração simulam chamada de TurnManager

---

### Risco 2: Performance com muitos recursos
**Problema:** Processar regeneração de 20+ recursos por entidade pode ser lento

**Mitigação:**
- Filtrar recursos por Timing antes de processar (reduz 50%)
- Pular recursos com Enabled=false (reduz mais 30%)
- Cache de fórmulas compiladas no IMathEngine (já existe)
- Processar apenas recursos que mudaram

**Benchmark esperado:**
- 10 recursos, 5 entidades, 50 regenerações/turno
- Objetivo: < 5ms por turno

---

### Risco 3: Fórmulas dinâmicas com contexto incompleto
**Problema:** Fórmula white_mana = plains_count precisa de plains_count no contexto

**Mitigação:**
- Fallback para AmountPerTurn se fórmula falhar
- Log de warning se fórmula falhar
- Validação de fórmulas no carregamento
- Documentar variáveis disponíveis

**Validação:**
- Testes para fórmulas com contexto faltando
- Testes verificam que fallback funciona
- Testes verificam que warning é logado

---

### Risco 4: Regeneração negativa pode causar valores negativos
**Problema:** Block com amountPerTurn: -999 pode deixar recurso negativo

**Mitigação:**
- Usar Set(0) ao invés de Spend(-999) para zerar
- Lógica especial para regeneração negativa:
```csharp
if (amount < 0 && Math.Abs(amount) > pool.Current)
    newPool = pool.Set(pool.Minimum);  // Zera ao invés de gastar
else
    newPool = amount >= 0 ? pool.Gain(amount) : pool.Spend(-amount);
```

**Validação:**
- Teste: block com 10 current, regeneração -999, deve zerar
- Teste: recurso com CanBeNegative=false não fica negativo

---

### Risco 5: Eventos sobrecarregam EventBus
**Problema:** 10 recursos × 5 entidades × 2 eventos = 100 eventos/turno

**Mitigação:**
- Eventos de regeneração são opcionais
- Batch events: publicar ResourceBatchChangedEvent
- Configurar via flag EnableResourceEvents (default: true)

**Benchmark esperado:**
- 100 eventos/turno
- Objetivo: < 2ms para publicar todos

---

### Risco 6: Carregamento dinâmico quebra configs existentes
**Problema:** Código atual espera recursos específicos (health, energy)

**Mitigação:**
- Manter lista de recursos "core" obrigatórios: ["health", "energy"]
- Validar que recursos core foram carregados
- Logar warning se recurso core faltando
- Fallback: criar recursos core com valores padrão

**Validação:**
- Teste: config sem health.json loga warning
- Teste: recursos core são criados com defaults se faltando

---

### Risco 7: Recursos compostos criam dependências circulares
**Problema:** current_mana regenera baseado em mana_crystals, que poderia regenerar baseado em current_mana

**Mitigação:**
- Processar recursos em ordem topológica
- Detectar ciclos via grafo de dependências
- Limitar profundidade de avaliação (max 3 níveis)
- Logar erro se ciclo detectado

**Validação:**
- Teste: ciclo A→B→A é detectado e loga erro
- Teste: dependência A→B→C funciona
- Teste: profundidade > 3 loga warning

---

## 5. RESUMO EXECUTIVO

### Escopo Total

**6 Fases de implementação:**

1. **Fase 1: Regeneração Básica** (2-3 dias) ⭐ CRÍTICO
   - Sistema de regeneração funcional
   - Suporte a fórmulas dinâmicas
   - Eventos de regeneração
   - 7 steps, 30+ testes

2. **Fase 2: Carregamento Dinâmico** (1 dia)
   - Escanear diretório automaticamente
   - 8+ recursos de exemplo
   - Documentação completa
   - 3 steps, 10+ testes

3. **Fase 3: Contexto para Fórmulas** (1-2 dias)
   - ResourceContext com turn_number, recursos, game state
   - Recursos compostos
   - Hearthstone-style mana crystals
   - 4 steps, 8+ testes

4. **Fase 4: Eventos de Mudança** (1 dia)
   - ResourceChangedEvent para UI
   - ResourcePoolManager wrapper
   - Integração com CombatSystem
   - 5 steps, 12+ testes

5. **Fase 5: Melhorias e Polish** (1-2 dias) - OPCIONAL
   - Validação de fórmulas no carregamento
   - Recursos "resetáveis"
   - 25+ recursos de exemplo organizados
   - 3 steps, 15+ testes

6. **Fase 6: Integração com Turn Management** (quando implementado)
   - Interface ITurnAware
   - Integração com TurnManager
   - 1 step, 3+ testes

**Total:** 5-9 dias de trabalho

---

### Estatísticas Esperadas

**Código novo:**
- 6 arquivos Core novos
- 3 arquivos de eventos novos
- 25+ arquivos JSON de recursos
- 2 documentos técnicos

**Testes:**
- Fase 1: 30+ testes
- Fase 2: 10+ testes
- Fase 3: 8+ testes
- Fase 4: 12+ testes
- Fase 5: 15+ testes
- **Total:** 75+ testes novos

---

### Impacto nos Jogos Analisados

| Jogo | Antes | Depois Fase 1 | Depois Fase 3 | Depois Fase 5 |
|------|-------|---------------|---------------|---------------|
| **Slay the Spire** | ❌ Energy não regenera | ✅ Energy regenera, block zera | ✅ Totalmente suportado | ✅ + 25 recursos exemplo |
| **Balatro** | ❌ Sem recursos | ⚠️ Recursos existem mas não regeneram | ✅ Hands/discards regeneram | ✅ Totalmente suportado |
| **Yu-Gi-Oh** | ❌ Sem recursos | ✅ Life points, summons regeneram | ✅ Totalmente suportado | ✅ Totalmente suportado |
| **Hearthstone** | ❌ Sem mana crystals | ⚠️ Regeneração fixa apenas | ✅ Mana cresce com turno | ✅ Totalmente suportado |
| **Magic** | ❌ Sem mana colorida | ⚠️ Regeneração fixa apenas | ✅ Mana baseado em terrenos | ✅ Totalmente suportado |
| **Inscryption** | ❌ Sem recursos | ⚠️ Regeneração fixa apenas | ⚠️ Precisa incremento de max | ✅ Totalmente suportado |

**Legenda:**
- ❌ Não suportado
- ⚠️ Parcialmente suportado
- ✅ Totalmente suportado

---

### Dependências e Bloqueios

**Bloqueia:**
- ⭐ Turn Management System (Fase 2.2 do roadmap) - precisa chamar ProcessRegeneration()
- ⭐ Status Effect System (Fase 2.1 do roadmap) - status podem modificar recursos
- Hand & Deck System (Fase 2.3 do roadmap) - cartas podem custar recursos

**Depende de:**
- ✅ IMathEngine (já existe)
- ✅ IEventBus (já existe)
- ✅ ILogger (já existe)
- ✅ ResourceManager (já existe)
- ⚠️ TurnManager (não existe, mas Fase 6 é opcional até ser implementado)

**Pode ser implementado independentemente:**
- ✅ Fases 1-5 não dependem de outros sistemas
- ✅ Pode ser testado isoladamente
- ✅ Integração com TurnManager é Fase 6 (futura)

---

### Priorização Recomendada

**Implementar imediatamente:**
1. **Fase 1** (2-3 dias) - CRÍTICO, desbloqueia gameplay
2. **Fase 2** (1 dia) - IMPORTANTE, melhora extensibilidade
3. **Fase 3** (1-2 dias) - IMPORTANTE, desbloqueia Hearthstone/MTG

**Implementar depois:**
4. **Fase 4** (1 dia) - MÉDIA, melhora UX mas não bloqueia gameplay
5. **Fase 5** (1-2 dias) - BAIXA, polish e exemplos

**Implementar quando TurnManager existir:**
6. **Fase 6** (1 dia) - Integração final

---

### Critérios de Sucesso

**Fase 1 completa quando:**
- ✅ Energy regenera 3 no início do turno
- ✅ Mana regenera 5 no início do turno
- ✅ Block zera no fim do turno
- ✅ Fórmulas dinâmicas funcionam
- ✅ Eventos são publicados
- ✅ 30+ testes passando

**Fase 2 completa quando:**
- ✅ Recursos carregam automaticamente de diretório
- ✅ Adicionar novo recurso = apenas criar JSON
- ✅ 8+ recursos de exemplo carregam
- ✅ Documentação completa

**Fase 3 completa quando:**
- ✅ Fórmulas acessam turn_number
- ✅ Fórmulas acessam outros recursos
- ✅ Fórmulas acessam game state
- ✅ Hearthstone mana crystals funcionam

**Fase 4 completa quando:**
- ✅ Mudanças de recurso publicam eventos
- ✅ UI pode reagir a mudanças
- ✅ Integração com CombatSystem funciona

**Fase 5 completa quando:**
- ✅ Fórmulas são validadas no carregamento
- ✅ 25+ recursos de exemplo organizados
- ✅ Performance < 5ms para 50 recursos

---

## 6. PRÓXIMOS PASSOS IMEDIATOS

Se aprovado, começar por:

1. **Step 1.1** - Criar IResourceRegenerationProcessor.cs (1-2h)
2. **Step 1.2** - Implementar ResourceRegenerationProcessor.cs (3-4h)
3. **Step 1.3** - Criar ResourceRegeneratedEvent.cs (30min)
4. **Step 1.4** - Adicionar ProcessRegeneration() ao IResourceManager (1h)
5. **Step 1.5** - Integrar com DI (30min)
6. **Step 1.6** - Atualizar JSONs de recursos (1h)
7. **Step 1.7** - Criar testes de integração (2-3h)

**Total Fase 1:** 8-12 horas de trabalho (1-1.5 dias)

---

## 7. PERGUNTAS PARA DECISÃO

Antes de prosseguir, preciso de algumas decisões:

### 1. Escopo de implementação
Qual escopo você prefere?
- **A)** Apenas Fase 1 (regeneração básica) - 2-3 dias
- **B)** Fases 1-3 (regeneração + carregamento dinâmico + contexto) - 4-6 dias
- **C)** Fases 1-4 (adicionar eventos) - 5-7 dias
- **D)** Fases 1-5 (completo com polish) - 6-9 dias

**Recomendação:** Opção B (Fases 1-3) oferece melhor custo-benefício.

### 2. Organização de recursos de exemplo
Como organizar os 25+ recursos de exemplo?
- **A)** Flat (todos em resources/) com tags para filtrar
- **B)** Subdiretórios por jogo (resources/balatro/, resources/magic/)
- **C)** Configs separados por jogo (configs/balatro/, configs/magic/)

**Recomendação:** Opção A (flat com tags) é mais simples.

### 3. Prioridade de eventos
Eventos de mudança de recurso (Fase 4) são importantes para você agora?
- **A)** Sim, preciso para UI reagir a mudanças
- **B)** Não, posso implementar depois
- **C)** Não sei, explique melhor o benefício

### 4. Recursos incrementais (Inscryption-style)
Inscryption tem energy que cresce incrementalmente (max aumenta 1 por turno). Implementar agora ou depois?
- **A)** Implementar agora (adiciona 1-2h à Fase 1)
- **B)** Implementar depois (Fase 5)
- **C)** Não preciso desse tipo de recurso

**Recomendação:** Opção B (depois) - não é crítico para MVP.

### 5. Validação de fórmulas
Validar fórmulas no carregamento (detectar erros cedo) ou em runtime (mais flexível)?
- **A)** Validar no carregamento (Fase 5, mais seguro)
- **B)** Validar em runtime com fallback (Fase 1, mais rápido)
- **C)** Ambos (validação no carregamento + fallback em runtime)

**Recomendação:** Opção C (ambos) - melhor experiência de desenvolvimento.

---

**FIM DO DOCUMENTO**

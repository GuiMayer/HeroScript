# Análise de Sistemas de Recursos e Custos

**Última atualização:** 2026-05-08  
**Status:** Análise Completa

---

## Visão Geral

Este documento analisa o sistema de recursos e custos do HeroScript, comparando-o com diferentes modelos de card games (MTG, Hearthstone, Slay the Spire, etc.) e mapeando responsabilidades por sistema. O objetivo é identificar gaps, oportunidades de expansão, e garantir que cada mecânica seja implementada no sistema correto.

---

## Sistema Atual: Capacidades

### Estrutura de Recursos

O HeroScript possui um sistema de recursos **altamente flexível e genérico**:

**Core Components:**
- `ResourceDefinition` - Metadados configuráveis via JSON
- `ResourcePool` - Estado imutável com operações (Gain, Spend, Set, Reset)
- `ResourceCategory` - 4 categorias (VITAL, TACTICAL, SPECIAL, TEMPORARY)
- `ResourceManager` - Carregamento e gerenciamento

**Recursos Implementados:**
- `health` - Vida (VITAL)
- `energy` - Energia tática (TACTICAL)
- `mana` - Mana mágica (TACTICAL)
- `shield` - Escudo temporário (TEMPORARY)

### Estrutura de Custos

**Core Components:**
- `ResourceCost` - Custo individual (resourceId, amount, formula, allowOverdraft)
- `ActionCosts` - Múltiplos custos por ação
- `ActionDefinition` - Ações configuráveis com custos, dano, cura, cooldown

**Capacidades Atuais:**
- ✅ Múltiplos recursos por ação (ex: energy + mana)
- ✅ Custos dinâmicos via fórmulas
- ✅ Overdraft (gastar além do disponível)
- ✅ Recursos negativos (dívidas)
- ✅ Recursos que excedem máximo (overcharge)
- ✅ Regeneração configurável por turno (definição)
- ✅ Cooldowns (definição)
- ✅ Multi-target

---

## Comparação com Card Games

### 1. Magic: The Gathering (MTG)

**Sistema de Custos:**
- Mana colorida (White, Blue, Black, Red, Green) + Mana genérica
- Custos híbridos (ex: 2 Blue OR 2 Green)
- Custos alternativos (Phyrexian mana: pagar 2 vida ao invés de mana)
- Custos adicionais (sacrificar criatura, descartar carta)
- Custos X (variável)

**Comparação com HeroScript:**

| Mecânica MTG | HeroScript Atual | Status |
|---|---|---|
| Mana colorida múltipla | ✅ Múltiplos recursos | **SUPORTADO** |
| Custos híbridos (A OU B) | ❌ Apenas AND | **NÃO SUPORTADO** |
| Custos alternativos | ⚠️ Via `allowOverdraft` parcial | **PARCIAL** |
| Custos adicionais | ✅ Múltiplos `ResourceCost` | **SUPORTADO** |
| Custos X (variável) | ✅ Via `Formula` | **SUPORTADO** |
| Pagar vida como custo | ✅ Health como recurso | **SUPORTADO** |

---

### 2. Hearthstone

**Sistema de Custos:**
- Mana Crystals (1-10, regenera todo turno)
- Overload (próximo turno tem menos mana)
- Combo (requer carta jogada antes)
- Sem custos alternativos

**Comparação com HeroScript:**

| Mecânica Hearthstone | HeroScript Atual | Status |
|---|---|---|
| Mana regenera por turno | ✅ `regeneration.amountPerTurn` | **SUPORTADO** |
| Overload (penalidade futura) | ❌ Sem sistema de débito futuro | **NÃO SUPORTADO** |
| Combo (requer ação prévia) | ❌ Sem sistema de pré-requisitos | **NÃO SUPORTADO** |
| Custo fixo simples | ✅ `ResourceCost.Amount` | **SUPORTADO** |

---

### 3. Slay the Spire

**Sistema de Custos:**
- Energy (3 por turno, não acumula)
- Cartas de custo 0, 1, 2, 3, X
- Cartas "Innate" (começam na mão)
- Cartas "Ethereal" (desaparecem se não usadas)
- Sem regeneração de energia entre turnos

**Comparação com HeroScript:**

| Mecânica Slay the Spire | HeroScript Atual | Status |
|---|---|---|
| Energy não acumula | ✅ `regeneration.enabled=false` | **SUPORTADO** |
| Custo 0 (grátis) | ✅ `Amount=0` ou sem custos | **SUPORTADO** |
| Custo X (variável) | ✅ Via `Formula` | **SUPORTADO** |
| Innate/Ethereal | ❌ Sem sistema de propriedades de carta | **NÃO SUPORTADO** |

---

### 4. Legends of Runeterra

**Sistema de Custos:**
- Mana (1-13, acumula até 3 spell mana)
- Spell mana só para magias
- Custos fixos
- Sem custos alternativos

**Comparação com HeroScript:**

| Mecânica LoR | HeroScript Atual | Status |
|---|---|---|
| Mana acumula parcialmente | ⚠️ Sem "overflow" para recurso específico | **PARCIAL** |
| Spell mana (recurso restrito) | ❌ Sem restrição por tipo de ação | **NÃO SUPORTADO** |
| Custos fixos | ✅ `ResourceCost.Amount` | **SUPORTADO** |

---

### 5. Gwent

**Sistema de Custos:**
- Sem custo de mana - joga 1 carta por turno
- Provisions (custo de deck building, não de jogo)
- Recursos baseados em pontos de poder ao invés de vida

**Comparação com HeroScript:**

| Mecânica Gwent | HeroScript Atual | Status |
|---|---|---|
| Sem custo de recursos | ✅ `Costs=[]` vazio | **SUPORTADO** |
| Combate baseado em pontos | ✅ Qualquer recurso pode ser "vida" | **SUPORTADO** |
| Provisions (deck cost) | ❌ Sem sistema de deck building | **NÃO APLICÁVEL** |

---

### 6. Yu-Gi-Oh!

**Sistema de Custos:**
- Sem mana - custos são sacrifícios, descartes, LP
- Tributos (sacrificar monstros)
- Pagar Life Points
- Banir cartas

**Comparação com HeroScript:**

| Mecânica Yu-Gi-Oh! | HeroScript Atual | Status |
|---|---|---|
| Sem mana | ✅ `Costs=[]` vazio | **SUPORTADO** |
| Pagar vida | ✅ Health como `ResourceCost` | **SUPORTADO** |
| Sacrificar unidades | ❌ Sem sistema de sacrifício | **NÃO SUPORTADO** |
| Banir recursos | ❌ Sem "zonas" de recursos | **NÃO SUPORTADO** |

---

## Mapeamento de Responsabilidades por Sistema

### 🎯 Sistema de Resources (`Core.Resources`)

**Responsabilidade:** Gerenciar **valores numéricos** (pools) e suas **operações básicas**

#### ✅ O QUE É RESPONSABILIDADE DO RESOURCES:

1. **Definição de recursos** (ResourceDefinition)
   - Metadados: nome, categoria, limites
   - Flags: `CanBeNegative`, `CanExceedMax`
   - `CostMultiplier` (multiplicador global)

2. **Operações em pools** (ResourcePool)
   - `Gain()` - adicionar valor
   - `Spend()` - gastar valor (com validação)
   - `Set()` - definir valor (com clamping)
   - `Reset()` - resetar para máximo
   - `CanAfford()` - verificar se há suficiente

3. **Regeneração** (RegenerationConfig)
   - `AmountPerTurn` - quantidade fixa
   - `Formula` - regeneração dinâmica
   - `Timing` - quando regenera (START_TURN, END_TURN, OUT_OF_COMBAT)

4. **Carregamento de definições** (ResourceManager)
   - Carregar JSONs de recursos
   - Criar pools a partir de definições
   - Validar configurações

#### ❌ O QUE NÃO É RESPONSABILIDADE DO RESOURCES:

1. **Quando aplicar regeneração** - É do CombatSystem
2. **Cooldowns de ações** - É do ActionSystem/CombatSystem
3. **Pré-requisitos de ações** - É do ActionSystem
4. **Custos alternativos (OR)** - É do ActionSystem
5. **Débitos futuros (Overload)** - É do CombatSystem (estado temporal)
6. **Restrições por tipo de ação** - É do ActionSystem
7. **Sacrifício de unidades** - É do CombatSystem (entity management)

---

### ⚔️ Sistema de Combat (`Core.Combat`)

**Responsabilidade:** Gerenciar **estado de combate**, **entidades**, **turnos** e **execução de ações**

#### ✅ O QUE É RESPONSABILIDADE DO COMBAT:

1. **Estado de combate** (CombatState)
   - Turno atual
   - Status (ACTIVE, VICTORY, DEFEAT)
   - Histórico de ações
   - Entidades participantes

2. **Entidades** (CombatEntity)
   - Estado de recursos por entidade (EntityResourceState)
   - Propriedades de conveniência (CurrentHp, MaxHp)
   - Operações: TakeDamage(), Heal()

3. **Execução de ações** (CombatSystem)
   - Validar custos
   - Aplicar efeitos (dano, cura)
   - Atualizar estado
   - Publicar eventos
   - **Aplicar regeneração por turno** ⚠️ (ainda não implementado)

4. **Gerenciamento de turnos**
   - Incrementar turno
   - Processar início/fim de turno
   - **Cooldowns de ações** ⚠️ (ainda não implementado)
   - **Débitos futuros** ⚠️ (ainda não implementado)

#### ❌ O QUE NÃO É RESPONSABILIDADE DO COMBAT:

1. **Definir quais recursos existem** - É do Resources
2. **Definir custos de ações** - É do ActionSystem
3. **Fórmulas de dano/cura** - É do MathEngine
4. **Carregamento de ações** - É do ActionManager

---

### 🎬 Sistema de Actions (`Core.Combat.Action*`)

**Responsabilidade:** Definir **ações configuráveis** e seus **custos/efeitos**

#### ✅ O QUE É RESPONSABILIDADE DO ACTIONS:

1. **Definição de ações** (ActionDefinition)
   - Metadados: nome, descrição, tipo
   - Dano/cura base
   - Fórmulas dinâmicas
   - Tags
   - **Cooldown** (definição, não execução)
   - RequiresTarget, MultiTarget

2. **Custos de ações** (ActionCosts, ResourceCost)
   - Lista de recursos necessários
   - Quantidade por recurso
   - Fórmulas de custo dinâmico
   - `AllowOverdraft` (flag)
   - Validação: `CanAfford()`, `GetAffordabilityError()`

3. **Carregamento de ações** (ActionManager)
   - Carregar JSONs de ações
   - Validar definições

#### ❌ O QUE NÃO É RESPONSABILIDADE DO ACTIONS:

1. **Executar ações** - É do CombatSystem
2. **Rastrear cooldowns ativos** - É do CombatSystem (estado temporal)
3. **Aplicar regeneração** - É do CombatSystem
4. **Calcular fórmulas** - É do MathEngine

---

### 🧮 Sistema de Math (`Core.Math`)

**Responsabilidade:** Avaliar **fórmulas dinâmicas**

#### ✅ O QUE É RESPONSABILIDADE DO MATH:

1. **Avaliação de expressões** (MathEngine)
   - Parsing de fórmulas
   - Contexto de variáveis
   - Operações matemáticas
   - Cache de resultados

2. **Fórmulas configuráveis** (FormulaLoader)
   - Carregar JSONs de fórmulas
   - Validar sintaxe

#### ❌ O QUE NÃO É RESPONSABILIDADE DO MATH:

1. **Definir quando usar fórmulas** - É dos sistemas que as consomem
2. **Validar lógica de negócio** - É dos sistemas específicos

---

## Gaps Identificados

### GAP 1: Custos Alternativos (OR Logic)

**Sistema responsável:** `ActionSystem` (ActionCosts)  
**Status:** ❌ Não implementado  
**Complexidade:** Média

**Problema:** Não há suporte para "pague 2 mana OU 3 vida"

**Solução Proposta:**
```csharp
// Em Core.Combat.ActionCosts.cs
public record ActionCosts
{
    public List<ResourceCost> Costs { get; init; } = new(); // AND logic
    public List<AlternativeCostOption> AlternativeCosts { get; init; } = new(); // OR logic
}

public record AlternativeCostOption
{
    public List<ResourceCost> Costs { get; init; } = new();
    public string Description { get; init; } = string.Empty;
}
```

**Exemplo de uso:**
```json
{
  "actionId": "desperate_strike",
  "costs": [],
  "alternativeCosts": [
    {
      "costs": [{ "resourceId": "mana", "amount": 3 }],
      "description": "Pay 3 mana"
    },
    {
      "costs": [{ "resourceId": "health", "amount": 5 }],
      "description": "Pay 5 health"
    }
  ]
}
```

---

### GAP 2: Pré-requisitos e Condições

**Sistema responsável:** `ActionSystem` (ActionDefinition)  
**Status:** ❌ Não implementado  
**Complexidade:** Alta

**Problema:** Não há sistema de "Combo" ou "requer X antes"

**Solução Proposta:**
```csharp
// Em Core.Combat.ActionDefinition.cs
public record ActionDefinition
{
    // ... campos existentes ...
    public List<ActionRequirement> Requirements { get; init; } = new();
}

public record ActionRequirement
{
    public RequirementType Type { get; init; }
    public string Condition { get; init; } = string.Empty; // Formula ou flag
    public string Description { get; init; } = string.Empty;
}

public enum RequirementType
{
    COMBO,           // Requer ação prévia no turno
    CONDITIONAL,     // Requer condição (formula)
    RESOURCE_STATE,  // Requer recurso em estado específico
    ENTITY_STATE     // Requer entidade em estado específico
}
```

**Validação:** `CombatSystem.ExecuteAction()` deve verificar requirements

**Exemplo de uso:**
```json
{
  "actionId": "combo_finisher",
  "requirements": [
    {
      "type": "COMBO",
      "condition": "basic_attack",
      "description": "Requires Basic Attack this turn"
    }
  ]
}
```

---

### GAP 3: Cooldowns (Rastreamento)

**Sistema responsável:** `CombatSystem` (estado temporal)  
**Status:** ⚠️ Definição existe, rastreamento não  
**Complexidade:** Média

**Problema:** `ActionDefinition.Cooldown` existe, mas não é rastreado

**Solução Proposta:**
```csharp
// Em Core.Combat.CombatState.cs
public record CombatState
{
    // ... campos existentes ...
    public Dictionary<string, int> ActionCooldowns { get; init; } = new();
    // Key: actionId, Value: turnos restantes
}
```

**Lógica:** `CombatSystem` deve:
1. Verificar cooldown antes de executar ação
2. Adicionar cooldown após executar ação
3. Decrementar cooldowns no início/fim de turno

---

### GAP 4: Débitos Futuros (Overload)

**Sistema responsável:** `CombatSystem` (estado temporal)  
**Status:** ❌ Não implementado  
**Complexidade:** Alta

**Problema:** Não há débito de recursos em turnos futuros

**Solução Proposta:**
```csharp
// Em Core.Combat.EntityResourceState.cs
public record EntityResourceState
{
    // ... campos existentes ...
    public List<ResourceDebt> PendingDebts { get; init; } = new();
}

public record ResourceDebt
{
    public string ResourceId { get; init; } = string.Empty;
    public float Amount { get; init; }
    public int TurnsRemaining { get; init; }
    public string Source { get; init; } = string.Empty; // Qual ação causou
}
```

**Lógica:** `CombatSystem` deve:
1. Adicionar débito ao executar ação com Overload
2. Aplicar débitos no início de turno
3. Decrementar `TurnsRemaining`

**Exemplo de uso:**
```json
{
  "actionId": "overload_lightning",
  "costs": [{ "resourceId": "mana", "amount": 2 }],
  "overload": {
    "resourceId": "mana",
    "amount": 2,
    "turns": 1
  }
}
```

---

### GAP 5: Recursos Restritos por Tipo de Ação

**Sistema responsável:** `ResourceSystem` + `ActionSystem` (validação cruzada)  
**Status:** ❌ Não implementado  
**Complexidade:** Média

**Problema:** Não há "spell mana" vs "unit mana"

**Solução Proposta:**
```csharp
// Em Core.Resources.ResourceDefinition.cs
public record ResourceDefinition
{
    // ... campos existentes ...
    public List<string> AllowedActionTags { get; init; } = new();
    // Ex: ["spell"] - só pode ser usado em ações com tag "spell"
}
```

**Validação:** `ActionCosts.CanAfford()` deve verificar se action.Tags intersecta com resource.AllowedActionTags

**Exemplo de uso:**
```json
{
  "resourceId": "spell_mana",
  "allowedActionTags": ["spell", "magic"]
}
```

---

### GAP 6: Regeneração por Turno (Execução)

**Sistema responsável:** `CombatSystem`  
**Status:** ⚠️ Definição existe (`RegenerationConfig`), execução não  
**Complexidade:** Média

**Problema:** `RegenerationConfig` está definido, mas `CombatSystem` não aplica

**Solução Proposta:**
```csharp
// Em Core.Combat.CombatSystem.cs
private CombatState ApplyRegeneration(CombatState state, RegenerationTiming timing)
{
    // Para cada entidade
    // Para cada recurso com regeneração habilitada
    // Se timing == recurso.Regeneration.Timing
    // Aplicar regeneração (AmountPerTurn ou Formula)
}
```

**Chamada:** No `ExecuteAction()` quando `ActionType.END_TURN` ou início de turno

---

### GAP 7: Sacrifício de Unidades

**Sistema responsável:** `CombatSystem` (entity management)  
**Status:** ❌ Não implementado (requer arquitetura maior)  
**Complexidade:** Muito Alta

**Problema:** Não há conceito de "remover entidade como custo"

**Solução Proposta:**
```csharp
// Em Core.Combat.ResourceCost.cs
public record ResourceCost
{
    // ... campos existentes ...
    public EntitySacrificeRequirement? SacrificeRequirement { get; init; }
}

public record EntitySacrificeRequirement
{
    public int Count { get; init; } = 1;
    public List<string> AllowedTags { get; init; } = new(); // Ex: ["minion"]
}
```

**Complexidade:** Requer sistema de "unidades invocadas" que não existe ainda

---

### GAP 8: Zonas de Recursos (Banir, Graveyard)

**Sistema responsável:** Novo sistema `ResourceZones` ou `CombatSystem`  
**Status:** ❌ Não implementado (requer arquitetura maior)  
**Complexidade:** Muito Alta

**Problema:** Recursos só têm valor numérico, não "localização"

**Solução Proposta:**
```csharp
// Novo namespace Core.Combat.Zones
public record ResourceZone
{
    public string ZoneId { get; init; } = string.Empty;
    public Dictionary<string, float> Resources { get; init; } = new();
}

public record EntityResourceState
{
    // ... campos existentes ...
    public Dictionary<string, ResourceZone> Zones { get; init; } = new();
    // Ex: ["hand"], ["graveyard"], ["banished"]
}
```

**Complexidade:** Requer repensar modelo de recursos (não apenas valores)

---

## Resumo de Tipos de Custos Suportados

### ✅ TOTALMENTE SUPORTADOS

1. **Custo fixo único** (ex: 3 energy)
2. **Múltiplos recursos** (ex: 2 energy + 5 mana)
3. **Custo variável via fórmula** (ex: `actor_level * 2`)
4. **Custo zero** (ações gratuitas)
5. **Pagar vida como custo** (health como recurso)
6. **Overdraft** (gastar além do disponível)
7. **Recursos negativos** (dívidas)
8. **Recursos que excedem máximo** (overcharge, shields)
9. **Regeneração por turno** (definição)
10. **Cooldowns** (definição)
11. **Combate sem vida** (pontos ao invés de HP)

### ⚠️ PARCIALMENTE SUPORTADOS

1. **Custos alternativos** (A OU B) - Apenas via `allowOverdraft`, não verdadeiro OR
2. **Mana acumula parcialmente** - Sem overflow para recurso específico
3. **Recursos restritos por tipo** - Sem "spell mana" vs "unit mana"
4. **Regeneração por turno** (execução) - Definição existe, execução não
5. **Cooldowns** (rastreamento) - Definição existe, rastreamento não

### ❌ NÃO SUPORTADOS

1. **Custos híbridos** (2 Blue OR 2 Green)
2. **Overload** (penalidade de recurso em turno futuro)
3. **Pré-requisitos** (Combo: requer ação prévia)
4. **Sacrifício de unidades** (tributos)
5. **Zonas de recursos** (banir, graveyard)
6. **Propriedades de carta** (Innate, Ethereal)

---

## Tabela de Priorização

| Mecânica | Sistema Responsável | Status | Complexidade | Prioridade |
|---|---|---|---|---|
| **Custos fixos** | ActionSystem | ✅ Implementado | Baixa | - |
| **Múltiplos recursos** | ActionSystem | ✅ Implementado | Baixa | - |
| **Custos dinâmicos (fórmula)** | ActionSystem + MathEngine | ✅ Implementado | Média | - |
| **Overdraft** | ActionSystem | ✅ Implementado | Baixa | - |
| **Regeneração (definição)** | ResourceSystem | ✅ Implementado | Baixa | - |
| **Regeneração (execução)** | CombatSystem | ❌ Não implementado | **Média** | **ALTA** |
| **Cooldowns (definição)** | ActionSystem | ✅ Implementado | Baixa | - |
| **Cooldowns (rastreamento)** | CombatSystem | ❌ Não implementado | **Média** | **ALTA** |
| **Custos alternativos (OR)** | ActionSystem | ❌ Não implementado | **Média** | **ALTA** |
| **Pré-requisitos/Combo** | ActionSystem + CombatSystem | ❌ Não implementado | **Alta** | **MÉDIA** |
| **Débitos futuros (Overload)** | CombatSystem | ❌ Não implementado | **Alta** | **MÉDIA** |
| **Recursos restritos por tipo** | ResourceSystem + ActionSystem | ❌ Não implementado | **Média** | **MÉDIA** |
| **Sacrifício de unidades** | CombatSystem | ❌ Não implementado | **Muito Alta** | **BAIXA** |
| **Zonas de recursos** | Novo sistema | ❌ Não implementado | **Muito Alta** | **BAIXA** |

---

## Recomendações de Implementação

### PRIORIDADE ALTA (Expandir sistema atual)

1. **Regeneração por turno** (GAP 6)
   - Definição existe, só falta execução
   - Impacto: Permite mana regenerativa (Hearthstone-style)
   - Esforço: Médio

2. **Cooldowns** (GAP 3)
   - Definição existe, só falta rastreamento
   - Impacto: Permite habilidades com cooldown
   - Esforço: Médio

3. **Custos alternativos (OR)** (GAP 1)
   - Expande flexibilidade de custos
   - Impacto: Permite MTG-style alternative costs
   - Esforço: Médio

### PRIORIDADE MÉDIA (Mecânicas avançadas)

4. **Pré-requisitos/Combo** (GAP 2)
   - Adiciona profundidade tática
   - Impacto: Permite Hearthstone-style combos
   - Esforço: Alto

5. **Recursos restritos por tipo** (GAP 5)
   - Para spell mana, etc.
   - Impacto: Permite LoR-style spell mana
   - Esforço: Médio

6. **Débitos futuros (Overload)** (GAP 4)
   - Mecânica interessante
   - Impacto: Permite Hearthstone-style overload
   - Esforço: Alto

### PRIORIDADE BAIXA (Requer arquitetura maior)

7. **Sacrifício de unidades** (GAP 7)
   - Requer sistema de invocação
   - Impacto: Permite Yu-Gi-Oh!-style tributes
   - Esforço: Muito Alto

8. **Zonas de recursos** (GAP 8)
   - Requer repensar modelo
   - Impacto: Permite Yu-Gi-Oh!-style zones
   - Esforço: Muito Alto

---

## Conclusão

O sistema atual do HeroScript é **surpreendentemente flexível** e já suporta:
- ✅ **Hearthstone-style** (mana simples com regeneração)
- ✅ **Slay the Spire-style** (energy sem acumulação)
- ✅ **Gwent-style** (sem custos, baseado em pontos)
- ✅ **Yu-Gi-Oh! parcial** (pagar vida, sem sacrifícios)
- ⚠️ **MTG parcial** (múltiplos recursos, sem híbridos)
- ⚠️ **LoR parcial** (mana, sem spell mana)

**Principais Gaps:**
1. Custos alternativos (OR logic)
2. Pré-requisitos/Combo
3. Débitos futuros (Overload)
4. Recursos restritos por tipo de ação

**Próximos Passos:**
- Implementar regeneração por turno (execução)
- Implementar cooldowns (rastreamento)
- Adicionar custos alternativos (OR logic)

---

## Referências

- [PHASE_1.md](PHASE_1.md) - Combat API e EventBus
- [PHASE_2.md](PHASE_2.md) - Status, Modifiers, Gambits
- [COMBAT_SYSTEM.md](../COMBAT_SYSTEM.md) - Documentação do sistema de combate
- [CONFIG_SYSTEM.md](../CONFIG_SYSTEM.md) - Sistema de configuração

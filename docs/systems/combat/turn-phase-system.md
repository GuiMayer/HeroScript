# Turn Phase System

**Status:** ✅ Implementado  
**Versão:** 1.0.0  
**Data:** 2026-05-11

---

## Visão Geral

O Turn Phase System é um sistema modular de fases de turno que permite ao HeroScript suportar múltiplos estilos de Trading Card Games (TCG), incluindo Magic: The Gathering, Yu-Gi-Oh!, Hearthstone, e sistemas customizados.

### Características Principais

- **Configurável via JSON**: Sequências de fases definidas em arquivos de configuração
- **Sistema de Prioridade**: Controle de ordem de ações entre jogadores
- **Action Stack**: Pilha LIFO para resolução de ações (estilo Magic)
- **Transições Automáticas e Manuais**: Fases podem avançar automaticamente ou aguardar input
- **Retrocompatibilidade**: Funciona com ou sem sistema de fases ativo
- **Event-Driven**: Integrado com EventBus para auditoria completa

---

## Arquitetura

### Componentes Principais

#### 1. PhaseManager
Gerencia o ciclo de vida das fases, transições e validações.

**Responsabilidades:**
- Iniciar fases e criar PhaseState inicial
- Validar e executar transições entre fases
- Verificar se ações são permitidas na fase atual
- Publicar eventos de fase no EventBus

**Localização:** `src/Core/Combat/TurnPhase/PhaseManager.cs`

#### 2. PrioritySystem
Controla a ordem de prioridade entre jogadores durante fases interativas.

**Responsabilidades:**
- Determinar qual jogador tem prioridade
- Gerenciar passagem de prioridade
- Resetar prioridade ao iniciar nova fase
- Obter ordem de jogadores do CombatState

**Localização:** `src/Core/Combat/TurnPhase/PrioritySystem.cs`

#### 3. ActionStackManager
Gerencia a pilha de ações pendentes (LIFO - Last In, First Out).

**Responsabilidades:**
- Empilhar ações (push)
- Desempilhar e resolver ações (pop)
- Verificar se pilha está vazia
- Limpar pilha quando necessário

**Localização:** `src/Core/Combat/TurnPhase/ActionStackManager.cs`

#### 4. PhaseSequenceLoader
Carrega sequências de fases de arquivos JSON.

**Responsabilidades:**
- Ler e parsear arquivos de configuração JSON
- Validar estrutura de sequências
- Fornecer sequências pré-configuradas (Magic, Yu-Gi-Oh!, etc.)

**Localização:** `src/Core/Combat/TurnPhase/PhaseSequenceLoader.cs`

#### 5. PhaseSystemFactory
Factory para criar instâncias completas do sistema de fases.

**Responsabilidades:**
- Criar PhaseManager, PrioritySystem e ActionStackManager
- Configurar dependências e logging
- Fornecer sistema pronto para uso

**Localização:** `src/Core/Combat/TurnPhase/PhaseSystemFactory.cs`

---

## Estruturas de Dados

### TurnPhase (Enum)

Define as fases disponíveis dentro de um turno.

```csharp
public enum TurnPhase
{
    NONE,                      // Sem sistema de fases (retrocompatibilidade)
    UNTAP,                     // Desvira permanentes (Magic)
    UPKEEP,                    // Manutenção/preparação
    DRAW,                      // Compra de cartas
    STANDBY,                   // Espera/preparação (Yu-Gi-Oh!)
    MAIN_1,                    // Fase principal 1 (pré-combate)
    BATTLE_START,              // Início da batalha
    BATTLE_DECLARE_ATTACKERS,  // Declaração de atacantes
    BATTLE_DECLARE_BLOCKERS,   // Declaração de bloqueadores
    BATTLE_DAMAGE,             // Resolução de dano
    BATTLE_END,                // Fim da batalha
    MAIN_2,                    // Fase principal 2 (pós-combate)
    END,                       // Fase final do turno
    CLEANUP                    // Limpeza (descarte, remove efeitos)
}
```

**Localização:** `src/Core/Combat/TurnPhase/TurnPhase.cs`

### PhaseDefinition (Record)

Define as características e regras de uma fase específica.

```csharp
public record PhaseDefinition
{
    public string Name { get; init; }                    // Nome para exibição
    public string Description { get; init; }             // Descrição da fase
    public List<ActionType> AllowedActions { get; init; } // Ações permitidas
    public List<TurnPhase> ValidNextPhases { get; init; } // Fases válidas para transição
    public bool AutoTransition { get; init; }            // Avança automaticamente?
    public bool AllowPriority { get; init; }             // Permite prioridade?
}
```

**Localização:** `src/Core/Combat/TurnPhase/PhaseDefinition.cs`

### PhaseSequenceDefinition (Record)

Define uma sequência completa de fases para um estilo de jogo.

```csharp
public record PhaseSequenceDefinition
{
    public string Name { get; init; }                    // Nome do estilo (ex: "Magic Style")
    public string Version { get; init; }                 // Versão da configuração
    public string Description { get; init; }             // Descrição do estilo
    public List<TurnPhase> Phases { get; init; }         // Sequência de fases
    public Dictionary<string, PhaseDefinition> PhaseDetails { get; init; } // Detalhes de cada fase
    public bool AllowPhaseSkipping { get; init; }        // Permite pular fases?
}
```

**Localização:** `src/Core/Combat/TurnPhase/PhaseSequenceDefinition.cs`

### PhaseState (Record)

Estado imutável do sistema de fases dentro de um combate.

```csharp
public record PhaseState
{
    public TurnPhase CurrentPhase { get; init; }                    // Fase atual
    public int PhaseIndex { get; init; }                            // Índice na sequência
    public PhaseSequenceDefinition PhaseSequence { get; init; }     // Sequência configurada
    public List<string> PriorityOrder { get; init; }                // Ordem de prioridade
    public int CurrentPriorityIndex { get; init; }                  // Índice de prioridade atual
    public string ActivePlayerId { get; init; }                     // Jogador com prioridade
    public ActionStack ActionStack { get; init; }                   // Pilha de ações
    public bool CanTransition { get; init; }                        // Pode transicionar?
    public Dictionary<string, bool> PlayerPassedPriority { get; init; } // Rastreio de prioridade
    public DateTime PhaseStartedAt { get; init; }                   // Timestamp de início
}
```

**Localização:** `src/Core/Combat/TurnPhase/PhaseState.cs`

### ActionStack (Record)

Pilha de ações pendentes para resolução (LIFO).

```csharp
public record ActionStack
{
    public Stack<PendingAction> Actions { get; init; }   // Pilha de ações
    public bool IsResolving { get; init; }               // Está resolvendo?
    public PendingAction? CurrentlyResolving { get; init; } // Ação sendo resolvida
    public int MaxStackSize { get; init; }               // Tamanho máximo (padrão: 100)
    public bool IsEmpty => Actions.Count == 0;           // Helper: pilha vazia?
    public int Size => Actions.Count;                    // Helper: tamanho atual
}
```

**Localização:** `src/Core/Combat/TurnPhase/ActionStack.cs`

### PendingAction (Record)

Representa uma ação empilhada aguardando resolução.

```csharp
public record PendingAction
{
    public string ActionId { get; init; }                // ID único da ação
    public string PlayerId { get; init; }                // Jogador que executou
    public ActionType ActionType { get; init; }          // Tipo de ação
    public Dictionary<string, object> Parameters { get; init; } // Parâmetros da ação
    public DateTime StackedAt { get; init; }             // Timestamp de empilhamento
}
```

**Localização:** `src/Core/Combat/TurnPhase/PendingAction.cs`

---

## Fluxo de Execução

### 1. Inicialização do Sistema

```csharp
// Criar factory
var factory = new PhaseSystemFactory(logger, eventBus);

// Criar sistema de fases
var phaseSystem = factory.CreatePhaseSystem();

// Carregar sequência de fases (JSON ou código)
var loader = new PhaseSequenceLoader(logger);
var sequence = loader.LoadFromFile("magic-style.json");
```

### 2. Integração com CombatSystem

O sistema de fases é **opcional** e integrado via `CombatSystemPhaseExtensions`:

```csharp
// Iniciar combate COM sistema de fases
var result = combatSystem.StartCombatWithPhases(
    heroEntity,
    enemyEntity,
    phaseSystem,
    sequence
);

// Iniciar combate SEM sistema de fases (retrocompatibilidade)
var result = combatSystem.StartCombat(heroEntity, enemyEntity);
```

**Localização:** `src/Core/Combat/CombatSystemPhaseExtensions.cs:25-55`

### 3. Ciclo de Fase

```
┌─────────────────────────────────────────────────────────┐
│ 1. StartPhase(phase, state)                            │
│    - Cria PhaseState inicial                           │
│    - Define jogador com prioridade                     │
│    - Publica PhaseStartedEvent                         │
└─────────────────────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────┐
│ 2. Fase Ativa                                          │
│    - Jogadores executam ações permitidas               │
│    - Ações podem ser empilhadas (ActionStack)          │
│    - Jogadores passam prioridade                       │
└─────────────────────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────┐
│ 3. Verificação de Transição                           │
│    - AllPlayersPassedPriority()?                       │
│    - ActionStack.IsEmpty?                              │
│    - AutoTransition habilitado?                        │
└─────────────────────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────┐
│ 4. TransitionToNextPhase(currentPhase, sequence)       │
│    - Valida transição                                  │
│    - Cria novo PhaseState                              │
│    - Publica PhaseEndedEvent                           │
│    - Retorna ao passo 1 com nova fase                  │
└─────────────────────────────────────────────────────────┘
```

### 4. Sistema de Prioridade

```
Jogador A tem prioridade
    │
    ├─> Executa ação → ActionStack.Push()
    │                  └─> Prioridade passa para Jogador B
    │
    ├─> Passa prioridade → PassPriority(A)
    │                       └─> Prioridade passa para Jogador B
    │
    └─> Todos passaram? → AllPlayersPassedPriority()
                          └─> Resolver ActionStack ou transicionar fase
```

---

## Configurações Pré-Definidas

O sistema inclui 4 configurações JSON pré-definidas:

### 1. Magic: The Gathering Style
**Arquivo:** `src/Core/Combat/TurnPhase/Configurations/magic-style.json`

**Sequência:**
```
UNTAP → UPKEEP → DRAW → MAIN_1 → BEGIN_COMBAT → 
DECLARE_ATTACKERS → DECLARE_BLOCKERS → COMBAT_DAMAGE → 
END_COMBAT → MAIN_2 → END_STEP → CLEANUP
```

**Características:**
- Prioridade interativa em quase todas as fases
- UNTAP e CLEANUP sem prioridade (auto-transition)
- Duas fases principais (pré e pós-combate)

### 2. Yu-Gi-Oh! Style
**Arquivo:** `src/Core/Combat/TurnPhase/Configurations/yugioh-style.json`

**Sequência:**
```
DRAW → STANDBY → MAIN_1 → BATTLE → MAIN_2 → END
```

**Características:**
- Mais simples que Magic
- Sem prioridade interativa (turnos alternados)
- Uma fase de batalha unificada

### 3. Hearthstone Style
**Arquivo:** `src/Core/Combat/TurnPhase/Configurations/hearthstone-style.json`

**Sequência:**
```
DRAW → MAIN → END
```

**Características:**
- Sistema mais simples
- Sem fases de combate separadas
- Ações resolvem imediatamente (sem stack)

### 4. Classic Style
**Arquivo:** `src/Core/Combat/TurnPhase/Configurations/classic-style.json`

**Sequência:**
```
MAIN → END
```

**Características:**
- Sistema minimalista
- Para jogos sem complexidade de fases
- Retrocompatível com sistema sem fases

---

## Integração com CombatSystem

O `CombatState` possui um campo opcional `PhaseState?`:

```csharp
public record CombatState
{
    // ... outros campos ...
    
    /// <summary>
    /// Estado do sistema de fases (null se não estiver usando fases)
    /// </summary>
    public PhaseState? PhaseState { get; init; }
}
```

**Localização:** `src/Core/Combat/Models/CombatState.cs:82`

### Extensões para CombatSystem

A classe `CombatSystemPhaseExtensions` adiciona métodos de extensão:

```csharp
// Iniciar combate com sistema de fases
public static Result<CombatState> StartCombatWithPhases(
    this ICombatSystem combatSystem,
    CombatEntity hero,
    CombatEntity enemy,
    PhaseSystemFactory phaseSystem,
    PhaseSequenceDefinition sequence
)

// Transicionar para próxima fase
public static Result<CombatState> TransitionPhase(
    this ICombatSystem combatSystem,
    string combatId,
    PhaseSystemFactory phaseSystem
)

// Passar prioridade
public static Result<CombatState> PassPriority(
    this ICombatSystem combatSystem,
    string combatId,
    string playerId,
    PhaseSystemFactory phaseSystem
)
```

**Localização:** `src/Core/Combat/CombatSystemPhaseExtensions.cs`

---

## Eventos Publicados

O sistema publica 5 tipos de eventos no EventBus:

### 1. PhaseStartedEvent
Publicado quando uma nova fase inicia.

```csharp
public record PhaseStartedEvent : DomainEvent
{
    public string CombatId { get; init; }
    public TurnPhase Phase { get; init; }
    public string ActivePlayerId { get; init; }
}
```

**Localização:** `src/Core/Events/Domain/PhaseStartedEvent.cs`

### 2. PhaseEndedEvent
Publicado quando uma fase termina.

```csharp
public record PhaseEndedEvent : DomainEvent
{
    public string CombatId { get; init; }
    public TurnPhase Phase { get; init; }
    public TurnPhase NextPhase { get; init; }
}
```

**Localização:** `src/Core/Events/Domain/PhaseEndedEvent.cs`

### 3. PriorityPassedEvent
Publicado quando um jogador passa prioridade.

```csharp
public record PriorityPassedEvent : DomainEvent
{
    public string CombatId { get; init; }
    public string PlayerId { get; init; }
    public TurnPhase CurrentPhase { get; init; }
}
```

**Localização:** `src/Core/Events/Domain/PriorityPassedEvent.cs`

### 4. ActionStackedEvent
Publicado quando uma ação é empilhada.

```csharp
public record ActionStackedEvent : DomainEvent
{
    public string CombatId { get; init; }
    public PendingAction Action { get; init; }
    public int StackSize { get; init; }
}
```

**Localização:** `src/Core/Events/Domain/ActionStackedEvent.cs`

### 5. ActionResolvedEvent
Publicado quando uma ação da pilha é resolvida.

```csharp
public record ActionResolvedEvent : DomainEvent
{
    public string CombatId { get; init; }
    public PendingAction Action { get; init; }
    public bool Success { get; init; }
}
```

**Localização:** `src/Core/Events/Domain/ActionResolvedEvent.cs`

---

## Exemplos de Uso

### Exemplo 1: Combate Estilo Magic

```csharp
// Setup
var logger = new ConsoleLogger("Combat");
var eventBus = new EventBus(logger);
var factory = new PhaseSystemFactory(logger, eventBus);
var loader = new PhaseSequenceLoader(logger);

// Carregar sequência Magic
var sequence = loader.LoadFromFile("magic-style.json");

// Criar entidades
var hero = new CombatEntity { EntityId = "hero1", Name = "Hero", CurrentHp = 20, MaxHp = 20 };
var enemy = new CombatEntity { EntityId = "enemy1", Name = "Enemy", CurrentHp = 20, MaxHp = 20 };

// Iniciar combate com fases
var combatSystem = new CombatSystem(logger, eventBus);
var result = combatSystem.StartCombatWithPhases(hero, enemy, factory, sequence);

if (result.IsSuccess)
{
    var state = result.Value;
    Console.WriteLine($"Combat started in phase: {state.PhaseState.CurrentPhase}");
    Console.WriteLine($"Active player: {state.PhaseState.ActivePlayerId}");
}
```

### Exemplo 2: Transição Manual de Fase

```csharp
// Jogador passa prioridade
var passResult = combatSystem.PassPriority(combatId, "hero1", factory);

// Verificar se pode transicionar
if (passResult.IsSuccess && passResult.Value.PhaseState.CanTransition)
{
    // Transicionar para próxima fase
    var transitionResult = combatSystem.TransitionPhase(combatId, factory);
    
    if (transitionResult.IsSuccess)
    {
        var newState = transitionResult.Value;
        Console.WriteLine($"Transitioned to: {newState.PhaseState.CurrentPhase}");
    }
}
```

### Exemplo 3: Empilhar Ações (Action Stack)

```csharp
// Jogador A joga um feitiço
var action1 = new PendingAction
{
    ActionId = "spell1",
    PlayerId = "playerA",
    ActionType = ActionType.POWER,
    Parameters = new Dictionary<string, object> { ["spellId"] = "fireball" }
};

var stackManager = factory.CreateActionStackManager();
var stackResult = stackManager.Push(state.PhaseState.ActionStack, action1);

// Jogador B responde com outro feitiço
var action2 = new PendingAction
{
    ActionId = "spell2",
    PlayerId = "playerB",
    ActionType = ActionType.POWER,
    Parameters = new Dictionary<string, object> { ["spellId"] = "counterspell" }
};

stackResult = stackManager.Push(stackResult.Value, action2);

// Resolver pilha (LIFO - spell2 resolve primeiro, depois spell1)
while (!stackResult.Value.IsEmpty)
{
    var popResult = stackManager.Pop(stackResult.Value);
    var resolvedAction = popResult.Value.Item1; // Ação resolvida
    stackResult = Result<ActionStack>.Success(popResult.Value.Item2); // Pilha atualizada
    
    Console.WriteLine($"Resolving: {resolvedAction.ActionId} by {resolvedAction.PlayerId}");
}
```

### Exemplo 4: Configuração Customizada

```json
{
  "name": "Custom TCG Style",
  "version": "1.0.0",
  "description": "Sistema customizado para meu TCG",
  "allowPhaseSkipping": false,
  "phases": ["DRAW", "MAIN", "BATTLE", "END"],
  "phaseDetails": {
    "DRAW": {
      "name": "Draw Phase",
      "description": "Compre 2 cartas",
      "allowedActions": [],
      "validNextPhases": ["MAIN"],
      "autoTransition": true,
      "allowPriority": false
    },
    "MAIN": {
      "name": "Main Phase",
      "description": "Jogue cartas e ative habilidades",
      "allowedActions": ["POWER", "ACTIVATE_ABILITY", "PASS_PRIORITY"],
      "validNextPhases": ["BATTLE"],
      "autoTransition": false,
      "allowPriority": true
    },
    "BATTLE": {
      "name": "Battle Phase",
      "description": "Declare atacantes e resolva combate",
      "allowedActions": ["ATTACK", "PASS_PRIORITY"],
      "validNextPhases": ["END"],
      "autoTransition": false,
      "allowPriority": true
    },
    "END": {
      "name": "End Phase",
      "description": "Fim do turno, descarte excesso",
      "allowedActions": [],
      "validNextPhases": ["DRAW"],
      "autoTransition": true,
      "allowPriority": false
    }
  }
}
```

---

## API Pública

### IPhaseManager

```csharp
Result<PhaseState> StartPhase(TurnPhase phase, CombatState state);
Result<PhaseState> TransitionToNextPhase(PhaseState currentPhase, PhaseSequenceDefinition sequence);
Result<bool> CanPerformAction(PhaseState phaseState, ActionType actionType);
Result<PhaseState> ValidatePhaseTransition(TurnPhase from, TurnPhase to, PhaseSequenceDefinition sequence);
```

**Localização:** `src/Core/Combat/TurnPhase/IPhaseManager.cs`

### IPrioritySystem

```csharp
Result<string> GetPriorityPlayer(CombatState state);
Result<PhaseState> PassPriority(PhaseState phaseState, string playerId);
bool AllPlayersPassedPriority(PhaseState phaseState);
Result<PhaseState> ResetPriority(PhaseState phaseState, string activePlayerId);
List<string> GetPlayerOrder(CombatState state);
```

**Localização:** `src/Core/Combat/TurnPhase/IPrioritySystem.cs`

### IActionStackManager

```csharp
Result<ActionStack> Push(ActionStack stack, PendingAction action);
Result<(PendingAction, ActionStack)> Pop(ActionStack stack);
Result<PendingAction> Peek(ActionStack stack);
Result<ActionStack> Clear(ActionStack stack);
```

**Localização:** `src/Core/Combat/TurnPhase/IActionStackManager.cs`

---

## Testes

### Localização dos Testes
`tests/Core.Tests/Combat/TurnPhase/`

### Arquivos de Teste

1. **PhaseSystemFactoryTests.cs** - Testes de criação do sistema
2. **PhaseSequenceLoaderTests.cs** - Testes de carregamento de JSON
3. **PrioritySystemTests.cs** - Testes de sistema de prioridade
4. **ActionStackTests.cs** - Testes de pilha de ações

### Cobertura

- ✅ Criação e inicialização do sistema
- ✅ Carregamento de configurações JSON
- ✅ Transições de fase válidas e inválidas
- ✅ Sistema de prioridade (passar, resetar, verificar)
- ✅ Action Stack (push, pop, peek, clear)
- ✅ Integração com CombatSystem
- ✅ Publicação de eventos

**Nota:** Os testes atualmente não compilam devido a incompatibilidades de API entre implementação e testes. Isso será corrigido na próxima fase de atualização.

---

## Retrocompatibilidade

O sistema de fases é **completamente opcional**:

- Se `CombatState.PhaseState` é `null`, o combate funciona no modo clássico (sem fases)
- Métodos antigos do CombatSystem continuam funcionando normalmente
- Novos métodos de extensão (`StartCombatWithPhases`, etc.) são opt-in

Isso permite migração gradual e suporte a múltiplos estilos de jogo no mesmo projeto.

---

## Referências

### Documentos Relacionados
- [Combat System](combat-system.md) - Sistema de combate principal
- [EventBus System](../events/eventbus-system.md) - Sistema de eventos
- [Architecture Overview](../../architecture/overview.md) - Visão geral da arquitetura

### Código Fonte
- **Namespace:** `Core.Combat.TurnPhase`
- **Diretório:** `src/Core/Combat/TurnPhase/`
- **Testes:** `tests/Core.Tests/Combat/TurnPhase/`
- **Configurações:** `src/Core/Combat/TurnPhase/Configurations/`

### Próximos Passos
- Corrigir incompatibilidades de API nos testes
- Adicionar mais configurações pré-definidas (Pokémon TCG, Legends of Runeterra, etc.)
- Implementar API REST para gerenciar fases via HTTP
- Adicionar suporte a fases condicionais (skip se condição X)

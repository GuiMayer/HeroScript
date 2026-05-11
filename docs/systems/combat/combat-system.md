# Combat System

**Status:** ✅ Implementado  
**Versão:** 1.1.0  
**Data:** 2026-05-11

---

## Visão Geral

O Combat System é o núcleo do sistema de combate do HeroScript, gerenciando estado imutável de combates, execução de ações, sistema de energia, e integração com EventBus para Event Sourcing.

### Características Principais

- **Estado Imutável**: Cada ação cria um novo `CombatState` usando records do C#
- **Event Sourcing**: Todos os eventos de combate são publicados no EventBus
- **Thread-Safe**: Usa `ConcurrentDictionary` para gerenciar combates ativos
- **Sistema de Energia**: Ataques básicos geram energia, poderes consomem
- **Histórico Completo**: Todas as ações são registradas para auditoria e replay
- **Turn Phase System**: Sistema modular de fases opcional para TCGs (Magic, Yu-Gi-Oh!, etc.)

---

## Arquitetura

### Estruturas de Dados

#### CombatEntity
Representa uma entidade em combate (herói ou inimigo).

```csharp
public record CombatEntity
{
    public string EntityId { get; init; }
    public string Name { get; init; }
    public int CurrentHp { get; init; }
    public int MaxHp { get; init; }
    public bool IsAlive => CurrentHp > 0;
    public bool IsHero { get; init; }
}
```

**Métodos:**
- `TakeDamage(int damage)` - Aplica dano e retorna nova instância
- `Heal(int amount)` - Cura e retorna nova instância

#### EnergyPool
Gerencia energia do herói.

```csharp
public record EnergyPool
{
    public int Current { get; init; }
    public int Maximum { get; init; }
    
    public bool CanAfford(int cost);
    public EnergyPool Spend(int amount);
    public EnergyPool Gain(int amount);
    public EnergyPool Reset();
}
```

#### CombatState
Estado completo e imutável de um combate.

```csharp
public record CombatState
{
    public Guid CombatId { get; init; }
    public DateTime StartedAt { get; init; }
    public int CurrentTurn { get; init; }
    public CombatStatus Status { get; init; }
    public CombatEntity Hero { get; init; }
    public IReadOnlyList<CombatEntity> Enemies { get; init; }
    public EnergyPool Energy { get; init; }
    public IReadOnlyList<CombatAction> ActionHistory { get; init; }
    public PhaseState? PhaseState { get; init; }  // Opcional: sistema de fases TCG
}
```

**Nota:** O campo `PhaseState` é opcional. Se `null`, o combate funciona no modo clássico sem fases. Veja [Turn Phase System](turn-phase-system.md) para detalhes.

#### CombatAction
Representa uma ação executada.

```csharp
public record CombatAction
{
    public Guid ActionId { get; init; }
    public DateTime Timestamp { get; init; }
    public int Turn { get; init; }
    public string ActorId { get; init; }
    public ActionType ActionType { get; init; }
    public string? PowerId { get; init; }
    public string? TargetId { get; init; }
    public int? DamageDealt { get; init; }
    public int? EnergyChange { get; init; }
}
```

---

## Sistema de Energia

### Regras

- **Ataque Básico**: Gera **+1 energia**
- **Poderes**: Consomem **-3 energia** (padrão)
- **Máximo**: 10 energia
- **Inicial**: 3 energia (configurável)

### Validação

O sistema valida automaticamente se há energia suficiente antes de executar poderes.

---

## Eventos de Domínio

### 1. CombatStartedEvent
Publicado ao iniciar combate.

```csharp
{
    "combatId": "guid",
    "heroId": "player-1",
    "enemyIds": ["goblin-1", "goblin-2"],
    "initialEnergy": 3
}
```

### 2. ActionExecutedEvent
Publicado após cada ação.

```csharp
{
    "combatId": "guid",
    "actionId": "guid",
    "actorId": "player-1",
    "actionTypeName": "BASIC_ATTACK",
    "targetId": "goblin-1",
    "damageDealt": 10,
    "energyChange": 1
}
```

### 3. EnergyChangedEvent
Publicado quando energia muda.

```csharp
{
    "combatId": "guid",
    "oldEnergy": 3,
    "newEnergy": 4,
    "delta": 1,
    "reason": "Basic attack"
}
```

### 4. CombatEndedEvent
Publicado ao finalizar combate.

```csharp
{
    "combatId": "guid",
    "statusName": "VICTORY",
    "totalTurns": 5,
    "totalActions": 12,
    "duration": "00:02:34"
}
```

---

## REST API

### POST /api/combat/start
Inicia novo combate.

**Request:**
```json
{
  "heroId": "player-1",
  "enemies": ["goblin-1", "goblin-2"],
  "initialEnergy": 3
}
```

**Response:**
```json
{
  "combatId": "550e8400-e29b-41d4-a716-446655440000",
  "status": "ACTIVE",
  "currentTurn": 1,
  "hero": {
    "entityId": "player-1",
    "name": "Hero",
    "currentHp": 100,
    "maxHp": 100,
    "isAlive": true
  },
  "enemies": [
    {
      "entityId": "goblin-1",
      "name": "Enemy-1",
      "currentHp": 50,
      "maxHp": 50,
      "isAlive": true
    }
  ],
  "energy": {
    "current": 3,
    "maximum": 10
  },
  "totalActions": 0
}
```

### POST /api/combat/{combatId}/action
Executa ação em combate.

**Request:**
```json
{
  "actionType": "BASIC_ATTACK",
  "targetId": "goblin-1"
}
```

**Tipos de Ação:**
- `BASIC_ATTACK` - Ataque básico (requer targetId)
- `POWER` - Usar poder (requer powerId e targetId)
- `PASS` - Passar turno
- `END_TURN` - Finalizar turno

**Response:** Retorna o novo estado do combate (mesmo formato do start).

### GET /api/combat/{combatId}/state
Obtém estado atual do combate.

**Response:** Estado do combate (mesmo formato do start).

### GET /api/combat/{combatId}/history
Obtém histórico de ações.

**Response:**
```json
{
  "combatId": "guid",
  "totalActions": 5,
  "actions": [
    {
      "actionId": "guid",
      "timestamp": "2026-05-08T12:00:00Z",
      "turn": 1,
      "actorId": "player-1",
      "actionType": "BASIC_ATTACK",
      "targetId": "goblin-1",
      "damageDealt": 10,
      "energyChange": 1
    }
  ]
}
```

### POST /api/combat/{combatId}/end
Finaliza combate.

**Response:**
```json
{
  "combatId": "guid",
  "status": "VICTORY",
  "totalTurns": 5,
  "totalActions": 12,
  "damageDealt": 150,
  "damageTaken": 30,
  "duration": 154.5
}
```

---

## Uso Básico

### Iniciar Combate

```csharp
var result = combatSystem.StartCombat(
    heroId: "player-1",
    enemyIds: new List<string> { "goblin-1", "goblin-2" },
    initialEnergy: 3
);

if (result.IsSuccess)
{
    var combatId = result.Value.CombatId;
    Console.WriteLine($"Combat started: {combatId}");
}
```

### Executar Ações

```csharp
// Ataque básico
var result = combatSystem.ExecuteAction(
    combatId,
    ActionType.BASIC_ATTACK,
    targetId: "goblin-1"
);

// Usar poder
var result = combatSystem.ExecuteAction(
    combatId,
    ActionType.POWER,
    powerId: "FIREBALL",
    targetId: "goblin-1"
);

// Passar turno
var result = combatSystem.ExecuteAction(
    combatId,
    ActionType.PASS
);

// Finalizar turno
var result = combatSystem.ExecuteAction(
    combatId,
    ActionType.END_TURN
);
```

### Verificar Estado

```csharp
var stateResult = combatSystem.GetCombatState(combatId);
if (stateResult.IsSuccess)
{
    var state = stateResult.Value;
    Console.WriteLine($"Turn: {state.CurrentTurn}");
    Console.WriteLine($"Energy: {state.Energy.Current}/{state.Energy.Maximum}");
    Console.WriteLine($"Hero HP: {state.Hero.CurrentHp}/{state.Hero.MaxHp}");
    
    if (state.Status == CombatStatus.VICTORY)
        Console.WriteLine("Victory!");
}
```

---

## Integração com EventBus

O Combat System publica eventos automaticamente. Para escutar eventos:

```csharp
eventBus.Subscribe<CombatStartedEvent>(e => 
{
    Console.WriteLine($"Combat {e.CombatId} started!");
});

eventBus.Subscribe<ActionExecutedEvent>(e => 
{
    Console.WriteLine($"{e.ActorId} used {e.ActionTypeName}");
});

eventBus.Subscribe<EnergyChangedEvent>(e => 
{
    Console.WriteLine($"Energy: {e.OldEnergy} -> {e.NewEnergy}");
});

eventBus.Subscribe<CombatEndedEvent>(e => 
{
    Console.WriteLine($"Combat ended: {e.StatusName}");
});
```

---

## Thread Safety

O `CombatSystem` usa `ConcurrentDictionary<Guid, CombatState>` para gerenciar combates ativos de forma thread-safe. Múltiplas threads podem:

- Iniciar combates diferentes simultaneamente
- Executar ações em combates diferentes simultaneamente
- Consultar estados de combates diferentes simultaneamente

**Nota:** Ações no mesmo combate são serializadas pelo dicionário.

---

## Limitações Atuais (MVP)

### Implementado

- ✅ Sistema de energia básico
- ✅ Ataque básico e poderes simples
- ✅ Tracking de HP e turnos
- ✅ Estado imutável
- ✅ Event Sourcing completo
- ✅ Validação de ações
- ✅ Detecção de vitória/derrota
- ✅ Histórico de ações

### Não Implementado (fases futuras)

- ❌ **Cálculo de dano complexo** - Damage Pipeline (Fase 1)
  - Crítico multi-tier
  - Armadura e resistências
  - Modificadores de dano
  
- ❌ **Status effects** (Fase 2)
  - Buffs e debuffs
  - DoTs (Damage over Time)
  - Stun, silêncio, etc.
  
- ❌ **Script modifiers** (Fase 2)
  - Go Again
  - Multi-Hit
  - Conditional effects
  
- ❌ **IA de inimigos** (Fase 2)
  - Inimigos atualmente não agem
  - Apenas recebem dano
  
- ❌ **Definições de poderes em JSON** (Fase 4)
  - Poderes atualmente hardcoded
  - Dano fixo (30 para poderes, 10 para básico)
  
- ❌ **Persistência em disco** (Fase 5)
  - Combates existem apenas em memória
  - Perdidos ao reiniciar aplicação

---

## Valores Hardcoded (Temporários)

```csharp
// src/Core/Combat/CombatSystem.cs
private const int BASIC_ATTACK_DAMAGE = 10;
private const int BASIC_ATTACK_ENERGY_GAIN = 1;
private const int DEFAULT_POWER_COST = 3;
private const int DEFAULT_POWER_DAMAGE = 30;

// Entidades criadas com valores fixos
Hero: 100 HP
Enemy: 50 HP
```

**Nota:** Estes valores serão substituídos pelo Damage Pipeline e sistema de definições em fases futuras.

---

## Testes

### Cobertura

- **27 testes de Combat** (100% passando)
- **142 testes totais** no projeto (100% passando)

### Arquivos de Teste

- `tests/Core.Tests/Combat/CombatSystemTests.cs` - Testes unitários do sistema
- `tests/Core.Tests/Combat/EnergyPoolTests.cs` - Testes do sistema de energia
- `tests/Core.Tests/Combat/CombatEntityTests.cs` - Testes de entidades
- `tests/Core.Tests/Combat/CombatIntegrationTests.cs` - Testes de integração

### Executar Testes

```bash
# Todos os testes
dotnet test

# Apenas testes de Combat
dotnet test --filter "FullyQualifiedName~Combat"
```

---

## Turn Phase System Integration

O Combat System possui integração opcional com o **Turn Phase System**, que permite suportar múltiplos estilos de Trading Card Games (TCG).

### Ativação do Sistema de Fases

O sistema de fases é **completamente opcional** e ativado através do campo `PhaseState?` no `CombatState`:

```csharp
// Combate SEM fases (modo clássico)
var result = combatSystem.StartCombat(hero, enemy);
// result.Value.PhaseState == null

// Combate COM fases (TCG style)
var result = combatSystem.StartCombatWithPhases(hero, enemy, phaseSystem, sequence);
// result.Value.PhaseState != null
```

### Funcionalidades do Sistema de Fases

- **Sequências Configuráveis**: Defina fases via JSON (Magic, Yu-Gi-Oh!, Hearthstone, etc.)
- **Sistema de Prioridade**: Controle de ordem de ações entre jogadores
- **Action Stack**: Pilha LIFO para resolução de ações (estilo Magic)
- **Transições Automáticas/Manuais**: Fases podem avançar automaticamente ou aguardar input
- **Event-Driven**: Publica eventos de fase no EventBus

### Exemplo de Uso

```csharp
// Carregar sequência de fases
var loader = new PhaseSequenceLoader(logger);
var sequence = loader.LoadFromFile("magic-style.json");

// Criar sistema de fases
var factory = new PhaseSystemFactory(logger, eventBus);

// Iniciar combate com fases
var result = combatSystem.StartCombatWithPhases(hero, enemy, factory, sequence);

if (result.IsSuccess)
{
    var state = result.Value;
    Console.WriteLine($"Phase: {state.PhaseState.CurrentPhase}");
    Console.WriteLine($"Active Player: {state.PhaseState.ActivePlayerId}");
}

// Passar prioridade
var passResult = combatSystem.PassPriority(combatId, playerId, factory);

// Transicionar para próxima fase
var transitionResult = combatSystem.TransitionPhase(combatId, factory);
```

### Configurações Pré-Definidas

O sistema inclui 4 estilos pré-configurados:

1. **Magic: The Gathering** - 12 fases com prioridade interativa
2. **Yu-Gi-Oh!** - 6 fases com turnos alternados
3. **Hearthstone** - 3 fases simplificadas
4. **Classic** - 2 fases minimalistas

**Documentação completa:** [Turn Phase System](turn-phase-system.md)

---

## Próximos Passos

### 1. Damage Pipeline (Fase 1)
Sistema de cálculo de dano com 6 buckets:
- Base damage
- Multiplicadores
- Flat bonuses
- Critical calculation
- Armor/resistance
- Final modifiers

### 2. Status System (Fase 2)
- Buffs e debuffs
- DoTs e HoTs
- Stun, silêncio, root
- Duração e stacks

### 3. Script Modifiers (Fase 2)
- Go Again
- Multi-Hit
- Conditional effects
- Triggers

---

## Referências

- **Código Core:** `src/Core/Combat/`
- **Testes:** `tests/Core.Tests/Combat/`
- **API:** `src/API/Controllers/CombatController.cs`
- **DI Setup:** `src/API/Program.cs:54-60`
- **Eventos:** `src/Core/Events/Domain/`

---

**Última atualização:** 2026-05-08  
**Versão:** 1.0.0  
**Status:** ✅ Implementado e testado

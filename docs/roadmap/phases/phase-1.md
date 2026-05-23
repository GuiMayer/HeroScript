# Fase 1 - EventBus e Combate Básico

**Status:** ✅ Implementado  
**Dependências:** Fase 0  
**Última atualização:** 2026-05-09

---

## Visão Geral

A Fase 1 introduz o sistema de eventos (EventBus) e o combate básico, estabelecendo a fundação para sistemas reativos e o pipeline de dano. Esta fase é crítica para permitir que diferentes sistemas se comuniquem de forma desacoplada.

## APIs Implementadas

### 1. Events API ✅

Sistema de eventos pub/sub para comunicação desacoplada entre sistemas.

**Status:** ✅ Implementado (2026-05-08)

**Endpoints:**
- `GET /api/events` - Lista eventos da sessão atual com filtros
- `GET /api/events/{eventId}` - Obtém evento específico por ID
- `GET /api/events/categories` - Lista categorias de eventos disponíveis
- `GET /api/events/severities` - Lista severidades de eventos disponíveis
- `DELETE /api/events` - Limpa histórico (dev mode apenas)

**Categorias de Eventos:**
- `COMBAT` - Eventos de combate (ataque, dano, morte)
- `PIPELINE` - Eventos do pipeline de dano (bucket processing)
- `META` - Eventos de sistema (config load, resource reload)
- `CONFIG` - Eventos de configuração
- `REALITY_BEND` - Eventos especiais do jogo

**Severidades:**
- `DEBUG` - Informação detalhada de debug
- `INFO` - Informação normal
- `WARN` - Aviso (inesperado mas não crítico)
- `ANOMALY` - Anomalia (Reality Bend, comportamento especial)

**Recursos Implementados:**
- ✅ Event sourcing (todos eventos registrados em memória)
- ✅ Histórico queryável por categoria e severidade
- ✅ Pub/sub thread-safe com IDisposable subscriptions
- ✅ Sequence numbers automáticos
- ✅ Exception handling em handlers
- ✅ Integração com MathEngine e ConfigManager
- ✅ REST API completa
- ✅ 18 testes (13 unitários + 5 integração)

**Documentação:**
- [eventbus-system.md](../../systems/events/eventbus-system.md) - Documentação completa do sistema

### 2. Combat API ✅

Sistema de combate básico com gerenciamento de estado.

**Status:** ✅ Implementado (2026-05-08)

**Endpoints:**
- `POST /api/combat/start` - Inicia novo combate
- `POST /api/combat/{combatId}/action` - Executa ação (ataque, habilidade)
- `GET /api/combat/{combatId}/state` - Obtém estado atual do combate
- `GET /api/combat/{combatId}/history` - Histórico de ações do combate
- `POST /api/combat/{combatId}/end` - Finaliza combate

**Recursos Implementados:**
- ✅ Sistema de energia (ataque básico gera, poderes consomem)
- ✅ Execução de ações (BASIC_ATTACK, POWER, PASS, END_TURN)
- ✅ Tracking de turnos e HP
- ✅ Estado imutável (event-sourced)
- ✅ Integração com EventBus
- ✅ Validação de ações (energia, alvos)
- ✅ Detecção de vitória/derrota
- ✅ Sistema de recursos genérico (ResourcePool, ResourceDefinition)
- ✅ Thread-safe (ConcurrentDictionary)
- ✅ 53 testes (12 CombatSystem + 6 CombatEntity + 2 Integration + 7 EnergyPool + 26 Alternative Costs)

**Documentação:**
- [combat-system.md](../../systems/combat/combat-system.md) - Documentação completa do sistema

### 2.1. Alternative Costs System ✅

Sistema de custos alternativos para ações (implementado como parte do Combat).

**Status:** ✅ Implementado (2026-05-08)

**Arquivos:**
- `src/Core/Combat/AlternativeCostOption.cs` - Opção de custo alternativo
- `src/Core/Combat/ActionCosts.cs` - Sistema de custos com lógica AND/OR
- `src/Core/Combat/ActionManager.cs` - Gerenciador de definições de ações
- `src/Core/Combat/ActionDefinition.cs` - Definição de ação com custos

**Recursos Implementados:**
- ✅ Múltiplas opções de pagamento para uma ação (OR logic)
- ✅ Múltiplos recursos por opção (AND logic)
- ✅ Verificação de affordability (CanAfford, GetAffordableOptions)
- ✅ Mensagens de erro descritivas (GetAffordabilityError)
- ✅ Suporte a overdraft (custos que podem deixar recurso negativo)
- ✅ Integração com CombatSystem (parâmetro costOptionId)
- ✅ Método ApplyCosts() preparado para aplicar custos
- ✅ ActionManager para carregar definições de JSON
- ✅ Endpoint API para obter opções de custo (placeholder)
- ✅ 26 testes (12 ActionCosts + 8 AlternativeCostOption + 6 Integration)

**Padrões de Design Suportados:**
- Resource Conversion - Converter um recurso em outro
- Risk vs Reward - Escolha entre seguro e arriscado
- Situational Flexibility - Adaptar a diferentes situações
- Thematic Choices - Escolhas temáticas/morais

**Documentação:**
- [alternative-costs.md](../alternative-costs.md) - Documentação completa do sistema
- [alternative-costs-examples.json](../examples/alternative-costs-examples.json) - Exemplos de uso

**Integração Pendente:**
- ⏳ Validação de costOptionId contra definições de ação (aguardando ActionManager completo)
- ⏳ Aplicação automática de custos em ExecutePower (aguardando ActionManager)
- ⏳ Endpoint /cost-options retornando dados reais (aguardando ActionManager)

### 3. Damage API ✅

Pipeline de cálculo de dano JSON-driven configurável.

**Status:** ✅ Implementado (2026-05-09)

**Endpoints:**
- `POST /api/damage/calculate` - Calcula dano através do pipeline
- `POST /api/damage/simulate` - Simula pipeline com detalhes de cada bucket
- `GET /api/damage/config` - Obtém configuração atual do pipeline
- `POST /api/damage/config/reload` - Recarrega configuração do pipeline

**Arquitetura:**
- **JSON-driven:** Pipeline completamente configurável via JSON
- **Bucket-based:** Sistema de buckets ordenados com operações
- **Filter system:** Condições para aplicação condicional de buckets
- **Event-driven:** Emite eventos para cada bucket processado

**Buckets Configuráveis:**
- Ordem definida por configuração JSON
- Operações: ADD_FLAT, MULTIPLY, APPLY_FORMULA, ROLL_CRIT_TIER, CLAMP, FLOOR, CEIL
- Filtros: TAG_PRESENT, TAG_ABSENT, MODIFIER_PRESENT, MODIFIER_ABSENT, MODIFIER_COMPARE
- Suporte a crítico multi-tier (0-5+)

**Recursos Implementados:**
- ✅ Pipeline JSON-driven com herança delta
- ✅ GenericBucketProcessor com 7 operações
- ✅ Sistema de filtros com 5 tipos de condições
- ✅ PipelineConfigLoader com validação
- ✅ PipelineManager com cache e reload
- ✅ DamageCalculator integrado com CombatSystem
- ✅ IRandomProvider para testabilidade
- ✅ 3 eventos (BucketProcessedEvent, DamageCalculatedEvent, PipelineReloadedEvent)
- ✅ REST API completa
- ✅ 126 testes (unitários, integração, edge cases, eventos)

**Testes Implementados:**
- 30 testes GenericBucketProcessor
- 20 testes FilterCondition
- 10 testes PipelineConfigLoader
- 12 testes PipelineManager
- 16 testes DamageCalculator
- 19 testes Edge Cases
- 11 testes Eventos
- 8 testes Integração (PoE, Genshin, Card Game, RPG styles)

**Documentação:**
- [damage-pipeline.md](../../systems/damage/damage-pipeline.md) - Documentação completa do sistema
- [damage-examples.md](../../systems/damage/damage-examples.md) - Exemplos práticos

---

## Integração com EventBus

Ver: [event-integration.md](../analysis/event-integration.md) para detalhes sobre as duas abordagens de integração.

**Resumo:**
- **Polling (GET):** Cliente consulta `/api/events` periodicamente
- **WebSocket (futuro):** Push de eventos em tempo real

---

## Dependências

### Sistemas Core Necessários

- ✅ MathEngine (Fase 0)
- ✅ ConfigManager (Fase 0)
- ✅ EventBus (implementado)
- ✅ CombatSystem (implementado)
- ✅ DamagePipeline (implementado)

### Ordem de Implementação

1. **EventBus Core** (src/Core/Events/)
   - IEvent, GameEvent, EventBus
   - Eventos de domínio (MathFormulaEvaluated, ConfigLoaded, etc.)
   - Integração com MathEngine e ConfigManager

2. **Events API** (src/API/Controllers/EventsController.cs)
   - Endpoints de consulta
   - Filtros e paginação
   - Clear history (dev mode)

3. **Combat Core** (src/Core/Combat/)
   - CombatState, CombatAction, CombatSystem
   - Energy pool management
   - Action resolver

4. **Damage Core** (src/Core/Combat/Damage/)
   - BucketPipeline
   - DamageContext
   - Bucket processors (6 baldes)

5. **Combat API** (src/API/Controllers/CombatController.cs)
   - Start/end combat
   - Execute actions
   - Query state

6. **Damage API** (src/API/Controllers/DamageController.cs)
   - Calculate damage
   - Simulate pipeline
   - List buckets

---

## Exemplos de Uso

### Iniciar Combate

```http
POST /api/combat/start
Content-Type: application/json

{
  "heroId": "player-1",
  "enemies": ["goblin-1", "goblin-2"],
  "initialEnergy": 3
}
```

### Executar Ação

```http
POST /api/combat/{combatId}/action
Content-Type: application/json

{
  "actorId": "player-1",
  "actionType": "POWER",
  "powerId": "FIREBALL",
  "targetId": "goblin-1"
}
```

### Calcular Dano (Simulação)

```http
POST /api/damage/calculate
Content-Type: application/json

{
  "baseDamage": 10,
  "additiveBonus": 5,
  "multiplicativeBonus": 1.5,
  "critChance": 0.25,
  "critMultiplier": 2.0,
  "targetArmor": 3
}
```

### Consultar Eventos

```http
GET /api/events?category=COMBAT&limit=50
```

---

## Documentação Relacionada

- [eventbus-implementation.md](../../systems/events/eventbus-implementation.md) - Plano detalhado do EventBus
- [event-integration.md](../analysis/event-integration.md) - Integração de eventos na API

---

## Resumo de Implementação

### ✅ Implementado (3/3 APIs)

1. **Events API** - Sistema completo de eventos pub/sub
   - 18 testes (13 unitários + 5 integração)
   - Integração com MathEngine e ConfigManager
   - REST API completa com filtros

2. **Combat API** - Sistema de combate básico
   - 53 testes (incluindo Alternative Costs)
   - Estado imutável e thread-safe
   - Sistema de recursos genérico
   - Integração com EventBus

3. **Alternative Costs System** - Custos alternativos para ações
   - 26 testes (12 + 8 + 6 integration)
   - 4 padrões de design suportados
   - Documentação completa com exemplos
   - Preparado para integração com ActionManager

4. **Damage API** - Pipeline de cálculo de dano JSON-driven
   - 126 testes (unitários, integração, edge cases, eventos)
   - Pipeline completamente configurável via JSON
   - Sistema de buckets com 7 operações e 5 tipos de filtros
   - Integração com EventBus e CombatSystem
   - REST API completa

### 📊 Estatísticas

- **Total de testes:** 294 testes (todos passando)
  - Combat: 53 testes
  - Events: 18 testes
  - Damage: 126 testes
  - Math: 97 testes
- **Arquivos Core:** 85+ arquivos .cs
- **Controllers API:** 10 controllers

---

## Próximos Passos

Após completar a Fase 1, a Fase 2 adicionará:
- **Status API** - Buffs, debuffs, DoTs
- **ScriptModifier API** - Go Again, Multi-Hit, Explosivo
- **Gambit API** - Sistema de companions com regras condicionais

Ver: [phase-2.md](phase-2.md)

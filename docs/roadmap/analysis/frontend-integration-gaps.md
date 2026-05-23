# Frontend Integration Gaps

**Data:** 2026-05-23  
**Contexto:** Análise realizada durante planejamento de protótipo visual (Phaser 3 + React)  
**Status:** 🚧 Atualizado — primeiras lacunas da Fase 3 fechadas

---

## Visão Geral

Este documento identifica lacunas na API REST atual que impedem a criação de um frontend visual completo. As lacunas foram descobertas durante o planejamento de um protótipo Phaser 3 + React que se conectaria à API existente.

**Princípio:** O frontend deve ser uma camada de apresentação pura, sem lógica de negócio. Toda lógica de combate, efeitos, status, modifiers e gambits permanece no backend C#.

---

## Lacunas Identificadas

### 1. Sistema de Mão de Cartas (Hand)

**Status:** ✅ Implementado
**Prioridade:** HIGH  
**Fase:** 3

**Problema:**
- `/api/combat/{id}/available-actions` retorna **TODAS** as ações disponíveis no jogo
- Não há conceito de "mão limitada" (ex: 5 cartas)
- Frontend não pode renderizar uma mão de cartas realista

**Impacto:**
- Impossível criar UI de card game (Slay the Spire, Hearthstone, etc.)
- Frontend precisa filtrar manualmente as ações, mas sem critério claro
- Não há mecânica de "draw" ou "discard"

**Solução Proposta:**

```
GET /api/combat/{combatId}/hand
Response: {
  combatId: string,
  hand: ActionDefinition[],  // Máximo 5-10 cartas
  handSize: number,
  maxHandSize: number,
  deckSize: number,          // Cartas restantes no deck
  discardSize: number        // Cartas no discard pile
}
```

**Dependências:**
- Sistema de Deck (ver lacuna #2)
- Sistema de Draw/Discard
- `RunState` para persistir deck entre combates

**Estado atual:**
- `GET /api/run/{runId}/hand` retorna a mão da run.
- `POST /api/run/{runId}/draw` e `POST /api/run/{runId}/discard` mutam o estado no backend.
- `POST /api/combat/{combatId}/action` aceita `runId` e `cardId`; `CombatRunCoordinator` valida que a carta está na mão, executa combate e consome a carta apenas após sucesso.
- `GET /api/combat/{combatId}/available-actions?actorId=...&runId=...` filtra ações pela mão real e informa `cardCountInHand`/`willConsumeTo`.

---

### 2. Sistema de Deck

**Status:** ✅ Implementado na primeira fatia
**Prioridade:** HIGH  
**Fase:** 3

**Problema:**
- Não há conceito de "deck" de cartas
- Ações parecem estar sempre disponíveis
- Não há mecânica de "draw card" no início do turno
- Não há limite de quantas vezes uma ação pode ser usada

**Impacto:**
- Impossível implementar mecânicas roguelike de deck-building
- Não há progressão de deck entre combates
- Não há sistema de "aprender novas cartas" após vitória

**Solução Proposta:**

```
GET /api/run/{runId}/deck
Response: {
  runId: string,
  deck: {
    cards: ActionDefinition[],
    totalCards: number,
    cardCounts: { [actionId: string]: number }
  },
  drawPile: string[],      // IDs das cartas no draw pile
  hand: string[],          // IDs das cartas na mão
  discardPile: string[],   // IDs das cartas no discard
  exhaustPile: string[]    // IDs das cartas exhausted
}

POST /api/run/{runId}/deck/add
Request: { actionId: string, count: number }

POST /api/run/{runId}/deck/remove
Request: { actionId: string, count: number }
```

**Dependências:**
- `RunState` (Fase 3)
- `CardSelection` API (Fase 3)
- Sistema de shuffle/draw/discard

**Estado atual:**
- `RunState` contém `DeckState` com draw pile, hand, discard e exhaust.
- `GET /api/run/{runId}/deck` expõe o estado real.
- `CardSelection`, `Shop` e `Preparation` já adicionam cartas ao estado da run.
- Ações de combate agora movem cartas da mão para discard/exhaust ou mantêm na mão por tag `retain`.

---

### 3. Endpoint de End Turn

**Status:** ✅ Implementado
**Prioridade:** MEDIUM  
**Fase:** 2 (melhorar contrato)

**Estado atual:**
- `POST /api/combat/{combatId}/end-turn` existe e executa `END_TURN` pelo backend.
- `POST /api/combat/{combatId}/action` tambem aceita `actionType: "END_TURN"`, mas agora exige `actorId`.

**Impacto:**
- Frontend precisa conhecer detalhes de implementação do backend
- Não é RESTful (end turn não é uma "ação" no sentido de carta)
- Dificulta documentação e descoberta da API

**Solução Proposta:**

```
POST /api/combat/{combatId}/end-turn
Response: CombatStateResponse

// Para atores nao-heroi, use /action com actorId explicito.
```

**Dependências:**
- Nenhuma (pode ser implementado imediatamente)

**Workaround Temporário:**
- Não necessário para end-turn; frontend deve preferir o endpoint dedicado.

---

### 4. Sistema de Polling/WebSocket

**Status:** ⚠️ Parcial
**Prioridade:** MEDIUM  
**Fase:** 3

**Problema:**
- Não há WebSocket ou Server-Sent Events (SSE)
- Frontend precisa fazer polling manual de `/api/combat/{id}/state`
- Não há notificação quando é turno do jogador novamente

**Impacto:**
- Frontend precisa fazer polling a cada 500ms-1s
- Desperdício de recursos (CPU, rede)
- Latência perceptível (jogador espera até próximo poll)
- Dificulta implementação de animações síncronas

**Solução Proposta (Opção A - SSE):**

```
GET /api/combat/{combatId}/events
Response: text/event-stream

Events:
- combat:state-changed
- combat:action-executed
- combat:turn-changed
- combat:ended
```

**Solução Proposta (Opção B - WebSocket):**

```
WS /api/combat/{combatId}/ws

Messages:
- { type: "state", data: CombatStateResponse }
- { type: "action", data: ActionExecutedEvent }
- { type: "turn", data: TurnChangedEvent }
```

**Dependências:**
- SignalR (ASP.NET Core) ou biblioteca WebSocket
- Refatoração de `CombatSystem` para emitir eventos

**Workaround Temporário:**
- Frontend faz polling de `/state` a cada 500ms
- Funciona, mas não é eficiente

---

### 5. Turno de IA Automático

**Status:** ✅ Implementado
**Prioridade:** MEDIUM  
**Fase:** 2-3

**Estado atual:**
- `POST /api/combat/{combatId}/process-ai-turns` existe e centraliza decisoes de IA via `GambitEngine`.
- O endpoint agora executa as acoes dos inimigos vivos usando `CombatActionCommand` com `actorId` do inimigo.
- `POST /api/combat/{combatId}/action` exige `actorId`, entao player, IA, script e futuro multiplayer usam o mesmo contrato.

**Impacto:**
- Frontend precisa implementar lógica de "esperar turno do inimigo"
- Não há feedback visual durante turno do inimigo
- Dificulta implementação de animações de ações do inimigo

**Solução Proposta:**

**Opção A: Backend executa turno de IA automaticamente**
```
POST /api/combat/{combatId}/action
Request: { actionId: "BASIC_ATTACK", targetId: "enemy_1" }
Response: {
  ...state após ação do jogador,
  aiTurns: [
    { entityId: "enemy_1", actionId: "BASIC_ATTACK", targetId: "hero" },
    { entityId: "enemy_2", actionId: "FIREBALL", targetId: "hero" }
  ],
  ...state após turnos de IA
}
```

**Opção B: Endpoint dedicado para processar turnos de IA**
```
POST /api/combat/{combatId}/process-ai-turns
Response: {
  aiTurns: [...],
  finalState: CombatStateResponse
}
```

**Dependências:**
- `GambitEngine` (já implementado na Fase 2)
- Sistema de turno (já existe)

**Workaround Temporário:**
- Frontend pode usar `process-ai-turns` para obter decisoes backend-authoritative e animar acoes ja aplicadas no estado final.

---

## Resumo de Prioridades

| Lacuna | Prioridade | Fase | Bloqueador para Protótipo? |
|--------|-----------|------|---------------------------|
| Sistema de Mão de Cartas | HIGH | 3 | ✅ Primeira fatia implementada |
| Sistema de Deck | HIGH | 3 | ✅ Primeira fatia implementada |
| Endpoint End Turn | MEDIUM | 2 | ✅ Implementado |
| Polling/WebSocket | MEDIUM | 3 | ✅ Polling incremental + base SSE implementados |
| Turno de IA Automático | MEDIUM | 2-3 | ✅ Implementado |
| Ownership/autorizacao por ator | LOW agora / HIGH antes de multiplayer | Futuro | ❌ Não para single player |

---

## Recomendações

### Para Protótipo Imediato (Fase 2.5)

1. **Usar endpoints implementados** para validar protótipo visual básico.
2. **Focar em validar integração** backend ↔ frontend.
3. **Identificar outras lacunas** durante implementação.

### Para Fase 3 (Loop de Run)

1. **Usar turnos/ativacao por entidade implementados** via `/api/combat/{combatId}/activation/*`.
2. **Usar polling incremental** via `/api/events?afterSequence=...` ou `/api/combat/{combatId}/events`; SSE já existe como base em `/events/stream`.
3. **Refinar Run/CardSelection/Shop/Preparation** com reroll, raridades, pricing dinâmico e inject real de modificadores.
4. **TODO futuro:** modelar ownership/autorizacao por ator antes de multiplayer/API multi-cliente; nao e bloqueador para single player.

### Para Fase 4 (Conteúdo MVP)

1. **Expandir sistema de Deck** com raridades, upgrades, transformações
2. **Adicionar sistema de CardSelection** após combate
3. **Implementar Shop** para comprar/vender cartas

---

## Notas de Implementação

### Contrato Atual

Todas as soluções propostas devem:
- **Usar `actorId` explicito** para comandos de combate vindos de player, IA, script ou futuro multiplayer
- **Evitar caminhos especiais** para player vs IA dentro do `CombatSystem`
- **Seguir convenções** da API REST existente
- **Usar contratos** data-driven (JSON configs)
- **Sincronizar UI por eventos**: preferir polling incremental por `afterSequence`; SSE pode ser usado quando o cliente suportar stream persistente.

### TODO Futuro: Ownership/Autorizacao

Ownership/autorizacao nao e prioridade para single player local. Deve ser implementado antes de multiplayer, API multi-cliente ou controle remoto real.

Responsabilidades esperadas:
- Mapear qual `controllerId`/sessao/jogador pode comandar cada `actorId`.
- Permitir fontes distintas (`PLAYER_INPUT`, `AI`, `SYSTEM`, `SCRIPT`) sem mudar o caminho de execucao do combate.
- Bloquear comandos externos tentando controlar inimigos, atores de outro jogador ou atores de sistema.
- Manter autorizacao na camada de controle/API; `CombatSystem` deve continuar actor-agnostic.

### Filosofia Data-Driven

- Sistema de Deck deve ser configurável via JSON
- Tamanho de mão, regras de draw, limite de deck devem ser data-driven
- Não hardcodar valores como "5 cartas na mão" ou "30 cartas no deck"
- Ativacao por entidade usa `combat-turn-rules/{rulesId}.json` para `drawCount`, `discardPolicy`, `retainTags`, escopo de ator e comportamento de IA

### Testes

Cada sistema novo deve ter:
- Testes unitários no Core
- Testes de integração na API
- Documentação de endpoints em `docs/api/endpoints.md`

---

## Referências

- [Phase 3 - Loop de Run](../phases/phase-3.md)
- [Data-driven Compliance](data-driven-compliance.md)
- [API Conventions](api-conventions.md)
- [Core Modules Analysis](core-modules.md)

---

**Última atualização:** 2026-05-23  
**Próxima revisão:** Após implementação do protótipo visual

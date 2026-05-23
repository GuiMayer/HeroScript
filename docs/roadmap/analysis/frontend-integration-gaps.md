# Frontend Integration Gaps

**Data:** 2026-05-23  
**Contexto:** Análise realizada durante planejamento de protótipo visual (Phaser 3 + React)  
**Status:** 📋 Documentado — Lacunas identificadas para Fase 3

---

## Visão Geral

Este documento identifica lacunas na API REST atual que impedem a criação de um frontend visual completo. As lacunas foram descobertas durante o planejamento de um protótipo Phaser 3 + React que se conectaria à API existente.

**Princípio:** O frontend deve ser uma camada de apresentação pura, sem lógica de negócio. Toda lógica de combate, efeitos, status, modifiers e gambits permanece no backend C#.

---

## Lacunas Identificadas

### 1. Sistema de Mão de Cartas (Hand)

**Status:** ❌ Ausente  
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

**Workaround Temporário:**
- Frontend pode pegar as primeiras N ações de `/available-actions` e simular uma "mão"
- Não é ideal, mas permite prototipar a UI

---

### 2. Sistema de Deck

**Status:** ❌ Ausente  
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

**Workaround Temporário:**
- Frontend pode simular um "deck virtual" em memória
- Não persiste entre combates, mas permite prototipar mecânicas

---

### 3. Endpoint de End Turn

**Status:** ⚠️ Parcial  
**Prioridade:** MEDIUM  
**Fase:** 2 (melhorar contrato)

**Problema:**
- Não há endpoint dedicado `POST /api/combat/{id}/end-turn`
- End turn é feito via `ExecuteAction` com `actionType: "END_TURN"`
- Contrato não é claro para frontend

**Impacto:**
- Frontend precisa conhecer detalhes de implementação do backend
- Não é RESTful (end turn não é uma "ação" no sentido de carta)
- Dificulta documentação e descoberta da API

**Solução Proposta:**

```
POST /api/combat/{combatId}/end-turn
Response: CombatStateResponse

// Mantém compatibilidade com ExecuteAction para não quebrar código existente
```

**Dependências:**
- Nenhuma (pode ser implementado imediatamente)

**Workaround Temporário:**
- Frontend usa `ExecuteAction` com `actionType: "END_TURN"`
- Funciona, mas não é ideal

---

### 4. Sistema de Polling/WebSocket

**Status:** ❌ Ausente  
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

**Status:** ❌ Ausente  
**Prioridade:** MEDIUM  
**Fase:** 2-3

**Problema:**
- Não há execução automática do turno do inimigo
- Frontend precisa detectar quando é turno do inimigo
- Frontend precisa aguardar manualmente (polling) até turno do jogador

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
- Frontend faz polling de `/state` até `currentTurn` mudar
- Não mostra ações do inimigo, apenas resultado final

---

## Resumo de Prioridades

| Lacuna | Prioridade | Fase | Bloqueador para Protótipo? |
|--------|-----------|------|---------------------------|
| Sistema de Mão de Cartas | HIGH | 3 | ⚠️ Parcial (workaround possível) |
| Sistema de Deck | HIGH | 3 | ⚠️ Parcial (workaround possível) |
| Endpoint End Turn | MEDIUM | 2 | ❌ Não (workaround funciona) |
| Polling/WebSocket | MEDIUM | 3 | ❌ Não (polling funciona) |
| Turno de IA Automático | MEDIUM | 2-3 | ❌ Não (polling funciona) |

---

## Recomendações

### Para Protótipo Imediato (Fase 2.5)

1. **Usar workarounds documentados** para criar protótipo visual básico
2. **Focar em validar integração** backend ↔ frontend
3. **Identificar outras lacunas** durante implementação

### Para Fase 3 (Loop de Run)

1. **Implementar `RunState` e `DeckState`** como fonte de verdade para deck, mão, descarte e exhaust.
2. **Criar endpoints Hand/Deck** em `/api/run/{runId}/hand` e `/api/run/{runId}/deck` antes de CardSelection/Shop.
3. **Adicionar endpoint explícito de end turn** e `process-ai-turns` para manter a lógica no backend.
4. **Adicionar WebSocket/SSE ou contrato formal de polling** para notificações em tempo real.

### Para Fase 4 (Conteúdo MVP)

1. **Expandir sistema de Deck** com raridades, upgrades, transformações
2. **Adicionar sistema de CardSelection** após combate
3. **Implementar Shop** para comprar/vender cartas

---

## Notas de Implementação

### Compatibilidade com Código Existente

Todas as soluções propostas devem:
- **Manter compatibilidade** com endpoints existentes
- **Não quebrar** testes Core (553 testes passando)
- **Seguir convenções** da API REST existente
- **Usar contratos** data-driven (JSON configs)

### Filosofia Data-Driven

- Sistema de Deck deve ser configurável via JSON
- Tamanho de mão, regras de draw, limite de deck devem ser data-driven
- Não hardcodar valores como "5 cartas na mão" ou "30 cartas no deck"

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

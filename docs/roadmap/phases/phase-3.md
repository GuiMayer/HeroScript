# Fase 3 - Loop de Run

**Status:** 🚧 Em implementação — primeiras fatias concluídas
**Dependências:** Fase 1 (Combat), Fase 2 (Status, Modifiers, Gambits)

---

## Visão Geral

A Fase 3 implementa o loop completo de uma run roguelike: gerenciamento de runs, seleção de cartas após combate, sistema de loja, e preparação antes do próximo combate. Esta fase conecta todos os sistemas anteriores em um fluxo de jogo coeso.

## Decisões de Implementação

- Começar por `RunState` e `DeckState`; eles são a fonte de verdade para ouro, PP, deck, mão, descarte, exhaust, recompensas e nó atual.
- Regras de mão/deck devem ser data-driven: tamanho inicial da mão, deck inicial, draw/discard/shuffle/exhaust, pools de recompensa, preços e mapa.
- Conectar os efeitos de deck/economia já existentes no `EffectResolver` a estado real antes de expor fluxos de UI.
- Manter o frontend como camada de apresentação: sem regra de compra, descarte, loja, recompensa ou IA duplicada no cliente.
- Corrigir a lacuna de integração visual com endpoints explícitos para mão/deck e processamento de turno/IA.
- Combate já usa ator arbitrário via `CombatActionCommand`; player, IA, scripts e futuro multiplayer devem passar pelo mesmo contrato com `actorId`.
- Integração combate↔mão já passa por `CombatRunCoordinator`: `runId`/`cardId` validam carta na mão, executam combate e consomem para discard/exhaust/retain conforme tags JSON da `ActionDefinition`.
- Ownership/autorização de controle por ator fica como TODO futuro, antes de multiplayer ou API multi-cliente.

## APIs Planejadas

### 1. Run API

Gerenciamento do estado e progressão de runs.

**Endpoints implementados na primeira fatia:**
- `POST /api/run/start` - Inicia nova run a partir de `runs/{runDefinitionId}.json`
- `GET /api/run/{runId}/state` - Obtém estado completo da run

**Endpoints futuros:**
- `POST /api/run/{runId}/advance` - Avança para próximo nó
- `GET /api/run/{runId}/map` - Obtém mapa de nós disponíveis
- `GET /api/run/{runId}/current-node` - Obtém nó atual
- `POST /api/run/{runId}/end` - Finaliza run (vitória/derrota)

**Tipos de Nó:**
- `COMBAT` - Combate normal
- `ELITE` - Combate elite (mais difícil, melhores recompensas)
- `BOSS` - Combate de boss
- `SHOP` - Loja
- `REST` - Descanso (cura)
- `EVENT` - Evento aleatório
- `TREASURE` - Baú de tesouro
- `CARD_SELECTION` - Seleção de cartas (após combate)
- `PREPARATION` - Preparação (injetar modificadores)

**Recursos:**
- Estrutura data-driven (RunDefinitionResource)
- Segmentos e biomas
- Pathfinding entre nós
- Estado em memória da run
- Deck/hand/discard/exhaust como parte do estado da run

### 1.1. Hand/Deck API

Estado de cartas da run e do combate atual.

**Endpoints implementados:**
- `GET /api/run/{runId}/deck` - Obtém deck, descarte, exhaust e pilha de compra
- `GET /api/run/{runId}/hand` - Obtém mão atual
- `POST /api/run/{runId}/draw` - Compra cartas conforme regras data-driven
- `POST /api/run/{runId}/discard` - Descarta cartas da mão
- `POST /api/run/{runId}/shuffle` - Embaralha descarte quando necessário

### 2. CardSelection API

Sistema de aprender/decompilar poderes após combate.

**Endpoints implementados na primeira fatia:**
- `POST /api/run/{runId}/card-selection/start` - Cria ofertas a partir de JSON
- `POST /api/run/{runId}/card-selection/{selectionInstanceId}/pick` - Escolhe cartas e aplica ao deck da run

**Endpoints futuros:**
- Reroll de ofertas
- Decompose por PP
- Pools por raridade/tags/desbloqueios

**Mecânicas:**
- 3 slots de ofertas
- Aprender é gratuito (1 carta por combate)
- Decompilar gera PP baseado em raridade
- Reroll: 1× grátis por slot, depois custa ouro
- Ofertas baseadas em pool de poderes desbloqueados

**Recursos:**
- Filtragem por tags (fire, crit, dot, etc.)
- Raridade (Comum, Incomum, Raro, Lendário)
- Verificação de combos
- Limite de deck (configurável)

### 3. Shop API

Sistema de loja com poderes, companions, e upgrades.

**Endpoints implementados na primeira fatia:**
- `POST /api/run/{runId}/shop/open` - Abre loja a partir de JSON
- `POST /api/run/{runId}/shop/{shopInstanceId}/buy/{itemId}` - Compra item validando custo no backend

**Endpoints futuros:**
- Reroll loja
- Precos dinamicos/descontos
- Venda de item

**Tipos de Item:**
- Poderes (root/derived)
- Companions
- Upgrades de poderes existentes
- Relíquias (futuro)
- Consumíveis (futuro)

**Recursos:**
- Preços dinâmicos baseados em raridade
- Descontos por raça
- Reroll com custo crescente (logarítmico)
- Pool de itens baseado em progresso da run

### 4. Preparation API

Sistema de injeção de modificadores antes do combate.

**Endpoints implementados na primeira fatia:**
- `POST /api/run/{runId}/preparation/start` - Cria preparacao a partir de JSON
- `POST /api/run/{runId}/preparation/{preparationInstanceId}/apply/{optionId}` - Aplica opcao validada no backend

**Endpoints futuros:**
- Injecao de modificadores em poderes
- Configuracao de gambits de companions
- Preview/remocao de modificadores

**Recursos:**
- Gasto de PP (Power Points)
- Validação de compatibilidade de modificadores
- Preview de efeitos
- Configuração de gambits
- Limite de modificadores por poder

---

## Fluxo Completo de Run

```
1. Seleção de Raça
   POST /api/run/start { raceId: "human" }

2. Loop Principal:
   a. Visualizar Mapa
      GET /api/run/{runId}/map
   
   b. Escolher Nó
      POST /api/run/{runId}/advance { nodeId: "combat-1" }
   
   c. Executar Nó:
      - COMBAT → Combat API (Fase 1)
      - CARD_SELECTION → CardSelection API
      - SHOP → Shop API
      - PREPARATION → Preparation API
      - REST → Cura automática
   
   d. Repetir até Boss derrotado ou herói morto

3. Finalizar Run
   POST /api/run/{runId}/end
```

---

## Integração entre Sistemas

### Run + Combat

Run gerencia sequência de combates:
- Cria CombatState para cada nó de combate
- Persiste estado do herói entre combates
- Aplica recompensas após vitória

### CardSelection + Powers

Ofertas baseadas em pool de poderes:
- Filtra por tags e raridade
- Verifica combos com deck atual
- Respeita desbloqueios (MetaProgression)

### Shop + Economy

Loja usa duas moedas:
- **Ouro** - Comprar itens na loja
- **PP (Power Points)** - Injetar modificadores

Fontes de moeda:
- Ouro: recompensa de combate, decompilar cartas
- PP: decompilar cartas (baseado em raridade)

### Preparation + Modifiers

Preparação permite customização:
- Injeta modificadores em poderes
- Configura gambits de companions
- Preview de mudanças antes de confirmar

---

## Dependências

### Sistemas Core Necessários

- ✅ CombatSystem (Fase 1)
- ✅ StatusSystem (Fase 2)
- ✅ ScriptModifierSystem (Fase 2)
- ✅ GambitEngine (Fase 2)
- ✅ RunManager/DeckState primeira fatia implementada
- ✅ CardSelection primeira fatia implementada
- ✅ Shop primeira fatia implementada
- ✅ Preparation primeira fatia implementada
- ✅ CombatSystem executa acoes por ator arbitrario via `CombatActionCommand`
- ✅ Integração combate↔deck/hand implementada para consumo real de cartas da mão durante ações com `runId`
- ✅ Turnos/ativação por entidade implementados com `ActivationState`, regras JSON, draw/discard automatico e eventos de ativacao
- ✅ Polling incremental e base SSE implementados para eventos de combate/run
- ⏳ Reroll, raridades, pricing dinamico, inject de modificadores e refinamentos de turnos por entidade
- 🧭 TODO futuro: ownership/autorizacao por ator antes de multiplayer ou controle remoto multi-cliente

### Ordem de Implementação

1. **Run/Deck Core** (`src/Core/Run/`)
   - `RunState`, `DeckState`, `RunDefinition`, `NodeConfig`
   - `RunManager`, `NodeResolver`, `DeckManager`
   - Pathfinding, progressão, mão, draw, discard, exhaust e shuffle
   - Integração dos effects `DRAW_CARD`, `DISCARD_CARD`, `EXHAUST_CARD`, `GAIN_GOLD` e `GAIN_PP` com estado real

2. **Run API** (src/API/Controllers/RunController.cs)
   - Start/end run
   - Advance node
   - Query state/map
   - Query hand/deck state

3. **Integração Visual de Combate**
    - `POST /api/combat/{combatId}/end-turn`
    - `POST /api/combat/{combatId}/process-ai-turns` executa decisoes de IA no backend
    - `POST /api/combat/{combatId}/action` exige `actorId` para player, IA, script e futuro multiplayer
   - `POST /api/combat/{combatId}/action` com `runId`/`cardId` consome carta real da mão apenas após sucesso do combate
   - `GET /api/combat/{combatId}/available-actions?actorId=...&runId=...` filtra por mão e informa destino de consumo
    - `POST /api/combat/{combatId}/activation/start|end|advance|process-ai` coordena ativacao por entidade
    - Regras em `combat-turn-rules/{rulesId}.json` controlam draw/discard automatico, escopo player/all actors e IA
    - `GET /api/events?afterSequence=...` e `GET /api/combat/{combatId}/events` fornecem polling incremental
    - `GET /api/events/stream` e `GET /api/combat/{combatId}/events/stream` fornecem base SSE

4. **TODO Futuro: Ownership/Autorizacao por Ator**
   - Definir `controllerId`/`playerId`/`source` para comandos externos
   - Mapear quais atores cada controlador pode comandar
   - Separar fontes `PLAYER_INPUT`, `AI`, `SYSTEM` e `SCRIPT` sem criar caminhos diferentes no combate
   - Validar permissao antes de chamar `CombatSystem.ExecuteAction`
   - Manter `CombatSystem` actor-agnostic; autorizacao deve ficar na camada de controle/API

5. **CardSelection Core** (src/Core/Run/CardSelection/)
   - CardOffer, CardPool
   - OfferGenerator, DeckManager
   - Reroll logic

6. **CardSelection API** (src/API/Controllers/CardSelectionController.cs)
   - Generate offers
   - Learn/decompose
   - Reroll

7. **Shop Core** (src/Core/Run/Shop/)
   - ShopInventory, ShopItem
   - PricingEngine, RerollCostCalculator
   - Purchase validation

8. **Shop API** (src/API/Controllers/ShopController.cs)
   - Generate shop
   - Buy/sell items
   - Reroll

9. **Preparation Core** (src/Core/Run/Preparation/)
   - PreparationState
   - ModifierInjector
   - GambitConfigurator

10. **Preparation API** (src/API/Controllers/PreparationController.cs)
   - Inject modifiers
   - Configure gambits
   - Preview changes

---

## Exemplos de Uso

### Iniciar Run

```http
POST /api/run/start
Content-Type: application/json

{
  "raceId": "human",
  "difficulty": "normal",
  "seed": null
}
```

### Gerar Ofertas de Cartas

```http
GET /api/cardselection/{runId}/offers
```

### Aprender Carta

```http
POST /api/cardselection/{runId}/learn
Content-Type: application/json

{
  "slotIndex": 0,
  "offerId": "offer-123"
}
```

### Comprar na Loja

```http
POST /api/shop/{runId}/buy
Content-Type: application/json

{
  "itemId": "companion-wolf",
  "gold": 150
}
```

### Injetar Modificador

```http
POST /api/preparation/{runId}/inject
Content-Type: application/json

{
  "powerId": "fireball",
  "modifierId": "GO_AGAIN",
  "ppCost": 50
}
```

---

## Próximos Passos

Próximo foco dentro da Fase 3:
- **Turnos/ativação por entidade** - substituir o end-turn global por regras data-driven de início/fim de ativação, compra/descarte automática e janelas de IA/player.
- **Eventos/polling/SSE** - expor mudanças de estado para o frontend animar ações já resolvidas no backend.
- **Refinamentos de Run** - reroll, raridades, pricing dinamico e inject real de modificadores.

Após completar a Fase 3, a Fase 4 adicionará conteúdo MVP:
- **Race API** - Raças jogáveis
- **Power API** - Poderes e árvores de derivados
- **Companion API** - Companions disponíveis
- **Enemy API** - Inimigos e intents

Ver: [phase-4.md](phase-4.md)

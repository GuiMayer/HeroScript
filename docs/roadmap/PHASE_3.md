# Fase 3 - Loop de Run

**Status:** 📋 Planejado  
**Dependências:** Fase 1 (Combat), Fase 2 (Status, Modifiers, Gambits)

---

## Visão Geral

A Fase 3 implementa o loop completo de uma run roguelike: gerenciamento de runs, seleção de cartas após combate, sistema de loja, e preparação antes do próximo combate. Esta fase conecta todos os sistemas anteriores em um fluxo de jogo coeso.

## APIs Planejadas

### 1. Run API

Gerenciamento do estado e progressão de runs.

**Endpoints:**
- `POST /api/run/start` - Inicia nova run com raça selecionada
- `POST /api/run/{runId}/advance` - Avança para próximo nó
- `GET /api/run/{runId}/state` - Obtém estado completo da run
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
- Estado persistente da run

### 2. CardSelection API

Sistema de aprender/decompilar poderes após combate.

**Endpoints:**
- `GET /api/cardselection/{runId}/offers` - Gera ofertas de cartas (3 slots)
- `POST /api/cardselection/{runId}/learn` - Aprende carta (gratuito)
- `POST /api/cardselection/{runId}/decompose` - Decompila carta por PP
- `POST /api/cardselection/{runId}/reroll` - Reroll ofertas (1× grátis/slot)
- `GET /api/cardselection/{runId}/deck` - Obtém deck atual

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

**Endpoints:**
- `GET /api/shop/{runId}` - Gera loja baseada em nó
- `POST /api/shop/{runId}/buy` - Compra item
- `POST /api/shop/{runId}/reroll` - Reroll loja (custo logarítmico)
- `GET /api/shop/{runId}/prices` - Obtém preços com descontos
- `POST /api/shop/{runId}/sell` - Vende item (futuro)

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

**Endpoints:**
- `POST /api/preparation/{runId}/inject` - Injeta modificador em poder
- `POST /api/preparation/{runId}/gambits` - Configura gambits de companions
- `GET /api/preparation/{runId}/state` - Obtém estado de preparação
- `GET /api/preparation/{runId}/available-modifiers` - Lista modificadores disponíveis
- `POST /api/preparation/{runId}/remove` - Remove modificador

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
- ⏳ RunManager (implementar)
- ⏳ CardSelectionSystem (implementar)
- ⏳ ShopSystem (implementar)
- ⏳ PreparationSystem (implementar)

### Ordem de Implementação

1. **Run Core** (src/Core/Run/)
   - RunState, RunDefinition, NodeConfig
   - RunManager, NodeResolver
   - Pathfinding e progressão

2. **Run API** (src/API/Controllers/RunController.cs)
   - Start/end run
   - Advance node
   - Query state/map

3. **CardSelection Core** (src/Core/Run/CardSelection/)
   - CardOffer, CardPool
   - OfferGenerator, DeckManager
   - Reroll logic

4. **CardSelection API** (src/API/Controllers/CardSelectionController.cs)
   - Generate offers
   - Learn/decompose
   - Reroll

5. **Shop Core** (src/Core/Run/Shop/)
   - ShopInventory, ShopItem
   - PricingEngine, RerollCostCalculator
   - Purchase validation

6. **Shop API** (src/API/Controllers/ShopController.cs)
   - Generate shop
   - Buy/sell items
   - Reroll

7. **Preparation Core** (src/Core/Run/Preparation/)
   - PreparationState
   - ModifierInjector
   - GambitConfigurator

8. **Preparation API** (src/API/Controllers/PreparationController.cs)
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

Após completar a Fase 3, a Fase 4 adicionará conteúdo MVP:
- **Race API** - Raças jogáveis
- **Power API** - Poderes e árvores de derivados
- **Companion API** - Companions disponíveis
- **Enemy API** - Inimigos e intents

Ver: [PHASE_4.md](PHASE_4.md)

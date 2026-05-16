# HeroScript - Roadmap Estratégico

**Última atualização:** 2026-05-16  
**Status:** Em estabilização da Fase 2  
**Escopo:** Engine versátil para Card Games (Slay the Spire, Balatro, TCGs)

---

## Visão Geral

Este documento consolida a visão estratégica do HeroScript, integrando o roadmap técnico de implementação com os pilares arquiteturais fundamentais que justificam a complexidade do sistema: **Timeline (Undo/Redo)**, **Baralhos Modulares (Deltas Universais)**, e **Sistema de Mão/Deck** para suportar múltiplos gêneros de card games.

**Documentos relacionados:**
- [roadmap/README.md](roadmap/README.md) - Roadmap técnico detalhado por fase
- [session.md](session.md) - Visão arquitetural original (Timeline + Baralhos)
- [roadmap/CORE_MODULES_ANALYSIS.md](roadmap/CORE_MODULES_ANALYSIS.md) - Análise de módulos implementados vs necessários

---

## Filosofia Arquitetural

O HeroScript não é apenas um card battler roguelike. É uma **engine versátil para card games** construída sobre três pilares fundamentais:

1. **Timeline** - Manipulação temporal de estado (undo/redo/simulação)
2. **Baralhos Modulares** - Fontes de poder configuráveis via JSON
3. **Sistema de Mão/Deck** - Fundação para qualquer card game

---

## Análise de Versatilidade: Suporte a Diferentes Gêneros

### **Gêneros Suportados**

| Gênero | Status | Tempo Estimado | Prioridade |
|--------|--------|----------------|------------|
| **Slay the Spire** | ⚠️ 80% pronto | +2-3 semanas | **ALTA** |
| **Balatro** | ✅ Totalmente suportado | Imediato | ALTA |
| **Vidaria (combate direto)** | ✅ Totalmente suportado | Imediato | ALTA |
| **MTG/Hearthstone** | ❌ Requer permanentes | +4-6 semanas | MÉDIA |
| **Inscryption** | ❌ Requer permanentes | +4-6 semanas | BAIXA |

### **Gaps Críticos Identificados**

#### **Gap 1: Sistema de Mão e Deck (CRÍTICO)**
**Status:** ❌ Não implementado  
**Impacto:** Bloqueia TODOS os card games tradicionais  
**Prioridade:** MÁXIMA  
**Tempo:** 1-2 semanas

**Arquivos faltando:**
- `src/Core/Cards/Hand.cs`
- `src/Core/Cards/Deck.cs`
- `src/Core/Cards/CardInstance.cs`
- `src/Core/Cards/ICardManager.cs`
- `src/Core/Cards/CardManager.cs`

**Funcionalidades necessárias:**
- Comprar cartas do deck
- Descartar cartas da mão
- Exhaust (remover cartas da run)
- Shuffle do discard pile de volta ao deck
- Limite de tamanho da mão
- Retain (cartas que permanecem na mão)
- Ethereal (cartas descartadas no fim do turno)

**Nota:** `EffectType` já define `DRAW_CARD`, `DISCARD_CARD`, `EXHAUST_CARD`, mas não têm implementação.

---

#### **Gap 2: Sistema de Permanentes (CRÍTICO para TCGs)**
**Status:** ❌ Não implementado  
**Impacto:** Bloqueia MTG/Hearthstone/Inscryption  
**Prioridade:** MÉDIA (apenas se quiser suportar TCGs)  
**Tempo:** 3-4 semanas

**Arquivos faltando:**
- `src/Core/Board/Permanent.cs`
- `src/Core/Board/BoardState.cs`
- `src/Core/Board/IBoardManager.cs`

**Funcionalidades necessárias:**
- Criaturas/permanentes em campo
- Ataque/defesa de criaturas
- Keywords (Taunt, Divine Shield, Charge)
- Combat phases (atacante escolhe atacantes, defensor escolhe bloqueadores)

**Nota:** Sistema atual assume combate direto (herói vs inimigos), não combate mediado por criaturas.

---

#### **Gap 3: Sistema de Reação (CRÍTICO para MTG)**
**Status:** ❌ Não implementado  
**Impacto:** Bloqueia MTG (instants são fundamentais)  
**Prioridade:** BAIXA (apenas se quiser MTG-like)  
**Tempo:** 2-3 semanas

**Arquivos faltando:**
- `src/Core/Events/TriggerSystem.cs`
- `src/Core/Events/PriorityStack.cs`

**Funcionalidades necessárias:**
- Instants em resposta a ações
- Secrets que disparam automaticamente
- Pilha de prioridade (stack)
- Reação interativa (jogador escolhe se quer ativar)

**Nota:** `StatusEffect` com `ON_DAMAGE_DEALT` / `ON_DAMAGE_TAKEN` é um trigger básico, mas não suporta reação interativa.

---

### **Matriz de Compatibilidade por Gênero**

| Mecânica | Slay the Spire | MTG/Hearthstone | Inscryption | Balatro | Status Atual |
|----------|----------------|-----------------|-------------|---------|--------------|
| **Deck dinâmico** | ✅ Essencial | ✅ Essencial | ✅ Essencial | ✅ Essencial | ❌ Gap 1 |
| **Mão de cartas** | ✅ Essencial | ✅ Essencial | ✅ Essencial | ✅ Essencial | ❌ Gap 1 |
| **Comprar/descartar** | ✅ Essencial | ✅ Essencial | ✅ Essencial | ✅ Essencial | ⚠️ Tipo existe |
| **Exhaust** | ✅ Essencial | ⚪ Não usa | ⚪ Não usa | ⚪ Não usa | ⚠️ Tipo existe |
| **Energia/Mana** | ✅ Essencial | ✅ Essencial | ✅ Essencial | ⚪ Não usa | ✅ Suportado |
| **Status effects** | ✅ Essencial | ✅ Essencial | ✅ Essencial | ⚪ Não usa | ⚠️ Fase 1 |
| **Permanentes** | ⚪ Não usa | ✅ Essencial | ✅ Essencial | ⚪ Não usa | ❌ Gap 2 |
| **Instants/Secrets** | ⚪ Não usa | ✅ Essencial | ⚪ Não usa | ⚪ Não usa | ❌ Gap 3 |
| **Graveyard** | ⚪ Opcional | ✅ Essencial | ⚪ Opcional | ⚪ Não usa | ⚠️ Viável |
| **Targeting complexo** | ⚪ Opcional | ✅ Essencial | ⚪ Opcional | ⚪ Não usa | ⚠️ Expansível |
| **Keywords** | ⚪ Opcional | ✅ Essencial | ✅ Essencial | ⚪ Não usa | ⚠️ Via Status |
| **Relíquias** | ✅ Essencial | ⚪ Opcional | ⚪ Opcional | ⚪ Não usa | ⚠️ Via Modifiers |
| **Upgrade de cartas** | ✅ Essencial | ⚪ Não usa | ⚪ Não usa | ⚪ Não usa | ⚠️ Viável |
| **Multiplicadores** | ⚪ Opcional | ⚪ Opcional | ⚪ Opcional | ✅ Essencial | ✅ Pipeline |
| **Efeitos condicionais** | ✅ Essencial | ✅ Essencial | ✅ Essencial | ✅ Essencial | ✅ Suportado |
| **Efeitos encadeados** | ✅ Essencial | ✅ Essencial | ✅ Essencial | ⚪ Opcional | ✅ Suportado |

**Legenda:**
- ✅ Essencial - Mecânica fundamental do gênero
- ⚪ Opcional/Não usa - Mecânica não é necessária
- ⚠️ Viável - Pode ser implementado com estrutura atual
- ❌ Gap - Não suportado, requer nova implementação

---

## Pilares Arquiteturais

### 1. Timeline (Manipulação Temporal de Estado)

### 1. Timeline (Manipulação Temporal de Estado)

**Conceito:** O jogo permite desfazer, refazer e simular ações futuras sem commit, transformando gameplay linear em exploração de possibilidades.

**Componentes:**
- **Snapshot-based Undo**: Cada ação gera snapshot leve do estado (recursos + tags)
- **Command Pattern**: Histórico armazena comandos (não apenas deltas), permitindo replay determinístico
- **Timeline Não-Linear**: Simulação de sequências futuras ("e se eu jogar essas 3 cartas?")
- **Playback Visual**: Interface apenas reflete estado; nunca comanda lógica

**Valor para o jogador:**
- Botão "Desfazer" 100% confiável (reverter erros táticos)
- Theory crafting interativo (testar combos sem commit)
- Exploração de árvore de decisões (não apenas jogo linear)

**Status atual:** ❌ **NÃO IMPLEMENTADO**
- Não existe `SnapshotManager`
- Não existe `CommandHistory`
- `CombatState` não é versionado/snapshotável
- Não há endpoints de undo/redo na API

---

### 2. Baralhos Modulares (Deltas Universais)

**Conceito:** Diferentes "fontes de poder" (Vidaria, Crolbia, etc.) não são hardcoded, mas configuradas via JSON com herança delta e injeção dinâmica de buckets no pipeline.

**Componentes:**
- **Herança de Dados**: JSONs herdam de templates base e aplicam deltas
- **Injeção de Buckets**: Cada baralho define quais buckets entram no pipeline de dano
- **Tags como DNA**: Baralhos injetam tags que criam sinergias
- **Recursos Customizados**: Baralhos mapeam para recursos arbitrários (Mana, Sanidade, Calor Mecânico)

**Exemplo prático (Vidaria - Magia de Sangue):**
```json
{
  "SourceID": "POWER_VIDARIA",
  "GlobalModifiers": {
    "all_actions_gain_tag": ["Organic"]
  },
  "BucketPipeline": [
    "base",
    "lifesteal",
    "blood_sacrifice",
    "increased",
    "more",
    "crit",
    "final"
  ],
  "ResourceAffinity": ["health", "blood_tokens"]
}
```

**Valor para o jogador:**
- Fontes de poder tematicamente distintas (não apenas reskins)
- Sinergias emergentes entre cartas do mesmo baralho
- Possibilidade de baralhos híbridos (sandbox mode)

**Status atual:** ⚠️ **PARCIALMENTE IMPLEMENTADO**
- ✅ `PipelineConfiguration` com buckets customizáveis existe
- ✅ Sistema de tags existe
- ✅ `ResourceManager` é genérico e extensível
- ❌ Não existe conceito de "Baralho" como entidade
- ❌ Não existe herança de JSON (deltas) para baralhos
- ❌ Não existe injeção dinâmica de buckets por baralho

---

## Comparação: Visão Original vs Sistema Atual

| Aspecto | Visão Original | Sistema Atual | Gap |
|---------|----------------|---------------|-----|
| **Arquitetura** | Headless Kernel determinístico | Headless com API REST | ✅ Alinhado |
| **Pipeline de Dano** | Bucket-based com deltas | Bucket-based implementado | ✅ Alinhado |
| **Timeline/Undo** | Snapshot + Command Pattern | Não existe | ❌ **Crítico** |
| **Baralhos Modulares** | JSON com herança + injeção de buckets | Ações individuais em JSON | ⚠️ Parcial |
| **Theory Crafting** | Simulação de futuro sem commit | Não existe | ❌ **Crítico** |
| **Recursos Customizados** | Mapeamento dinâmico por baralho | ResourceManager genérico | ✅ Alinhado |
| **Determinismo** | 100% reproduzível (417 testes em 470ms) | Testes existem mas sem replay | ⚠️ Parcial |

---

## Roadmap de Implementação

### Fase 0: Fundação ✅ **COMPLETO**
**Status:** Implementado (2026-05-07)

**Sistemas:**
- Config API (herança delta estruturada)
- MathExpression API (3 modos de operação)
- Resource API (carregamento e cache)
- Formula API (fórmulas data-driven)

**Estatísticas:**
- 97 testes de Math
- 23 testes de Resources
- 6 APIs REST

---

### Fase 1: EventBus e Combate Básico ✅ **COMPLETO**
**Status:** Implementado (2026-05-09)

**Sistemas:**
- EventBus (pub/sub + event sourcing)
- CombatSystem (estado imutável, thread-safe)
- DamagePipeline (bucket-based, JSON-driven)
- Alternative Costs (múltiplas opções de pagamento)

**Estatísticas:**
- 294 testes (Combat: 53, Events: 18, Damage: 126, Math: 97)
- 3 APIs REST (Events, Combat, Damage)
- 85+ arquivos Core

**Documentação:**
- [PHASE_1.md](roadmap/PHASE_1.md)
- [EVENTBUS_SYSTEM.md](EVENTBUS_SYSTEM.md)
- [DAMAGE_PIPELINE.md](DAMAGE_PIPELINE.md)

---

### Fase 2: Fundação para Card Games 📋 **EM PLANEJAMENTO**
**Dependências:** Fase 1  
**Tempo estimado:** 14-18 dias  
**Prioridade:** CRÍTICA (desbloqueia Slay the Spire e outros card games)

**Atualização 2026-05-16:** a Fase 2 não está mais totalmente em planejamento. `StatusEffectManager`, `StatusEffectProcessor`, API de StatusEffect e configs de status existem, mas ainda estão em estabilização. Antes de iniciar Run/Shop/CardSelection/Content, o próximo passo natural é fechar Status lifecycle, EffectResolver e integração ActionManager/CombatSystem.

**Sistemas a implementar:**

#### **2.1. Status Effect System (3-4 dias)**
- ✅ `StatusEffectDefinition` - Definição de status (burning, poison, shield, etc.)
- ✅ `StatusEffectInstance` - Instância ativa com stacks e duração
- ✅ `StatusEffectManager` - Gerenciador de status ativos
- ⚠️ Integração com `EffectResolver` para `APPLY_STATUS` e `REMOVE_STATUS` ainda precisa fechamento semântico
- ⚠️ Processamento de status no início/fim do turno existe parcialmente; DoT/HoT ainda precisam aplicar dano/cura real via sistemas corretos
- ⚠️ API.Tests de StatusEffect ainda precisam ser destravados no runner de integração

**Entregável:** Ações podem aplicar status effects que são processados ao longo do tempo.

**Correções recentes:** rotas REST de StatusEffect alinhadas, `DamageCalculator` recebe `IStatusEffectManager` via DI, Core.Tests estabilizados em 524 testes passando.

---

#### **2.2. Turn Management System (2-3 dias)**
- `TurnPhase.cs` - Enum de fases (HERO_TURN, ENEMY_TURN, END_OF_TURN)
- `TurnManager.cs` - Gerenciador de fases e processamento de turnos
- Processamento de status effects no end of turn
- Regeneração passiva de recursos (energy)
- Transição automática entre fases
- 20+ testes unitários

**Entregável:** Sistema de turnos estruturado com fases e processamento de efeitos temporais.

---

#### **2.3. Hand & Deck System (4-5 dias)** ⭐ **NOVO - CRÍTICO**
- `Hand.cs` - Mão de cartas com limite de tamanho
- `Deck.cs` - Deck com draw pile, discard pile, exhaust pile
- `CardInstance.cs` - Instância de carta com upgrades e modificadores
- `CardManager.cs` - Gerenciador de operações de cartas
- Implementação de `DRAW_CARD`, `DISCARD_CARD`, `EXHAUST_CARD` em `EffectResolver`
- Shuffle automático quando draw pile esvazia
- Suporte a Retain (cartas que permanecem na mão)
- Suporte a Ethereal (cartas descartadas no fim do turno)
- 40+ testes unitários + 5 testes de integração

**Entregável:** Sistema completo de mão e deck, desbloqueando card games tradicionais.

---

#### **2.4. Enemy AI System (2-3 dias)**
- `EnemyBehavior.cs` - Enum de comportamentos (AGGRESSIVE, DEFENSIVE, RANDOM)
- `EnemyAI.cs` - Sistema de decisão de ações
- Integração com `CombatSystem` para turno dos inimigos
- Endpoint `/api/combat/{combatId}/enemy-turn`
- 15+ testes unitários

**Entregável:** Inimigos atacam automaticamente, gameplay funcional.

---

#### **2.5. Effect-Action Integration (2 dias)**
- Integrar `EffectResolver` com `CombatSystem`
- Remover lógica hardcoded de dano (BASIC_ATTACK_DAMAGE, DEFAULT_POWER_COST)
- Processar ações via `EffectResolver.ResolveEffects()`
- Criar ações de exemplo em JSON (basic_attack, fireball, heal)
- 20+ testes de integração

**Entregável:** Ações processadas via sistema de efeitos, totalmente data-driven.

---

#### **2.6. Action Result Feedback (1-2 dias)**
- `ActionResult.cs` - Record com breakdown detalhado de efeitos
- Modificar `CombatSystem.ExecuteAction()` para retornar `ActionResult`
- Incluir: dano, crítico, status aplicados, cartas compradas, etc.
- 10+ testes

**Entregável:** API retorna feedback detalhado para animações e UI.

---

**Estatísticas esperadas:**
- 135+ testes novos
- 15+ arquivos Core novos
- 2 APIs REST expandidas (Combat, Cards)

**Documentação:**
- [PHASE_2.md](roadmap/PHASE_2.md) (atualizar)
- `STATUS_EFFECTS_SYSTEM.md` (criar)
- `HAND_DECK_SYSTEM.md` (criar)

**Resultado:** MVP jogável com suporte completo a Slay the Spire e Balatro.

---

### Fase 3: Camadas Avançadas de Combate 📋 **PLANEJADO**
**Dependências:** Fase 2  
**Tempo estimado:** 8-10 dias  
**Prioridade:** MÉDIA (polish e features avançadas)

**Sistemas a implementar:**

#### **3.1. ScriptModifier System (3-4 dias)**
- Modificadores que alteram comportamento de poderes (Go Again, Multi-Hit, Explosivo)
- Validação de compatibilidade entre modificadores
- Custo em PP (Power Points)
- Raridade e tags para filtros
- 25+ testes

**Entregável:** Poderes podem ter modificadores injetados dinamicamente.

---

#### **3.2. Gambit System (3-4 dias)**
- Companions com regras condicionais (IF/THEN)
- Triggers (HERO_HP_BELOW, ON_HERO_ACTION, etc.)
- Actions (usar poder, aplicar status, curar)
- Prioridade e cooldowns
- 20+ testes

**Entregável:** Companions reagem automaticamente a condições de combate.

---

#### **3.3. Advanced Targeting (2 dias)**
- Suporte a múltiplos alvos (escolher N alvos)
- Targeting de cartas na mão/discard
- Targeting de permanentes (futuro)
- 15+ testes

**Entregável:** Ações podem ter targeting complexo.

---

**Documentação:**
- [PHASE_3.md](roadmap/PHASE_3.md) (atualizar)
- `SCRIPT_MODIFIERS.md` (criar)
- `GAMBIT_SYSTEM.md` (criar)

---

### Fase 4: Loop de Run (Roguelike) 📋 **PLANEJADO**
**Dependências:** Fase 2, Fase 3  
**Tempo estimado:** 10-12 dias  
**Prioridade:** ALTA (gameplay loop completo)

**Sistemas a implementar:**

#### **4.1. Run Management (3-4 dias)**
- `RunState.cs` - Estado de uma run (deck, relíquias, ouro, progresso)
- `RunManager.cs` - Gerenciador de runs
- Estrutura de nós (COMBAT, ELITE, BOSS, SHOP, REST, EVENT)
- Pathfinding entre nós
- 30+ testes

**Entregável:** Sistema de runs com progressão e mapa de nós.

---

#### **4.2. Card Selection (2-3 dias)**
- Ofertas de cartas após combate (3 slots)
- Aprender carta (gratuito)
- Decompilar carta por PP
- Reroll ofertas (1× grátis/slot)
- 20+ testes

**Entregável:** Sistema de aprender/decompilar cartas após combate.

---

#### **4.3. Shop System (2-3 dias)**
- Loja com poderes, companions, upgrades
- Preços dinâmicos baseados em raridade
- Descontos por raça
- Reroll com custo crescente
- 20+ testes

**Entregável:** Sistema de loja funcional.

---

#### **4.4. Relic System (2 dias)**
- `Relic.cs` - Definição de relíquia
- `RelicManager.cs` - Gerenciador de relíquias
- Modificadores passivos permanentes
- 15+ testes

**Entregável:** Sistema de relíquias (modificadores permanentes).

---

#### **4.5. Card Upgrade System (1-2 dias)**
- Upgrade de cartas em fogueiras
- Modificadores permanentes em cartas
- 10+ testes

**Entregável:** Sistema de upgrade de cartas.

---

**Documentação:**
- [PHASE_4.md](roadmap/PHASE_4.md) (atualizar)
- `RUN_SYSTEM.md` (criar)
- `CARD_SELECTION.md` (criar)

---

### Fase 5: Conteúdo e Baralhos Modulares 📋 **PLANEJADO**
**Dependências:** Fase 2, Fase 3, Fase 4  
**Tempo estimado:** 12-15 dias  
**Prioridade:** ALTA (diferencial competitivo)

**Sistemas a implementar:**

#### **5.1. Deck System (Baralhos Modulares) (4-5 dias)**
- `DeckDefinition.cs` - Definição de baralho com herança JSON
- `DeckManager.cs` - Gerenciador de baralhos
- Injeção dinâmica de buckets no pipeline
- Global modifiers por baralho
- Resource affinity por baralho
- 30+ testes

**Entregável:** Sistema de baralhos modulares (Vidaria, Crolbia, etc.).

---

#### **5.2. Power Library (3-4 dias)**
- Criar 15-20 poderes básicos em JSON
- 3-5 poderes por baralho (Vidaria, Crolbia, etc.)
- Balanceamento inicial
- 20+ testes de integração

**Entregável:** Biblioteca de poderes jogáveis.

---

#### **5.3. Enemy Library (2-3 dias)**
- Criar 10-15 inimigos em JSON
- Comportamentos variados (AGGRESSIVE, DEFENSIVE, RANDOM)
- Balanceamento inicial
- 15+ testes

**Entregável:** Biblioteca de inimigos.

---

#### **5.4. Race System (2-3 dias)**
- `Race.cs` - Definição de raça
- Starter deck por raça
- Modificadores passivos por raça
- 15+ testes

**Entregável:** Sistema de raças jogáveis.

---

**Documentação:**
- [PHASE_5.md](roadmap/PHASE_5.md) (atualizar)
- `DECK_SYSTEM.md` (criar)
- `CONTENT_CREATION_GUIDE.md` (criar)

---

### Fase 6: Persistência e Meta-Progression 📋 **PLANEJADO**
**Dependências:** Fase 4, Fase 5  
**Tempo estimado:** 8-10 dias  
**Prioridade:** MÉDIA (retenção de jogadores)

**Sistemas a implementar:**

#### **6.1. Save/Load System (4-5 dias)**
- Serialização de `RunState`
- Persistência em arquivo/banco
- Load de runs salvas
- Validação de integridade
- 25+ testes

**Entregável:** Sistema de save/load de runs.

---

#### **6.2. Meta-Progression (4-5 dias)**
- Desbloqueios permanentes (cartas, relíquias, raças)
- Progressão entre runs
- Achievements
- 20+ testes

**Entregável:** Sistema de meta-progressão.

---

**Documentação:**
- [PHASE_6.md](roadmap/PHASE_6.md) (atualizar)
- `SAVE_SYSTEM.md` (criar)

---

### Fase 7: Timeline System (Undo/Redo) 📋 **PLANEJADO**
**Dependências:** Fase 2  
**Tempo estimado:** 10-12 dias  
**Prioridade:** ALTA (diferencial competitivo único)

**Sistemas a implementar:**

#### **7.1. Snapshot System (3-4 dias)**
- `ISnapshotManager.cs` - Interface de snapshots
- `SnapshotManager.cs` - Gerenciador de snapshots
- Snapshot leve (apenas deltas)
- Garbage collection de snapshots antigos
- 25+ testes

**Entregável:** Sistema de snapshots para undo/redo.

---

#### **7.2. Command History (3-4 dias)**
- `ICommand.cs` - Interface de comandos
- `CommandHistory.cs` - Histórico de comandos
- Command Pattern para todas as ações
- Replay determinístico
- 25+ testes

**Entregável:** Sistema de histórico de comandos.

---

#### **7.3. Timeline API (2-3 dias)**
- `POST /api/combat/{combatId}/undo` - Desfaz última ação
- `POST /api/combat/{combatId}/redo` - Refaz ação desfeita
- `POST /api/combat/{combatId}/simulate` - Simula sequência sem commit
- `GET /api/combat/{combatId}/timeline` - Obtém histórico
- 20+ testes de integração

**Entregável:** API de timeline com undo/redo/simulação.

---

#### **7.4. Theory Crafting UI (2 dias)**
- Endpoint de simulação de sequências
- Feedback de estado final sem commit
- 10+ testes

**Entregável:** Sistema de theory crafting interativo.

---

**Documentação:**
- `TIMELINE_SYSTEM.md` (criar)
- `THEORY_CRAFTING.md` (criar)

---

### Fase 8: Suporte a TCGs (Opcional) 📋 **PLANEJADO**
**Dependências:** Fase 2, Fase 3  
**Tempo estimado:** 15-20 dias  
**Prioridade:** BAIXA (apenas se quiser suportar MTG/Hearthstone)

**Sistemas a implementar:**

#### **8.1. Permanent System (5-6 dias)**
- `Permanent.cs` - Criaturas/permanentes em campo
- `BoardState.cs` - Estado do tabuleiro
- `BoardManager.cs` - Gerenciador de permanentes
- Attack/defense de criaturas
- Keywords (Taunt, Divine Shield, Charge)
- 40+ testes

**Entregável:** Sistema de permanentes (criaturas em campo).

---

#### **8.2. Reaction System (4-5 dias)**
- `TriggerSystem.cs` - Sistema de triggers
- `PriorityStack.cs` - Pilha de prioridade
- Instants em resposta
- Secrets que disparam automaticamente
- 30+ testes

**Entregável:** Sistema de reação (instants/secrets).

---

#### **8.3. Combat Phases (2-3 dias)**
- Fases de combate complexas (Untap, Upkeep, Draw, Main, Combat, End)
- Transições entre fases
- 20+ testes

**Entregável:** Sistema de fases de combate estilo MTG.

---

#### **8.4. Graveyard & Zones (2-3 dias)**
- Interação com cartas no graveyard
- Exile zone
- Ressurreição de cartas
- 15+ testes

**Entregável:** Sistema de zonas completo.

---

#### **8.5. Mulligan System (1-2 dias)**
- Trocar mão inicial
- 10+ testes

**Entregável:** Sistema de mulligan.

---

**Documentação:**
- `PERMANENT_SYSTEM.md` (criar)
- `REACTION_SYSTEM.md` (criar)
- `TCG_SUPPORT.md` (criar)

---

### Fase 9: Modos Especiais 📋 **PLANEJADO**
**Dependências:** Fase 6  
**Tempo estimado:** 5-7 dias  
**Prioridade:** BAIXA (features extras)

**Sistemas a implementar:**

#### **9.1. Seed System (2-3 dias)**
- Runs com seed determinística
- Compartilhamento de seeds
- 15+ testes

**Entregável:** Sistema de seeds para runs reproduzíveis.

---

#### **9.2. Daily Challenges (2-3 dias)**
- Desafios diários com seed fixa
- Leaderboard
- 15+ testes

**Entregável:** Sistema de desafios diários.

---

#### **9.3. Custom Modes (1-2 dias)**
- Modos customizados (sandbox, draft, etc.)
- 10+ testes

**Entregável:** Modos de jogo customizados.

---

**Documentação:**
- [PHASE_9.md](roadmap/PHASE_9.md) (criar)
- `CUSTOM_MODES.md` (criar)

---

## Pilares Arquiteturais: Roadmap de Implementação

### Timeline System (Fase 2.5 - Nova)

**Prioridade:** Alta (diferencial competitivo)  
**Dependências:** Fase 1 (CombatSystem), Fase 2 (Status Effects)  
**Tempo estimado:** 3-4 semanas

**Componentes:**

#### 1. Snapshot System
```csharp
public interface ISnapshotManager
{
    Snapshot<T> CreateSnapshot<T>(T state);
    Result<T> RestoreSnapshot<T>(string snapshotId);
    void ClearSnapshots(Guid combatId);
}
```

**Implementação:**
- Snapshot leve (apenas deltas, não estado completo)
- Serialização eficiente (System.Text.Json)
- Garbage collection de snapshots antigos

#### 2. Command History
```csharp
public interface ICommandHistory
{
    void RecordCommand(ICommand command);
    Result<CombatState> Undo(Guid combatId);
    Result<CombatState> Redo(Guid combatId);
    List<ICommand> GetHistory(Guid combatId);
}
```

**Implementação:**
- Command Pattern para todas as ações
- Replay determinístico (mesma seed = mesmo resultado)
- Histórico persistido em memória (ConcurrentDictionary)

#### 3. Timeline API
**Endpoints:**
- `POST /api/combat/{combatId}/undo` - Desfaz última ação
- `POST /api/combat/{combatId}/redo` - Refaz ação desfeita
- `POST /api/combat/{combatId}/simulate` - Simula sequência de ações sem commit
- `GET /api/combat/{combatId}/timeline` - Obtém histórico de comandos

**Exemplo de uso (Theory Crafting):**
```http
POST /api/combat/{combatId}/simulate
{
  "actions": [
    { "actionType": "POWER", "powerId": "fireball", "targetId": "enemy1" },
    { "actionType": "POWER", "powerId": "ice_lance", "targetId": "enemy1" },
    { "actionType": "BASIC_ATTACK", "targetId": "enemy2" }
  ]
}

Response:
{
  "finalState": { ... },
  "actionResults": [ ... ],
  "committed": false
}
```

**Testes necessários:**
- Undo/redo de ações simples
- Undo/redo com status effects
- Simulação de sequências complexas
- Determinismo (replay produz mesmo resultado)
- Performance (snapshots não devem causar lag)

---

### Deck System (Fase 4.5 - Nova)

**Prioridade:** Média (pode ser simplificado no MVP)  
**Dependências:** Fase 4 (Race API, Power API)  
**Tempo estimado:** 2-3 semanas

**Componentes:**

#### 1. Deck Definition
```json
{
  "deckId": "vidaria_starter",
  "sourceId": "POWER_VIDARIA",
  "displayName": "Vidaria - Blood Magic",
  "description": "Sacrifice health for devastating power",
  
  "globalModifiers": {
    "all_actions_gain_tag": ["Organic"],
    "health_cost_multiplier": 0.8
  },
  
  "bucketPipeline": [
    "base",
    "lifesteal",
    "blood_sacrifice",
    "increased",
    "more",
    "crit",
    "final"
  ],
  
  "resourceAffinity": ["health", "blood_tokens"],
  
  "starterCards": [
    "vidaria_blood_strike",
    "vidaria_crimson_shield",
    "vidaria_life_tap"
  ]
}
```

#### 2. Deck Manager
```csharp
public interface IDeckManager
{
    Result<DeckDefinition> LoadDeck(string deckId);
    Result<PipelineConfiguration> GetPipelineForDeck(string deckId);
    Result<List<ActionDefinition>> GetStarterCards(string deckId);
    Result<Dictionary<string, object>> GetGlobalModifiers(string deckId);
}
```

#### 3. Deck API
**Endpoints:**
- `GET /api/decks` - Lista baralhos disponíveis
- `GET /api/decks/{deckId}` - Obtém definição de baralho
- `GET /api/decks/{deckId}/pipeline` - Obtém pipeline customizado
- `GET /api/decks/{deckId}/starter-cards` - Obtém cartas iniciais

**Integração com CombatSystem:**
```csharp
// Ao iniciar combate, carregar pipeline do baralho
var deckPipeline = _deckManager.GetPipelineForDeck(heroRace);
_damageCalculator.SetPipeline(deckPipeline);
```

**Testes necessários:**
- Carregamento de baralhos de JSON
- Injeção de buckets customizados
- Aplicação de modificadores globais
- Interação entre tags de baralho
- Baralhos híbridos (sandbox mode)

---

## Estratégias de Implementação

### Opção A: MVP Pragmático (2-3 semanas)
**Foco:** Gameplay básico funcional, adiar Timeline e Baralhos Modulares

**Implementar:**
1. Effect Resolver (processar todos os tipos de efeito)
2. Status Effect System (aplicar, processar, remover)
3. Turn Management (fases, end of turn)
4. Enemy AI básico (sempre ataca)
5. 1 baralho hardcoded (sem modularidade)

**Resultado:** Jogo jogável, mas sem diferencial competitivo

**Prós:** Rápido, validação de mercado cedo  
**Contras:** Perde diferencial competitivo inicial

---

### Opção B: Visão Completa (2-3 meses)
**Foco:** Implementar Timeline e Baralhos Modulares antes do lançamento

**Implementar:**
1. Snapshot System + Command History
2. Timeline API (undo/redo/simulate)
3. Deck System com herança de JSON
4. Injeção dinâmica de buckets
5. 2-3 baralhos modulares (Vidaria, Crolbia)
6. Gameplay básico (Status Effects, Turn Management, AI)

**Resultado:** Produto único, difícil de copiar

**Prós:** Produto único, difícil de copiar  
**Contras:** Risco de over-engineering, demora para validar

---

### Opção C: Híbrido (4-6 semanas) ⭐ **RECOMENDADO**
**Foco:** Implementar versão simplificada de Timeline + 1 baralho modular

**Implementar:**
1. Snapshot System básico (apenas undo, sem redo/simulate)
2. 1 baralho modular como prova de conceito (Vidaria)
3. Gameplay básico (Status Effects, Turn Management, AI)
4. Effect Resolver completo

**Resultado:** MVP com "undo" como diferencial, validação do conceito core

**Prós:** Balanceado, valida conceito core  
**Contras:** Complexidade média, risco de scope creep

**Roadmap detalhado:**

**Semana 1-2: Effect Resolver + Status Effects**
- Implementar `IEffectHandler` interface
- Handlers: Damage, Heal, Status, Resource, Conditional
- Status Effect Manager (aplicar, tick, remover)
- Turn Management (fases, end of turn)
- 50+ testes

**Semana 3: Timeline Básico**
- Snapshot System (apenas undo)
- Command History
- Timeline API (apenas `/undo` endpoint)
- 30+ testes

**Semana 4: Baralho Modular (Vidaria)**
- Deck Definition JSON
- Deck Manager
- Injeção de buckets customizados
- 3 cartas iniciais de Vidaria
- 20+ testes

**Semana 5-6: Enemy AI + Polish**
- Enemy AI básico
- Integração completa
- Testes end-to-end
- Documentação

---

## Gaps Críticos Identificados

### Bloqueiam Gameplay Básico

1. **❌ Status Effect System**
   - Não existe `StatusEffect` class
   - Não existe `StatusEffectManager`
   - `EffectDefinition` tem campos mas não há processamento
   - **Impacto:** Ações como "Fireball" não podem aplicar "burning"

2. **❌ Effect Resolver**
   - `EffectDefinition` existe mas não há sistema para executar efeitos
   - Apenas `DAMAGE` é processado (via `DamageCalculator`)
   - Outros tipos (`APPLY_STATUS`, `HEAL`, `MODIFY_RESOURCE`) não funcionam
   - **Impacto:** 80% dos efeitos não funcionam

3. **❌ Turn Management**
   - Não existe sistema de fases (`HERO_TURN`, `ENEMY_TURN`, `END_OF_TURN`)
   - Não existe `EndTurn()` endpoint
   - Não existe processamento de "end of turn" para status effects
   - **Impacto:** Jogo não tem estrutura de turnos

4. **❌ Enemy AI**
   - Não existe `EnemyAIService`
   - Não existe endpoint para turno dos inimigos
   - **Impacto:** Inimigos não podem agir

### Limitam Experiência

5. **⚠️ Action Result Feedback**
   - `ExecuteAction()` retorna apenas `CombatState` atualizado
   - Não retorna breakdown detalhado de efeitos aplicados
   - Frontend não sabe o que aconteceu (dano crítico? status aplicado?)
   - **Impacto:** UI não pode mostrar animações/feedback adequado

6. **⚠️ Available Actions Calculation**
   - `GetAvailableActions()` existe mas não considera:
     - Cooldowns
     - Condições (ex: "só pode usar se HP < 50%")
     - Múltiplas opções de custo (`AlternativeCosts`)
   - **Impacto:** UI pode mostrar ações que não podem ser executadas

7. **⚠️ Resource Regeneration**
   - Não existe sistema de regeneração passiva
   - Energy não regenera automaticamente
   - **Impacto:** Herói pode ficar sem energy permanentemente

---

## Estatísticas Atuais

### Fase 0 + Fase 1 (Implementadas)
- **Total de testes:** 423 testes (todos passando)
  - Combat: 53 testes
  - Events: 18 testes  
  - Damage: 126 testes
  - Math: 97 testes
  - Resources: 23 testes
- **APIs implementadas:** 3/3 (Events, Combat, Damage)
- **Sistemas Core:** EventBus, CombatSystem, DamagePipeline, MathEngine, ResourceManager, ConfigManager
- **Documentação:** 9 documentos técnicos completos

### Estimativa para MVP Jogável (Slay the Spire-like)
- **Fases necessárias:** Fase 2 (completa), Fase 3 (parcial), Fase 4 (parcial)
- **Tempo estimado:** 6-8 semanas
- **Sistemas críticos faltando:** 
  - **Fase 2:** Status Effects, Turn Management, Hand & Deck System, Enemy AI, Effect-Action Integration, Action Result Feedback
  - **Fase 3:** ScriptModifier System (opcional), Gambit System (opcional)
  - **Fase 4:** Run Management, Card Selection, Shop System, Relic System

### Estimativa para Engine Versátil (Suporte a TCGs)
- **Fases necessárias:** Fase 2-8
- **Tempo estimado:** 12-16 semanas
- **Sistemas adicionais:** Permanent System, Reaction System, Combat Phases, Graveyard & Zones, Mulligan System

---

## Próximos Passos Recomendados

### Decisão Estratégica Necessária

Antes de prosseguir, é necessário decidir qual estratégia seguir:

#### **Opção A: Slay the Spire MVP** ⭐ **RECOMENDADO**
**Tempo:** 6-8 semanas  
**Objetivo:** Card game roguelike jogável e completo

**Inclui:**
- Fase 2 completa (Status Effects, Turn Management, Hand & Deck System, Enemy AI)
- Fase 4 parcial (Run Management, Card Selection, Shop, Relics)
- 1 baralho jogável (Vidaria)
- 15-20 cartas
- 10-15 inimigos
- Sistema de progressão de run

**Diferencial:** Engine versátil que suporta Slay the Spire, Balatro, e jogos de combate direto.

---

#### **Opção B: Engine Versátil Completa**
**Tempo:** 12-16 semanas  
**Objetivo:** Suportar múltiplos gêneros (Slay the Spire + TCGs)

**Inclui:**
- Tudo da Opção A
- Fase 7 (Timeline System com undo/redo)
- Fase 8 (Suporte a TCGs: Permanentes, Reaction System, Graveyard)
- 3+ baralhos modulares
- Sistema de meta-progressão

**Diferencial:** Engine que suporta desde Slay the Spire até MTG/Hearthstone.

---

#### **Opção C: MVP Minimalista**
**Tempo:** 3-4 semanas  
**Objetivo:** Gameplay básico funcional (sem deck system)

**Inclui:**
- Status Effects
- Turn Management
- Enemy AI
- Ações hardcoded (sem sistema de cartas)

**Limitação:** Não é verdadeiramente um "card game" - apenas combate com ações fixas.

---

### Recomendação: Opção A (Slay the Spire MVP)

**Justificativa:**
1. **Versatilidade:** Sistema de mão/deck desbloqueia TODOS os card games tradicionais
2. **Validação:** MVP jogável completo em 6-8 semanas
3. **Diferencial:** Engine reutilizável para múltiplos projetos
4. **Risco controlado:** Escopo bem definido, sem over-engineering
5. **Expansibilidade:** Fundação sólida para adicionar Timeline (Fase 7) e TCGs (Fase 8) depois

**Sem o sistema de mão/deck (Opção C), HeroScript não é uma "engine para card games" - é apenas um sistema de combate com ações fixas.**

---

### Implementação Imediata (Fase 2 - Prioridade CRÍTICA)

**Atualização 2026-05-16:** executar primeiro a estabilização abaixo; só depois iniciar sistemas novos.

**Estabilização imediata:**
1. Fechar ciclo de vida de Status Effects: aplicação real de DoT/HoT, expiração, stacks e fórmulas via MathEngine/ExpressionEvaluator
2. Completar `EffectResolver`: dano via DamageCalculator, cura/recurso via ResourceManager, status via StatusEffectManager
3. Integrar `ActionManager` com `CombatSystem`: `costOptionId`, custos preparados e remoção de hardcodes de custo/dano
4. Quebrar gradualmente responsabilidades do `CombatSystem` sem alterar comportamento público
5. Atualizar/recuperar API.Tests legados de Config/Resource/Action e destravar testes de StatusEffect

**Semana 1-2:**
1. Status Effect System (3-4 dias)
2. Turn Management System (2-3 dias)
3. Hand & Deck System (4-5 dias)

**Semana 3:**
4. Enemy AI System (2-3 dias)
5. Effect-Action Integration (2 dias)
6. Action Result Feedback (1-2 dias)

**Entregável:** Gameplay funcional com cartas, turnos, status effects e inimigos que reagem.

---

## Changelog

| Data | Mudança |
|------|---------|
| 2026-05-16 | **Auditoria de estabilização** - Fase 2 classificada como parcial; StatusEffects existem mas precisam fechamento semântico; EffectResolver e CombatSystem/ActionManager são os próximos high priority |
| 2026-05-16 | **Correções aplicadas** - Core.Tests estabilizados, Resource reload corrigido, rotas StatusEffect alinhadas, DamageCalculator integrado ao StatusEffectManager, guard de ambiente em EventsController |
| 2026-05-09 | **Roadmap Estratégico criado** - Integração de session.md com roadmap técnico |
| 2026-05-09 | Análise de gaps críticos (Timeline, Baralhos Modulares, Effect Resolver) |
| 2026-05-09 | Definição de 3 estratégias de implementação (Pragmático, Completo, Híbrido) |
| 2026-05-09 | Fase 1 completa (126 testes de dano, EventBus, CombatSystem) |
| 2026-05-09 | **Análise de versatilidade para múltiplos gêneros** - Identificação de Gap 1 (Hand & Deck System) como CRÍTICO |
| 2026-05-09 | **Expansão do escopo da Fase 2** - Adicionado Hand & Deck System (4-5 dias) |
| 2026-05-09 | **Matriz de compatibilidade criada** - 16 mecânicas vs 4 gêneros (Slay the Spire, MTG, Inscryption, Balatro) |
| 2026-05-09 | **Fase 7 (Timeline) e Fase 8 (TCGs) adicionadas** - Suporte opcional para undo/redo e permanentes |
| 2026-05-09 | **Recomendação atualizada** - Opção A (Slay the Spire MVP) como caminho principal (6-8 semanas) |

---

## Documentação Relacionada

### Roadmap Técnico
- [roadmap/README.md](roadmap/README.md) - Visão geral do roadmap
- [roadmap/PHASE_0.md](roadmap/PHASE_0.md) - Fundação (Config, Math, Resources)
- [roadmap/PHASE_1.md](roadmap/PHASE_1.md) - EventBus e Combate Básico
- [roadmap/PHASE_2.md](roadmap/PHASE_2.md) - Camadas de Combate
- [roadmap/PHASE_3.md](roadmap/PHASE_3.md) - Loop de Run
- [roadmap/CORE_MODULES_ANALYSIS.md](roadmap/CORE_MODULES_ANALYSIS.md) - Análise de módulos

### Sistemas Implementados
- [EVENTBUS_SYSTEM.md](EVENTBUS_SYSTEM.md) - Sistema de eventos
- [DAMAGE_PIPELINE.md](DAMAGE_PIPELINE.md) - Pipeline de dano
- [COMBAT_SYSTEM.md](COMBAT_SYSTEM.md) - Sistema de combate
- [CONFIG_SYSTEM.md](CONFIG_SYSTEM.md) - Sistema de configuração

### Visão Arquitetural
- [session.md](session.md) - Visão original (Timeline + Baralhos Modulares)

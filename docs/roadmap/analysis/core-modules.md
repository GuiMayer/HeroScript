# Análise de Módulos Core - HeroScript Engine

**Data:** 2026-05-23
**Status:** Sincronizado com Fase 2 estabilizada
**Objetivo:** Avaliar quais submódulos do HeroScript.Core estão implementados e quais faltam para criar um jogo completo

---

## Resumo Executivo

### Estado Atual
- **Core.Tests:** 553 testes passando após estabilização da Fase 2
- **API.Tests:** projeto compila, mas o runner local ainda pode congelar/atingir timeout; não usar a suite completa como gate único até investigação dedicada
- **Fases implementadas:** Fase 0 e Fase 1 completas; Fase 2 estabilizada
- **Próxima fase:** iniciar Fase 3 pelo núcleo `RunState`/`DeckState`, depois Run API, Hand/Deck, CardSelection, Shop e Preparation
- **Data-driven Compliance:** diagnóstico dedicado em [data-driven-compliance.md](data-driven-compliance.md); score atual 9.0/10

### Capacidade Atual
Com os módulos implementados, é possível criar:
- ✅ Sistema de combate básico (herói vs inimigos)
- ✅ Sistema de dano configurável via JSON
- ✅ Sistema de recursos (HP, energia, mana, etc.)
- ✅ Sistema de ações com custos alternativos
- ✅ Sistema de eventos (pub/sub)
- ✅ Configuração data-driven com herança
- ✅ Ações e execução básica de combate migradas para `ActionDefinition`/JSON na primeira rodada de compliance
- ✅ Status Effects carregados de JSON com schema legado/canonical
- ✅ Script Modifiers data-driven com pipeline/tags/tick
- ✅ Gambit Engine data-driven com regras de decisão JSON
- ✅ Effect Engine consolidado com efeitos de combate, economia, deck, modifiers e controle

### O Que Falta Para Um Jogo Completo
- ❌ Loop de run (progressão, mapa, recompensas)
- ❌ Estado real de deck/mão/discard/exhaust para aplicar efeitos de cartas
- ❌ Conteúdo jogável (raças, poderes, inimigos)
- ❌ Sistema de loja e economia
- ❌ Persistência (save/load)

---

## Módulos Core Implementados

### 1. Config System ✅ (Fase 0)
**Localização:** `src/Core/Config/`

**Implementado:**
- ConfigManager - Gerenciamento de configurações
- ConfigLoader - Carregamento de arquivos JSON
- Herança delta (configs derivam de outros)
- Validação de configurações
- Cache de configs

**Testes:** incluídos nos 553 testes Core atuais

**Uso em um jogo:**
- Definir diferentes dificuldades (easy, normal, hard)
- Criar variações de inimigos (goblin_base → goblin_elite)
- Configurar diferentes modos de jogo

---

### 2. Math System ✅ (Fase 0)
**Localização:** `src/Core/Math/`

**Implementado:**
- MathEngine - Avaliação de expressões matemáticas
- Suporte a variáveis e parâmetros
- Funções matemáticas (min, max, abs, etc.)
- Validação de expressões
- Logging de avaliações (Fase 6a)

**Testes:** 97 testes

**Uso em um jogo:**
- Fórmulas de dano dinâmicas
- Cálculos de scaling (dano cresce com nível)
- Condições complexas (if/then)
- Balanceamento via JSON

---

### 3. Resources System ✅ (Fase 0)
**Localização:** `src/Core/Resources/`

**Implementado:**
- ResourceManager - Gerenciamento de recursos
- ResourcePool - Pools individuais (HP, energia, etc.)
- ResourceDefinition - Definições configuráveis
- Validação de custos
- Operações (spend, restore, set)

**Testes:** 23 testes

**Uso em um jogo:**
- Sistema de HP/energia/mana
- Recursos customizados (rage, shield, stamina)
- Validação de affordability
- Regeneração de recursos

---

### 4. Events System ✅ (Fase 1)
**Localização:** `src/Core/Events/`

**Implementado:**
- EventBus - Pub/sub system
- Event sourcing (histórico de eventos)
- Eventos tipados
- Subscribers com prioridade
- Replay de eventos

**Testes:** 18 testes

**Uso em um jogo:**
- Reação a eventos de combate
- Triggers de habilidades
- Sistema de achievements
- Debug e replay de partidas

---

### 5. Combat System ✅ (Fase 1)
**Localização:** `src/Core/Combat/`

**Implementado:**
- CombatSystem - Gerenciamento de combate
- CombatEntity - Entidades de combate
- ActionDefinition - Definições de ações
- ActionManager - Gerenciamento de ações
- Validação de custos (recursos + alternativos)
- Execução de ações

**Gap data-driven restante:**
- Ataque básico e poderes usam `ActionDefinition`; início de combate prefere `EntityDefinition` JSON.
- `CombatSystem` ainda aplica parte dos effects/status diretamente; o próximo passo é extrair isso para um executor central de effects.
- Detalhes e checklist: [data-driven-compliance.md](data-driven-compliance.md).

**Testes:** 53 testes

**Uso em um jogo:**
- Combate turno-a-turno
- Herói vs múltiplos inimigos
- Ações com custos variados
- Validação de affordability

---

### 6. Damage System ✅ (Fase 1)
**Localização:** `src/Core/Damage/`

**Implementado:**
- DamageCalculator - Cálculo de dano
- PipelineManager - Gerenciamento de pipeline
- 7 operações de dano (AddFlat, MultiplyMore, etc.)
- 5 filtros (ByTag, BySource, etc.)
- 3 eventos de pipeline
- Sistema de crítico configurável
- Mitigação (armadura)

**Testes:** 126 testes

**Uso em um jogo:**
- Sistema de dano flexível (PoE-style, Genshin-style, etc.)
- Críticos com tiers
- Buffs/debuffs via pipeline
- Mitigação de dano

---

### 7. Validation System ✅
**Localização:** `src/Core/Validation/`

**Implementado:**
- Validadores genéricos
- Result<T> pattern
- Validação de entrada

**Uso em um jogo:**
- Validação de input do jogador
- Tratamento de erros consistente

---

### 8. Logging System ✅
**Localização:** `src/Core/Logging/`

**Implementado:**
- ILogger interface
- LoggerFactory
- Níveis de log (Debug, Info, Warning, Error)
- Thread-safety (Fase 2)

**Uso em um jogo:**
- Debug de problemas
- Telemetria
- Auditoria de eventos

---

### 9. Caching System ✅
**Localização:** `src/Core/Caching/`

**Implementado:**
- Sistema de cache genérico
- Cache de configurações
- Cache de recursos

**Uso em um jogo:**
- Performance (evitar recarregar JSONs)
- Cache de cálculos pesados

---

### 10. Dependency Injection ✅
**Localização:** `src/Core/DependencyInjection/`

**Implementado:**
- ServiceContainer
- Registro de serviços
- Resolução de dependências

**Uso em um jogo:**
- Arquitetura limpa
- Testabilidade
- Modularidade

---

## Módulos Core Faltando (Críticos para um Jogo)

### 1. Status Effects System ✅ Estabilizado (Fase 2)
**Prioridade:** ALTA  
**Localização:** `src/Core/StatusEffects/`, `src/API/Controllers/StatusEffectController.cs`, `UserData/Configs/default/StatusEffects/status_effects.json`

**Implementado:**
- `StatusEffectManager`, `StatusEffectProcessor`, `StatusEffectDefinition`, `StatusEffectInstance`
- API REST principal em `/api/StatusEffect/*`
- Rotas compatíveis por entidade em `/api/combat/{combatId}/entities/{targetId}/status`
- Integração com `CombatSystem` por comportamento para controle, DoT/HoT, shield/reactive/damage cap/death prevention
- `DamageCalculator` recebe `IStatusEffectManager` via DI e pode aplicar modificadores de pipeline

**Lacunas restantes:**
- Fórmulas e modificadores ainda usam parsing manual em alguns pontos; integrar com `MathEngine`/`ExpressionEvaluator` canônico.
- Testes API de StatusEffect dependem da estabilização do runner de integração.

**Impacto:**
Sem status effects, não há:
- Buffs/debuffs
- Veneno, queimadura, sangramento
- Stun, silence, root
- Regeneração

**Estimativa restante:** 1-2 dias de limpeza técnica, não bloqueante para iniciar Fase 3.

---

### 1.1. Effect System ✅ Estabilizado para Fase 2
**Prioridade:** ALTA  
**Localização:** `src/Core/Effects/`

**Implementado:**
- `EffectResolver`, `EffectDefinition`, `EffectInstance`, `EffectResult`, `EffectType`
- Dano, cura, recursos, status, economia, deck, modifiers e controle como primitivas data-driven

**O que falta:**
- Fórmulas, condições e filtros devem convergir para `MathEngine`/`ExpressionEvaluator`.
- Efeitos de cartas/economia (`DRAW_CARD`, `DISCARD_CARD`, `EXHAUST_CARD`, `GAIN_GOLD`, etc.) aguardam `RunState`/`DeckState` para alterar estado real em vez de retornar apenas metadata.

**Impacto:**
Sem isso, ações continuam parcialmente hardcoded e o jogo não fica plenamente data-driven.

**Estimativa restante:** depende da primeira fatia da Fase 3 (`RunState`/`DeckState`).

---

### 2. Script Modifiers System ✅ Estabilizado (Fase 2)
**Prioridade:** ALTA  
**Localização:** `src/Core/Combat/Modifiers/`, `src/API/Controllers/ModifierController.cs`, `UserData/Configs/default/Modifiers/script_modifiers.json`

**Implementado:**
- `ScriptModifierManager`, definitions e instances.
- Loader data-driven via JSON.
- Aplicação por owner, stacking, duração/tick e remoção.
- Modificadores de pipeline filtrados por tags.
- API `/api/modifiers` com definitions, reload, apply, active, pipeline e tick.

**Lacunas restantes:**
- Avaliação de fórmulas ainda usa avaliador simples local.
- Validação avançada de compatibilidade pode evoluir na Preparation API.

**Impacto:**
Sem modificadores, não há:
- Customização de poderes
- Builds variadas
- Profundidade estratégica

**Estimativa restante:** 1-2 dias quando Preparation exigir compatibilidade mais rígida.

---

### 3. Gambit System ✅ Estabilizado (Fase 2)
**Prioridade:** MÉDIA  
**Localização:** `src/Core/Combat/Gambits/`, `src/API/Controllers/GambitController.cs`, `UserData/Configs/default/Gambits/gambits.json`

**Implementado:**
- `GambitEngine` com carregamento JSON, prioridade, condições e decisão de ação.
- API `/api/gambits` com definitions, reload e decide.
- Fallback para `PASS` quando nenhuma regra casa.

**Lacunas restantes:**
- Execução automática completa do turno de IA ainda não existe no Combat API.
- Integração com eventos pode crescer conforme companions e Preparation forem implementados.

**Impacto:**
Sem gambits, não há:
- Companions inteligentes
- Automação de ações
- Reação a eventos

**Estimativa restante:** 1-2 dias para o endpoint de processamento automático de IA.

---

### 4. Run Management System ❌ (Fase 3)
**Prioridade:** CRÍTICA  
**Localização planejada:** `src/Core/Run/`

**O que falta:**
- RunManager - Gerenciamento de runs
- RunState - Estado da run
- NodeResolver - Resolução de nós
- MapGenerator - Geração de mapas
- Progressão entre combates

**Impacto:**
Sem run management, não há:
- Loop de jogo (combate → recompensa → próximo combate)
- Progressão
- Mapa de nós
- Estrutura de roguelike

**Estimativa:** 4-5 dias de implementação

---

### 5. Card Selection System ❌ (Fase 3)
**Prioridade:** ALTA  
**Localização planejada:** `src/Core/Run/CardSelection/`

**O que falta:**
- CardOffer - Ofertas de cartas
- CardPool - Pool de poderes disponíveis
- OfferGenerator - Geração de ofertas
- DeckManager - Gerenciamento de deck
- Reroll logic

**Impacto:**
Sem card selection, não há:
- Aprender novos poderes
- Decompilar cartas por PP
- Reroll de ofertas
- Progressão de deck

**Estimativa:** 2-3 dias de implementação

---

### 6. Shop System ❌ (Fase 3)
**Prioridade:** MÉDIA  
**Localização planejada:** `src/Core/Run/Shop/`

**O que falta:**
- ShopInventory - Inventário da loja
- ShopItem - Itens vendáveis
- PricingEngine - Cálculo de preços
- RerollCostCalculator - Custo de reroll
- Purchase validation

**Impacto:**
Sem shop, não há:
- Compra de poderes/companions
- Economia de ouro
- Escolhas estratégicas de compra

**Estimativa:** 2-3 dias de implementação

---

### 7. Preparation System ❌ (Fase 3)
**Prioridade:** MÉDIA  
**Localização planejada:** `src/Core/Run/Preparation/`

**O que falta:**
- PreparationState - Estado de preparação
- ModifierInjector - Injeção de modificadores
- GambitConfigurator - Configuração de gambits
- Preview de mudanças

**Impacto:**
Sem preparation, não há:
- Injeção de modificadores em poderes
- Configuração de gambits
- Gasto de PP

**Estimativa:** 2 dias de implementação

---

### 8. Content System ❌ (Fase 4)
**Prioridade:** CRÍTICA  
**Localização planejada:** `src/Core/Content/`

**O que falta:**
- ContentLoader - Carregamento de conteúdo
- RaceDefinition - Definições de raças
- PowerDefinition - Definições de poderes
- CompanionDefinition - Definições de companions
- EnemyDefinition - Definições de inimigos
- ComboValidator - Validação de combos

**Impacto:**
Sem content system, não há:
- Raças jogáveis
- Poderes concretos
- Companions
- Inimigos
- Conteúdo jogável

**Estimativa:** 5-7 dias de implementação

---

### 9. Persistence System ❌ (Fase 5)
**Prioridade:** BAIXA (MVP pode funcionar sem)  
**Localização planejada:** `src/Core/Persistence/`

**O que falta:**
- SaveManager - Gerenciamento de saves
- SaveSerializer - Serialização de estado
- MetaProgressionSystem - Estatísticas globais
- FeatSystem - Sistema de feats/achievements

**Impacto:**
Sem persistence, não há:
- Save/load de runs
- Estatísticas globais
- Meta-progressão
- Desbloqueios

**Estimativa:** 3-4 dias de implementação

---

### 10. Seed & Mode System ❌ (Fase 6)
**Prioridade:** BAIXA (feature adicional)  
**Localização planejada:** `src/Core/Modes/`

**O que falta:**
- SeedGenerator - Geração de seeds
- SeedValidator - Validação de seeds
- ModeManager - Gerenciamento de modos
- DailyChallengeSystem - Daily challenges

**Impacto:**
Sem seed/mode, não há:
- Runs com seed fixa
- Daily challenges
- Custom modes
- Competição entre jogadores

**Estimativa:** 2-3 dias de implementação

---

## Roadmap Atualizado

### Fase 0 ✅ (Completa)
- Config System
- Math System
- Resources System

### Fase 1 ✅ (Completa)
- Events System
- Combat System
- Damage System

### Fase 2 ✅ (Estabilizada)
- Status Effects System
- Effect System / EffectResolver
- Script Modifiers System
- Gambit System

### Fase 3 📋 (8-13 dias)
- Run Management System
- Card Selection System
- Shop System
- Preparation System

### Fase 4 📋 (5-7 dias)
- Content System (Races, Powers, Companions, Enemies)

### Fase 5 📋 (3-4 dias - Opcional para MVP)
- Persistence System
- Meta-Progression System

### Fase 6 📋 (2-3 dias - Feature adicional)
- Seed & Mode System

---

## Estimativa Total para MVP Jogável

### Mínimo Viável (sem persistence)
**Fases 3 + 4:** 13-20 dias de desenvolvimento

### MVP Completo (com persistence)
**Fases 3 + 4 + 5:** 16-24 dias de desenvolvimento

### MVP + Features Extras
**Fases 3 + 4 + 5 + 6:** 18-27 dias de desenvolvimento

---

## Priorização Recomendada

### Crítico (Sem isso não há jogo)
1. **Run Management System** - Loop de jogo
2. **Deck/Hand State** - draw/discard/exhaust/shuffle e mão real
3. **Content System** - Conteúdo jogável
4. **Card Selection System** - Progressão de deck

### Importante (Jogo funciona mas limitado)
5. **Shop System** - Economia
6. **Preparation System** - Customização pré-combate
7. **Automatic AI Turn** - uso real do Gambit Engine no loop de combate

### Desejável (Adiciona profundidade)
8. **Formula evaluator unificado** - reduzir duplicação técnica
9. **SSE/WebSocket ou polling formal** - integração frontend em tempo real

### Opcional (Pode vir depois do MVP)
10. **Persistence System** - Save/load
11. **Seed & Mode System** - Replayability

---

## Conclusão

### Estado Atual
O HeroScript.Core tem uma **fundação sólida** (Fases 0, 1 e 2) com:
- 553 testes Core passando
- Sistemas core bem arquitetados
- Padrões consistentes (Result<T>, EventBus, data-driven)
- Status Effects, EffectResolver, Script Modifiers e Gambit Engine estabilizados

### O Que Falta
Para criar um jogo jogável, faltam **4 frentes críticas**:
1. Run Management (loop de jogo)
2. Deck/Hand State (mão, deck, descarte, exhaust e shuffle)
3. Content System (raças, poderes, inimigos)
4. Card Selection/Shop (progressão e economia)

### Próximos Passos
**Recomendação:** iniciar a Fase 3 por `RunState` e `DeckState`, pois os efeitos de deck/economia já existem no `EffectResolver`, mas ainda não têm estado real para persistir mão, descarte, exhaust, ouro, PP e recompensas.

**Ordem sugerida:**
1. Fase 3 (8-13 dias) - Implementar loop de run, deck/hand e APIs de estado
2. Fase 4 (5-7 dias) - Adicionar conteúdo jogável mínimo
3. Fase 5 (opcional) - Adicionar persistence
4. Fase 6 (opcional) - Seeds, modos e desafios

**Tempo total estimado para MVP sem persistence:** 13-20 dias de desenvolvimento focado.

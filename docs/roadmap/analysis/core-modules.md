# Análise de Módulos Core - HeroScript Engine

**Data:** 2026-05-23
**Status:** Sincronizado com Fase 3 em implementação
**Objetivo:** Avaliar quais submódulos do HeroScript.Core estão implementados e quais faltam para criar um jogo completo

---

## Resumo Executivo

### Estado Atual
- **Core.Tests:** 553 testes passando após estabilização da Fase 2
- **API.Tests:** projeto compila, mas o runner local ainda pode congelar/atingir timeout; não usar a suite completa como gate único até investigação dedicada
- **Fases implementadas:** Fase 0 e Fase 1 completas; Fase 2 estabilizada; primeiras fatias da Fase 3 implementadas
- **Foco atual:** continuar Fase 3 com refinamentos de ativacao e conteudo MVP ampliado
- **Data-driven Compliance:** diagnóstico dedicado em [data-driven-compliance.md](data-driven-compliance.md); score atual 9.7/10

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
- ✅ Run/Deck state com mão, draw pile, discard, exhaust, ouro, PP e recompensas
- ✅ CardSelection, Shop e Preparation com contratos JSON e rollback em operações compostas
- ✅ Integração combate↔run: ações validam carta real na mão e consomem para discard/exhaust/retain
- ✅ Ativação por entidade, turno de IA backend-authoritative, polling incremental e base SSE

### O Que Falta Para Um Jogo Completo
- ⚠️ Loop de run ampliado (mapa/progressão/end run ainda precisam refinamento)
- ⚠️ Regras avançadas de ativação, intents e status por início/fim de ativação
- ❌ Conteúdo jogável ampliado (raças, poderes, inimigos, companions)
- ⚠️ Conteúdo MVP inicial para cartas, pools, lojas, preparações e modificadores
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
- Testes API de StatusEffect dependem da estabilização do runner de integração.
- Regras mais ricas de status por início/fim de ativação pertencem aos refinamentos atuais da Fase 3.

**Impacto:**
Sem status effects, não há:
- Buffs/debuffs
- Veneno, queimadura, sangramento
- Stun, silence, root
- Regeneração

**Estimativa restante:** limpeza técnica pontual, não bloqueante para continuar Fase 3.

---

### 1.1. Effect System ✅ Estabilizado para Fase 2
**Prioridade:** ALTA  
**Localização:** `src/Core/Effects/`

**Implementado:**
- `EffectResolver`, `EffectDefinition`, `EffectInstance`, `EffectResult`, `EffectType`
- Dano, cura, recursos, status, economia, deck, modifiers e controle como primitivas data-driven

**O que falta:**
- Expandir regras de ativação/intents que consomem esses efeitos no loop de combate.
- Refinar transações futuras quando persistência/versionamento entrarem na Fase 5.

**Impacto:**
Sem isso, ações continuam parcialmente hardcoded e o jogo não fica plenamente data-driven.

**Estimativa restante:** depende dos refinamentos atuais da Fase 3 e da futura persistência.

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
- Validação avançada de compatibilidade pode evoluir na Preparation API.
- Conteúdo MVP deve adicionar mais modificadores em JSON usando os contratos atuais.

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
- Regras de decisão e intents podem ficar mais ricas conforme companions e conteúdo MVP forem implementados.
- Integração com eventos pode crescer conforme companions e Preparation evoluírem.

**Impacto:**
Sem gambits, não há:
- Companions inteligentes
- Automação de ações
- Reação a eventos

**Estimativa restante:** 1-2 dias para refinamentos de intents/ativação, não para o endpoint base.

---

### 4. Run Management System 🚧 (Fase 3)
**Prioridade:** CRÍTICA  
**Localização:** `src/Core/Run/`

**Implementado na primeira fatia:**
- `RunManager` e `RunState` como fonte de verdade da run.
- `DeckState` com mão, draw pile, discard e exhaust.
- Ouro, PP, recompensas e nó atual no estado da run.
- Endpoints de start/state/deck/hand/draw/discard/shuffle.
- Operações compostas com snapshot/rollback para recompensas, loja, card selection, decompose, reroll e preparation.

**O que falta:**
- `advance`, `map`, `current-node` e `end` como fluxo completo de progressão.
- Mapas/nós mais ricos para conectar combate, loja, descanso, eventos e boss.
- Persistência/versionamento futuro para transações fora de memória.

**Impacto:**
Sem run management, não há:
- Loop de jogo (combate → recompensa → próximo combate)
- Progressão
- Mapa de nós
- Estrutura de roguelike

**Estimativa restante:** 2-4 dias para fluxo de mapa/progressão sem persistência.

---

### 5. Card Selection System ✅ Primeira fatia (Fase 3)
**Prioridade:** ALTA  
**Localização:** `src/Core/Run/CardSelection/`

**Implementado:**
- Catálogo de cartas e pools por raridade/tags carregados por `ResourceLoader`.
- Geração de ofertas a partir de `card-selections/{selectionId}.json`.
- Pick adicionando carta ao deck real da run.
- Reroll com custo/free rerolls e rollback em falha.
- Decompose de oferta em PP conforme catálogo.

**O que falta:**
- Mais pools e cartas de conteúdo MVP.
- Regras de combo/desbloqueio mais ricas conforme Fase 4.

**Impacto:**
Sem card selection, não há:
- Aprender novos poderes
- Decompilar cartas por PP
- Reroll de ofertas
- Progressão de deck

**Estimativa restante:** conteúdo e refinamentos, não infraestrutura base.

---

### 6. Shop System ✅ Primeira fatia (Fase 3)
**Prioridade:** MÉDIA  
**Localização:** `src/Core/Run/Shop/`

**Implementado:**
- Loja aberta a partir de `shops/{shopId}.json`.
- Itens e preços baseados em catálogo/pools JSON.
- Compra validada no backend com gasto de ouro e alteração do estado da run.
- Reroll com custo progressivo e rollback em falha.

**O que falta:**
- Venda de itens.
- Mais tipos de item e conteúdo MVP.
- Descontos/regras especiais conforme raças/progresso forem adicionados.

**Impacto:**
Sem shop, não há:
- Compra de poderes/companions
- Economia de ouro
- Escolhas estratégicas de compra

**Estimativa restante:** 1-3 dias conforme escopo de conteúdo/itens.

---

### 7. Preparation System ✅ Primeira fatia (Fase 3)
**Prioridade:** MÉDIA  
**Localização:** `src/Core/Run/Preparation/`

**Implementado:**
- Preparação criada por `preparations/{preparationId}.json`.
- Aplicação de opções validadas no backend.
- Grants de modifiers para `run:{runId}` via `ScriptModifierManager`.
- Modifiers de run aplicados em cartas pelo `CombatRunCoordinator` conforme tags.
- Compensação de modifiers externos se uma etapa posterior falhar.

**O que falta:**
- Configuração de gambits de companions.
- Preview/remoção de modificadores.
- Validação avançada de compatibilidade.

**Impacto:**
Sem preparation, não há:
- Injeção de modificadores em poderes
- Configuração de gambits
- Gasto de PP

**Estimativa restante:** 1-3 dias conforme preview/gambits/compatibilidade.

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

### Fase 3 🚧 (em implementação)
- Run/Deck Core e APIs base implementados
- CardSelection, Shop e Preparation implementados em primeira fatia
- Integração combate↔run, consumo real de cartas e modifiers de run implementados
- Próximo foco: refinamentos de ativação, intents e conteúdo MVP ampliado

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
**Fase 3 restante + Fase 4:** 8-15 dias de desenvolvimento

### MVP Completo (com persistence)
**Fase 3 restante + Fase 4 + Fase 5:** 11-19 dias de desenvolvimento

### MVP + Features Extras
**Fase 3 restante + Fase 4 + Fase 5 + Fase 6:** 13-22 dias de desenvolvimento

---

## Priorização Recomendada

### Crítico (Sem isso não há jogo)
1. **Refinamentos de ativação** - janelas de player/IA, status por início/fim de ativação e intents
2. **Content System** - Conteúdo jogável ampliado
3. **Run progression/map** - advance/map/current-node/end para fechar loop completo
4. **Conteúdo de CardSelection/Shop/Preparation** - pools, cartas, lojas, modificadores e preparações MVP

### Importante (Jogo funciona mas limitado)
5. **Shop refinements** - venda, tipos adicionais e regras de desconto
6. **Preparation refinements** - preview, remoção e configuração de gambits
7. **Ownership/autorizacao por ator** - obrigatório antes de multiplayer/API multi-cliente

### Desejável (Adiciona profundidade)
8. **SSE/WebSocket avançado** - além da base SSE/polling incremental atual
9. **Transações persistentes/versionadas** - quando Fase 5 começar

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
- Run/Deck, CardSelection, Shop, Preparation, ativação por entidade, consumo real de cartas e eventos incrementais em primeira fatia

### O Que Falta
Para criar um jogo jogável, faltam **4 frentes críticas**:
1. Refinamentos de ativação/intents para o loop de combate ficar jogável e previsível
2. Conteúdo MVP ampliado (cartas, pools, modificadores, preparações, inimigos)
3. Run progression/map/end para fechar o loop entre nós
4. Content System formal da Fase 4 (raças, poderes, inimigos, companions)

### Próximos Passos
**Recomendação:** continuar a Fase 3 pelos refinamentos de ativacao e conteúdo MVP ampliado. `RunState`, `DeckState`, CardSelection, Shop, Preparation, consumo real de cartas e rollback de operações compostas já existem em primeira fatia.

**Ordem sugerida:**
1. Fase 3 restante - Refinar ativação/intents, completar progressão de mapa e ampliar conteúdo MVP inicial
2. Fase 4 (5-7 dias) - Formalizar conteúdo jogável mínimo
3. Fase 5 (opcional) - Adicionar persistence, versionamento e transações persistentes
4. Fase 6 (opcional) - Seeds, modos e desafios

**Tempo total estimado para MVP sem persistence:** 8-15 dias de desenvolvimento focado, considerando as primeiras fatias da Fase 3 já implementadas.

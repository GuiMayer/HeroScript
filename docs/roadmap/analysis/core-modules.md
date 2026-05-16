# Análise de Módulos Core - HeroScript Engine

**Data:** 2026-05-16  
**Status:** Análise Atualizada  
**Objetivo:** Avaliar quais submódulos do HeroScript.Core estão implementados e quais faltam para criar um jogo completo

---

## Resumo Executivo

### Estado Atual
- **Core.Tests:** 524 testes passando após estabilização
- **API.Tests:** compila e o host sobe; ainda há falhas legadas de contrato em Config/Resource/Action e timeout nos testes filtrados de StatusEffect
- **Fases implementadas:** Fase 0 e Fase 1 completas; Fase 2 parcialmente implementada
- **Próxima fase:** concluir estabilização da Fase 2 antes de iniciar Run/Shop/CardSelection/Content

### Capacidade Atual
Com os módulos implementados, é possível criar:
- ✅ Sistema de combate básico (herói vs inimigos)
- ✅ Sistema de dano configurável via JSON
- ✅ Sistema de recursos (HP, energia, mana, etc.)
- ✅ Sistema de ações com custos alternativos
- ✅ Sistema de eventos (pub/sub)
- ✅ Configuração data-driven com herança

### O Que Falta Para Um Jogo Completo
- ❌ Loop de run (progressão, mapa, recompensas)
- ⚠️ Sistema de status effects parcial (Core/API/config existem; semântica ainda incompleta)
- ❌ Sistema de modificadores (Go Again, Multi-Hit, etc.)
- ❌ Sistema de companions com gambits
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

**Testes:** Incluídos nos 423 testes totais

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

### 1. Status Effects System ⚠️ Parcial (Fase 2)
**Prioridade:** ALTA  
**Localização:** `src/Core/StatusEffects/`, `src/API/Controllers/StatusEffectController.cs`, `UserData/Configs/default/StatusEffects/status_effects.json`

**Implementado:**
- `StatusEffectManager`, `StatusEffectProcessor`, `StatusEffectDefinition`, `StatusEffectInstance`
- API REST principal em `/api/StatusEffect/*`
- Rotas compatíveis por entidade em `/api/combat/{combatId}/entities/{targetId}/status`
- Integração parcial com `CombatSystem` para controle, DoT/HoT, shield/thorns/intangible e BUFFER
- `DamageCalculator` recebe `IStatusEffectManager` via DI e pode aplicar modificadores de pipeline

**O que falta estabilizar:**
- Processamento real de DoT/HoT deve aplicar dano/cura via sistemas de recurso/dano, não apenas retornar resultados intermediários
- Fórmulas e modificadores ainda usam parsing manual em alguns pontos; integrar com `MathEngine`/`ExpressionEvaluator`
- Ciclo de expiração/tick precisa de testes mais fortes e contrato claro entre `ProcessStatusEffects` e `TickDurations`
- Testes API de StatusEffect precisam ser destravados no runner de integração

**Impacto:**
Sem status effects, não há:
- Buffs/debuffs
- Veneno, queimadura, sangramento
- Stun, silence, root
- Regeneração

**Estimativa:** 2-4 dias de estabilização

---

### 1.1. Effect System ⚠️ Parcial (Fase 2)
**Prioridade:** ALTA  
**Localização:** `src/Core/Effects/`

**Implementado:**
- `EffectResolver`, `EffectDefinition`, `EffectInstance`, `EffectResult`, `EffectType`
- Esqueleto para dano, cura, recursos, status, gold e draw/discard/exhaust

**O que falta:**
- `DAMAGE` precisa usar o `DamageCalculator` de forma completa e alterar estado real
- `HEAL` e `MODIFY_RESOURCE` precisam aplicar mudanças no `ResourceManager`
- `APPLY_STATUS`/`REMOVE_STATUS` precisam fechar o contrato com `StatusEffectManager`
- Fórmulas, condições e filtros devem usar `MathEngine`/`ExpressionEvaluator`
- Efeitos de cartas (`DRAW_CARD`, `DISCARD_CARD`, `EXHAUST_CARD`) aguardam Hand/Deck System

**Impacto:**
Sem isso, ações continuam parcialmente hardcoded e o jogo não fica plenamente data-driven.

**Estimativa:** 2-3 dias de estabilização

---

### 2. Script Modifiers System ❌ (Fase 2)
**Prioridade:** ALTA  
**Localização planejada:** `src/Core/Combat/Modifiers/`

**O que falta:**
- ScriptModifier - Modificadores de comportamento
- ModifierValidator - Validação de compatibilidade
- Integração com CombatSystem
- Modificadores: Go Again, Multi-Hit, Explosivo, etc.

**Impacto:**
Sem modificadores, não há:
- Customização de poderes
- Builds variadas
- Profundidade estratégica

**Estimativa:** 2-3 dias de implementação

---

### 3. Gambit System ❌ (Fase 2)
**Prioridade:** MÉDIA  
**Localização planejada:** `src/Core/Combat/Gambits/`

**O que falta:**
- Gambit - Regras condicionais (IF/THEN)
- GambitEngine - Avaliação de condições
- GambitAction - Ações executáveis
- Integração com EventBus

**Impacto:**
Sem gambits, não há:
- Companions inteligentes
- Automação de ações
- Reação a eventos

**Estimativa:** 2-3 dias de implementação

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

### Fase 2 📋 (Próxima - 6-9 dias)
- Status Effects System (parcial; estabilizar)
- Effect System / EffectResolver (parcial; estabilizar)
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
**Fases 2 + 3 + 4:** 19-29 dias de desenvolvimento

### MVP Completo (com persistence)
**Fases 2 + 3 + 4 + 5:** 22-33 dias de desenvolvimento

### MVP + Features Extras
**Fases 2 + 3 + 4 + 5 + 6:** 24-36 dias de desenvolvimento

---

## Priorização Recomendada

### Crítico (Sem isso não há jogo)
1. **Estabilização Fase 2** - Status lifecycle, EffectResolver e integração ActionManager/CombatSystem
2. **Run Management System** - Loop de jogo
3. **Content System** - Conteúdo jogável
4. **Status Effects System** - Profundidade de combate

### Importante (Jogo funciona mas limitado)
5. **Script Modifiers System** - Customização
6. **Card Selection System** - Progressão de deck
7. **Shop System** - Economia

### Desejável (Adiciona profundidade)
8. **Gambit System** - Companions inteligentes
9. **Preparation System** - Customização pré-combate

### Opcional (Pode vir depois do MVP)
10. **Persistence System** - Save/load
11. **Seed & Mode System** - Replayability

---

## Conclusão

### Estado Atual
O HeroScript.Core tem uma **fundação sólida** (Fases 0 e 1) com:
- 524 testes Core passando
- Sistemas core bem arquitetados
- Padrões consistentes (Result<T>, EventBus, data-driven)

### O Que Falta
Para criar um jogo jogável, faltam **4 frentes críticas**:
1. Estabilização Fase 2 (Status lifecycle, EffectResolver, ActionManager/CombatSystem)
2. Run Management (loop de jogo)
3. Content System (raças, poderes, inimigos)
4. Card Selection (progressão)

### Próximos Passos
**Recomendação:** concluir a estabilização da Fase 2 antes de partir para Fase 3 (Run loop), pois os sistemas de combate precisam estar completos antes de construir o loop de jogo em cima deles.

**Ordem sugerida:**
1. Fase 2 (6-9 dias) - Completar sistemas de combate
2. Fase 4 (5-7 dias) - Adicionar conteúdo jogável (pode ser feito em paralelo com Fase 3)
3. Fase 3 (8-13 dias) - Implementar loop de run
4. Fase 5 (opcional) - Adicionar persistence

**Tempo total estimado para MVP:** 19-29 dias de desenvolvimento focado.

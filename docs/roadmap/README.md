# API Roadmap

**Última atualização:** 2026-05-16  
**Status:** Fase 2 estabilizada parcialmente; API atualizada para contratos data-driven

---

## Visão Geral

Este roadmap documenta a evolução da API REST do HeroScript, mapeando todos os endpoints planejados organizados por fase de implementação. O objetivo é fornecer uma visão geral da arquitetura da API para guiar o desenvolvimento e garantir que todos os sistemas se integrem de forma coesa.

**Análise Completa:** Para uma análise detalhada dos módulos Core implementados vs. necessários para criar um jogo completo, consulte [analysis/core-modules.md](analysis/core-modules.md).

**Diagnóstico Data-driven:** Para controlar a aderência à filosofia principal de regras/conteúdo em JSON, consulte [analysis/data-driven-compliance.md](analysis/data-driven-compliance.md).

## Filosofia da API

A API do HeroScript segue os princípios da arquitetura headless:

- **Stateless:** Cada requisição contém todas as informações necessárias
- **Data-driven:** Configurações e regras são dados (JSON), não código
- **Event-sourced:** Todos os eventos são registrados para replay e auditoria
- **Modular:** Sistemas podem ser consumidos independentemente
- **RESTful:** Endpoints seguem convenções REST quando aplicável

## Estrutura do Roadmap

O roadmap está organizado em 6 fases principais, alinhadas com o desenvolvimento técnico do Core:

| Fase | Status | Descrição | Documento |
|------|--------|-----------|-----------|
| **Fase 0** | ✅ Implementado | Fundação (Config, Math, Resources) | [phases/phase-0.md](phases/phase-0.md) |
| **Fase 1** | ✅ Implementado | EventBus, Combate Básico, TurnPhase System | [phases/phase-1.md](phases/phase-1.md) |
| **Fase 2** | ⚠️ Parcial / Estabilização | Camadas de Combate (Status, Modifiers, Gambits) | [phases/phase-2.md](phases/phase-2.md) |
| **Fase 3** | 📋 Planejado | Loop de Run (Run, CardSelection, Shop) | [phases/phase-3.md](phases/phase-3.md) |
| **Fase 4** | 📋 Planejado | Conteúdo MVP (Races, Powers, Companions, Enemies) | [phases/phase-4.md](phases/phase-4.md) |
| **Fase 5** | 📋 Planejado | Persistência (Save/Load, MetaProgression) | [phases/phase-5.md](phases/phase-5.md) |
| **Fase 6** | 📋 Planejado | Modos Especiais (Seed, Daily, Custom) | [phases/phase-6.md](phases/phase-6.md) |

## Convenções da API

Para detalhes sobre padrões, nomenclatura, segurança e versionamento, consulte:

- [analysis/api-conventions.md](analysis/api-conventions.md) - Convenções gerais da API
- [analysis/event-integration.md](analysis/event-integration.md) - Integração com EventBus

## Base URL

```
http://localhost:5260/api
```

**Nota:** Versionamento (`/api/v1/`) será adicionado apenas quando tivermos o primeiro MVP em beta.

## Documentação Relacionada

- [../api/endpoints.md](../api/endpoints.md) - Documentação detalhada dos endpoints atuais
- [../systems/events/eventbus-system.md](../systems/events/eventbus-system.md) - Sistema EventBus (pub/sub e Event Sourcing)
- [../systems/config/config-system.md](../systems/config/config-system.md) - Sistema de configuração com herança delta
- [../systems/math/expression-modes.md](../systems/math/expression-modes.md) - Modos de expressão matemática
- [../systems/damage/damage-pipeline.md](../systems/damage/damage-pipeline.md) - Sistema de pipeline de dano configurável
- [../systems/damage/damage-examples.md](../systems/damage/damage-examples.md) - Exemplos práticos do pipeline de dano
- [../systems/combat/turn-phase-system.md](../systems/combat/turn-phase-system.md) - Sistema de fases de turno para TCGs

## Como Usar Este Roadmap

1. **Durante o planejamento:** Consulte a fase correspondente para entender quais endpoints serão necessários
2. **Durante a implementação:** Use os documentos como referência para estrutura e nomenclatura
3. **Durante a integração:** Verifique dependências entre APIs em cada fase
4. **Após cada fase:** Atualize o status e adicione links para documentação detalhada

## Changelog

| Data | Fase | Mudança |
|------|------|---------|
| 2026-05-16 | API-9 | `docs/api/endpoints.md` atualizado para os contratos atuais: `actionId`, `effects[]`, `/api/effect`, `/api/status` e dano como simulacao |
| 2026-05-16 | API-8 | Testes unitarios de Config/Resource alinhados aos mocks atuais; projeto `API.Tests` compila, mas o runner local ainda congela em `ResourceControllerTests` filtrado |
| 2026-05-16 | API-7 | Respostas de validacao normalizadas para `400/404` em erros esperados de API |
| 2026-05-16 | API-6 | Rota canonica `/api/status` adicionada mantendo compatibilidade com `/api/StatusEffect` e aliases de combate |
| 2026-05-16 | API-5 | Damage API reposicionada como simulacao/diagnostico (`/api/damage/simulate`) |
| 2026-05-16 | API-4 | Endpoint central `/api/effect/apply` criado para aplicar effects por contexto `COMBAT` ou `RUN` |
| 2026-05-16 | API-3 | Combat API passou a aceitar `actionId` como contrato preferido para executar acoes |
| 2026-05-16 | API-2 | Action API passou a expor `effects[]` completo; `baseDamage` virou campo derivado/compatibilidade |
| 2026-05-16 | API-1 | DI/defaults de API alinhados aos contratos atuais (`IActionManager`, config `default`) |
| 2026-05-16 | DC-5 | Documentação consolidada com resultado final da trilha Data-driven Compliance, lacunas restantes e próxima fase técnica |
| 2026-05-16 | DC-4 | Aplicação de Status Effects em combate passou a usar `StatusEffectBehavior` em vez de tipos específicos como Burning/Poison/Buffer |
| 2026-05-16 | DC-3 | `StartCombat` passou a criar entidades por `EntityDefinition` JSON quando o ID existir no loader |
| 2026-05-16 | DC-2 | `CombatSystem` passou a executar ataque básico e poderes por `ActionDefinition`, sem dano/custo/tags numéricos hardcoded |
| 2026-05-16 | DC-1 | `ActionManager` passou a descobrir ações JSON em vez de usar lista fixa de nomes |
| 2026-05-16 | Data-driven Compliance | Diagnóstico criado: `ActionManager`, `CombatSystem`, início de combate, Status/Effects e AI ainda tinham regras hardcoded a migrar para JSON |
| 2026-05-16 | Estabilização | Build/Core.Tests estabilizados; Resource reload corrigido; `*.lscache` ignorado; rotas REST de Status Effects alinhadas; DamageCalculator passou a receber StatusEffectManager via DI |
| 2026-05-16 | Análise | Auditoria atualizada: Status Effects existem parcialmente; Script Modifiers, Run, CardSelection, Shop, Preparation e Content continuam faltando; EffectResolver e CombatSystem ainda precisam de integração/refatoração |
| 2026-05-11 | Docs | **Documentação reorganizada** - Nova estrutura com diretórios temáticos (architecture/, systems/, api/, roadmap/) |
| 2026-05-11 | Fase 1 | **TurnPhase System documentado** - Sistema de fases para TCGs completamente documentado |
| 2026-05-11 | Fase 1 | **TurnPhase System implementado** - Sistema modular de fases (Magic, Yu-Gi-Oh!, Hearthstone, Classic) |
| 2026-05-11 | Fase 1 | TurnOrder System implementado - 5 calculadores de ordem de turno (ATB, Initiative, Speed, Fixed, Conditional) |
| 2026-05-09 | Análise | **Análise completa de módulos Core** - Documentado estado atual e roadmap para MVP jogável |
| 2026-05-09 | Fase 6a | MathEngine melhorado com logging e Result<T> pattern (commit fb3d24d) |
| 2026-05-09 | Fase 1 | **Fase 1 completa!** 126 testes de dano implementados (unitários, integração, edge cases, eventos) |
| 2026-05-09 | Fase 1 | IRandomProvider adicionado para testabilidade do sistema de crítico |
| 2026-05-09 | Fase 1 | Testes de integração simulando PoE, Genshin, Card Game e RPG styles |
| 2026-05-08 | Fase 1 | Damage Pipeline System implementado (7 operações, 5 filtros, 3 eventos, API REST) |
| 2026-05-08 | Fase 1 | Combat System implementado com 53 testes |
| 2026-05-08 | Fase 1 | Alternative Costs System implementado com 26 testes |
| 2026-05-08 | Fase 1 | EventBus implementado e integrado |
| 2026-05-08 | Todas | Criação inicial do roadmap |

---

**Próximo passo:** corrigir o congelamento do runner de `API.Tests` em `ResourceControllerTests` e entao iniciar Run/Shop/CardSelection/Content sobre o contrato central de effects.

**Nota:** A documentação foi reorganizada em 2026-05-11. Veja [../MOVED.md](../MOVED.md) para mapeamento de caminhos antigos.

## Estatísticas Atuais

### Estado Verificado em 2026-05-16
- **Core.Tests:** 540 testes passando apos Effect Application Engine e atualizacao de API
- **API.Tests:** projeto compila; `ActionControllerTests`, `GameResourceControllerTests` e `ConfigControllerTests` foram alinhados/validados; o runner local ainda congela ao filtrar `ResourceControllerTests`, sem falha de assercao reportada
- **Status Effects:** Core, API e configuração existem; aplicação em combate usa comportamentos genéricos, mas schema e executor central ainda precisam consolidação
- **Data-driven Compliance:** 8.2/10; ações, execução básica de combate, início de combate por entidades JSON, aplicação de status por comportamento e API baseada em `effects[]` foram migrados; AI/schemas e cobertura completa de run/deck/shop ainda têm lacunas conforme [analysis/data-driven-compliance.md](analysis/data-driven-compliance.md)
- **Correções aplicadas:** Resource reload/hot reload, ActionStack null guard, rotas REST compatíveis para Status Effects, StatusEffectManager no DamageCalculator, guard de ambiente em `EventsController.ClearHistory`, remoção de `Directory.SetCurrentDirectory` do factory de testes

### Fase 0 + Fase 1 (Implementadas)
- **Total de testes Core:** 540 testes passando
  - Combat: 53 testes
  - Events: 18 testes  
  - Damage: 126 testes
  - Math: 97 testes
  - Resources: 23 testes
  - Config: incluído no total
  - Outros: incluído no total
- **APIs implementadas:** Events, Combat, Damage, Resource, Config, Action, Entity, StatusEffect e outras APIs de suporte
- **Sistemas Core:** EventBus, CombatSystem, TurnPhase System, TurnOrder System, DamagePipeline, MathEngine, ResourceManager, ConfigManager, Entity System, StatusEffects parcial
- **Documentação:** 9 documentos técnicos completos

### Estimativa para MVP Jogável
- **Fases restantes:** 2, 3, 4 (críticas)
- **Tempo estimado:** 19-29 dias de desenvolvimento
- **Sistemas críticos faltando:** Run Management, Content System, Card Selection, Shop, Preparation, Script Modifiers, Gambit Engine real
- **Sistemas críticos parciais:** EffectResolver/Effect Application Engine, schemas de status, AI/Gambit data-driven

Para detalhes completos sobre módulos implementados e faltantes, consulte [analysis/core-modules.md](analysis/core-modules.md).

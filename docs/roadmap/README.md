# API Roadmap

**Última atualização:** 2026-05-09  
**Status:** Em Desenvolvimento Ativo

---

## Visão Geral

Este roadmap documenta a evolução da API REST do HeroScript, mapeando todos os endpoints planejados organizados por fase de implementação. O objetivo é fornecer uma visão geral da arquitetura da API para guiar o desenvolvimento e garantir que todos os sistemas se integrem de forma coesa.

**Análise Completa:** Para uma análise detalhada dos módulos Core implementados vs. necessários para criar um jogo completo, consulte [CORE_MODULES_ANALYSIS.md](CORE_MODULES_ANALYSIS.md).

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
| **Fase 0** | ✅ Implementado | Fundação (Config, Math, Resources) | [PHASE_0.md](PHASE_0.md) |
| **Fase 1** | ✅ Implementado | EventBus e Combate Básico | [PHASE_1.md](PHASE_1.md) |
| **Fase 2** | 📋 Planejado | Camadas de Combate (Status, Modifiers, Gambits) | [PHASE_2.md](PHASE_2.md) |
| **Fase 3** | 📋 Planejado | Loop de Run (Run, CardSelection, Shop) | [PHASE_3.md](PHASE_3.md) |
| **Fase 4** | 📋 Planejado | Conteúdo MVP (Races, Powers, Companions, Enemies) | [PHASE_4.md](PHASE_4.md) |
| **Fase 5** | 📋 Planejado | Persistência (Save/Load, MetaProgression) | [PHASE_5.md](PHASE_5.md) |
| **Fase 6** | 📋 Planejado | Modos Especiais (Seed, Daily, Custom) | [PHASE_6.md](PHASE_6.md) |

## Convenções da API

Para detalhes sobre padrões, nomenclatura, segurança e versionamento, consulte:

- [API_CONVENTIONS.md](API_CONVENTIONS.md) - Convenções gerais da API
- [EVENT_INTEGRATION.md](EVENT_INTEGRATION.md) - Integração com EventBus

## Base URL

```
http://localhost:5260/api
```

**Nota:** Versionamento (`/api/v1/`) será adicionado apenas quando tivermos o primeiro MVP em beta.

## Documentação Relacionada

- [API-ENDPOINTS.md](../API-ENDPOINTS.md) - Documentação detalhada dos endpoints atuais
- [EVENTBUS_SYSTEM.md](../EVENTBUS_SYSTEM.md) - Sistema EventBus (pub/sub e Event Sourcing)
- [CONFIG_SYSTEM.md](../CONFIG_SYSTEM.md) - Sistema de configuração com herança delta
- [API_MATH_EXPRESSION_MODES.md](../API_MATH_EXPRESSION_MODES.md) - Modos de expressão matemática
- [DAMAGE_PIPELINE.md](../DAMAGE_PIPELINE.md) - Sistema de pipeline de dano configurável
- [DAMAGE_PIPELINE_EXAMPLES.md](../DAMAGE_PIPELINE_EXAMPLES.md) - Exemplos práticos do pipeline de dano

## Como Usar Este Roadmap

1. **Durante o planejamento:** Consulte a fase correspondente para entender quais endpoints serão necessários
2. **Durante a implementação:** Use os documentos como referência para estrutura e nomenclatura
3. **Durante a integração:** Verifique dependências entre APIs em cada fase
4. **Após cada fase:** Atualize o status e adicione links para documentação detalhada

## Changelog

| Data | Fase | Mudança |
|------|------|---------|
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

**Próximo passo:** Implementar Status Effects System (Fase 2)

## Estatísticas Atuais

### Fase 0 + Fase 1 (Implementadas)
- **Total de testes:** 423 testes (todos passando)
  - Combat: 53 testes
  - Events: 18 testes  
  - Damage: 126 testes
  - Math: 97 testes
  - Resources: 23 testes
  - Config: incluído no total
  - Outros: incluído no total
- **APIs implementadas:** 3/3 (Events, Combat, Damage)
- **Sistemas Core:** EventBus, CombatSystem, DamagePipeline, MathEngine, ResourceManager, ConfigManager
- **Documentação:** 9 documentos técnicos completos

### Estimativa para MVP Jogável
- **Fases restantes:** 2, 3, 4 (críticas)
- **Tempo estimado:** 19-29 dias de desenvolvimento
- **Sistemas críticos faltando:** Run Management, Content System, Status Effects, Card Selection

Para detalhes completos sobre módulos implementados e faltantes, consulte [CORE_MODULES_ANALYSIS.md](CORE_MODULES_ANALYSIS.md).

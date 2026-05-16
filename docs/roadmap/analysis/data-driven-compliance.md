# Diagnostico Data-driven - HeroScript

**Data:** 2026-05-16  
**Status:** Em execucao  
**Objetivo:** medir e controlar a aderencia do projeto a filosofia principal: conteudo e regras de sistema devem morar em JSON; o codigo deve interpretar dados e aplicar primitivas de engine.

---

## Veredito

O projeto esta **parcialmente data-driven**. A base tecnica existe, mas ainda ha regras de gameplay em C# que deveriam estar em JSON.

**Score atual:** 6.3/10

### O que ja esta alinhado

- `ConfigManager`, `ResourceLoader` e heranca delta existem.
- `ResourceManager` carrega definicoes de recursos em JSON.
- `DamagePipeline` usa buckets, filtros e operacoes configuraveis por JSON.
- `StatusEffectManager` carrega definicoes de status em JSON.
- `ActionManager` existe e interpreta `ActionDefinition`.
- Entidades possuem definicoes JSON em `data/configs/default/Entities/`.
- TurnPhase possui configuracoes JSON para estilos de TCG.

### O que ainda viola a filosofia

| Severidade | Area | Problema | Acao |
|---|---|---|---|
| BLOCKING | Actions | `ActionManager` carregava lista fixa de acoes conhecidas | Descobrir acoes por JSON/manifest |
| ✅ Resolvido | Combat | `CombatSystem` ainda tinha dano/custo/tags de `BASIC_ATTACK` e fallback de `POWER` hardcoded | `BASIC_ATTACK` e `POWER` agora exigem `ActionDefinition` e aplicam dano/custo/energia por effects |
| HIGH | Combat start | `StartCombat` cria Hero/Enemy com HP, energia e nomes fixos | Resolver entidades via definicoes JSON |
| HIGH | Status | `CombatSystem` conhece tipos especificos como `BURNING`, `POISON`, `SHIELD`, `THORNS`, `BUFFER` | Aplicar status por comportamento/effects genericos |
| HIGH | Effects | `EffectResolver` ainda nao aplica todos os tipos nem altera estado completo sozinho | Centralizar execucao/aplicacao de effects |
| MEDIUM | Formulas | Existem avaliadores simples duplicados em status/effects | Usar um avaliador canonico |
| MEDIUM | AI | `AIController` decide comportamento por enum/thresholds em codigo | Migrar para regras/gambits JSON |
| MEDIUM | Schemas | Ha formatos divergentes de status entre `data/configs` e `UserData/Configs` | Definir schema canonico e migrar legado |

---

## Fronteira Aceitavel

### Pode ficar no codigo

- Primitivas genericas de engine: `DAMAGE`, `HEAL`, `APPLY_STATUS`, `MODIFY_RESOURCE`.
- Contratos de pipeline, validacao estrutural, cache, event bus e DI.
- Operacoes matematicas genericas.
- IDs convencionais quando documentados como contrato da engine.

### Deve morar em JSON

- Dano base, custo, cooldown, tags e alvo de acoes.
- Regras de status, duracao, stacks, formulas e timing.
- HP/energia inicial, recursos de entidades, nomes e stats.
- Regras de IA/gambit.
- Sequencias de turno, pipelines, formulas e modificadores.

---

## Fases de Compliance

| Fase | Status | Entregavel | Evidencia |
|---|---|---|---|
| DC-0 | ✅ Documentado | Diagnostico e plano de controle em `docs/` | Este documento |
| DC-1 | ✅ Implementado | `ActionManager` descobre acoes a partir dos arquivos JSON | Teste cobre acao nova carregada via discovery |
| DC-2 | ✅ Implementado | `CombatSystem` executa `ActionDefinition` para ataque basico/poder | Dano, custo, tags e ganho de energia vêm da definicao da acao/testes |
| DC-3 | ⏳ Pendente | Inicio de combate usa definicoes de entidade JSON quando disponiveis | HP/nome/recursos vêm de JSON |
| DC-4 | ⏳ Pendente | Status sao aplicados por comportamento generico | Remover checagens especificas de `BURNING`/`POISON` etc. |
| DC-5 | ⏳ Pendente | Docs atualizados com progresso final e lacunas restantes | Roadmap reflete estado real |

---

## Proximo Passo Natural

Priorizar DC-3: iniciar combate a partir de definicoes de entidade JSON quando disponiveis. As acoes ja sao descobertas e executadas por `ActionDefinition`, mas `StartCombat` ainda cria HP/energia/nome padrao em C#.

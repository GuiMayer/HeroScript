# Diagnostico Data-driven - HeroScript

**Data:** 2026-05-16  
**Status:** Compliance inicial concluido; API alinhada aos contratos data-driven  
**Objetivo:** medir e controlar a aderencia do projeto a filosofia principal: conteudo e regras de sistema devem morar em JSON; o codigo deve interpretar dados e aplicar primitivas de engine.

---

## Veredito

O projeto esta **parcialmente data-driven**. A base tecnica existe, mas ainda ha regras de gameplay em C# que deveriam estar em JSON.

**Score atual:** 8.2/10

### O que ja esta alinhado

- `ConfigManager`, `ResourceLoader` e heranca delta existem.
- `ResourceManager` carrega definicoes de recursos em JSON.
- `DamagePipeline` usa buckets, filtros e operacoes configuraveis por JSON.
- `StatusEffectManager` carrega definicoes de status em JSON.
- `ActionManager` existe e interpreta `ActionDefinition`.
- API de acoes expoe `effects[]` como contrato principal; `baseDamage` e apenas derivado/compatibilidade.
- API de combate aceita `actionId` como forma preferida de executar acoes data-driven.
- API central `/api/effect/apply` aplica efeitos por contexto `COMBAT`/`RUN`.
- Entidades possuem definicoes JSON em `data/configs/default/Entities/`.
- TurnPhase possui configuracoes JSON para estilos de TCG.

### O que ainda viola a filosofia

| Severidade | Area | Problema | Acao |
|---|---|---|---|
| ✅ Resolvido | Actions | `ActionManager` carregava lista fixa de acoes conhecidas | Acoes agora sao descobertas por JSON |
| ✅ Resolvido | Combat | `CombatSystem` ainda tinha dano/custo/tags de `BASIC_ATTACK` e fallback de `POWER` hardcoded | `BASIC_ATTACK` e `POWER` agora exigem `ActionDefinition` e aplicam dano/custo/energia por effects |
| ✅ Resolvido parcial | Combat start | `StartCombat` criava Hero/Enemy com HP, energia e nomes fixos | Quando ha `EntityDefinitionLoader`, IDs de entidade sao resolvidos por JSON; fallback legado permanece para compatibilidade |
| ✅ Resolvido parcial | Status | `CombatSystem` conhecia tipos especificos como `BURNING`, `POISON`, `SHIELD`, `THORNS`, `BUFFER` | Aplicacao em combate agora usa `StatusEffectBehavior`; falta centralizar execucao completa em Effects |
| HIGH | Effects | `EffectResolver` ainda nao aplica todos os tipos nem altera estado completo sozinho | Centralizar execucao/aplicacao de effects |
| MEDIUM | Formulas | Existem avaliadores simples duplicados em status/effects | Usar um avaliador canonico |
| MEDIUM | AI | `AIController` decide comportamento por enum/thresholds em codigo | Migrar para regras/gambits JSON |
| MEDIUM | Schemas | Ha formatos divergentes de status entre `data/configs` e `UserData/Configs` | Definir schema canonico e migrar legado |
| MEDIUM | Test runner API | `API.Tests` compila, mas o runner local congela ao filtrar `ResourceControllerTests` | Investigar ambiente/fixture antes de usar a suite API como gate obrigatório |

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
| DC-3 | ✅ Implementado | Inicio de combate usa definicoes de entidade JSON quando disponiveis | Teste cobre `player_warrior` e `enemy_orc_warrior` carregados de JSON |
| DC-4 | ✅ Implementado | Status sao aplicados por comportamento generico | `CombatSystem` nao depende mais de tipos especificos para DoT/HoT/shield/reactive/cap/death-prevention |
| DC-5 | ✅ Implementado | Docs atualizados com progresso final e lacunas restantes | Roadmap reflete estado real apos DC-1..DC-4 |
| API-1..9 | ✅ Implementado parcial | API atualizada para contratos data-driven e docs sincronizadas | `actionId`, `effects[]`, `/api/effect`, `/api/status`; API.Tests compila, com pendencia de runner |

---

## Resultado da Trilha DC-0..DC-5

A primeira rodada de compliance removeu os principais bloqueios data-driven de combate:

- Acoes deixaram de depender de lista fixa em C#.
- Ataque basico e poderes passaram a exigir `ActionDefinition`.
- Dano, custo, tags e alteracao de energia passaram a vir dos effects/custos configurados.
- Inicio de combate passou a preferir `EntityDefinition` JSON para recursos, nomes e stats.
- Status em combate passaram a ser interpretados por `StatusEffectBehavior`, nao por tipos especificos como Burning/Poison/Buffer.
- API publica passou a expor a linguagem central de efeitos (`effects[]`) e a aceitar execucao por `actionId`.
- `/api/effect/apply` virou o ponto de entrada para acontecimentos unicos de combate/run.

## Lacunas Restantes

| Prioridade | Lacuna | Motivo |
|---|---|---|
| HIGH | `EffectResolver` nao e executor universal de estado | Ainda resolve parte dos effects como resultado intermediario; `CombatSystem` ainda aplica efeitos de combate diretamente |
| HIGH | Schemas de status divergentes | `UserData/Configs` e `data/configs` usam formatos diferentes; isso aumenta risco de conteudo quebrar conforme o loader usado |
| MEDIUM | Formula evaluators duplicados | Status e Effects ainda possuem avaliadores simples locais; a fonte canonica deveria ser `MathEngine`/`ExpressionEvaluator` |
| MEDIUM | AI/Gambit ainda nao e JSON-driven | `AIController`/gambit placeholder ainda usam decisoes estruturais em codigo |
| MEDIUM | Fallback legado de entidades | `StartCombat` ainda cria entidades padrao se JSON nao existir; aceitavel por compatibilidade, mas producao deve tratar definicao ausente como erro |
| MEDIUM | Runner de `API.Tests` instavel | Testes compilam e subsets passam, mas `ResourceControllerTests` filtrado congela no ambiente atual |

## Proximo Passo Natural

Priorizar a estabilizacao do runner de `API.Tests` e depois iniciar Run/Deck/Shop usando `/api/effect/apply` como contrato base para acontecimentos unicos. Em paralelo, migrar AI/Gambit para JSON e unificar o schema de Status Effects.

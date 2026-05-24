# Diagnostico Data-driven - HeroScript

**Data:** 2026-05-23
**Status:** Fase 3 em refinamento — loaders principais migrados para `ResourceLoader`
**Objetivo:** medir e controlar a aderencia do projeto a filosofia principal: conteudo e regras de sistema devem morar em JSON; o codigo deve interpretar dados e aplicar primitivas de engine.

---

## Veredito

O projeto esta **majoritariamente data-driven**. A base tecnica agora cobre loaders de conteudo e regras via `ResourceLoader`, cache por cadeia de configs/mods e recursos JSON em runtime. As lacunas restantes sao principalmente avaliadores duplicados, garantias transacionais e alguns pontos de infraestrutura que precisam tocar o filesystem por desenho.

**Score atual:** 9.5/10

### O que ja esta alinhado

- `ConfigManager`, `ResourceLoader` e heranca delta existem.
- `ResourceManager` carrega definicoes de recursos em JSON via `ResourceLoader`.
- `DamagePipeline` usa buckets, filtros e operacoes configuraveis por JSON; fallback hardcoded foi removido.
- `StatusEffectManager` carrega definicoes de status via `ResourceLoader`.
- `ActionManager` existe e interpreta `ActionDefinition`.
- `ScriptModifierManager` carrega modificadores via `ResourceLoader`, filtra por tags e calcula pipeline modifiers.
- `GambitEngine` carrega regras de decisao de AI via `ResourceLoader` (condicoes, prioridade, acoes).
- API de acoes expoe `effects[]` como contrato principal; `baseDamage` e apenas derivado/compatibilidade.
- API de combate aceita `actionId` como forma preferida de executar acoes data-driven.
- API central `/api/effect/apply` aplica efeitos por contexto `COMBAT`/`RUN`.
- API `/api/modifiers` expoe Script Modifiers para aplicar/consultar/tick modificadores data-driven.
- API `/api/gambits` expoe decisoes de AI data-driven e definicoes de gambit.
- Entidades possuem definicoes JSON em `data/configs/default/Resources/Entities/`.
- TurnPhase possui configuracoes JSON em `data/configs/default/Resources/phase-sequences/`.
- Card pools, card catalog, run definitions, card selections, shops, preparations e combat activation rules usam recursos JSON.

### O que ainda viola a filosofia

| Severidade | Area | Problema | Acao |
|---|---|---|---|
| ✅ Resolvido | Actions | `ActionManager` carregava lista fixa de acoes conhecidas | Acoes agora sao descobertas por JSON |
| ✅ Resolvido | Combat | `CombatSystem` ainda tinha dano/custo/tags de `BASIC_ATTACK` e fallback de `POWER` hardcoded | `BASIC_ATTACK` e `POWER` agora exigem `ActionDefinition` e aplicam dano/custo/energia por effects |
| ✅ Resolvido parcial | Combat start | `StartCombat` criava Hero/Enemy com HP, energia e nomes fixos | Quando ha `EntityDefinitionLoader`, IDs de entidade sao resolvidos por JSON; fallback legado permanece para compatibilidade |
| ✅ Resolvido parcial | Status | `CombatSystem` conhecia tipos especificos como `BURNING`, `POISON`, `SHIELD`, `THORNS`, `BUFFER` | Aplicacao em combate agora usa `StatusEffectBehavior`; falta centralizar execucao completa em Effects |
| ✅ Resolvido | Schemas | Ha formatos divergentes de status entre `data/configs` e `UserData/Configs` | `StatusEffectManager.DeserializeStatusDefinitions` aceita ambos os formatos e converte legado para canonical |
| ✅ Resolvido | AI/Gambit | `AIController`/gambit placeholder decidia por enum/thresholds em codigo | `GambitEngine` carrega regras JSON; `GambitController` delega decisoes ao engine data-driven |
| ✅ Resolvido | Effects | `EffectResolver` so cobria 7 tipos de efeito | Agora cobre economia (PP), deck (draw/discard/exhaust/add), modifiers (damage/crit/cooldown) e controle (prevent/force/skip/reflect/absorb) |
| ✅ Resolvido | Modifiers | Nao existia sistema de script modifiers | `ScriptModifierManager` carrega/aplica/tick modificadores JSON com pipeline filtrado por tags |
| ✅ Resolvido | Loaders diretos | Status, modifiers, gambits, entidades e phase sequences ainda liam arquivos por caminho fisico | Todos foram migrados para `ResourceLoader` e recursos em `Resources/` |
| ✅ Resolvido | Damage pipeline | Loader tinha fallback hardcoded de bucket quando JSON falhava | Pipeline agora exige JSON valido e propaga erro de configuracao |
| MEDIUM | Formulas | Existem avaliadores simples duplicados em status/effects | Usar um avaliador canonico |
| MEDIUM | Test runner API | `API.Tests` compila, mas o runner local congela ao filtrar `ResourceControllerTests` | Investigar ambiente/fixture antes de usar a suite API como gate obrigatorio |
| ✅ Resolvido parcial | Transacoes de Run | Operacoes compostas podiam gastar recurso antes de falhar em etapa posterior | `RunManager` agora usa snapshot/rollback para operacoes compostas e compensa modifiers externos de preparacao; refinamentos futuros ficam para persistencia/versionamento |
| LOW | Infraestrutura filesystem | `ConfigManager`, providers fisicos e hot reload precisam observar arquivos reais | Aceitavel; estes pontos sao infraestrutura, nao regra/conteudo de gameplay |

---

## Fronteira Aceitavel

### Pode ficar no codigo

- Primitivas genericas de engine: `DAMAGE`, `HEAL`, `APPLY_STATUS`, `MODIFY_RESOURCE`.
- Contratos de pipeline, validacao estrutural, cache, event bus e DI.
- Operacoes matematicas genericas.
- IDs convencionais quando documentados como contrato da engine.
- Acesso fisico a disco dentro de infraestrutura de config/providers/hot reload, pois essa camada descobre e observa arquivos JSON reais para desenvolvimento e mods.

### Deve morar em JSON

- Dano base, custo, cooldown, tags e alvo de acoes.
- Regras de status, duracao, stacks, formulas e timing.
- HP/energia inicial, recursos de entidades, nomes e stats.
- Regras de IA/gambit.
- Sequencias de turno, pipelines, formulas e modificadores.
- Catalogos, pools, recompensas, lojas, preparacoes, entidades, status, gambits e phase sequences.

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
| Estab-1 | ✅ Implementado | Status schemas unificados com loader dual | `StatusEffectManager.DeserializeStatusDefinitions` aceita legacy array e canonical dictionary |
| Estab-2 | ✅ Implementado | Script Modifiers Core + API | `ScriptModifierManager` com pipeline/tags/tick; API `/api/modifiers` |
| Estab-3 | ✅ Implementado | Gambit Engine Core + API data-driven | `GambitEngine` carrega regras JSON; `GambitController` delega; API `/api/gambits` |
| Estab-4 | ✅ Implementado | Effect Engine consolidado com 20+ tipos | Economia, deck, modifiers e controle resolvidos por `EffectResolver`; 553 testes Core passando |
| Loader-1 | ✅ Implementado | Card pools carregados por ID | `CardPoolResolver` carrega `card-pools/{poolId}.json` |
| Loader-2 | ✅ Implementado | Script modifiers via `ResourceLoader` | Sem leitura direta de `Modifiers/script_modifiers.json` |
| Loader-3 | ✅ Implementado | Status effects via `ResourceLoader` | Sem leitura direta de `StatusEffects/status_effects.json` |
| Loader-4 | ✅ Implementado | Entidades via `ResourceLoader` | `EntityDefinitionLoader` usa `Resources/Entities/{id}.json` |
| Loader-5 | ✅ Implementado | Gambits sem fallback de arquivo fisico | `GambitEngine` exige `IResourceLoader` |
| Loader-6 | ✅ Implementado | Damage pipeline sem fallback hardcoded | JSON invalido agora falha em vez de criar pipeline em C# |
| Loader-7 | ✅ Implementado | TurnPhase via `ResourceLoader` | Presets migrados para `Resources/phase-sequences/{id}.json` |
| RunTx-1 | ✅ Implementado | Fronteira transacional de Run | `RunManager` faz rollback de pick, decompose, shop buy/reroll, card-selection reroll e preparation; modifiers aplicados em preparacao sao compensados em falha |

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
| MEDIUM | Formula evaluators duplicados | Status, Effects e Modifiers ainda possuem avaliadores simples locais; a fonte canonica deveria ser `MathEngine`/`ExpressionEvaluator` |
| LOW | Transacoes de Run | Primeira fatia em memoria esta coberta; persistencia futura ainda precisara de versionamento/concorrencia otimista |
| MEDIUM | Runner de `API.Tests` instavel | Testes compilam e subsets passam, mas runner completo congela no ambiente atual |
| LOW | Hot reload de recursos usa `FileSystemWatcher` | Essencial para detectar alteracoes reais em JSON durante desenvolvimento; deve ficar isolado em infraestrutura |

## Proximo Passo Natural

Fase 2 esta estabilizada. O proximo passo e iniciar a **Fase 3 — Loop de Run** (Run Management, Card Selection, Shop) usando `/api/effect/apply` como contrato base para acontecimentos unicos. A Fase 3 trara `RunState` e `DeckState` que permitirao efeitos de economia/deck aplicarem estado real.

## Decisao de Execucao para Fase 3

Para evitar retorno de hardcodes, a Fase 3 deve começar pelo estado e pelas regras data-driven antes de controllers mais ricos:

1. Criar `RunState` e `DeckState` como fonte real para economia, deck, mão, discard e exhaust.
2. Tornar tamanho de mão, deck inicial, regras de draw/discard/shuffle, pools de cartas, recompensas, preços e mapa configuráveis via JSON.
3. Conectar efeitos já existentes de deck/economia no `EffectResolver` ao estado real da run.
4. Só então expor `RunController`, Hand/Deck API, CardSelection, Shop e Preparation.
5. Manter frontend como camada de apresentação: nenhuma regra de deck, loja, IA ou recompensa deve morar no cliente.

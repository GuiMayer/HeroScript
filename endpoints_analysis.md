# Análise de endpoints — contrato atual e arquitetura-alvo

**Data da análise:** 2026-08-16  
**Base analisada:** código no commit `91fa17a` e alterações locais não relacionadas preservadas  
**Escopo:** API, Core, testes de integração e roadmap. O Dashboard foi desconsiderado, conforme decisão do projeto.

## 1. Conclusão executiva

A API atual já é ampla: existem **133 rotas HTTP em 22 controllers**, e não 113 como informa `docs/API_ENDPOINTS.md`. Ela é suficiente para testar subsistemas isolados, mas **ainda não oferece um contrato completo para uma run jogável, recuperável e deterministicamente verificável de ponta a ponta**.

Os endpoints faltantes mais importantes não são novos CRUDs. São as fronteiras que transformam Run, Combate, mapa, recompensas e persistência em um único fluxo autoritativo:

1. progressão e resolução de nós do mapa;
2. criação de combate pertencente a uma run, derivando seed e revisão de conteúdo dela;
3. resolução atômica do resultado do combate na run;
4. comandos idempotentes com controle de concorrência por `expectedSequence`/`expectedStep`;
5. recuperação de runs e combates após reinício;
6. journal durável e verificação por replay real;
7. identidade única para instâncias de entidades em Combate, Status e Modifiers;
8. estado completo para reconexão do cliente, incluindo seleção de cartas, loja e preparação ativas.

A direção recomendada é manter temporariamente as rotas atuais como adaptadores de compatibilidade, mas fazer todas as mutações convergirem para **dois gateways de comandos**:

- `POST /api/v1/runs/{runId}/commands`
- `POST /api/v1/combats/{combatId}/commands`

Isso evita criar um endpoint diferente para cada regra futura e garante que mapa, relíquias, upgrades, TCG, IA e multiplayer usem a mesma disciplina determinística.

## 2. Princípios obrigatórios do contrato

### 2.1. Separar os três planos da API

| Plano | Responsabilidade | Pode alterar uma run ativa? |
|---|---|---:|
| Runtime | comandos e consultas de Run/Combate | Sim, somente por transições registradas |
| Conteúdo | catálogos e revisões imutáveis | Não |
| Administração/diagnóstico | publicar conteúdo, invalidar cache e simular | Não diretamente |

Hoje essas responsabilidades estão misturadas. Por exemplo, Status, Modifiers e Effect expõem mutações globais fora de Run/Combate, e alguns endpoints de reload são públicos.

### 2.2. Envelope obrigatório para comandos

Toda mutação autoritativa deve receber:

```json
{
  "commandId": "UUID determinístico ou fornecido pelo cliente",
  "expectedSequence": 17,
  "expectedStep": 42,
  "type": "BUY_SHOP_ITEM",
  "payload": {}
}
```

Regras:

- `commandId` torna retry idempotente;
- `expectedSequence` protege a versão persistida da run;
- `expectedStep` protege o estado lógico do combate;
- a mesma combinação de estado anterior, comando, seed, versão da engine e revisão de conteúdo deve produzir o mesmo estado e hash;
- conflito de versão retorna `409 Conflict`, não `400`;
- violação de regra de jogo retorna `422 Unprocessable Entity`;
- respostas de erro devem usar `ProblemDetails` com `code`, `correlationId`, `currentSequence` e `currentStep` quando aplicável.

Resposta mínima:

```json
{
  "commandId": "...",
  "sequence": 18,
  "step": 43,
  "previousStateHash": "...",
  "stateHash": "...",
  "state": {},
  "events": []
}
```

### 2.3. Identidade

- IDs de **definição de conteúdo** continuam textuais: `basic_attack`, `enemy_goblin`, `poison`.
- IDs de **instância em runtime** devem usar um único tipo opaco e determinístico em todos os módulos, preferencialmente `Guid`/UUID.
- `entityDefinitionId` e `entityInstanceId` não podem compartilhar o mesmo campo.
- aliases como `hero` ou `enemy_1` podem existir na apresentação, mas não devem ser a chave interna de Status/Modifiers.

### 2.4. Conteúdo imutável

`contentRevision` precisa identificar um manifesto completo: run definition, cartas, actions, entidades, recursos, fórmulas, pipelines, status, modifiers, gambits, shops, pools, preparações e regras de ativação. Atualmente, quando não fornecida, a revisão da run é derivada apenas de `RunDefinition`; quando fornecida, é aceita sem validação.

Uma run iniciada deve continuar lendo exatamente a revisão fixada. `reload` ou publicação de conteúdo novo só pode afetar runs futuras.

### 2.5. Journal não é feed de eventos

- **Journal:** comandos aceitos e resultados necessários para reexecução e auditoria; durável, ordenado por run/agregado.
- **Eventos:** projeção para UI, telemetria e integrações; podem ser filtrados, compactados e retransmitidos.

O `EventBus` atual usa sequência global em memória e grava no `IEventStore` de forma assíncrona e não transacional. O `EventsController` consulta somente a memória. Portanto ele não pode ser a fonte de verdade de replay ou recuperação.

## 3. Inventário real da API atual

| Controller | Base | Rotas | Papel predominante | Avaliação |
|---|---|---:|---|---|
| Action | `/api/action` | 9 | conteúdo/admin | Útil, mas CRUD mutável deve virar publicação versionada |
| CardSelection | `/api/run/{runId}/card-selection` | 4 | runtime | Implementado; falta consulta explícita/reconexão |
| CombatActivation | `/api/combat/{combatId}/activation` | 6 | runtime | Implementado; comandos não têm versão esperada |
| Combat | `/api/combat` | 11 | runtime/simulação | Não persiste; início não aceita seed nem vínculo com run |
| Config | `/api/config` | 6 | conteúdo/admin | Config ativa global pode divergir de runs já iniciadas |
| Damage | `/api/damage` | 4 | simulação/admin | `calculate` e `simulate` são aliases do mesmo método |
| Diagnostics | `/api/diagnostics` | 5 | diagnóstico/admin | Adequado como plano operacional |
| Effect | `/api/effect` | 4 | metadados/mutação direta | Não deve ser caminho autoritativo de gameplay |
| Entity | `/api/entity` | 7 | conteúdo/factory | Entidade criada é efêmera; não há store de runtime |
| Events | `/api/events` | 8 | projeção/SSE/admin | Histórico consultado é apenas o da sessão atual |
| Formula | `/api/formula` | 4 | conteúdo/simulação/admin | Avaliação deve ser marcada explicitamente como simulação |
| Gambit | `/api/gambits` | 7 | conteúdo/IA/admin | `reload` está sem proteção administrativa |
| GameResource | `/api/game-resources` | 8 | conteúdo/factory | `reload` e criação de pool estão no plano público |
| Health | `/api` | 1 | operação | Falta separar liveness, readiness e versão |
| MathExpression | `/api/math/expression` | 1 | simulação | Não autoritativo |
| Modifier | `/api/modifiers` | 8 | conteúdo/mutação direta | Estado singleton fora de Run/Combate; `reload` público |
| Operation | `/api/operation` | 3 | metadados | Adequado para tooling |
| Preparation | `/api/run/{runId}/preparation` | 2 | runtime | Implementado; falta consulta/reconexão |
| Resource | `/api/resource` | 3 | diagnóstico/admin | `stats` retorna `501`; `reload` não usa `AdminEndpoint` |
| Run | `/api/run` | 10 | runtime/persistência | Boa base; faltam mapa, lifecycle, journal e verificação |
| Shop | `/api/run/{runId}/shop` | 3 | runtime | Implementado; falta consulta/reconexão e venda futura |
| StatusEffect | `/api/status` | 19 | conteúdo/mutação direta/admin | IDs incompatíveis com Combate; há aliases redundantes |
| **Total** |  | **133** |  |  |

Existem **19 rotas protegidas** por `AdminEndpoint`. Porém pelo menos estas operações mutáveis continuam sem essa proteção:

- `POST /api/gambits/reload`
- `POST /api/modifiers/reload`
- `POST /api/game-resources/reload`
- `POST /api/resource/reload` — possui apenas a flag `AllowConfigReload`
- todas as mutações diretas de Status e Modifiers

## 4. Bloqueadores encontrados no contrato atual

### P0.1 — Combate iniciado a partir de uma run não pertence à run

`GameEngineClientSimulator.StartCombatAsync` envia `runId`, mas `StartCombatRequest` não possui essa propriedade. O JSON extra é ignorado e o controller chama o overload que cria uma seed aleatória própria. Consequências:

- não há vínculo persistido Run ↔ Combate;
- seed e `contentRevision` da run não são herdadas;
- o cliente não consegue retomar o encontro ativo;
- finalizar o combate não avança o nó nem gera recompensa atomicamente.

### P0.2 — Combate não é recuperável nem externamente reproduzível

O `CombatSystem` guarda combates apenas em memória. A API de início não aceita `seed`/`contentRevision`, e `CombatStateResponse` não expõe seed, step, revisão ou hash. Um reinício de processo perde todos os combates ativos.

### P0.3 — Atualização Run + Combate não é atômica

`CombatRunCoordinator` executa primeiro a ação no combate e depois consome a carta na run. Se a segunda persistência falhar, o combate já mudou e não há rollback transacional entre os dois agregados.

### P0.4 — Estado de reconexão da run está incompleto

`RunState` contém `CardSelections`, `Shops` e `Preparations`, mas `GET /api/run/{runId}/state` não os retorna. Após reconectar, o cliente não consegue reconstruir a tela do nó atual usando apenas a API.

### P0.5 — Status e Combate usam identidades incompatíveis

Combate usa `string EntityId`; os requests de Status exigem `Guid TargetId` e `Guid? SourceId`. Os testes e o simulador usam valores como `enemy_1`, que falham no model binding com `400`.

### P0.6 — Não há concorrência otimista nem idempotência

Os managers serializam operações com locks locais, mas nenhum endpoint recebe a versão esperada ou um ID de comando. Dois clientes podem enviar decisões com base no mesmo estado; ambos serão executados em sequência, mesmo que a segunda decisão tenha ficado obsoleta. Retry após timeout também pode duplicar compra, reroll ou ação.

### P0.7 — “Replay” atual verifica integridade, não semântica

`RunReplayVerifier` valida sequência e cadeia de hashes dos checkpoints. Ele não reexecuta comandos a partir do estado inicial. Além disso, journal e snapshot estão no mesmo arquivo e a retenção padrão remove checkpoints antigos após 100 versões. Isso impede replay completo de runs longas.

### P0.8 — Eventos não sobrevivem como contrato de leitura

O store durável recebe eventos em fire-and-forget; uma falha não invalida a transição. Após reinício, `GET /api/events` não lê o store e começa novamente a sequência global em zero. SSE também não fornece garantia durável de retomada.

### P0.9 — O simulador está divergente da API

- eventos: o helper espera array, mas a API responde `{ total, returned, lastSequence, events }`;
- actions: o helper ainda monta `actionType`/`powerId` em vez de usar prioritariamente `actionId`;
- Status: envia IDs textuais para campos `Guid`;
- Combate: envia `runId` que o request ignora;
- o helper não cobre mapa, lifecycle, deck completo, shuffle, checkpoints, restore, activation, intents, history, custos, ações disponíveis, end, SSE ou verificação.

### P1 — Contratos operacionais inconsistentes

- `GET /api/resource/stats` existe, mas sempre retorna `501 Not Implemented`;
- `GET /api/status/{targetId}` e `GET /api/status/{targetId}/active` são aliases;
- não existe `GET /api/status/definitions`, apenas consulta por ID;
- erros alternam entre string, `{ error }`, `{ error, details }` e respostas automáticas do model binding;
- não existe versão no path da API;
- `appsettings.json` contém uma chave administrativa de desenvolvimento conhecida; produção deve usar secrets e rotação;
- alguns DTOs de estado ocultam recursos, determinismo e campos exigidos pelos próprios testes de integração.

## 5. Endpoints P0 — necessários para o loop determinístico funcionar

Os paths abaixo são a arquitetura-alvo. As rotas atuais podem continuar existindo por uma janela de compatibilidade, delegando ao mesmo application service.

### 5.1. Operação e compatibilidade

| Método | Endpoint | Finalidade |
|---|---|---|
| GET | `/api/v1/health/live` | processo está vivo, sem dependências |
| GET | `/api/v1/health/ready` | conteúdo carregado, stores graváveis e serviços prontos |
| GET | `/api/v1/version` | versão da API, engine, schema e revisão padrão de conteúdo |
| GET | `/api/v1/capabilities` | features e versões de contrato suportadas pelo servidor |

`/api/health` pode permanecer como alias temporário de liveness.

### 5.2. Run

| Método | Endpoint | Finalidade |
|---|---|---|
| POST | `/api/v1/runs` | iniciar run com `modeId`, seed opcional e revisão resolvida pelo servidor |
| GET | `/api/v1/runs` | listar runs recuperáveis, paginadas e filtráveis por status/jogador |
| GET | `/api/v1/runs/{runId}` | estado completo e reconstruível da run |
| GET | `/api/v1/runs/{runId}/map` | mapa, visitados e transições legais |
| GET | `/api/v1/runs/{runId}/available-commands` | comandos legais no estado/sequence atual |
| POST | `/api/v1/runs/{runId}/commands` | única fronteira canônica para mutações da run |
| POST | `/api/v1/runs/{runId}/end` | adapter explícito para `END_RUN`, com resultado final |
| POST | `/api/v1/runs/{runId}/abandon` | registra abandono sem apagar histórico |

Comandos P0 de run:

- `ADVANCE_NODE`
- `RESOLVE_NODE`
- `DRAW_CARDS`
- `DISCARD_CARDS`
- `SHUFFLE_DISCARD`
- `PICK_CARD_REWARD`
- `REROLL_CARD_REWARD`
- `DECOMPOSE_CARD_REWARD`
- `BUY_SHOP_ITEM`
- `REROLL_SHOP`
- `APPLY_PREPARATION_OPTION`
- `RESOLVE_COMBAT`
- `END_RUN`
- `RESTORE_CHECKPOINT`

### 5.3. Recursos ativos de uma run

| Método | Endpoint | Finalidade |
|---|---|---|
| GET | `/api/v1/runs/{runId}/deck` | todas as zonas e instâncias de cartas |
| GET | `/api/v1/runs/{runId}/hand` | projeção leve da mão |
| GET | `/api/v1/runs/{runId}/card-selections` | seleções criadas na run |
| GET | `/api/v1/runs/{runId}/card-selections/{selectionId}` | seleção e opções atuais |
| GET | `/api/v1/runs/{runId}/shops` | lojas abertas na run |
| GET | `/api/v1/runs/{runId}/shops/{shopId}` | inventário e preços atuais |
| GET | `/api/v1/runs/{runId}/preparations` | preparações criadas na run |
| GET | `/api/v1/runs/{runId}/preparations/{preparationId}` | opções e grants aplicados |

Os `POST .../start` atuais devem deixar de criar recursos arbitrariamente em produção. O tipo do nó atual deve determinar qual seleção, loja ou preparação pode ser criada.

### 5.4. Encontros e Combate

| Método | Endpoint | Finalidade |
|---|---|---|
| POST | `/api/v1/runs/{runId}/encounters` | criar combate a partir do nó atual, seed derivada da run |
| GET | `/api/v1/runs/{runId}/encounters/current` | localizar encontro ativo após reconexão |
| GET | `/api/v1/combats/{combatId}` | estado completo, incluindo step, hash, recursos, fases e atores |
| GET | `/api/v1/combats/{combatId}/cards/evaluations` | mão, custos, alvos e prévias legais pelo fluxo canônico |
| GET | `/api/v1/combats/{combatId}/cards/{cardInstanceId}/evaluation` | inspeção detalhada de uma instância de carta |
| POST | `/api/v1/combats/{combatId}/commands` | executar ação, encerrar ativação, passar ou processar IA |
| GET | `/api/v1/combats/{combatId}/intents` | intents materializados no step atual |
| GET | `/api/v1/combats/{combatId}/history` | histórico de comandos/ações do combate |
| POST | `/api/v1/runs/{runId}/encounters/{combatId}/resolve` | commit atômico de vitória/derrota, loot e avanço da run |

Comandos P0 de combate:

- `EXECUTE_ACTION`
- `END_TURN`
- `START_ACTIVATION_CYCLE`
- `END_ACTIVATION`
- `ADVANCE_ACTIVATION`
- `PROCESS_AI_ACTIVATION`
- `CONCEDE`

O servidor deve validar que `combatId` pertence ao `runId` informado. Um combate standalone para ferramentas deve usar `/api/v1/simulations/combat`, não o mesmo endpoint autoritativo.

### 5.5. Journal, checkpoints e eventos

| Método | Endpoint | Finalidade |
|---|---|---|
| GET | `/api/v1/runs/{runId}/journal` | comandos duráveis por sequence/cursor |
| GET | `/api/v1/runs/{runId}/checkpoints` | metadados de checkpoints, sem carregar todos os estados |
| GET | `/api/v1/runs/{runId}/checkpoints/{sequence}` | checkpoint específico |
| POST | `/api/v1/runs/{runId}/verify` | reexecutar comandos e comparar hashes |
| GET | `/api/v1/runs/{runId}/events` | projeção paginada por cursor local à run |
| GET | `/api/v1/runs/{runId}/events/stream` | SSE retomável com `Last-Event-ID` |
| GET | `/api/v1/combats/{combatId}/journal` | comandos duráveis do combate |
| POST | `/api/v1/combats/{combatId}/verify` | replay do combate e comparação de hash |

Journal deve ter retenção independente dos snapshots. Checkpoints podem ser compactados; comandos necessários ao replay não.

## 6. Endpoints P1 — conteúdo e módulos do MVP ainda incompletos

### 6.1. Content API unificada

Em vez de criar um controller CRUD mutável para cada tipo, usar um catálogo versionado:

| Método | Endpoint | Finalidade |
|---|---|---|
| GET | `/api/v1/content/revisions` | revisões publicadas disponíveis |
| GET | `/api/v1/content/revisions/{revision}` | manifesto e hashes de artefatos |
| GET | `/api/v1/content/{kind}` | catálogo paginado por tipo, tag e revisão |
| GET | `/api/v1/content/{kind}/{definitionId}` | definição específica |
| POST | `/api/v1/content/validate` | valida bundle sem publicar |
| POST | `/api/v1/admin/content/drafts` | criar draft administrativo |
| PUT | `/api/v1/admin/content/drafts/{draftId}` | atualizar draft |
| POST | `/api/v1/admin/content/drafts/{draftId}/validate` | validação completa e referências cruzadas |
| POST | `/api/v1/admin/content/drafts/{draftId}/publish` | publicar nova revisão imutável |

Valores de `kind` já necessários:

- `actions`, `cards`, `entities`, `resources`, `formulas`, `pipelines`;
- `status-effects`, `modifiers`, `gambits`;
- `runs`, `card-pools`, `card-selections`, `shops`, `preparations`;
- `activation-rules`, `phase-sequences`.

Valores futuros que já devem caber no manifesto:

- `races`, `powers`, `companions`, `enemies`, `decks`;
- `relics`, `card-upgrades`, `modes`, `daily-challenges`;
- `boards`, `zones`, `keywords`. Políticas de reação pertencem às regras de
  combate e não formam um catálogo ou endpoint paralelo.

### 6.2. Mapa e eventos de run

O modelo já carrega `MapNodes` e define `CurrentNodeId`, mas não implementa transições. O mínimo é coberto por:

- `GET /api/v1/runs/{runId}/map`;
- `GET /api/v1/runs/{runId}/available-commands`;
- comando `ADVANCE_NODE` com `targetNodeId`;
- comando `RESOLVE_NODE` com o resultado produzido pelo submódulo correto.

Eventos narrativos devem ser conteúdo (`kind=run-events`) e seu estado ativo deve viver na run. Escolhas usam `RUN_EVENT_CHOICE` pelo gateway de comandos.

### 6.3. Relíquias

Consultas:

- `GET /api/v1/content/relics`
- `GET /api/v1/content/relics/{relicId}`
- `GET /api/v1/runs/{runId}/relics`

Aquisição, remoção e triggers são comandos/eventos de run; não criar `POST /relic/apply` fora do agregado.

### 6.4. Upgrade e instâncias de cartas

O deck atual armazena apenas IDs textuais, o que não representa duas cópias da mesma carta com upgrades diferentes. Antes do endpoint, introduzir `cardInstanceId`, `definitionId` e deltas imutáveis.

Consultas:

- `GET /api/v1/runs/{runId}/cards/{cardInstanceId}`
- `GET /api/v1/runs/{runId}/cards/{cardInstanceId}/upgrade-options`

Mutação: comando `UPGRADE_CARD` durante um nó que permita a operação.

### 6.5. Raças, poderes, companions, inimigos e decks

São majoritariamente conteúdo e devem usar `/content/{kind}`. Algumas rotas do roadmap devem ser reinterpretadas:

| Necessidade | Endpoint recomendado |
|---|---|
| árvore de poder | `GET /api/v1/content/powers/{powerId}/tree` |
| análise de combo | `POST /api/v1/simulations/power-combos` |
| bônus/deck inicial de raça | campos/links de `GET /api/v1/content/races/{raceId}` |
| gambits/poderes de companion | links na definição do companion |
| inimigos por tier | `GET /api/v1/content/enemies?tier=elite` |
| intent do inimigo ativo | `GET /api/v1/combats/{combatId}/intents` |
| definição de deck modular | `GET /api/v1/content/decks/{deckId}` |

`GET /api/enemies/{name}/intent` do roadmap mistura definição com estado runtime e não deve ser implementado dessa forma.

### 6.6. Save/load

Como a run já é persistida automaticamente a cada transição, `POST /api/save/{runId}` não deve ser o fluxo principal. O necessário é:

| Método | Endpoint | Finalidade |
|---|---|---|
| GET | `/api/v1/runs?status=active` | listar e retomar runs |
| GET | `/api/v1/runs/{runId}/export` | pacote portátil com manifesto, journal e checkpoints |
| POST | `/api/v1/runs/import` | importar e validar pacote sem sobrescrever histórico |
| POST | `/api/v1/runs/{runId}/archive` | ocultar run encerrada sem apagá-la |

Slots nomeados de save só devem ser adicionados se o produto realmente permitir múltiplas ramificações manuais.

## 7. Endpoints P2 — módulos futuros já previstos

### 7.1. Meta-progressão e perfis

| Método | Endpoint | Finalidade |
|---|---|---|
| GET | `/api/v1/profiles/{playerId}` | perfil e revisão |
| GET | `/api/v1/profiles/{playerId}/stats` | estatísticas derivadas |
| GET | `/api/v1/profiles/{playerId}/unlocks` | desbloqueios conquistados |
| GET | `/api/v1/profiles/{playerId}/achievements` | progresso de achievements |
| GET | `/api/v1/profiles/{playerId}/runs` | histórico paginado de runs |

Não expor `POST .../unlock` para clientes comuns. Unlocks devem resultar de eventos autoritativos de run ou de uma operação administrativa auditada.

### 7.2. Modes, seeds e daily challenge

| Método | Endpoint | Finalidade |
|---|---|---|
| GET | `/api/v1/content/modes` | modos publicados |
| GET | `/api/v1/content/modes/{modeId}` | regras e restrições |
| POST | `/api/v1/runs` | inicia normal/custom usando `modeId` e seed opcional |
| GET | `/api/v1/challenges/daily/current` | manifesto assinado do desafio diário |
| POST | `/api/v1/challenges/daily/current/attempts` | inicia tentativa vinculada ao perfil |
| POST | `/api/v1/challenges/daily/current/submissions` | envia journal/hash para verificação |
| GET | `/api/v1/challenges/daily/current/leaderboard` | ranking verificado |

`POST /api/seed/generate` é redundante: quando a seed for omitida, `POST /runs` deve gerar e devolver a seed escolhida. `POST /seed/run` também duplica o início de run.

### 7.3. Timeline, branches e theory crafting

Em um modelo append-only, “undo” não apaga o futuro. Ele registra restauração ou cria uma branch.

| Método | Endpoint | Finalidade |
|---|---|---|
| GET | `/api/v1/runs/{runId}/timeline` | visão combinada de journal/checkpoints |
| POST | `/api/v1/runs/{runId}/branches` | cria branch a partir de uma sequence |
| GET | `/api/v1/runs/{runId}/branches` | lista branches |
| POST | `/api/v1/simulations` | inicia simulação sem commit no agregado original |
| GET | `/api/v1/simulations/{simulationId}` | status |
| GET | `/api/v1/simulations/{simulationId}/result` | estado final e diferenças |

Para Combate, usar o mesmo padrão. `redo` só existe dentro de uma branch que ainda referencia o futuro anterior.

### 7.4. Suporte a TCG

Permanentes, zonas, fases, mulligan e reação não precisam de dezenas de endpoints mutáveis. O estado de combate passa a incluir `board`, `zones`, `phase`, `priority` e `stack`; as ações continuam no command gateway.

Consultas adicionais:

- `GET /api/v1/combats/{combatId}/cards/evaluations`
- `GET /api/v1/combats/{combatId}/cards/{cardInstanceId}/evaluation`
- `GET /api/v1/combats/{combatId}/stack`

Legalidade, custo e alvos não possuem projeções paralelas por `actionId`: são
resultado da avaliação da instância de carta, usando a revisão fixada na run e
o mesmo compilador/executor de `PLAY_CARD`.

Comandos futuros:

- `MULLIGAN`, `PLAY_CARD`, `DECLARE_ATTACKERS`, `DECLARE_BLOCKERS`;
- `PASS_PRIORITY`, `RESPOND`, `RESOLVE_STACK`;
- `ADVANCE_PHASE`, quando não for automático.

### 7.5. Multiplayer, se entrar no escopo

O Core já usa `actorId`, mas ainda não há ownership. Antes de multiplayer, adicionar `controllerId`, `playerId` e `source` ao comando e validar atores controláveis. Só então seriam necessários endpoints de sessão/lobby. Eles não são requisito do MVP atual e não devem condicionar o design do Core.

## 8. Endpoints que devem permanecer somente como simulação ou administração

Estas operações são úteis para tooling, testes e criação de conteúdo, mas não devem alterar uma partida autoritativa:

- `/api/damage/calculate` e `/api/damage/simulate`;
- `/api/math/expression/evaluate`;
- `/api/formula/evaluate`;
- `/api/gambits/decide`;
- `/api/entity/create`;
- `/api/game-resources/create-pool` e `/validate-cost`;
- `/api/effect/apply`;
- mutações diretas em `/api/status/*` e `/api/modifiers/*`;
- `/api/combat/{combatId}/auto-play`.

Destino recomendado: `/api/v1/simulations/*` ou `/api/v1/admin/*`, com estado de entrada explícito e resultado sem commit. Para testes internos, esses endpoints podem continuar ativos por feature flag.

## 9. Catálogo completo das 133 rotas atuais

Legenda: **[admin]** exige `X-Admin-Key`; **[diagnóstico]** não deve ser usada como comando de gameplay; **[alias]** compartilha implementação com outra rota.

### Action — 9

- `GET /api/action`
- `GET /api/action/{actionId}`
- `GET /api/action/by-type/{actionType}`
- `GET /api/action/by-tag/{tag}`
- `POST /api/action/validate`
- `POST /api/action/reload` **[admin]**
- `POST /api/action` **[admin]**
- `PUT /api/action/{actionId}` **[admin]**
- `DELETE /api/action/{actionId}` **[admin]**

### CardSelection — 4

- `POST /api/run/{runId}/card-selection/start`
- `POST /api/run/{runId}/card-selection/{selectionInstanceId}/pick`
- `POST /api/run/{runId}/card-selection/{selectionInstanceId}/reroll`
- `POST /api/run/{runId}/card-selection/{selectionInstanceId}/decompose/{cardId}`

### CombatActivation — 6

- `POST /api/combat/{combatId}/activation/start`
- `GET /api/combat/{combatId}/activation/state`
- `GET /api/combat/{combatId}/activation/intents`
- `POST /api/combat/{combatId}/activation/end`
- `POST /api/combat/{combatId}/activation/advance`
- `POST /api/combat/{combatId}/activation/process-ai`

### Combat — 11

- `POST /api/combat/start`
- `POST /api/combat/{combatId}/action`
- `POST /api/combat/{combatId}/end-turn`
- `POST /api/combat/{combatId}/process-ai-turns`
- `GET /api/combat/{combatId}/state`
- `GET /api/combat/{combatId}/history`
- `POST /api/combat/{combatId}/end`
- `POST /api/combat/{combatId}/auto-play` **[diagnóstico]**

### Config — 6

- `GET /api/config`
- `GET /api/config/current`
- `GET /api/config/{name}`
- `GET /api/config/{name}/chain`
- `POST /api/config/{name}/validate`
- `POST /api/config/{name}/load` **[admin]**

### Damage — 4

- `POST /api/damage/calculate` **[diagnóstico]**
- `POST /api/damage/simulate` **[diagnóstico, alias]**
- `GET /api/damage/pipeline/config`
- `POST /api/damage/pipeline/reload` **[admin]**

### Diagnostics — 5

- `GET /api/diagnostics/cache/stats`
- `GET /api/diagnostics/cache/stats/{cacheName}`
- `POST /api/diagnostics/cache/invalidate/{cacheName}` **[admin]**
- `POST /api/diagnostics/cache/invalidate/{cacheName}/key` **[admin]**
- `GET /api/diagnostics/health/cache`

### Effect — 4

- `GET /api/effect/types`
- `GET /api/effect/targets`
- `GET /api/effect/scopes`
- `POST /api/effect/apply` **[diagnóstico/mutação direta]**

### Entity — 7

- `GET /api/entity/definitions`
- `GET /api/entity/definitions/{definitionId}`
- `POST /api/entity/create` **[factory efêmera]**
- `POST /api/entity/definitions/validate`
- `POST /api/entity/definitions` **[admin]**
- `PUT /api/entity/definitions/{definitionId}` **[admin]**
- `DELETE /api/entity/definitions/{definitionId}` **[admin]**

### Events — 8

- `GET /api/events`
- `GET /api/combat/{combatId}/events`
- `GET /api/events/stream`
- `GET /api/combat/{combatId}/events/stream`
- `GET /api/events/{eventId}`
- `GET /api/events/categories`
- `GET /api/events/severities`
- `DELETE /api/events` **[admin, somente Development]**

### Formula — 4

- `GET /api/formula`
- `GET /api/formula/{name}`
- `POST /api/formula/evaluate` **[diagnóstico]**
- `POST /api/formula/reload` **[admin]**

### Gambit — 7

- `GET /api/gambits`
- `GET /api/gambits/{gambitId}`
- `POST /api/gambits/reload`
- `POST /api/gambits/decide` **[diagnóstico]**
- `POST /api/gambits/definitions` **[admin]**
- `PUT /api/gambits/definitions/{gambitId}` **[admin]**
- `DELETE /api/gambits/definitions/{gambitId}` **[admin]**

### GameResource — 8

- `GET /api/game-resources`
- `GET /api/game-resources/{resourceId}`
- `GET /api/game-resources/by-category/{category}`
- `GET /api/game-resources/by-tag/{tag}`
- `POST /api/game-resources/validate`
- `POST /api/game-resources/reload`
- `POST /api/game-resources/create-pool` **[factory efêmera]**
- `POST /api/game-resources/validate-cost` **[diagnóstico]**

### Health — 1

- `GET /api/health`

### MathExpression — 1

- `POST /api/math/expression/evaluate` **[diagnóstico]**

### Modifier — 8

- `GET /api/modifiers`
- `GET /api/modifiers/{modifierId}`
- `POST /api/modifiers/reload`
- `POST /api/modifiers/apply` **[mutação direta]**
- `GET /api/modifiers/active/{ownerId}`
- `GET /api/modifiers/active/{ownerId}/pipeline`
- `DELETE /api/modifiers/active/{ownerId}/{instanceId}` **[mutação direta]**
- `POST /api/modifiers/active/{ownerId}/tick` **[mutação direta]**

### Operation — 3

- `GET /api/operation`
- `GET /api/operation/{name}`
- `GET /api/operation/categories`

### Preparation — 2

- `POST /api/run/{runId}/preparation/start`
- `POST /api/run/{runId}/preparation/{preparationInstanceId}/apply/{optionId}`

### Resource — 3

- `GET /api/resource/origins`
- `POST /api/resource/reload`
- `GET /api/resource/stats` **[retorna 501]**

### Run — 10

- `POST /api/run/start`
- `GET /api/run/{runId}/state`
- `GET /api/run/{runId}/deck`
- `GET /api/run/{runId}/hand`
- `POST /api/run/{runId}/draw`
- `POST /api/run/{runId}/discard`
- `POST /api/run/{runId}/shuffle`
- `GET /api/run/{runId}/snapshots`
- `GET /api/run/{runId}/snapshots/{sequence}`
- `POST /api/run/{runId}/undo`

### Shop — 3

- `POST /api/run/{runId}/shop/open`
- `POST /api/run/{runId}/shop/{shopInstanceId}/buy/{itemId}`
- `POST /api/run/{runId}/shop/{shopInstanceId}/reroll`

### StatusEffect — 19

- `POST /api/status/apply` **[mutação direta]**
- `DELETE /api/status/remove` **[mutação direta]**
- `DELETE /api/status/{targetId}/status/{statusId}` **[mutação direta]**
- `DELETE /api/status/{targetId}/all` **[mutação direta]**
- `POST /api/status/add-stacks` **[mutação direta]**
- `POST /api/status/remove-stacks` **[mutação direta]**
- `PUT /api/status/{targetId}/status/{instanceId}/duration` **[mutação direta]**
- `GET /api/status/{targetId}`
- `GET /api/status/{targetId}/active` **[alias]**
- `GET /api/status/{targetId}/status/{instanceId}`
- `GET /api/status/{targetId}/has/{type}`
- `GET /api/status/{targetId}/stacks/{type}`
- `POST /api/status/process` **[mutação direta]**
- `POST /api/status/{targetId}/tick` **[mutação direta]**
- `GET /api/status/{targetId}/modifiers`
- `POST /api/status/definitions` **[admin]**
- `PUT /api/status/definitions/{statusId}` **[admin]**
- `DELETE /api/status/definitions/{statusId}` **[admin]**
- `GET /api/status/definitions/{statusId}`

## 10. Cobertura necessária do cliente de engine

O `GameEngineClientSimulator` deve deixar de ser um conjunto manual de chamadas `JsonElement` e passar a validar o contrato público. Ordem recomendada:

1. criar DTOs versionados de request/response compartilhados ou gerar cliente a partir do OpenAPI;
2. corrigir `StartCombat` para usar encontro pertencente à run;
3. trocar execução por `actionId` e comandos tipados;
4. corrigir o envelope de eventos;
5. unificar IDs de instância;
6. cobrir mapa, available commands, lifecycle, estado completo, journal e verify;
7. adicionar testes de retry idempotente, `409` por versão obsoleta e replay após restart;
8. manter testes separados para endpoints de simulação/admin.

## 11. Ordem de implementação recomendada

1. **Contrato comum:** versionamento, `ProblemDetails`, `commandId`, sequence/step e hashes.
2. **Identidade e DTOs:** separar definition IDs de instance IDs e corrigir Status/Combate.
3. **Content manifest:** calcular e fixar a revisão completa da run.
4. **Run read model:** retornar todos os subestados e listar runs persistidas.
5. **Mapa:** `map`, `available-commands`, `ADVANCE_NODE` e `RESOLVE_NODE`.
6. **Run-owned combat:** criar, persistir, recuperar e resolver encontro atomicamente.
7. **Command gateways:** migrar mutações existentes de run e combate.
8. **Journal/replay:** journal sem truncamento, reexecução real e endpoints de verificação.
9. **Eventos duráveis:** projeções por run/combate e SSE retomável.
10. **Content/Admin API:** drafts e publicação imutável; retirar reloads públicos.
11. **Módulos P1:** relíquias, upgrades e catálogos de conteúdo.
12. **Módulos P2:** meta, daily, branches/simulações e extensões TCG.

## 12. Critérios de aceite arquiteturais

O contrato estará alinhado à filosofia do projeto quando:

- repetir uma run com seed, revisão, versão da engine e journal iguais produzir o mesmo hash final;
- retry do mesmo `commandId` não aplicar a operação duas vezes;
- comando com sequence/step obsoleto retornar `409` sem mutação;
- reiniciar a API no meio de um combate permitir retomar exatamente o mesmo estado;
- nenhum endpoint público conseguir aplicar Status, Modifier ou Effect fora de uma transição autoritativa;
- uma run antiga continuar usando a revisão de conteúdo com que começou após novas publicações;
- o cliente reconstruir qualquer tela somente com o estado retornado e os eventos posteriores ao cursor;
- journal e checkpoints detectarem adulteração e o replay reexecutar comandos, não apenas comparar snapshots;
- todos os endpoints administrativos estiverem isolados, autenticados e auditados;
- OpenAPI e testes de contrato impedirem nova divergência entre controller, documentação e cliente.

## 13. Evidência de testes

Foi iniciada a suíte `API.Tests` durante esta análise. Ela compilou, mas apresentou diversas falhas de integração coerentes com os contratos divergentes descritos acima: Status retornando `400` para IDs textuais, execução de actions retornando `400`, fórmulas retornando `500`, DTO de combate sem os recursos esperados e preparações inválidas. A execução também deixou de progredir e precisou ser interrompida. Isso não altera o inventário de rotas, mas confirma que “endpoint existente” não equivale a “fluxo integrado funcional”.

## 14. Documentação que precisa ser reconciliada depois

- `docs/API_ENDPOINTS.md` registra 113 endpoints e está desatualizado;
- `docs/api/endpoints.md` não inclui todas as rotas atuais, especialmente snapshots/undo, CRUDs administrativos e diagnósticos;
- `docs/roadmap/phases/phase-3.md` contém exemplos antigos como `/api/cardselection/{runId}/offers` e `/api/shop/{runId}/buy`;
- fases 5 e 6 ainda tratam persistência e seed como totalmente não implementadas, embora já existam snapshots, journal básico, seed e `contentRevision`;
- `docs/roadmap/strategic.md` mistura fases antigas e atuais e deve apontar para este contrato-alvo antes de orientar novas implementações.

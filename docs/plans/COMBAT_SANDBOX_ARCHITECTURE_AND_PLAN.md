# Arquitetura e plano — Combat Sandbox configurável

**Data da análise:** 2026-08-17  
**Base analisada:** `eb7b172` e contratos/código presentes no diretório de trabalho  
**Escopo:** evolução da engine HeroScript para operar cenários de combate, replay, timeline, branches, simulações e hot reload configuráveis por conteúdo JSON. A Godot permanece responsável somente por apresentação e input.

## 1. Objetivo e princípios

O `combat_sandbox` não é uma implementação paralela de combate. É um `GameMode` que usa o mesmo motor autoritativo do jogo, mas habilita capacidades de inspeção e experimentação determinadas por JSON.

Princípios que orientam o desenho:

1. **A engine é a única fonte de verdade.** A Godot envia intenção pela REST API e renderiza snapshots retornados pela engine. Ela nunca calcula regras de jogo.
2. **Todo comportamento de jogo é configurado por conteúdo versionado.** Um setting é um conjunto de recursos JSON efetivos; o `GameMode` os compõe por referência.
3. **Dados publicados e históricos são imutáveis.** Uma mudança em JSON cria uma revisão nova; snapshots e journal são append-only.
4. **Flexibilidade não elimina determinismo.** Hot reload pode alterar comandos futuros, mas a mudança de revisão precisa ser uma transição registrada e reproduzível.
5. **Visualizar o passado não o modifica.** Continuar a partir do passado sempre cria uma branch; a run e o combate de origem permanecem intactos.
6. **Capacidades pertencem ao modo.** Sandbox, theorycraft, modding e jogo publicado podem ativar políticas diferentes sem endpoints de mutação arbitrária.

### 1.1 Estado de implementação em 2026-09-01

As fases de engine deste plano foram implementadas em ordem e protegidas por
testes de contrato e integração. A interface visual da Godot permanece uma etapa
do projeto cliente, mas os contratos necessários já estão disponíveis.

| Fase | Estado | Entrega principal |
| --- | --- | --- |
| 0. Contrato e baseline | Concluída | `a79bb71` |
| 1. Configuração composta | Concluída | `f2f8c99` |
| 2. Revisões e hot reload | Concluída | `3c71425`, `389cf02`, `cb68116` |
| 3. Cenários | Concluída | `1416809` |
| 4. Snapshots | Concluída | `ad61cee` |
| 5. Timeline | Concluída | `3071470` |
| 6. Branches de combate | Concluída | `f8e15ae` |
| 7. Simulações | Concluída | `4e5a095` |
| 8. Contrato para Godot | Engine concluída; UI externa pendente | `0e7de9f` |
| 9. Regressão e operabilidade | Concluída e contínua | `a443f84` a `188eb84` |

O endurecimento transversal posterior consolidou imutabilidade profunda,
conteúdo revisionado, caches coordenados, matemática central, gateway único de
comandos, validação semântica, telemetria correlacionada, Problem Details, DI e
concorrência. O contrato vigente está em
`docs/architecture/cross-cutting-systems.md`.

### 1.2 Fechamento do loop de combate em 2026-09-02

O segundo ciclo implementou as decisões específicas do demo de combate sem
criar outro motor para o sandbox:

| Decisão | Implementação atual |
| --- | --- |
| Resolução automática | `ToNextPlayerInput`; ação do jogador, ativações inimigas e retorno ao próximo input são um único lote atômico. |
| Ordem | Snapshot imutável por rodada, com viés de empate configurável. |
| Orçamento | `combat_sandbox` usa energia/custos; `combat_sandbox_fixed_actions` ignora custos e limita ações por ativação. |
| Deck e recursos | Compra, descarte/retenção, embaralhamento e refresh são políticas do `CombatRules`. |
| IA e intents | Gambit do modo decide a ação e publica o intent derivado da mesma decisão determinística. |
| Status | Gatilho, redução de duração e prioridade pertencem à definição JSON do status e são executados nos boundaries configurados. |
| Resultado | Avaliado depois da ação atual, com empate configurável; conclusão do encontro exige `ManualAck`. |
| Apresentação | Cada comando persiste frames `FullSnapshots` ou `CompactWithSnapshotLookup`, recuperáveis por `commandId`. |
| Autoridade HTTP | Toda run omissa usa o modo `standard`; criação e mutação de combate existem somente nos gateways canônicos. |

Os marcos desse ciclo vão de `75a96ad` a `4cf7b0d`. A suíte de fechamento tem
1.515 testes (`Core.Tests`: 1.295; `API.Tests`: 220), incluindo sandbox real,
replay semântico, timeline, branches, os dois orçamentos e os dois formatos de
frame.

Opções mantidas no modelo para evolução, mas deliberadamente não executáveis,
falham na resolução do modo e produzem warning de conteúdo: reações/prioridade,
resolução automática do encontro e boundaries de resultado diferentes de
`AfterCurrentAction`. A navegação por várias fases interativas dentro da mesma
ativação também permanece fora do primeiro demo; o runtime atual usa a sequência
`classic-style`, com exatamente uma fase de cada papel semântico. Uma regra de
combate que selecione um grafo multifase é rejeitada na validação de conteúdo,
em vez de ter fases ignoradas silenciosamente. Esses itens não possuem fallback
silencioso nem rota paralela parcialmente autoritativa.

## 2. Estado da engine na análise original (2026-08-17)

### 2.1 Capacidades presentes na baseline

| Área | Evidência atual | Avaliação para o objetivo |
|---|---|---|
| Estado determinístico | `DeterministicContext`, seed, step, hashes canônicos e IDs determinísticos | Base adequada |
| Conteúdo imutável | `ContentManifest`, drafts, validação e publicação por hash | Base adequada para revisões; falta ativação runtime por run/modo |
| Modo de jogo | `GameModeDefinition` é recurso JSON do catálogo `modes` | Estrutura inicial; ainda não compõe políticas |
| Comandos autoritativos | `POST /api/v1/runs/{runId}/commands` e `POST /api/v1/combats/{combatId}/commands` usam `commandId`, sequence e step | Adequado e deve permanecer como única fronteira de gameplay |
| Estado recuperável | snapshots e journal duráveis são escritos a cada transição | Adequado para timeline e replay |
| Replay | `RunSemanticReplayService` reexecuta journal e compara hashes | Base adequada; disponibilidade ainda não é política de modo |
| Leitura de combate | estado, ações disponíveis/legais, alvos legais, custos, stack e histórico | Boa base para Godot renderizar sem regra local |
| Instâncias de cartas | `DeckState.CardInstances` e endpoints de carta/upgrades | Adequado; clientes devem usar `cardInstanceId` |
| Timeline de run | journal, checkpoints e `GET /api/v1/runs/{runId}/timeline` | Parcial: não há timeline específica de combate, agrupamento por turno ou projeção de UI |
| Branches | branch determinística de uma sequência persistida | Parcial: branches de combate ativo são proibidas |
| Simulações | simulação cria branch isolada e retorna hash final | Parcial: não executa fluxo de combate |

### 2.2 Lacunas confirmadas na baseline

1. `GameModeDefinition` contém somente `modeId`, `runDefinitionId`, `allowCustomSeed` e um mapa genérico de `rules`. O `RunManager` efetivamente usa apenas a seleção da run e a regra de seed. O modo ainda não escolhe fluxo, regras de combate, pools, replay, timeline, hot reload ou capacidades.
2. A publicação de conteúdo já cria bundles imutáveis por hash, e o reload administrativo troca a configuração global. Porém não existe uma ativação de revisão para uma run ativa nem uma entrada de journal que delimite quando uma regra nova começou a valer.
3. Uma run parte de uma `RunDefinition` fixa. Não existe um documento de cenário capaz de declarar deck, upgrades, inimigos, estado inicial e capacidades permitidas pelo modo.
4. A timeline existente é de run, retorna principalmente metadados de checkpoint e não tem agrupamento por turno. O endpoint de checkpoint devolve o modelo interno, não um read model de timeline desenhado para cliente.
5. `RunBranchTransitions.Create` rejeita estado com `ActiveEncounterId`. Portanto não é possível derivar alternativas durante um combate, que é justamente o caso do sandbox.
6. `RunSimulationService` rejeita iniciar/resolver encontros e executa somente o processador de comandos de run; não pode executar ações de combate via o coordenador autoritativo.
7. A árvore de branches não existe. A API lista somente filhas diretas da run consultada, não a linhagem completa nem os combates derivados.
8. A especificação OpenAPI pública cobre a fundação v1, mas ainda não descreve timeline, branches, checkpoints, cenários e a maior parte das leituras de combate que a API já fornece.

### 2.3 Mapa de aderência na baseline

| Etapa proposta | Situação | Justificativa |
|---|---|---|
| 1. Modo e configuração composta | Não iniciada | Há catálogo de modos, mas não há políticas tipadas/referências compostas aplicadas pelo runtime |
| 2. Conteúdo/hot reload versionado | Parcial | Revisões imutáveis e drafts existem; ativação controlada para runs/modos não |
| 3. Cenários de combate | Não iniciada | A API cria run/encontro, porém não recebe declaração de cenário validada |
| 4. Snapshot de inspeção | Parcial | Estado e leituras legais existem; falta snapshot consolidado de sandbox e projeções históricas |
| 5. Timeline configurável | Parcial | Journal/checkpoints e timeline de run existem; falta escopo de combate, turnos e política |
| 6. Branches em combate | Não iniciada | Serviço atual recusa encontro ativo |
| 7. Simulações de combate | Não iniciada | Simulações persistidas existem, mas não usam o coordenador de combate |
| 8. Integração Godot de sandbox | Não iniciada na engine | Há um protótipo mínimo externo; não há contrato de cenário/timeline/árvore para ele consumir |
| 9. Garantias e testes | Parcial | Existem testes de determinismo, manifesto, replay e branches básicos; faltam os critérios desta arquitetura |

## 3. Arquitetura-alvo

### 3.1 Configuração composta

O `GameMode` é a raiz de composição, não um arquivo monolítico. Cada referência aponta para outro recurso JSON, resolvido dentro de uma mesma revisão de conteúdo.

```text
Configuração efetiva (cadeia de settings)
└── ContentRevision / ContentManifest
    └── GameModeDefinition
        ├── FlowRulesDefinition
        ├── CombatRulesDefinition
        ├── DamagePipelineDefinition
        ├── CardPoolDefinition(s)
        ├── EnemyPoolDefinition(s)
        ├── ActivationRulesDefinition
        ├── ReplayPolicyDefinition
        ├── TimelinePolicyDefinition
        ├── ContentBindingPolicyDefinition
        └── CapabilityPolicyDefinition
```

O modo deve ser expandido para referências tipadas. O uso de `JsonElement` pode continuar somente para metadados de extensão, nunca como a única forma de uma regra que altera fluxo.

Exemplo de composição:

```json
{
  "modeId": "combat_sandbox",
  "runDefinitionId": "sandbox_run",
  "allowCustomSeed": true,
  "flowRulesId": "sandbox_flow",
  "combatRulesId": "standard_combat",
  "damagePipelineId": "default_damage",
  "cardPoolIds": ["all_cards"],
  "enemyPoolIds": ["all_enemies"],
  "replayPolicyId": "sandbox_replay",
  "timelinePolicyId": "sandbox_timeline",
  "contentBindingPolicyId": "development_versioned",
  "capabilityPolicyId": "theorycraft_tools"
}
```

O resolvedor de modo deve validar no início da run que todos os recursos existem, pertencem à mesma revisão e são compatíveis entre si. O resultado resolvido é fixado no snapshot da run.

### 3.2 Replay, journal e timeline como políticas do modo

O journal mínimo deve continuar obrigatório para runs persistidas: é a base de recuperação, idempotência e auditoria. O `ReplayPolicy` determina o que pode ser exposto e executado, não se a engine pode esquecer uma transição aceita.

```json
{
  "replayPolicyId": "sandbox_replay",
  "journalEnabled": true,
  "semanticVerification": true,
  "timelineAccess": "full",
  "allowHistoricalInspection": true,
  "allowForkFromHistory": true,
  "allowHeadRestore": false,
  "retention": "all_commands"
}
```

Políticas possíveis:

| Política | Jogo publicado | Sandbox/theorycraft | Desenvolvimento/mod |
|---|---:|---:|---:|
| Journal interno | Sim | Sim | Sim |
| Timeline para cliente | Opcional/resumida | Completa | Completa |
| Replay semântico | Auditoria | Exposto | Exposto |
| Branch de histórico | Não | Sim | Sim |
| Restore da cabeça | Não | Não por padrão | Opcional e explícito |

### 3.3 Conteúdo e hot reload sem quebrar determinismo

Há duas operações distintas:

1. **Autoria/publicação:** uma mudança em arquivo JSON é capturada, validada e publicada como revisão nova, imutável e endereçada por hash.
2. **Ativação:** uma revisão publicada passa a ser utilizada por novas runs, por um modo ou, se a política permitir, por uma run ativa.

Uma run em modo de desenvolvimento não pode consultar conteúdo mutável sem registrar a revisão em vigor. A troca precisa ocorrer em uma fronteira explícita — recomendada: antes do próximo comando aceito.

```text
Sequência 18: revision A
ACTIVATE_CONTENT_REVISION(B)
Sequência 19: revision B
```

A ativação é um comando de run somente quando `ContentBindingPolicy` permite. Ela altera o `ContentManifest` efetivo da run e gera journal/snapshot. O replay usa A até a entrada de ativação e B depois dela.

Exemplo de política:

```json
{
  "contentBindingPolicyId": "development_versioned",
  "newRuns": "latest_published",
  "activeRuns": "allow_versioned_activation",
  "activationBoundary": "next_command",
  "retainHistoricalRevisions": true
}
```

Não haverá hot reload silencioso de estado ativo. Isso permitiria que a mesma sequência de comandos tivesse resultados diferentes sem uma transição observável.

### 3.4 Cenário de combate

`CombatScenarioDefinition` é uma declaração JSON de entradas, não um snapshot editável enviado pela Godot. Ela referencia definições publicadas e expõe somente overrides que a política de capacidade autoriza.

```json
{
  "schemaVersion": 1,
  "modeId": "combat_sandbox",
  "contentRevision": "<hash-publicado>",
  "seed": 12345,
  "attemptKey": "poison-fireball-01",
  "hero": { "entityDefinitionId": "player_warrior" },
  "deck": [
    { "definitionId": "basic_attack" },
    { "definitionId": "fireball", "upgradeIds": ["sharpened_edge"] }
  ],
  "enemies": [
    { "alias": "goblin_a", "entityDefinitionId": "enemy_goblin" }
  ],
  "initialState": {
    "heroResources": { "energy": 3 },
    "effects": [
      { "targetAlias": "goblin_a", "statusId": "poison", "stacks": 2 }
    ]
  }
}
```

O compilador de cenário deve:

- validar schema e limites da política;
- resolver IDs na revisão fixada;
- normalizar ordem para cálculo de hash;
- gerar IDs de instância determinísticos para cartas e entidades;
- rejeitar overrides não autorizados;
- produzir `scenarioHash` e uma entrada de journal inicial;
- criar run e encontro em uma operação autoritativa.

`attemptKey` diferencia uma tentativa nova de uma repetição. Repetir todos os inputs, inclusive essa chave, deve retornar a mesma run; trocar somente a chave inicia outra tentativa determinística.

### 3.5 Timeline, logs e branches

Cada comando aceito já possui sequence, step, hashes e checkpoint. A nova projeção de timeline deve derivar itens de combate sem duplicar a fonte de verdade.

```text
Journal/checkpoints autoritativos
        ├── timeline por comando (logs e depuração)
        ├── agrupamento por turno (navegação humana)
        └── snapshot histórico sob demanda
```

Um item de timeline precisa ser um DTO de leitura, não o `RunCheckpoint` interno:

```json
{
  "runSequence": 18,
  "combatStep": 42,
  "turn": 4,
  "phase": "PLAYER_ACTION",
  "actorId": "hero-1",
  "commandType": "EXECUTE_ACTION",
  "stateHash": "...",
  "previousStateHash": "...",
  "snapshotAvailable": true,
  "summary": {
    "cardInstanceId": "...",
    "actionId": "fireball",
    "targets": ["goblin_a"]
  }
}
```

Ao observar passado, o cliente usa o snapshot histórico em modo somente-leitura. Para continuar dele, cria uma run filha. A branch deve conter também a âncora de combate; se o encontro estiver ativo, um novo `combatId` determinístico é derivado para evitar colisão entre pai e filha.

```text
Run pai / combate pai: 1 ─ 2 ─ 3 ─ 4
                                  └── Run filha / combate filho: 3A ─ 4A
```

O branch precisa copiar o estado do combate no checkpoint selecionado, manter as referências de conteúdo da origem e começar um novo journal. Cartas e entidades carregam suas identidades de instância no contexto da run; qualquer identificação global deve usar ao menos o par `(runId, instanceId)`.

### 3.6 Capacidades pertencentes ao modo

O cliente não recebe permissões implícitas. O modo declara quais ações são legais, e a engine as aplica em cada endpoint/command handler.

```json
{
  "capabilityPolicyId": "theorycraft_tools",
  "allowScenarioAuthoring": true,
  "allowCustomDeck": true,
  "allowInitialEffects": true,
  "allowResourceOverrides": true,
  "allowTimelineFork": true,
  "allowHotReloadActivation": true,
  "maxCards": 100,
  "maxEnemies": 5,
  "maxBranchesPerRoot": 50
}
```

Autorização operacional pode restringir quem aciona uma capacidade, mas a decisão de que ela existe pertence ao `GameMode`. Um modo publicado pode ocultar timeline e negar branches mesmo ao mesmo cliente REST que usa um modo de teoria.

### 3.7 Fronteiras REST

Os gateways canônicos de comandos de run e combate permanecem. As novas superfícies são criação/consulta de cenário e projeções de ferramenta:

```text
GET  /api/v1/content/modes
GET  /api/v1/content/{kind}?revision={revision}

POST /api/v1/sandbox/scenarios/validate
POST /api/v1/sandbox/runs
GET  /api/v1/sandbox/runs/{runId}/scenario

POST /api/v1/runs/{runId}/commands
POST /api/v1/combats/{combatId}/commands

GET  /api/v1/combats/{combatId}/timeline
GET  /api/v1/combats/{combatId}/timeline/{sequence}/state
POST /api/v1/combats/{combatId}/timeline/{sequence}/branches
GET  /api/v1/runs/{runId}/branch-tree
```

`ACTIVATE_CONTENT_REVISION` deve ser um tipo de comando de run, não um endpoint de mutação ad hoc. A criação de branch pode expor uma rota de ferramenta, mas precisa delegar ao mesmo serviço transacional que cria o journal da filha.

Todos os novos contratos devem usar `ProblemDetails`, `commandId` quando houver mutação, versões esperadas, hashes e OpenAPI atualizada.

## 4. Plano de implementação detalhado

### Fase 0 — Contrato e linha de base

**Objetivo:** congelar a semântica antes de expandir o domínio.

1. Registrar schemas e exemplos JSON para todas as definições novas.
2. Atualizar OpenAPI para refletir o contrato v1 real antes de adicionar superfícies de sandbox.
3. Adicionar testes de contrato para comandos, conteúdo, journal, timeline e branches existentes.
4. Documentar que rotas de mutação direta de status/modifier não são caminho de gameplay do sandbox.

**Aceite:** OpenAPI, controllers e testes de contrato cobrem a mesma superfície canônica.

### Fase 1 — Configuração composta de modo

**Objetivo:** tornar o `GameMode` um compositor de recursos JSON tipados.

1. Substituir regras estruturais em `GameModeDefinition.Rules` por IDs tipados de políticas e rule sets.
2. Criar recursos JSON e catálogos para `ReplayPolicy`, `TimelinePolicy`, `ContentBindingPolicy` e `CapabilityPolicy`.
3. Criar resolvedor de modo que produza um `ResolvedGameMode` imutável.
4. Validar referências, tipos, limites e compatibilidade durante publicação de conteúdo.
5. Persistir no estado da run a composição resolvida ou referências suficientes para reconstruí-la pela revisão fixada.
6. Criar `combat_sandbox`, `theorycraft_tools` e exemplos de modo publicado restrito.

**Aceite:** iniciar uma run com um modo inválido falha antes de persistir; modos diferentes ativam capacidades diferentes sem `if` específico no cliente.

### Fase 2 — Revisões de conteúdo e hot reload versionado

**Objetivo:** permitir evolução de regras em desenvolvimento sem conteúdo mutável invisível.

1. Expor leitura de modos e políticas pelo catálogo de conteúdo com OpenAPI.
2. Criar fluxo de captura/validação/publicação de revisão de desenvolvimento.
3. Criar serviço de ativação de conteúdo para novas runs de um modo.
4. Adicionar comando `ACTIVATE_CONTENT_REVISION` para runs ativas, condicionado a `ContentBindingPolicy` e `CapabilityPolicy`.
5. Atualizar manifest efetivo da run somente por esse comando e registrar hashes antes/depois.
6. Garantir que replay carregue cada revisão histórica necessária; revisões referenciadas por journal não podem ser removidas.

**Aceite:** uma mudança de dano ativada entre duas ações afeta somente ações posteriores e o replay reproduz o hash final.

### Fase 3 — Cenário de combate e lançamento atômico

**Objetivo:** transformar combinações de cartas, deck, inimigos e condições iniciais em entradas formais da engine.

1. Implementar `CombatScenarioDefinition`, DTOs de validação e hash canônico.
2. Implementar `ScenarioCompiler`, resolvendo definições na revisão solicitada.
3. Validar capabilities: deck, upgrades, inimigos, recursos e efeitos iniciais.
4. Derivar `cardInstanceId`, aliases de entidades e IDs de combate deterministicamente.
5. Criar `POST /api/v1/sandbox/scenarios/validate` sem persistência.
6. Criar `POST /api/v1/sandbox/runs` para iniciar run e combate numa transação lógica, com recibo completo.
7. Registrar `scenarioHash`, `attemptKey` e revision no journal inicial e no read model.

**Aceite:** mesmas entradas normalizadas produzem o mesmo cenário e estado inicial; alteração de qualquer entrada relevante muda o hash.

### Fase 4 — Snapshot e leituras de inspeção

**Objetivo:** permitir que qualquer cliente renderize e depure o combate sem reimplementar regra.

1. Criar DTO de snapshot de sandbox para run, combate e cenário efetivo.
2. Expor status, modifiers, intents, stack, fase, ator prioritário e recursos de todos os atores.
3. Expor mão como instâncias de carta, com definition, upgrades, zona e ordem.
4. Garantir que recibos dos comandos canônicos devolvam run/combat consistentes de uma mesma transição.
5. Consolidar ações legais, opções de custo e alvos legais em contratos documentados.

**Aceite:** uma Godot vazia consegue montar a tela completa somente com leituras REST e recibos de comando.

### Fase 5 — Timeline configurável de combate

**Objetivo:** expor logs e estados históricos de forma eficiente e orientada a ferramenta.

1. Criar `CombatTimelineProjectionService` a partir do journal autoritativo.
2. Produzir itens por comando e agrupamentos por turno/phase, sem alterar snapshots.
3. Criar `GET /api/v1/combats/{combatId}/timeline` paginado por cursor.
4. Criar leitura de snapshot histórico em `/timeline/{sequence}/state`.
5. Fazer a política definir retenção, acesso, granularidade e resumo disponível.
6. Manter listas compactas; snapshots completos são carregados sob demanda.

**Aceite:** um cliente identifica o início/fim de cada turno, abre qualquer estado permitido e verifica o hash correspondente.

### Fase 6 — Branches de combate e grafo de linhagem

**Objetivo:** continuar de qualquer ponto histórico sem reescrever dados.

1. Estender o modelo de branch com raiz, âncora de combate, turno, step e hash de origem.
2. Permitir branch de checkpoint com encontro ativo.
3. Derivar novo `runId` e `combatId` no ramo e manter o pai imutável.
4. Criar serviço de grafo de branches que encontre raiz, ancestrais e descendentes.
5. Criar `GET /api/v1/runs/{runId}/branch-tree` e criação de branch a partir da timeline de combate.
6. Aplicar limites definidos na política de capacidades.

**Aceite:** duas branches de um mesmo turno aceitam comandos divergentes, têm hashes próprios e não alteram a run/combat pai.

### Fase 7 — Simulação de combate isolada

**Objetivo:** suportar theorycraft automatizado usando exatamente as mesmas regras do combate real.

1. Fazer simulações criarem/usararem branches que possam conter encontro ativo.
2. Encaminhar `EXECUTE_ACTION`, `END_TURN`, IA e resolução pelo `ICombatRunCoordinator` no runtime isolado.
3. Limitar quantidade de comandos, branches e duração pela política do modo.
4. Retornar métricas, hash final, timeline resumida e erro de regra por comando.
5. Permitir repetição idempotente de uma simulação pelo hash da sequência de comandos.

**Aceite:** uma simulação e a mesma sequência manual na branch produzem os mesmos hashes finais.

### Fase 8 — Integração Godot

**Objetivo:** construir a ferramenta visual sem transferir lógica de jogo para a Godot.

1. Separar transporte HTTP, sessão atual, DTOs de leitura e cenas de apresentação.
2. Montar seletor de revisão, modo e cenário usando catálogos REST.
3. Construir tabuleiro que só habilita entradas presentes em ações/alvos legais.
4. Construir painel de logs/timeline e estado histórico somente-leitura.
5. Construir navegador de branches em árvore, com seleção de ramo e criação de continuação a partir de um item de timeline.
6. Mostrar ativação de conteúdo apenas se a capability do modo autorizar.

**Aceite:** a Godot troca de branch e de snapshot exclusivamente por chamadas REST; nenhum cálculo de regra existe no cliente.

### Fase 9 — Garantias de regressão e operabilidade

**Objetivo:** assegurar que flexibilidade não degrada determinismo.

1. Testar determinismo de cenários, incluindo IDs de instância e `attemptKey`.
2. Testar replay com múltiplas revisões de conteúdo ativadas em sequência.
3. Testar reinício da API com timeline, branches e conteúdo histórico.
4. Testar isolamento de pai/filho e divergência entre branches de combate.
5. Testar negação de capabilities em modos que não as habilitam.
6. Testar integridade de OpenAPI e compatibilidade dos clientes.
7. Adicionar métricas de carga para timeline e limites de simulação.

**Aceite final:** qualquer cenário permitido pelo modo pode ser criado, reproduzido, inspecionado, ramificado e retomado após reinício sem alterar dados históricos ou duplicar comandos.

## 5. Ordem de entrega recomendada

1. Fase 0 e Fase 1 — contrato e composição de modo.
2. Fase 2 — hot reload versionado, porque todos os recursos posteriores dependem de revisão efetiva bem definida.
3. Fase 3 e Fase 4 — lançar cenário e apresentar estado completo.
4. Fase 5 e Fase 6 — timeline e branches de combate.
5. Fase 7 — simulação automatizada.
6. Fase 8 — Godot como ferramenta visual sobre os contratos já estáveis.
7. Fase 9 — endurecimento contínuo; testes de cada fase entram antes da próxima.

## 6. Decisões que permanecem deliberadamente fora da Godot

- Cálculo de dano, custo, efeitos, status, IA, fases, turnos, embaralhamento e movimento de cartas.
- Validação de cenário, identidade de instância, geração de seed e cálculo de hashes.
- Publicação/ativação de conteúdo e seleção da revisão efetiva.
- Criação de checkpoint, replay, branch, simulação e resolução de conflitos de versão.
- Decisão de que uma capacidade está habilitada em determinado modo.

A Godot pode oferecer uma experiência rica para essas operações, mas nunca se torna uma segunda implementação das regras.

## 7. Arquivos que fundamentam esta análise

- `src/Core/Run/RunModeDefinition.cs`
- `src/Core/Run/RunManager.cs`
- `src/Core/Content/ContentPublicationService.cs`
- `src/Core/Run/Branching/RunBranchService.cs`
- `src/Core/Run/Branching/RunSimulationService.cs`
- `src/Core/Run/Replay/RunSemanticReplayService.cs`
- `src/API/Controllers/RunCommandController.cs`
- `src/API/Controllers/CombatCommandController.cs`
- `src/API/Controllers/RunJournalController.cs`
- `src/API/Controllers/CombatJournalController.cs`
- `src/API/Controllers/ContentController.cs`
- `openapi/heroscript-v1.json`

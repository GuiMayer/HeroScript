# Consolidação dos sistemas centrais restantes

**Status:** proposto para implementação

**Data da análise:** 2026-09-07

**Escopo:** turnos/fases/prioridade/stack; entidades/IA; run/progressão;
configuração/conteúdo/mods; eventos/persistência/replay/branches.

## 1. Objetivo

Eliminar as autoridades sobrepostas que ainda existem ao redor do núcleo já
consolidado de efeitos, cálculos, recursos, status, relíquias e modifiers. Ao
final, toda ação de gameplay deverá seguir o mesmo contrato:

```text
(snapshot imutável + comando tipado + conteúdo fixado + contexto determinístico)
    -> plano de transição imutável
    -> commit atômico no histórico autoritativo
    -> projeções de estado, timeline, eventos e fila visual
```

Godot continua responsável somente por input, apresentação, navegação visual da
timeline e reprodução dos frames já calculados. Nenhuma regra de turno, IA,
prioridade, progressão, conteúdo ou replay deve existir no cliente.

Este plano assume as decisões já estabelecidas durante o projeto:

- runs são determinísticas e todos os dados de gameplay são imutáveis;
- `RunState` é o agregado autoritativo e contém o combate ativo;
- todas as mutações entram pela REST API e pelo mesmo gateway de comandos;
- regras vêm de JSON e são fixadas por revisão de conteúdo;
- hot reload numa run é explícito, configurável pelo modo e journalado;
- fases são configuráveis e precisam preservar, no mínimo, os papéis semânticos
  `Start`, `Middle` e `End`;
- o modo inicial usa ativações por ator e suporta orçamento por recurso ou por
  quantidade de ações;
- desempate e ordem de ativação são configuráveis;
- a engine conclui cada transição aceita antes de a Godot reproduzir sua fila
  visual;
- timeline é por comando e também serve de base para logs e branches;
- não há requisito de compatibilidade com schemas, rotas ou saves legados nesta
  fase pré-produção;
- o dashboard permanece fora do escopo.

## 2. Resultado da análise atual

O diagnóstico anterior continua correto como indicação das áreas a revisar, mas
precisa ser atualizado pelo trabalho de consolidação já concluído.

| Área | O que já está correto | Lacuna real atual | Prioridade |
| --- | --- | --- | --- |
| Turnos, fases, prioridade e stack | `CombatFlowPlanner` é o único executor atual; managers paralelos de fase/prioridade/stack foram removidos | o planner e o coordinator continuam supermódulos; o grafo rico de fases não é executado; existem opções reservadas sem semântica; turn order ainda vem de `appsettings` | Alta |
| Entidades e IA | combate usa snapshots imutáveis, lados e controllers para parte da autoridade; gambits são determinísticos no caminho canônico | coexistem `Entity` e `CombatEntity`; components/controllers antigos são mutáveis; `IsHero` ainda governa materialização; IA antiga tem regras hardcoded; intent e decisão usam caminhos duplicados | Alta |
| Run e progressão | `RunState` já possui deck, recursos, mapa, encounters, lojas, recompensas, preparação, relíquias e modifiers | `RunManager` tem 2.524 linhas e mistura loading, regra, lock, comando, commit, evento e DTO interno; há métodos laterais de mutação; tipos de nó e encerramento da run são hardcoded | Média-alta |
| Configuração, conteúdo e mods | bundle publicado, manifest hash, conteúdo fixado e validação de grafo são sólidos | gameplay ainda possui loaders/fallbacks opcionais e `strictMode: false`; há diretórios duplicados; o parser adivinha formatos; herança de entidade duplica o sistema delta; `src/Mods` é vazio | Média |
| Eventos, persistência, replay e branches | checkpoint atômico, hash chain, replay semântico, timeline e branches já funcionam | `journal`, `batch`, `checkpoint` e `snapshot` ainda se sobrepõem; existem dois repositórios e caminhos `legacy.snapshot`; replay recompõe manualmente toda a engine; filtros de combate se repetem; EventBus ainda se descreve como event sourcing | Média |

### 2.1 Evidências específicas

#### Turnos e fases

- `CombatFlowPlanner` possui aproximadamente mil linhas e também coordena deck,
  recursos, status, relíquias, intents e resultado.
- `CombatRunCoordinator` também possui aproximadamente mil linhas e contém o
  loop de IA, execução automática, dispatch de carta/habilidade e preparação do
  commit.
- `PhaseDefinition.ValidNextPhaseIds`, `AutoTransition`, `AllowPriority` e
  `PhaseSequenceDefinition.AllowPhaseSkipping` são validados ou serializados,
  mas não dirigem o runtime.
- `ValidateCanonicalActivationSequence` rejeita mais de uma fase de cada papel,
  embora o schema estrutural anuncie grafos mais ricos.
- a inicialização materializa diretamente a fase `Middle`; `Start` e `End` são
  principalmente rótulos ao redor de lifecycles hardcoded.
- `OutcomeEvaluationBoundary.AfterResolutionStack`, `ReactionStrategy.Immediate`
  e `ReactionStrategy.Stack` existem, mas qualquer seleção diferente de
  `Disabled` é rejeitada.
- `ITurnOrderCalculator` é selecionado por `CombatOptions`/`appsettings`, não
  pelo game mode fixado na revisão de conteúdo. O planner usa a ordem já
  materializada e não recalcula todas as estratégias nos boundaries prometidos.

#### Entidades e IA

- `Core.Entity.Entity` carrega components e uma instância de controller, enquanto
  `CombatEntity` carrega o estado realmente usado pelo combate.
- `ComponentBase` mantém `Owner` e `IsEnabled` mutáveis e executa callbacks de
  attach/detach; isso conflita com snapshots imutáveis.
- `EntityCombatAdapter.UpdateEntityFromCombat` descarta os novos records
  retornados por `RemoveComponent`/`AddComponent`, portanto não atualiza o valor
  recebido e confirma que o caminho bidirecional não é uma autoridade segura.
- `CombatEntity.IsHero`, `CombatState.Hero/Enemies`, `CombatFactory` e o snapshot
  do sandbox ainda codificam papéis que deveriam vir de lado e controller.
- `AIController` implementa comportamentos aggressive/defensive/balanced em C#,
  escolhe `BASIC_ATTACK` e trata o herói como alvo especial.
- `GambitController` possui um `EmptyGambitEngine` que transforma configuração
  ausente em `PASS`, mascarando erro de composição.
- `GambitEngine` mistura leitura, cache mutável, edição de arquivos e decisão de
  gameplay; ainda possui caminho sem revisão fixada.
- `IntentResolver` cria uma `Entity` parcial a partir de um `CombatEntity`,
  ignora seu `runId` e estima dano somando apenas `FlatValue`, fora do pipeline
  canônico de preview/cálculo.
- o coordinator calcula o gambit novamente na ativação de IA em vez de consumir
  uma decisão/telegraph com política explícita.

#### Run e progressão

- `RunManager` implementa simultaneamente `IRunManager`, `IRunCommandProcessor`
  e `IRunCombatResolutionCommitter`, tem muitas dependências opcionais e guarda
  caches, receipts e locks.
- `Execute` despacha para métodos públicos que individualmente carregam,
  transitam, persistem e publicam eventos. Esses métodos também podem ser
  chamados sem passar pelo envelope canônico.
- loaders de run, seleção, loja e preparação ainda fazem leitura direta quando
  `ContentRuntime` não está disponível.
- ausência de modo ainda ativa `ResolveLegacyMode` ou deixa políticas nulas.
- `RunMapTransitions` reconhece `combat`, `elite`, `boss`, `upgrade`,
  `card_upgrade`, `rest` e `forge` por strings hardcoded.
- aplicar upgrade verifica os mesmos nomes de nó novamente dentro de
  `RunManager`.
- não há um `RunLifecycleState` explícito nem política completa que converta
  resultado de encounter/mapa em continuação, falha ou conclusão da run.
- `GetAvailableCommands` cobre principalmente navegação e não é o catálogo único
  de todas as ações legais do estado atual.

#### Configuração, conteúdo e mods

- `ResourceLoader` interpreta silenciosamente documento sem `$delta` como
  replace legado e, por padrão, ignora deltas inválidos.
- o parser decide se um arquivo é definição única observando o tipo dos campos;
  o formato não é declarado no próprio documento.
- vários consumidores chamam `LoadResource(..., strictMode: false)` e possuem
  fallback próprio para conteúdo não fixado.
- `EntityDefinitionLoader` implementa outra herança (`BaseDefinitionId`) e outro
  merge além do delta universal.
- `ConfigManager` mantém uma configuração global atual, enquanto a run já deve
  ser independente e fixada por revisão.
- existem cópias rastreadas de entidades dentro e fora de `Resources`, conteúdo
  antigo em `UserData/Configs` e itens inexistentes ainda listados em
  `HeroScript.slnx`.
- `ContentManifestProvider.Sources` é uma lista hardcoded separada dos tipos que
  `ContentGraphValidator` conhece.
- `src/Mods` contém apenas `Class1`; não há manifest de pacote, dependências,
  ordenação, conflitos ou política de segurança.

#### Histórico, eventos, replay e branches

- o repositório canônico salva batches contendo checkpoints completos, mas
  também mantém diretórios chamados `journal` e `snapshots`; leituras combinam
  as três representações.
- `IRunStateRepository.SaveAsync`, `JsonFileRunStateRepository` e migrações para
  `legacy.snapshot` preservam uma segunda semântica de persistência.
- uma ação externa pode produzir várias sequências de run, exigindo
  `RootCommandId`, `TransitionIndex` e agrupamento especial no replay.
- `RunSemanticReplayService` possui muitas dependências, remonta manualmente
  `RunManager`, combat, efeitos, AI e providers; essa composição pode divergir
  da composição live.
- o replay possui um switch próprio por string e chama vários métodos laterais
  do `RunManager`, duplicando o dispatch live.
- timeline, journal de combate e projeção de eventos repetem sua própria lista
  de nomes que contam como comando de combate.
- `EventBus` e `IEventStore` armazenam observabilidade separada, mas nomes e
  comentários ainda sugerem que esse histórico seja event sourcing. Recuperação
  e replay, corretamente, não o usam.
- branches são descobertas varrendo todas as runs; contagem e árvore têm custo
  crescente e não possuem índice de linhagem.
- a branch copia o snapshot do pai quase integralmente; IDs de owner ligados à
  run, resoluções anteriores e referências internas precisam de uma política de
  rebase explícita.
- `ValidateBranchPolicy` aceita run sem modo por compatibilidade.

## 3. Decisões arquiteturais propostas

### 3.1 Uma autoridade por conceito

| Conceito | Autoridade de escrita | Estado autoritativo | Derivações somente leitura |
| --- | --- | --- | --- |
| Conteúdo | compilador/publicador de settings | `ContentBundle` imutável por revisão | catálogos, inspeções e schemas |
| Gameplay | gateway de comandos | `RunState` | DTOs de run/combate |
| Combate | reducer de combate chamado pelo handler da run | `CombatState` dentro de `RunState` | intents visuais, ações legais, previews |
| Histórico | `IRunCommitStore.Append` | sequência de `RunCommit` | snapshot latest, journal HTTP, timeline, eventos |
| Replay | gateway normal em runtime isolado | comandos do `RunCommit` + revisões fixadas | relatório de verificação |
| Branch | comando de criação baseado em commit existente | primeira `RunCommit` da nova run | índice/árvore de linhagem |
| Telemetria | bus operacional pós-commit | store operacional opcional | endpoint administrativo |

Nenhum manager, loader, controller ou cache pode manter uma segunda cópia de
estado de gameplay.

### 3.2 Comando raiz como unidade atômica

Cada request mutável corresponde exatamente a um comando raiz e incrementa a
`RunState.Sequence` uma vez. Transições internas continuam avançando
`DeterministicContext.Step`, mas são frames dentro do mesmo commit:

```text
RunCommit
  schemaVersion
  engineVersion
  runId
  sequence
  rootCommand { id, type, payload, payloadHash, expectedSequence, expectedStep }
  previousStateHash
  stateHash
  beforeStep / afterStep
  frames[] { frameIndex, step, scope, kind, hashes, resolution }
  facts[]  { factIndex, type, scope, payload }
  stateAfter
```

Guardar `stateAfter` em cada commit é aceitável para a fase atual e torna o
histórico autossuficiente para timeline e branch. Uma futura compactação pode
manter commits e snapshots periódicos, mas não muda o contrato conceitual.

### 3.3 Projeções não escrevem gameplay

- `journal` é uma visualização paginada de `RunCommit`.
- `timeline` organiza commits e seus frames por combate, turno, ativação e fase.
- eventos duráveis são projeções determinísticas de `facts` do commit.
- latest state é o `stateAfter` do último commit.
- um snapshot materializado é apenas cache; apagá-lo não apaga histórico.
- `EventBus` passa a ser explicitamente telemetria operacional pós-commit.

### 3.4 Setting, package, bundle e revision são conceitos distintos

- **Package:** pacote base ou mod, com manifest, arquivos e dependências.
- **Setting:** lista ordenada de packages e o mode usado como entrada do jogo.
- **Bundle:** resultado já composto e validado, sem deltas pendentes.
- **Revision:** hash canônico do bundle completo.

O runtime de gameplay só lê o bundle. Filesystem, herança, patches e hot reload
existem somente antes da publicação.

### 3.5 Entidade é definição + instância, não objeto com comportamento

- `EntityDefinition` contém components declarativos e referências de conteúdo.
- `EntityState`/`CombatActorState` contém somente IDs, revisão e component states
  imutáveis.
- lado, relações e `ControllerBinding` definem quem controla e quem é aliado ou
  oponente.
- `hero`, `enemy`, `companion` e `npc` podem existir como tags de conteúdo ou
  projeções visuais, nunca como branches de regra.
- controllers não são objetos armazenados na entidade; são estratégias puras
  selecionadas por binding e conteúdo.

### 3.6 IA escolhe entre ações legais

O mesmo `LegalActionResolver` usado por REST/preview produz candidatos para a IA.
Uma política pura de decisão recebe candidatos, snapshot, revisão e contexto
determinístico e retorna `AiDecision` junto do contexto sucessor. A ação escolhida
ainda passa pelo mesmo handler e validador de qualquer ação do jogador.

Intent terá política explícita no modo:

- `LockedUntilActivation`: a decisão publicada é guardada e executada, se ainda
  legal; ilegalidade posterior segue uma política configurada de fallback.
- `RecomputeAtActivation`: intent é uma previsão marcada com o step observado e
  a IA recalcula ao assumir prioridade/ativação.

Estimativas numéricas usam preview do executor canônico; não há cálculo resumido
paralelo.

### 3.7 Fluxo de combate é um grafo executável

`PhaseSequenceDefinition` passa a declarar entrada, edges, condições,
auto-transição, ações permitidas e política de janela. Os papéis `Start`,
`Middle` e `End` continuam obrigatórios, mas podem existir várias fases de cada
papel. O snapshot guarda apenas cursor e estado de fluxo; a definição é lida da
revisão fixada.

O atual planner será separado em reducers/serviços puros:

- `ActivationOrderResolver`;
- `PhaseGraphReducer`;
- `PriorityWindowReducer`;
- `ResolutionStackReducer`;
- `CombatBoundaryExecutor`;
- `CombatOutcomeResolver`;
- `AutomaticFlowDriver` com limite de passos.

### 3.8 Prioridade e stack são estado, não manager

```text
CombatFlowState
  round
  activation
  phaseCursor
  priorityWindow?
  resolutionStack[]

PriorityWindowState
  windowId
  eligibleActorIds[]
  currentActorId
  consecutivePasses
  cycle

PendingActionState
  stackEntryId
  proposedBy
  commandSnapshot
  lockedTargets[]
  costReservation
  sourceRevision
  provenance
```

As estratégias configuráveis serão:

- `Disabled`: ação legal resolve na mesma transação, comportamento dos modos
  atuais;
- `Automatic`: reações configuradas são inseridas numa fila determinística e
  resolvidas sem input adicional;
- `PriorityStack`: ações e respostas entram na stack; `PASS_PRIORITY` avança a
  janela; a resolução ocorre na ordem declarada quando todos passam.

Target locking, momento do custo, ordem LIFO/FIFO, elegibilidade de resposta,
reabertura de prioridade e boundary de outcome serão políticas JSON validadas.
Não haverá valor declarado sem implementação.

### 3.9 Progressão é uma máquina de estados dirigida por atividades

`RunLifecycleState` torna explícitos `Active`, `Completed`, `Failed` e
`Abandoned`. O game mode seleciona uma `RunProgressionPolicyDefinition` que
mapeia resultado de encounter, fim de mapa e condições configuradas para essas
transições.

Cada nó aponta para uma atividade registrada, em vez de depender de aliases de
string:

```text
RunNodeDefinition
  nodeId
  activity { type, definitionId, parameters }
  edges[] { targetNodeId, condition }
  entryEffects[]
  exitEffects[]
  completionPolicy
```

Handlers de encounter, reward, shop, preparation e card upgrade implementam o
mesmo contrato puro: abrir atividade, listar comandos legais, executar comando e
informar se pode concluir. Novos tipos entram por registro explícito e validação
de conteúdo, não por `if (nodeType == ...)`.

### 3.10 Mods são data packages, não assemblies arbitrários

O primeiro contrato de mods será somente dados JSON. `src/Mods` implementará
descoberta, manifest, resolução de dependências, ordenação, patches, diagnóstico
e compilação de packages. Carregamento de assemblies de terceiros fica fora do
escopo por segurança e porque quebraria a garantia de engine version/replay.

Manifest mínimo:

```json
{
  "schemaVersion": 1,
  "packageId": "author.example",
  "version": "1.0.0",
  "engineVersion": "4",
  "dependencies": [
    { "packageId": "heroscript.base", "version": ">=1.0.0" }
  ],
  "loadAfter": [],
  "contentRoots": ["content"]
}
```

Definições base e patches terão envelopes distintos. Um override nunca será
inferido pela ausência de `$delta`. Patches declararão target, operação e
precondição de hash; conflito ambíguo falhará na compilação.

## 4. Sequência detalhada de implementação

Cada etapa deve terminar com build e suites verdes e gerar um commit próprio.
Não se deve manter adapters de compatibilidade depois que todos os consumidores
da etapa forem migrados.

### Etapa 0 — Congelar contratos e baseline

**Objetivo:** transformar as decisões acima em restrições verificáveis antes da
refatoração.

1. Registrar ADRs para autoridade de comando, commit, conteúdo, entidade, fluxo
   e telemetria.
2. Criar testes arquiteturais que proíbam novos stores de gameplay, mutação em
   componentes e leitura direta de filesystem nos reducers.
3. Criar um cenário dourado cobrindo run, encounter, carta, IA, fim de turno,
   recompensa, shop, branch e replay.
4. Persistir os hashes, steps, frames e comandos esperados somente como baseline
   temporário de migração; atualizar intencionalmente quando o novo contrato de
   sequência entrar.
5. Registrar o baseline atual: build; 1.099 Core.Tests; 148 API.Tests.

**Aceite:** qualquer nova autoridade paralela ou input ambiental não classificado
falha em teste.

**Commit sugerido:** `docs(architecture): define remaining system authorities`

### Etapa 1 — Introduzir comando tipado e plano de transição

**Objetivo:** separar cálculo de estado de persistência sem mudar ainda as regras.

1. Criar `GameplayCommandEnvelope`, `GameplayCommandDescriptor` e codec estrito
   por tipo de comando.
2. Criar `RunTransitionPlan` contendo estado anterior, candidato, contexto,
   frames, facts e resultado retornável.
3. Criar `IRunCommandHandler<TCommand>` puro e um registry determinístico.
4. Mover normalização, payload hash e expected sequence/step para uma única
   fronteira.
5. Adaptar inicialmente os métodos do `RunManager` por handlers sem duplicar
   execução.
6. Proibir payload desconhecido e propriedades extras em todos os comandos.

**Testes:** round-trip de codec, tipo desconhecido, propriedade desconhecida,
payload hash, handler duplicado e rollback de plano rejeitado.

**Aceite:** live, replay e simulation podem receber o mesmo envelope e descobrir
o mesmo handler, embora a persistência antiga ainda esteja atrás do adapter.

**Commit sugerido:** `refactor(commands): introduce typed deterministic transition plans`

### Etapa 2 — Tornar `RunCommit` a autoridade persistente

**Objetivo:** uma ação externa, uma sequência, um append atômico.

1. Implementar `RunCommit` e validar identidade, hash chain, steps, frame indexes
   e `stateAfter`.
2. Substituir `IRunStateRepository`/`IRunCheckpointRepository` por interfaces
   separadas de leitura e `IRunCommitStore` append-only.
3. Implementar file store com arquivo temporário, flush, rename atômico e
   verificação idempotente de colisão.
4. Manter `sequence` por comando raiz e `step` por transição interna.
5. Materializar latest state como projeção opcional do último commit.
6. Remover `SaveAsync`, `legacy.snapshot`, migração automática e o repositório
   JSON alternativo.
7. Versionar o envelope persistido e rejeitar schema/engine desconhecido.

**Testes:** falha antes/depois do rename, retry idempotente, colisão de ID,
corrupção de payload/state hash, batch de frames, reinício e concorrência na
mesma run.

**Aceite:** não existe caminho que publique `RunState` sem antes anexar um
`RunCommit` válido.

**Commit sugerido:** `refactor(persistence): make root run commits authoritative`

### Etapa 3 — Recriar journal, timeline e eventos como projeções

**Objetivo:** eliminar classificação e storage duplicados.

1. Adicionar `scope` e referências de run/combat/actor/fase aos frames/facts.
2. Criar readers compartilhados de commit, sem listas repetidas de command type.
3. Projetar journal HTTP diretamente dos commits.
4. Projetar timeline por comando, com frames internos e agrupamentos por
   round/activation/phase.
5. Projetar eventos duráveis de `facts`, com ID derivado de
   run/sequence/factIndex.
6. Renomear `EventBus` para deixar claro que é telemetria operacional ou criar
   `IOperationalEventSink`; publicar somente após commit.
7. Renomear a rota administrativa para telemetria e manter `/runs/.../events`
   exclusivamente derivado do histórico.
8. Remover `IEventStore` do caminho de eventos de gameplay; falha de telemetria
   não altera commit.

**Testes:** apagar/reconstruir cada projeção, SSE após restart, múltiplos facts no
mesmo commit, filtro de combate por scope e ausência de dual write autoritativo.

**Aceite:** journal, timeline e eventos apresentam o mesmo sequence/stateHash e
podem ser reconstruídos apenas de `RunCommit`.

**Commit sugerido:** `refactor(events): derive gameplay projections from run commits`

### Etapa 4 — Unificar composição live, replay e simulation

**Objetivo:** reexecução não pode possuir uma segunda montagem da engine.

1. Criar `IGameplayRuntimeFactory` que recebe revisões disponíveis, persistence
   mode e sinks operacionais.
2. Fazer a composição principal e o replay usarem o mesmo módulo de registro de
   handlers, reducers, cálculo, efeitos, IA e fluxo.
3. Remover a construção manual de dependências de `RunSemanticReplayService`.
4. Reexecutar todos os commits pelo gateway tipado; remover switch de replay e
   chamadas laterais ao manager.
5. Validar hash/step/frame fingerprint após cada comando, não apenas o final.
6. Fazer simulation usar runtime isolado criado pela mesma factory.

**Testes:** um teste de composição compara os descriptors live/replay; dez
runtimes independentes repetem o cenário dourado; omitir um handler faz replay
falhar antes de executar.

**Aceite:** adicionar comando novo exige registrar um handler uma vez e já o
torna utilizável por live, simulation e replay.

**Commit sugerido:** `refactor(replay): share one gameplay runtime composition`

### Etapa 5 — Consolidar branches, restore e linhagem

**Objetivo:** história não linear sem cópia ambígua ou varredura global.

1. Criar `RunLineage` no commit inicial da branch: root, parent, source sequence,
   source hash, source combat e branch key.
2. Implementar `IRunLineageIndex` como projeção reconstruível dos commits
   iniciais.
3. Definir política de rebase para IDs e owners ligados à run, encounter ativo,
   resoluções herdadas e cursor determinístico.
4. Garantir que IDs históricos de carta/relic permaneçam estáveis quando são a
   mesma instância e que IDs novos usem o cursor da branch.
5. Fazer criação de branch usar o commit solicitado, não um snapshot lateral.
6. Remover compatibilidade para runs sem modo/política.
7. Substituir restore implícito por duas operações claras:
   `CREATE_BRANCH_FROM_HISTORY` e, somente se o modo permitir,
   `RESTORE_HEAD_FROM_HISTORY`, que cria novo commit e nunca regride sequence ou
   determinismo.
8. Fazer simulation criar branch interna pelo mesmo serviço e índice.

**Testes:** duas branches divergentes, branch de combate ativo, owners corretos,
árvore após restart, limite por raiz, restore journalado, pai intacto e replay
recursivo da linhagem.

**Aceite:** árvore e lookup de branch não varrem todas as runs e nenhuma operação
reescreve commits existentes.

**Commit sugerido:** `refactor(branching): anchor lineage to authoritative commits`

### Etapa 6 — Implementar packages e settings no projeto `Mods`

**Objetivo:** dar semântica real a mods antes de migrar todos os conteúdos.

1. Substituir `Class1` por modelos e serviços de `PackageManifest`,
   `SettingDefinition`, dependencies e diagnostics.
2. Implementar descoberta por providers explícitos configurados na aplicação;
   retirar autodetecção ambiental do compilador.
3. Resolver DAG de dependências com ordem estável, versões compatíveis, ciclos,
   conflitos e packages ausentes.
4. Definir envelopes distintos para definition e patch, com schema version.
5. Fazer patch exigir target explícito e, quando substituir conteúdo existente,
   precondição de hash/revision.
6. Compilar um setting em bundle normalizado e produzir relatório de origem por
   campo/definição.
7. Manter mods estritamente data-only nesta etapa.
8. Expor leitura/validação/publicação administrativa por REST, sem permitir que
   uma edição altere silenciosamente run ativa.

**Testes:** DAG, ciclo, versão, ordem, conflito, patch inválido, path traversal,
case collision, mesmo bundle em ordens de enumeração de filesystem diferentes e
mesmo revision hash em restart.

**Aceite:** base game é também um package e um setting publicado pode ser
reconstruído byte a byte a partir de manifests e arquivos.

**Commit sugerido:** `feat(mods): compile deterministic data packages into settings`

### Etapa 7 — Migrar todo gameplay para o bundle canônico

**Objetivo:** nenhum fallback de conteúdo participa de uma run.

1. Criar um registry único de content kinds contendo path canônico, tipo CLR,
   deserializer estrito, normalizador e validator.
2. Fazer manifest, compiler, runtime e graph validator consumirem esse registry.
3. Tornar `IContentRuntimeResolver` obrigatório em todo serviço de gameplay.
4. Remover paths de leitura direta e fallbacks de `RunManager`, action, cards,
   resources, entity, gambit e formulas.
5. Remover `strictMode: false` da publicação e fazer erro de patch abortar bundle.
6. Remover `BaseDefinitionId`/merge de entidade; herança ocorre somente no
   compiler de packages.
7. Migrar todos os JSONs para diretórios lowercase e formatos declarados.
8. Excluir `data/configs/default/Entities`, conteúdo rastreado em `UserData`,
   aliases antigos e itens inexistentes do `.slnx`.
9. Tornar mode/setting/revision obrigatórios para iniciar run.
10. Manter hot reload como draft -> validate -> publish -> activate command.

**Testes:** busca por APIs proibidas/paths legados, unknown property, conteúdo
ausente, revision indisponível, ativação compatível/incompatível e replay através
de duas revisões.

**Aceite:** alterar arquivos não publicados ou configuração global não muda uma
run; toda definição usada pode apontar package, artifact hash e revision.

**Commit sugerido:** `refactor(content): remove runtime loaders and implicit fallbacks`

### Etapa 8 — Decompor `RunManager`

**Objetivo:** deixar o agregado grande, mas o serviço de aplicação pequeno.

1. Separar `IRunQueryService` de `IRunCommandGateway`.
2. Criar `RunSessionCoordinator` apenas para load, lock por run, idempotência,
   execução do handler, append do commit e publicação pós-commit.
3. Mover start, map, deck, reward, shop, preparation, collectibles, content
   activation e encounter para handlers próprios.
4. Mover geração de ofertas e resolução de definições para serviços puros
   específicos.
5. Eliminar dependências opcionais; uma composição incompleta deve falhar no
   startup.
6. Remover métodos públicos laterais de mutação de `IRunManager`.
7. Trocar lock global por serialização por aggregate e fluxo async até storage.
8. Eliminar `AsyncLocal<RunCommand>`; o envelope acompanha o plano explicitamente.
9. Garantir que nenhum handler chama persistence, EventBus ou outro handler por
   método público.

**Testes:** regra arquitetural de dependências, duas runs concorrentes, dois
comandos concorrentes na mesma run, rollback de cada handler e idempotência após
restart.

**Aceite:** coordinator não conhece regras de shop/deck/combat e cada handler
retorna somente `RunTransitionPlan`.

**Commit sugerido:** `refactor(run): split aggregate coordination from domain handlers`

### Etapa 9 — Tornar progressão e atividades configuráveis

**Objetivo:** fechar a máquina de estado da run sem aliases hardcoded.

1. Adicionar `RunLifecycleState` e `RunProgressionPolicyDefinition` ao mode.
2. Substituir `NodeType` livre por `RunActivityDefinition` discriminada e
   validada pelo registry.
3. Implementar handlers iniciais para encounter, card selection, shop,
   preparation e card upgrade.
4. Fazer cada handler expor ações legais e condição de conclusão.
5. Aplicar entry/exit effects pelo executor universal.
6. Mover tick de modifiers de nó/run para boundaries explícitos da progressão.
7. Configurar políticas para vitória, derrota, draw, fim de mapa, retry e
   abandono; nenhuma delas deve depender do nome de um resource.
8. Derivar `availableCommands` de todos os handlers, inclusive payload schema,
   alvos/opções válidos e expected version.
9. Separar meta-progressão em projeção de commits; ela não pode alterar uma run
   retroativamente.

**Testes:** loop combate -> recompensa -> shop -> boss -> conclusão; derrota que
continua e derrota que encerra em modos distintos; node effect; comando ilegal;
fim de mapa; replay/branch em cada atividade.

**Aceite:** adicionar um tipo de atividade não exige editar `RunMapTransitions`
nem um switch central de strings.

**Commit sugerido:** `feat(progression): drive run lifecycle through configured activities`

### Etapa 10 — Unificar definição e estado de entidades

**Objetivo:** uma entidade runtime, totalmente imutável e component-driven.

1. Definir `EntityDefinition.Components` com discriminadores e schemas estritos
   para resources, stats, inventory, abilities e AI binding.
2. Definir `EntityState`/`CombatActorState` com `instanceId`, `definitionId`,
   `contentRevision`, `sideId`, `controllerBinding` e component states.
3. Migrar `CombatState` para roster ordenado/dicionário, removendo
   `Hero`, `Enemies` e `IsHero` da regra.
4. Fazer cenário declarar participantes, lado e controller explicitamente.
5. Substituir `EntityCombatAdapter` por um materializador unidirecional puro
   definition -> state.
6. Remover `Entity`, `IComponent`, `ComponentBase`, components mutáveis,
   callbacks e `EntityFactory` antigos.
7. Tornar stats um mapa configurável; nomes como strength/dexterity não têm
   semântica no core e entram em fórmulas somente por ID.
8. Atualizar targeting, ownership, calculations, turn order, sandbox e DTOs para
   o roster genérico.

**Testes:** dois lados e três lados, múltiplos atores player-controlled, ausência
de herói, componente desconhecido, cópia defensiva, materialização determinística
e derrota por políticas de resources arbitrários.

**Aceite:** busca estática não encontra `IsHero`, `CombatState.Hero/Enemies`,
components mutáveis nem caminho de sincronização pós-combate.

**Commit sugerido:** `refactor(entities): use one immutable component actor model`

### Etapa 11 — Consolidar decisão de IA e intents

**Objetivo:** IA é uma política pura consumidora do mesmo sistema de legalidade.

1. Remover `IEntityController`, `PlayerController`, `AIController` e
   `GambitController`.
2. Implementar `ControllerBindingDefinition` e registry de decision policies.
3. Criar `LegalActionResolver` compartilhado por REST, player, AI e preview.
4. Transformar gambits em definitions fixadas e `GambitDecisionReducer` sem
   cache, filesystem, CRUD ou fallback.
5. Substituir condições hardcoded por predicates/target selectors comuns e
   fórmulas validadas.
6. Fazer decision retornar contexto sucessor se consumir RNG; ordenar regras,
   alvos e empates ordinalmente.
7. Implementar `IntentPolicyDefinition` com lock/recompute e comportamento em
   caso de invalidação.
8. Produzir estimated outcomes através de preview canônico, com fingerprint e
   indicação de incerteza, nunca por soma de `FlatValue`.
9. Fazer execução automática reenviar a decisão como comando normal e validar
   novamente pelo gateway.

**Testes:** mesma decisão em dez runtimes, IA sem ação legal, empate, target
multi-side, status que bloqueia ação, intent locked/recomputed, preview idêntico à
execução e gambit ausente como erro de conteúdo.

**Aceite:** não existe ação de IA que ignore a lista de ações legais ou bypass o
executor canônico.

**Commit sugerido:** `refactor(ai): select canonical legal actions with pure policies`

### Etapa 12 — Levar ordem de turnos para as regras do modo

**Objetivo:** ordem/ativação usa somente JSON fixado e estado serializável.

1. Mover toda `TurnOrderConfiguration` de `appsettings` para
   `CombatRulesDefinition`.
2. Criar policies discriminadas para fixed, resource/speed, initiative, ATB e
   conditional formula.
3. Remover callbacks/delegates não serializáveis da estratégia conditional.
4. Definir boundary de recálculo por estratégia: combat start, round start,
   activation end ou continuous tick.
5. Fazer seeded tie-break incluir o epoch configurado e retornar contexto quando
   a política exigir consumo real de RNG.
6. Persistir `TurnOrderState` específico e genérico no snapshot, sem estado em
   singleton.
7. Remover `CombatOptions` e a seleção host-global do `Program.cs`.

**Testes:** todas as estratégias em dois modos simultâneos, recálculo após
alteração de resource, tie bias, ATB em replay/branch e ausência de appsettings no
hash de resultado.

**Aceite:** duas runs no mesmo processo podem usar estratégias diferentes sem
interferência.

**Commit sugerido:** `refactor(turns): resolve activation order from pinned mode content`

### Etapa 13 — Implementar o grafo real de fases

**Objetivo:** campos de fase deixam de ser decorativos.

1. Adicionar entry phase explícita e edges com condição/prioridade.
2. Validar reachability, dead ends, ciclos automáticos, ambiguidade de edge e
   presença de ao menos um `Start`, `Middle` e `End` alcançável.
3. Implementar `PhaseGraphReducer.Enter`, `HandleCommand`, `Exit` e
   `AdvanceAutomatic`.
4. Guardar somente sequence ID/revision/cursor no `CombatFlowState`.
5. Converter lifecycles de status, relic, resource, deck e modifiers em
   boundaries associados a entry/exit de fase, round e activation.
6. Executar entry/exit effects pelo executor universal e preservar traces.
7. Usar allowed command types/tags da fase no `LegalActionResolver`.
8. Aplicar `AllowPhaseSkipping` por política executável ou remover o campo.
9. Proteger transições automáticas com limite determinístico configurado.

**Testes:** fluxo mínimo, múltiplas fases Middle, escolha condicional, skip,
ciclo inválido, terminal em Start/End, status em boundary e hash igual em dez
execuções.

**Aceite:** sequences Magic/Yu-Gi-Oh/Hearthstone só permanecem no conteúdo se
forem integralmente executáveis; presets fictícios são excluídos.

**Commit sugerido:** `feat(phases): execute validated configurable combat graphs`

### Etapa 14 — Implementar prioridade, reações e stack

**Objetivo:** completar as opções hoje reservadas sem criar manager paralelo.

1. Finalizar schemas de priority window, reaction eligibility, stack order,
   target/cost locking e outcome boundary.
2. Adicionar `PriorityWindowState` e `PendingActionState` ao snapshot.
3. Implementar comandos `PASS_PRIORITY` e resposta normal por action/card.
4. Implementar reducers puros para abrir janela, propor ação, passar, reabrir,
   desempilhar e fechar.
5. Integrar `Disabled`, `Automatic` e `PriorityStack` ao mesmo flow reducer.
6. Reservar/pagar/restituir custos transacionalmente conforme policy; falha na
   resolução descarta toda a transação atual sem corromper stack anterior.
7. Fazer IA participar da prioridade pelo mesmo `LegalActionResolver` e decision
   policy.
8. Avaliar vitória/derrota no boundary configurado, inclusive após stack.
9. Emitir frames/facts completos para Godot renderizar proposta, resposta,
   passes e resolução.
10. Expor stack, priority holder, respostas legais e pass count no snapshot.

**Testes:** LIFO/FIFO configurado, todos passam, resposta à resposta, target
morto, custo no propose/resolve, IA passa/responde, limite de profundidade,
outcome atrasado, branch com stack aberta, replay e retry.

**Aceite:** toda estratégia publicável tem execução; não existem
`AllowPriority`, `ReactionStrategy` ou outcome boundary aceitos e ignorados.

**Commit sugerido:** `feat(reactions): add immutable priority windows and action stack`

### Etapa 15 — Reduzir planner e coordinator a composição

**Objetivo:** fechar os dois supermódulos do combate.

1. Fazer `CombatCommandHandler` validar e compor reducers sem conter regra
   específica de AI/deck/status/relic.
2. Fazer `AutomaticFlowDriver` avançar somente enquanto não houver input e
   respeitar limite do modo.
3. Fazer `CombatBoundaryExecutor` ordenar lifecycles declarados e gerar frames.
4. Manter `CombatOutcomeResolver` isolado e genericamente baseado em sides,
   controllers e políticas de resources.
5. Reduzir `CombatRunCoordinator` a adapter temporário e então removê-lo em favor
   do handler da run.
6. Remover métodos estáticos duplicados e aliases antigos de comando/transição.

**Testes:** regra arquitetural de tamanho/dependência, composição de lifecycles,
automatic stop em player/priority input, rollback do último passo e equivalência
manual/simulation.

**Aceite:** nenhum serviço de fluxo concentra lifecycle, IA, conteúdo,
persistência e execução de ação ao mesmo tempo.

**Commit sugerido:** `refactor(combat): compose flow from focused immutable reducers`

### Etapa 16 — Consolidar REST, Godot e documentação

**Objetivo:** expor a arquitetura final sem rotas ou conceitos antigos.

1. Atualizar OpenAPI para actors/sides, phase cursor, priority, stack, legal
   commands, root sequence, frames, facts, lineage e revision provenance.
2. Manter controllers como tradução HTTP e queries; nenhuma regra ou lookup
   lateral de conteúdo.
3. Atualizar o protótipo Godot para tratar cada receipt como uma unidade, tocar
   frames e aguardar somente quando snapshot indicar input/priority.
4. Atualizar navegador de timeline/branches usando commit sequence e frame index.
5. Remover endpoints, DTOs e documentos de Entity CRUD/gameplay direto,
   snapshots legados e nomes hero/enemy autoritativos.
6. Reescrever `timeline-system.md`, config/mod docs, eventos e roadmap; remover
   afirmações antigas de EventBus como event sourcing.
7. Limpar `HeroScript.slnx` e adicionar documentação de package/setting.

**Testes:** OpenAPI snapshot, controller architecture, cliente Godot simulado,
409/idempotência, SSE reconnect, stack aberta e troca de branch.

**Aceite:** uma Godot vazia pode renderizar, escolher ação, responder/passar
prioridade, navegar histórico e mudar branch usando apenas o contrato REST.

**Commit sugerido:** `docs(api): publish unified gameplay and history contracts`

### Etapa 17 — Verificação final e remoção de resíduos

**Objetivo:** provar que as cinco revisões formam uma arquitetura única.

1. Remover adapters temporários, tipos órfãos, fallbacks e schemas não
   alcançáveis.
2. Executar busca estática pelas APIs proibidas e nomes legados.
3. Executar build completo e todas as suites.
4. Executar o cenário dourado dez vezes em processos/runtimes novos.
5. Reexecutar semanticamente cada resultado e comparar commits, states, RNG,
   frames, facts, intents e fingerprints.
6. Reiniciar a API no meio de run, de branch, de activity e de stack aberta.
7. Excluir caches/projeções e provar reconstrução.
8. Corromper cópias de teste de commit/content e provar fail-fast.
9. Medir limites de stack, timeline, branch e replay; registrar budgets, sem usar
   otimização que altere ordem semântica.

**Aceite final:** mesmos setting/revision/seed/comandos produzem bytes canônicos
iguais; nenhuma run depende de estado de processo, filesystem mutável,
appsettings de regra ou implementação duplicada.

**Commit sugerido:** `test(architecture): verify unified deterministic gameplay runtime`

## 5. Dependências entre etapas

```text
0 contratos
  -> 1 comando/plano
      -> 2 commit store
          -> 3 projeções
          -> 4 replay/composição
              -> 5 branches
              -> 6 packages/settings
                  -> 7 bundle-only gameplay
                      -> 8 decomposição da run
                          -> 9 progressão
                          -> 10 entidades
                              -> 11 IA/intents
                              -> 12 ordem de turnos
                                  -> 13 grafo de fases
                                      -> 14 prioridade/stack
                                          -> 15 composição do combate
                                              -> 16 REST/Godot/docs
                                                  -> 17 verificação final
```

Etapas 3 e 4 podem ser desenvolvidas em paralelo apenas depois do contrato de
`RunCommit`, mas devem ser integradas antes de branches. As demais devem manter a
ordem para evitar adapters duradouros.

## 6. Matriz mínima de testes de aceite

| Garantia | Teste obrigatório |
| --- | --- |
| Determinismo | dez runtimes novos com igualdade de commit/state/frame/fact |
| Imutabilidade | mutation isolation de todas as collections e component states |
| Atomicidade | falha no último reducer/storage não publica estado nem consome RNG |
| Idempotência | mesmo command ID/envelope retorna mesmo receipt após restart |
| Conteúdo | mesmo setting produz mesma revision independentemente da enumeração de arquivos |
| Hot reload | revisão só muda por comando e replay usa cada revisão histórica |
| IA | escolhe apenas ação legal; preview e execução compartilham pipeline |
| Fases | todo edge e campo aceito é exercitado pelo runtime |
| Stack | janela aberta sobrevive restart, branch e replay |
| Progressão | outcomes configurados geram continue/complete/fail corretamente |
| Histórico | timeline/eventos/latest reconstroem apenas de commits |
| Branch | pai imutável, lineage íntegra, owners/referências consistentes |
| API | nenhum controller calcula regra; OpenAPI cobre todo comando/snapshot |

## 7. Breaking changes deliberados

Como o projeto não saiu da pré-produção, o plano prefere remoção direta a
compatibilidade:

- saves e `legacy.snapshot` atuais deixam de ser aceitos;
- `sequence` passa a contar comandos raiz, não frames internos;
- DTOs `hero/enemies` tornam-se `actors/sides`;
- `IsHero` e `EntityType` deixam de decidir gameplay;
- rotas de edição direta de entidade/gambit não alteram conteúdo publicado;
- mode, setting e content revision tornam-se obrigatórios;
- JSON sem envelope/schema ou patch explícito é rejeitado;
- presets de fase não executáveis são removidos;
- endpoints administrativos de EventBus são descritos como telemetria, não
  histórico de gameplay.

O protótipo Godot deverá ser atualizado na Etapa 16. Não haverá camada permanente
de tradução dos contratos antigos.

## 8. Definition of Done global

O plano estará concluído quando:

1. `RunState` for a única autoridade de gameplay e `RunCommit` a única autoridade
   histórica.
2. Toda mutação entrar pelo gateway e produzir um único commit raiz atômico.
3. Replay e simulation usarem a mesma composição e os mesmos handlers live.
4. Timeline, eventos, snapshots e lineage forem projeções reconstruíveis.
5. Toda run usar exclusivamente um bundle publicado e fixado.
6. Mods forem packages data-only com dependências e patches determinísticos.
7. Houver um único modelo imutável de ator, sem `IsHero` na regra.
8. Player e IA consumirem o mesmo conjunto de ações legais.
9. Ordem, fases, prioridade, stack e outcome forem políticas JSON integralmente
   executáveis.
10. Progressão não depender de aliases hardcoded de nó ou resource.
11. O build, Core.Tests, API.Tests, replay, restart e dez execuções determinísticas
    passarem sem resíduos de compatibilidade.


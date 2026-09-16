# Changelog do contrato público

## v1 — 2026-09-16

- O estado da run expõe `cardZones` em vez da projeção fixa `deck`.
- As rotas específicas `/runs/{runId}/deck` e `/runs/{runId}/hand` foram
  removidas; `/runs/{runId}/card-zones` é a única projeção das zonas.
- A inspeção de carta usa `isInPlayableZone`, derivado de `allowsCardPlay`, sem
  presumir uma zona chamada `hand`.
- Runs e cenários recebem `startingCards`; a entrada não presume que essas
  instâncias formarão um deck, pois o grafo decide a zona inicial.
- Fronteiras autoradas publicam `CardZonesTransitionedEvent`, com os passos e o
  hash da topologia. `CardDrawnEvent` fica restrito ao executor legado sem grafo.
- Os comandos específicos `DRAW_CARDS`, `DISCARD_CARDS`, `MOVE_CARDS`,
  `ADD_CARDS_TO_HAND` e `SHUFFLE_DISCARD` não são mais publicados pelo codec.

## v1 — 2026-09-15

- `GET /runs/{runId}/card-zones` expõe a topologia de cartas e a apresentação
  revisionada do modo sem impor nomes ou funções às zonas. O campo
  `topologyHash` identifica o snapshot; conteúdo e ordem não autorizados são
  ocultados na projeção. A capacidade `run-card-zones` anuncia o contrato.
- `INVOKE_CARD_ZONE_FLOW` permite a modos com ferramentas de zonas executar
  somente fluxos `Tool` publicados no grafo. Instâncias e definições de carta
  são entradas; colocação, seleção e ordem continuam autoradas em JSON.

## v1 — 2026-09-13

- Atividade `Dialogue` e conteúdo `dialogues` revisionado. `START_DIALOGUE` e
  `CHOOSE_DIALOGUE_OPTION` usam o gateway canônico de comandos da run.
- A leitura de run inclui `dialogues` (falas, opções, disponibilidade e
  transcrição) e `narrativeFlags`. Custos e efeitos de uma resposta são atômicos.
- Consulte [Sistema de diálogos](../systems/dialogue-system.md) para autoria,
  integração Godot, comportamento de revisões e limitações de contexto.

## v1 — 2026-09-11

- Candidatos de `/combats/{combatId}/legal-actions` expõem `costs`, obtidos da
  avaliação canônica de cartas, habilidades e reações. O cliente não precisa
  reconstruir custos a partir do conteúdo ou do texto de apresentação.
- O snapshot de combate passou a expor os status ativos de cada ator para que
  clientes possam renderizá-los sem reconstruir regras ou consumir outro estado.
- O read model de run agora inclui modifiers e atividades concluídas.
- Encontros publicam no próprio comando `START_ENCOUNTER` o payload canônico
  configurado no nó; `RESOLVE_COMBAT` usa a versão do combate que realmente
  será resolvido.
- Recompensas de relíquia possuem atividade própria e transação canônica, sem
  permitir aquisição fora do fluxo em modos que não habilitam ferramentas livres.
- O showcase Godot exercita campanha, sandboxes, timeline, branches, simulação
  e replay consumindo exclusivamente a API versionada.

## v1 — 2026-09-09

- O read model de combate agora documenta e expõe atores, lados, relações,
  cursor de fase, ativação, prioridade e stack pendente na mesma resposta.
- Resoluções usam uma única `rootSequence`; `firstSequence`/`finalSequence`
  foram removidos porque um comando externo produz exatamente um commit.
- A timeline expõe `stateAvailable`, frames e facts por item, e a árvore de
  branches possui schema de linhagem explícito.
- O catálogo paralelo `/entities/definitions` e seus DTOs foram removidos.
  Entidades são conteúdo em `/api/v1/content/entities`.
- `/runs/{runId}/commits` substitui a documentação antiga de checkpoints e o
  cliente Godot de referência passa a ser orientado a receipts/snapshots.

- O snapshot de combate passou a expor `priorityWindow` e `pendingActions` como
  estado imutável; não foi reintroduzido endpoint paralelo `/stack`.
- Ações legais e previews agora informam `reactionTransition`, comando resolvido
  e ação pendente. `PASS_PRIORITY` usa o mesmo gateway de comandos.
- Resoluções visuais distinguem proposta, passe, resolução e fizzle; custos
  pagos na proposta e reembolsos preservam traces de aplicação.
- O conteúdo opt-in `priority_stack_combat` demonstra LIFO, lock de alvo,
  pagamento na resolução e outcome após esvaziar a stack.

## v1 — 2026-09-06

- O vocabulário de `EffectType` agora contém somente primitivas executáveis.
  Campos rejeitados/inertes (`timing`, `isPercentage`, modifier numérico legado e
  `conditionalEffects`) foram removidos; timing pertence ao trigger do owner e
  condição pertence ao próprio efeito/filho encadeado.
- Definições de status e modifier deixaram de expor campos comportamentais ou
  numéricos paralelos. Regras são compostas por triggers, influências, restrições
  e políticas de instância.
- A publicação/runtime de conteúdo passou a rejeitar propriedades JSON
  desconhecidas. O fallback legado `card.actionId` e o catálogo antigo fora de
  `Resources` foram removidos; cartas autoritativas exigem componentes/bundles.
- Scopes e desempates de combate usam lados/controllers (`RunOwner`,
  `PlayerControlled`, `AiControlled`, `All`) em vez de papéis de herói/inimigo.
- Aplicações de modifier passaram a expor todas as instâncias removidas e o trace
  anterior/posterior de stacks em `modifierStackChanges`.
- Resoluções de combate agora expõem hashes de estado inicial/final, fingerprint
  da fila e traces tipados de efeitos, cálculos e aplicações em cada frame.
- A avaliação de cartas passou a expor `previewSteps`, produzido pela mesma
  transação pura usada na execução.
- O início do encontro passou a gerar a resolução durável
  `combat.initialized`, preservando triggers de abertura para animação e replay.
- Itens da timeline apontam para a fila correspondente por
  `resolutionCommandId` e `resolutionFingerprint`.
- Removido do contrato versionado o endpoint não implementado `/stack`; o estado
  de reação passou posteriormente a integrar o snapshot canônico.

## v1 — 2026-09-03

- Removidas as APIs paralelas baseadas em estado global para ações, dano,
  efeitos, status, modificadores e gambits. Definições agora vêm de
  `/api/v1/content`; mutações passam exclusivamente pelos gateways de comando.
- Removidas as projeções duplicadas `legal-actions`, `legal-targets`,
  `available-actions`, `cost-options` e `can-afford`. Clientes usam as
  avaliações de instâncias de carta, que compartilham as regras de `PLAY_CARD`.
- Adicionada avaliação determinística de uma carta e projeção em lote da mão,
  incluindo container base/compilado, upgrades, legalidade, buckets, efeitos
  previstos e fingerprint da resolução.
- O game mode agora controla o detalhe da inspeção entre `Disabled`, `Resolved`
  e `Full`; o sandbox expõe as fontes contextuais completas para theorycraft.
- `isPlayable` compartilha as regras canônicas de ator ativo, fase e orçamento
  usadas pelo gateway de comandos.

## v1 — 2026-09-02

- Comandos de combate passaram a expor uma resolução visual durável, composta
  por frames ordenados e recuperável por `commandId` após reconexão.
- Foram documentados os modos `FullSnapshots` e
  `CompactWithSnapshotLookup`, incluindo a busca histórica por
  `snapshotSequence`.
- Os sandboxes iniciais agora exercitam os dois orçamentos de ação: energia
  configurada e quantidade fixa por ativação.
- Foram removidas as mutações diretas de combate (`start`, `action`,
  `end-turn`, `process-ai-turns`, `end` e `auto-play`). Toda partida passa a
  pertencer a uma run e aos gateways canônicos; omitir `modeId` seleciona
  `standard`.

## v1 — 2026-09-01

- Todas as respostas HTTP não bem-sucedidas foram normalizadas como RFC 9457
  Problem Details, com `code` estável e `correlationId`.
- Eventos duráveis passaram a expor correlação, configuração, revisão de
  conteúdo, seed, versão esperada e hash canônico do comando.
- Publicações de conteúdo agora validam o grafo semântico inteiro e só trocam a
  revisão ativa de forma atômica.
- A superfície v1 permanece pré-produção: não há rotas legado nem compromisso de
  compatibilidade com contratos anteriores ao v1 documentado.

## v1 — 2026-08-16

- Publicado contrato OpenAPI em `/openapi/v1.json`.
- Definidos gateways idempotentes de comandos de run e combate.
- Documentadas projeções SSE por AsyncAPI.
- Consolidada a superfície HTTP em `/api/v1`; rotas sem versão não são
  expostas.

Mudanças incompatíveis futuras serão anunciadas aqui antes da remoção de uma
rota estável ou de um campo público.

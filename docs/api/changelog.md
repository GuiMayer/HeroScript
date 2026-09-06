# Changelog do contrato público

## v1 — 2026-09-06

- Resoluções de combate agora expõem hashes de estado inicial/final, fingerprint
  da fila e traces tipados de efeitos, cálculos e aplicações em cada frame.
- A avaliação de cartas passou a expor `previewSteps`, produzido pela mesma
  transação pura usada na execução.
- O início do encontro passou a gerar a resolução durável
  `combat.initialized`, preservando triggers de abertura para animação e replay.
- Itens da timeline apontam para a fila correspondente por
  `resolutionCommandId` e `resolutionFingerprint`.
- Removido do contrato versionado o endpoint não implementado `/stack`;
  reações continuam somente como capacidade reservada e desabilitada.

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

# Changelog do contrato público

## v1 — 2026-09-02

- Comandos de combate passaram a expor uma resolução visual durável, composta
  por frames ordenados e recuperável por `commandId` após reconexão.
- Foram documentados os modos `FullSnapshots` e
  `CompactWithSnapshotLookup`, incluindo a busca histórica por
  `snapshotSequence`.
- Os sandboxes iniciais agora exercitam os dois orçamentos de ação: energia
  configurada e quantidade fixa por ativação.

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

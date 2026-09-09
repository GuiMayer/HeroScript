# Persistência autoritativa e telemetria

HeroScript separa estado de gameplay de observabilidade.

## Run commits

`IRunCommitStore` é a autoridade persistente. `FileRunCommitStore` grava um
commit append-only para cada comando externo aceito. O commit contém envelope,
estado posterior, hashes, steps, frames e facts. Append, índice idempotente e
head da run avançam como uma única operação observável; um conflito de versão
não publica estado parcial.

O caminho é configurado por `Persistence:RunStatePath` (padrão `data/runs`). A
recuperação lê o último commit; a inspeção histórica lê uma sequence específica;
replay reexecuta os comandos e compara o resultado canônico.

Não existe mutação direta de arquivo de snapshot. Snapshots são otimizações de
leitura substituíveis e podem ser reconstruídos dos commits.

## Conteúdo

Bundles publicados vivem em `Persistence:ContentStorePath` (padrão
`data/content`). A revisão é o hash do manifest canônico. Runs fixam
`settingId`, `contentRevision` e `engineVersion`; apagar caches não muda essas
coordenadas.

## Telemetria operacional

`IOperationalEventStore` e `JsonFileOperationalEventStore` usam
`Persistence:OperationalTelemetryPath` (padrão `data/telemetry`). Esses eventos
servem a logs e diagnóstico. Eles não são a origem de replay e sua perda não
altera gameplay.

Os eventos SSE de run/combate são projeções duráveis de commits, não o histórico
em memória do EventBus.

## REST de recuperação

| Objetivo | Endpoint |
| --- | --- |
| Estado atual | `GET /api/v1/runs/{runId}` |
| Journal | `GET /api/v1/runs/{runId}/journal` |
| Commits | `GET /api/v1/runs/{runId}/commits` |
| Commit | `GET /api/v1/runs/{runId}/commits/{sequence}` |
| Verificar replay | `POST /api/v1/runs/{runId}/verify` |
| Eventos retomáveis | `GET /api/v1/runs/{runId}/events/stream` |

Clientes devem reenviar o mesmo envelope e `commandId` após uma resposta
incerta. O store devolve o receipt persistido com `duplicate: true`; gerar outro
ID representa uma nova intenção.

## Requisitos de implementação alternativa

Um backend de produção pode substituir `FileRunCommitStore`, desde que preserve:

- compare-and-append por `runId + expectedSequence`;
- idempotência por comando e hash de payload;
- ordenação exata de frames/facts;
- leitura por sequence e head;
- escrita atômica de commit e índices;
- bytes canônicos independentes do host.

SQLite, PostgreSQL ou outro banco são detalhes de infraestrutura. Um banco de
event sourcing genérico não substitui esses invariantes automaticamente.

# Timeline, replay e branches

## Autoridade

O `RunCommit` é a única unidade autoritativa de histórico. Um comando aceito
gera exatamente um commit contendo:

- a identidade do comando raiz e seu payload canônico;
- hashes anterior e posterior, intervalo de steps e estado posterior;
- frames internos ordenados, usados para explicar a transição;
- facts duráveis, usados por projeções e integrações;
- a linhagem somente no primeiro commit da run.

Snapshots, timeline, eventos e filas visuais são projeções. Nenhuma delas pode
ser usada para aplicar regras ou reconstruir gameplay sem os commits.

## Coordenadas

Uma posição autoritativa é identificada por `(runId, sequence)`. A seleção
visual dentro de um comando usa `(runSequence, frameIndex)`. Todos os frames de
um receipt compartilham a mesma `rootSequence`; `frameIndex` é ordinal e começa
em zero.

`combatStep` ordena a evolução determinística dentro do combate, mas não
substitui a sequence do journal. Hashes permitem verificar que cliente,
projeção e armazenamento apontam para o mesmo estado.

## Leituras REST

| Necessidade | Endpoint |
| --- | --- |
| Commits da run | `GET /api/v1/runs/{runId}/commits` |
| Commit específico | `GET /api/v1/runs/{runId}/commits/{sequence}` |
| Timeline de combate | `GET /api/v1/combats/{combatId}/timeline` |
| Estado histórico | `GET /api/v1/combats/{combatId}/timeline/{sequence}/state` |
| Resolução visual | `GET /api/v1/combats/{combatId}/resolutions/{commandId}` |
| Árvore de branches | `GET /api/v1/runs/{runId}/branch-tree` |

A timeline traz frames e facts do commit, além de
`resolutionCommandId`. `stateAvailable` indica que a sequence pode ser aberta
como estado histórico. Em modo compacto, `snapshotSequence` do frame aponta
para a leitura histórica correspondente.

## Undo, exploração e branch

O estado existente nunca é rebobinado ou sobrescrito. Para continuar de um
ponto anterior, crie uma branch em
`POST /api/v1/combats/{combatId}/timeline/{sequence}/branches`. A engine fixa no
filho `rootRunId`, `parentRunId`, `sourceSequence`, `sourceStateHash`,
`sourceCombatId` e `branchKey`. A linha pai permanece imutável.

Um botão visual de “voltar” deve primeiro abrir o estado histórico em modo
somente leitura. Se o jogador quiser agir, o cliente cria a branch e troca
`runId`/`combatId` antes de habilitar input.

## Replay

`POST /api/v1/runs/{runId}/verify` reexecuta semanticamente os comandos com a
seed, versão da engine e revisões de conteúdo registradas. A validação compara
estado, hashes, frames, facts e fingerprints. Repetir comandos no estado atual
ou reverter deltas localmente não é replay.

## Eventos

O stream SSE é uma projeção retomável dos commits e aceita cursor de sequence.
O EventBus operacional não participa de replay. Perder sua memória ou seu
armazenamento de telemetria não altera a run.

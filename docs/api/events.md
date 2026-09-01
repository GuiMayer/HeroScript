# Eventos duráveis e SSE

Os streams v1 são projeções do journal, descritas em
`asyncapi/heroscript-events-v1.json`. Eles existem para atualizar interfaces e
integrações; comandos e replay continuam sendo as fronteiras autoritativas.

| Escopo | Consulta paginada | Stream SSE |
| --- | --- | --- |
| Run | `/api/v1/runs/{runId}/events` | `/api/v1/runs/{runId}/events/stream` |
| Combate | `/api/v1/combats/{combatId}/events` | `/api/v1/combats/{combatId}/events/stream` |

Cada evento tem `sequence`, `step`, hashes anterior/resultante, `commandId`,
`correlationId`, configuração, revisão de conteúdo, seed, versão esperada, hash
do payload do comando e payload projetado. Isso permite relacionar REST, journal,
logs e telemetria sem usar dados operacionais para decidir a simulação.

O servidor escreve `sequence` como `id` do SSE e aceita `Last-Event-ID` ou
`afterSequence` para retomar a partir de um cursor. Clientes devem persistir o
último ID processado e buscar o endpoint paginado se precisarem fechar uma
lacuna.

O formato atual emite `RUN_TRANSITION_COMMITTED` e usa `commandType` para
distinguir o efeito da transição. Novos tipos de evento exigem evolução do
contrato AsyncAPI e entrada no changelog.

O EventBus interno também persiste eventos operacionais correlacionados, mas não
é uma segunda fonte de verdade. Recuperação, idempotência, replay e SSE usam o
journal autoritativo e suas projeções.

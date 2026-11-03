# Diagnóstico e telemetria administrativa

Os endpoints abaixo são administrativos. Consulte [autenticação](authentication.md) para o controle de acesso da instância. A telemetria operacional é uma visão de melhor esforço da sessão e não substitui o journal ou os [eventos duráveis](events.md) como fonte de estado da run.

## Estatísticas e invalidação de cache

A base é `/api/v1/admin/diagnostics`:

- `GET /cache/stats`: estatísticas de todos os serviços registrados, incluindo `timestamp`, `services`, `totalServices` e `averageHitRate`.
- `GET /cache/stats/{cacheName}`: estatísticas de um serviço; cache desconhecido retorna `404`.
- `GET /health/cache`: estado calculado a partir das estatísticas e dos serviços; sua interpretação depende da atividade da instância.
- `POST /cache/invalidate/{cacheName}`: invalida o cache identificado.
- `POST /cache/invalidate/{cacheName}/key?key=<chave>`: invalida uma chave específica. `key` não pode ser vazia.

As invalidações retornam `200` com mensagem e timestamp, `404` para serviço desconhecido e `400` para nome ou chave vazios quando recebidos pela ação. Erros internos retornam `500`. Os POSTs alteram o cache operacional; não os use como consultas.

## Eventos operacionais

`GET /api/v1/admin/telemetry` aceita filtros opcionais `category`, `severity`, `limit` (padrão 100), `afterSequence`, `combatId`, `runId` e `eventType`. A resposta contém `total`, `returned`, `afterSequence`, `lastSequence` e `events`. Quando `afterSequence` é fornecido, retorna eventos posteriores em ordem; sem ele, retorna os eventos mais recentes dentro do limite. Quando uma categoria válida é fornecida, ela tem precedência sobre o filtro de severidade. Valores de categoria ou severidade não reconhecidos não são tratados como filtros válidos pela ação.

- `GET /api/v1/admin/telemetry/combats/{combatId}` aplica o filtro de combate e aceita `afterSequence`, `runId`, `eventType` e `limit`.
- `GET /api/v1/admin/telemetry/{eventId}` consulta um evento; `404` indica que ele não está no histórico da sessão.
- `GET /api/v1/admin/telemetry/categories` e `GET /api/v1/admin/telemetry/severities` listam os valores disponíveis.
- `DELETE /api/v1/admin/telemetry` limpa o histórico operacional e só é disponibilizado em ambiente de desenvolvimento; fora dele retorna `404`.

## Stream operacional

`GET /api/v1/admin/telemetry/stream` envia `text/event-stream`. Aceita `afterSequence` (padrão -1), `combatId`, `runId`, `eventType` e `delayMs`; o intervalo aplicado é de pelo menos 1000 ms. Cada evento contém `id` com a sequência, `event` com o tipo e `data` com o JSON do evento. `GET /api/v1/admin/telemetry/combats/{combatId}/stream` aplica o filtro de combate e aceita `afterSequence`, `runId`, `eventType` e `delayMs`.

Ao reconectar, o consumidor pode fornecer a última sequência recebida como `afterSequence`. Para retomada de eventos autoritativos de run ou combate, use os endpoints duráveis descritos em [eventos](events.md): o histórico operacional pode ser limitado ou limpo.

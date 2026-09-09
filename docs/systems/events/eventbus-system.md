# Eventos e observabilidade

## Duas responsabilidades diferentes

O HeroScript possui duas trilhas intencionalmente separadas:

| Trilha | Autoridade | Uso |
| --- | --- | --- |
| `RunCommit` + journal | autoritativa e transacional | recuperação, replay, timeline, branches |
| `IOperationalEventBus` | best-effort | logs, métricas, diagnóstico e notificações locais |

O EventBus não implementa event sourcing. O histórico pode ser limpo, truncado
ou ficar indisponível sem alterar nenhum resultado de gameplay.

## EventBus operacional

`OperationalEventBus` oferece publish/subscribe tipado, ordem local e histórico
de processo thread-safe. Handlers executam fora do lock; a falha de um handler
é registrada e não impede os demais. Quando há `IOperationalEventStore`, os
eventos também podem ser armazenados para diagnóstico.

`GameEventContext` adiciona correlação como `runId`, `combatId`, `commandId`,
`contentRevision` e `traceId`. Esses campos tornam a telemetria rastreável, mas
não a transformam em entrada determinística.

## Projeções duráveis

Os endpoints `/api/v1/runs/{runId}/events` e `/events/stream` são derivados do
journal autoritativo. O SSE usa sequence como cursor; após reconexão, o cliente
retoma do último ID recebido. A mesma regra vale para o stream filtrado de
combate.

Use:

- commits/frames/facts para auditoria de regras;
- SSE durável para sincronização de clientes;
- EventBus e `/api/v1/telemetry` para observabilidade operacional;
- `POST /api/v1/runs/{runId}/verify` para provar replay.

Não use handlers do EventBus para aplicar dano, avançar fase, consumir custo ou
persistir a única cópia de uma decisão.

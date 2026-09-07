# ADR 0006 — Telemetria operacional

**Status:** aceito

## Contexto

O EventBus já é usado para observabilidade, mas nomes e documentação ainda
sugerem que ele seja a fonte para reconstrução do jogo.

## Decisão

- Apenas commits reconstroem gameplay.
- Fatos determinísticos pertencem ao `RunCommit`.
- Eventos duráveis são projeções dos fatos, com IDs derivados do commit.
- `IEventBus` publica telemetria somente depois de um append bem-sucedido.
- Falha de telemetria não desfaz nem altera o commit de gameplay.
- Consumidores de telemetria não podem escrever no agregado.


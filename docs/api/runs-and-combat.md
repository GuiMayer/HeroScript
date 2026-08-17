# Runs, combates e replay

## Fluxo autoritativo

1. Crie a run com `POST /api/v1/runs`, informando seed e revisão de conteúdo
   quando o cliente precisar reproduzir uma partida externa.
2. Leia `GET /api/v1/runs/{runId}` e guarde `sequence`, `determinism.step`,
   `contentRevision` e hash retornados.
3. Consulte mapa e comandos permitidos em `/map` e `/available-commands`.
4. Envie uma mutação ao gateway `/commands` com as versões observadas.
5. Para encontro ativo, use o gateway de combate associado à run.
6. Atualize o cliente exclusivamente pelo estado e hashes da resposta aceita.

O contrato v1 é a única superfície HTTP exposta. Clientes que precisam de
retry, reconexão ou replay devem usar os gateways de comando descritos aqui.

## Exemplo: avançar uma run

```http
POST /api/v1/runs/{runId}/commands
Content-Type: application/json

{
  "commandId": "a4c5ba8f-6af4-4d84-96fb-f979c3ad598b",
  "expectedSequence": 3,
  "expectedStep": 7,
  "type": "ADVANCE_NODE",
  "payload": { "targetNodeId": "combat-2" }
}
```

Reenviar exatamente esse request deve devolver o recibo já persistido. Enviar
o mesmo `commandId` com payload diferente é inválido. Se outra decisão tiver
vencido a corrida, a resposta é `409` e informa a sequência e o step atuais.

## Encontro e combate

`START_ENCOUNTER` e `RESOLVE_COMBAT` são comandos de run porque modificam
simultaneamente a progressão. Dentro do encontro, use:

- `GET /api/v1/combats/{combatId}` para o read model;
- `GET /legal-actions` e `/legal-targets` para não duplicar regras no cliente;
- `POST /commands` com `EXECUTE_ACTION` ou `END_TURN`;
- `GET /stack` para sistemas TCG que exibem prioridade e pilha.

O payload de `EXECUTE_ACTION` aceita `actionId` (preferível), `actorId`,
`targetId`, `costOptionId` e `cardId` quando aplicável. A API resolve o tipo da
ação a partir do conteúdo fixado na run.

## Recuperação e auditoria

Use os seguintes recursos depois de reconectar ou para suporte:

| Objetivo | Endpoint |
| --- | --- |
| Estado completo | `GET /api/v1/runs/{runId}` |
| Histórico de comandos | `GET /api/v1/runs/{runId}/journal` |
| Checkpoints e timeline | `/checkpoints` e `/timeline` |
| Replay semântico | `POST /api/v1/runs/{runId}/verify` |
| Auditoria de encontro | `/api/v1/combats/{combatId}/journal` e `/verify` |

O journal é a fonte de auditoria; eventos servem como projeção. Uma verificação
de replay reexecuta comandos usando seed, versão da engine e conteúdo fixado,
comparando hashes em vez de confiar no estado enviado pelo cliente.

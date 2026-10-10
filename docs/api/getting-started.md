# Quickstart: uma run determinística

Consulte `GET /api/v1/content/settings` e copie `currentRevision` do setting
desejado. Defina seed, modo e um `commandId` por tentativa, então crie a run.
Guarde o `runId`, a sequência e o step retornados.

```http
POST /api/v1/runs
Content-Type: application/json

{
  "settingId": "default",
  "contentRevision": "<currentRevision do setting default>",
  "modeId": "standard",
  "runDefinitionId": "default_run",
  "playerEntityId": "player",
  "seed": 424242,
  "commandId": "978346b8-8564-4f4b-aa91-3b2b8e74482c"
}
```

Reenvie o mesmo request em retry. Isso recupera a criação original, incluindo
sua elegibilidade, não o estado atual da partida. Outra tentativa exige um
novo `commandId`. Veja [o contrato](../content/run-eligibility.md).

Leia o estado e os comandos que o mapa permite. O cliente não deve decidir
quais comandos são válidos por conta própria.

```http
GET /api/v1/runs/{runId}
GET /api/v1/runs/{runId}/available-commands
```

Para mutar, use o gateway com os valores observados. Por exemplo, compre uma
carta, avance um nó ou compre cartas usando um `commandId` novo para cada
intenção do usuário. Em caso de timeout, reenvie o mesmo request.

Quando receber `409`, descarte a intenção local, recupere o read model e peça
uma nova decisão ao usuário. Quando receber `422`, preserve o estado local e
mostre a violação de regra; não tente corrigir a resposta por heurística.

Ao finalizar ou auditar, consulte `/journal` e execute `POST /verify`. Uma run
é reproduzível quando os inputs capturados (incluindo elegibilidade), seed,
conteúdo fixado, journal e hash final concordam.

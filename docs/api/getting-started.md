# Quickstart: uma run determinística

Defina uma seed e crie a run. Guarde o `runId`, a sequência e o step retornados.

```http
POST /api/v1/runs
Content-Type: application/json

{
  "configName": "default",
  "runDefinitionId": "default_run",
  "playerEntityId": "player",
  "seed": 424242
}
```

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
é reproduzível apenas quando seed, conteúdo fixado, journal e hash final
concordam.

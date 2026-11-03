# Atividades da run e desafio diário

Este guia complementa o [inventário de endpoints](endpoint-inventory.md) e os [contratos de comando](contracts.md). A disponibilidade de atividades e seus efeitos depende do setting e da run. As leituras abaixo não executam escolhas; consulte a sessão e os comandos disponíveis antes de enviar uma decisão.

## Seleções de cartas

- `GET /api/v1/runs/{runId}/card-selections`: lista as seleções da run.
- `GET /api/v1/runs/{runId}/card-selections/{selectionInstanceId}`: retorna uma seleção específica.

Ambos retornam `200` com os dados encontrados; a segunda retorna um objeto e a primeira um array. Uma run ou seleção inexistente retorna `404`. O objeto inclui `selectionInstanceId`, `runId`, `selectionId`, `pickCount`, `offerCount`, `cardPoolId`, `options`, `rerollsUsed`, `freeRerollsRemaining`, `rerollCosts`, `completed`, `pickedCardIds` e `decomposedCardIds`. IDs de instância identificam a escolha concreta da run; não os substitua pelos IDs de definição.

## Preparações

- `GET /api/v1/runs/{runId}/preparations`: lista as preparações da run.
- `GET /api/v1/runs/{runId}/preparations/{preparationInstanceId}`: consulta uma preparação.

Retornam `200` com array ou objeto, respectivamente. Run ou preparação inexistente retorna `404`. A representação contém `preparationInstanceId`, `runId`, `preparationId`, `options` e `appliedOptionIds`. Use os comandos disponíveis da run para aplicar uma opção e atualize a leitura após a confirmação.

## Lojas

- `GET /api/v1/runs/{runId}/shops`: lista as lojas.
- `GET /api/v1/runs/{runId}/shops/{shopInstanceId}`: consulta uma loja.

Retornam `200` com array ou objeto; run ou loja inexistente retorna `404`. A representação inclui `shopInstanceId`, `runId`, `shopId`, `cardPoolId`, `offerCount`, `rerollsUsed`, `rerollCosts`, `pricing` e `items`. Preço e disponibilidade são autoritativos na engine; a interface apresenta os dados e envia o comando correspondente, sem efetivar a compra localmente.

## Desafio diário

A API usa `/api/v1/challenges/daily/current` como base:

1. `GET` consulta o desafio atual. A resposta `200` inclui `challenge`, `contentRevision`, `engineVersion` e `proofHash`. Desafio não configurado pode retornar `404`.
2. `POST /attempts` inicia uma tentativa com `{"playerId":"player-1"}`. Em `200`, a resposta inclui `challengeId`, `runId`, `sequence`, `step`, `seed`, `contentRevision` e `stateHash`. Guarde o `runId` e use a sessão e os comandos normais para jogar.
3. `POST /submissions` envia a tentativa com `{"runId":"<uuid-da-run>","playerId":"player-1"}`. Em `200`, o retorno inclui `accepted`, `challengeId`, `playerEntityId`, `runId`, `sequence`, `stateHash` e `commandsReplayed`.
4. `GET /leaderboard?limit=100` consulta classificação, com `limit` entre 1 e 500. O retorno contém `challengeId` e `entries`, cada entrada com `rank` e `entry`.

A submissão pode retornar `403` para tentativa de outro jogador, `404` quando a tentativa não existe, `422` quando uma regra rejeita a operação e `503` quando o conteúdo necessário está indisponível. `400` pode indicar jogador inválido ou limite fora da faixa. Trate erros conforme o [contrato](contracts.md), sem assumir que uma tentativa foi aceita antes da confirmação.

## Capacidades e alteração de conteúdo

`GET /api/v1/runs/{runId}/capabilities` retorna as capacidades efetivas (`granted` e `denied`), perfil e identificadores das políticas de modo, replay e timeline. Uma run inexistente retorna `404`. Os recursos disponíveis dependem do perfil e das regras do modo; leia o resultado para adaptar a interface.

`GET /api/v1/runs/{runId}/content/activation-preview?targetRevision=<revisao>` consulta a possibilidade de ativar uma revisão na run. O endpoint exige a capacidade de ativação de conteúdo: pode responder `403` quando negada, `404` para run ausente ou `422` quando a prévia falha. A consulta não ativa a revisão; a alteração exige o comando autorizado descrito no [guia de conteúdo e plataforma](content-and-platform.md).

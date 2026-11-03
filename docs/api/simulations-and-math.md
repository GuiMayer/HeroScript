# Simulações e consultas matemáticas

Estas operações apoiam ferramentas de análise. Consulte também o [inventário de endpoints](endpoint-inventory.md) e a [política de acesso](combat-sandbox.md). Cada cálculo deve usar a revisão de conteúdo apropriada; uma prévia não substitui o comando confirmado da run.

## Simular comandos de uma run

`POST /api/v1/simulations` executa uma sequência a partir de um commit da run. O corpo contém `sourceRunId` (UUID), `sourceSequence` (inteiro) e `commands`, uma lista de objetos com `type` e `payload` JSON. Os tipos e payloads devem corresponder aos comandos aceitos pela engine para o estado escolhido. A operação exige a capacidade de simulação na política efetiva da run.

```json
{"sourceRunId":"00000000-0000-0000-0000-000000000001","sourceSequence":0,"commands":[]}
```

O UUID é ilustrativo: use uma run e uma sequência existentes. A resposta `200` contém o resultado da simulação. Commit inexistente retorna `404`, capacidade negada retorna `403` e simulação rejeitada retorna `422`.

- `GET /api/v1/simulations/{simulationId}` retorna resumo com `simulationId`, `sourceRunId`, `sourceSequence`, `status`, `commandsExecuted`, `finalStateHash` e `timeline`.
- `GET /api/v1/simulations/{simulationId}/result` retorna o resultado completo.

As consultas retornam `404` quando não encontram a simulação e `403` quando a capacidade necessária é negada. Consulte o [guia de sandbox](combat-sandbox.md) para políticas de ferramentas e histórico.

## Fórmulas da revisão

- `GET /api/v1/simulations/formulas?contentRevision=<revisao>` lista fórmulas disponíveis na revisão.
- `GET /api/v1/simulations/formulas/{name}?contentRevision=<revisao>` consulta uma fórmula. Os metadados incluem nome, descrição, parâmetros padrão e origem.
- `POST /api/v1/simulations/formulas/evaluate` avalia uma fórmula publicada. Exemplo de corpo:

```json
{"formulaName":"NOME_DA_FORMULA","contentRevision":"REVISAO_PUBLICADA","inputValue":10,"paramOverrides":{}}
```

Substitua nome e revisão por valores existentes; `paramOverrides` é opcional e substitui parâmetros padrão para a avaliação. Nome ou revisão ausente retorna `400` na avaliação; fórmula ou revisão não encontrada pode retornar `404`. A resposta inclui resultado e passos da operação; consulte o contrato correspondente à versão implantada antes de depender de detalhes adicionais.

## Expressões e metadados de operações

`POST /api/v1/simulations/math/expression/evaluate` recebe `initialValue`, `steps` e, opcionalmente, `parameters`. Cada passo pode usar valores numéricos ou operandos simbólicos, como `$current`, `$initial` e `params.X`. Cada passo tem `operation` e usa `values` ou `operands`, que são mutuamente exclusivos. Um exemplo com valores literais:

```json
{"initialValue":10,"steps":[{"operation":"ADD","values":[5]}]}
```

A resposta `200` contém `result`, `initialValue`, `steps` e `executionTimeMs`; uma expressão inválida retorna `400`. O tempo de execução é telemetria, não parte determinística do resultado matemático.

`GET /api/v1/operations` lista os metadados das operações matemáticas; `GET /api/v1/operations/categories` as agrupa; `GET /api/v1/operations/{name}` consulta uma operação pelo nome sem diferenciar maiúsculas e minúsculas e retorna `404` se não existir. Esses endpoints descrevem operações de cálculo; não alteram a run.

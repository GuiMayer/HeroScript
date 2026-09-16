# Runs, combates e replay

## Fluxo autoritativo

1. Crie a run com `POST /api/v1/runs`, informando seed e revisão de conteúdo
   quando o cliente precisar reproduzir uma partida externa. Omitir `modeId`
   seleciona o modo canônico `standard`.
2. Leia `GET /api/v1/runs/{runId}` e guarde `sequence`, `step`,
   `contentRevision` e hash retornados.
3. Consulte mapa e comandos permitidos em `/map` e `/available-commands`.
4. Envie uma mutação ao gateway `/commands` com as versões observadas.
5. Para encontro ativo, use o gateway de combate associado à run.
6. Atualize o cliente exclusivamente pelo estado e hashes da resposta aceita.

O contrato v1 é a única superfície HTTP exposta. Clientes que precisam de
retry, reconexão ou replay devem usar os gateways de comando descritos aqui.
Não existem rotas alternativas para criar combate, executar ação, avançar IA,
encerrar turno ou forçar resultado fora da run.

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
simultaneamente a progressão.

O início do encontro separa a identidade da instância (`instanceId`) da definição
de conteúdo (`definitionId`). Cada participante declara lado e controlador;
recursos iniciais opcionais são um dicionário genérico por instância:

```http
POST /api/v1/runs/{runId}/commands
Content-Type: application/json
```

```json
{
  "commandId": "54dc2f17-37cb-4e77-91ca-8464367f49e6",
  "expectedSequence": 1,
  "expectedStep": 1,
  "type": "START_ENCOUNTER",
  "payload": {
    "participants": [
      {
        "instanceId": "player", "definitionId": "player_warrior",
        "sideId": "player", "controllerBinding": { "kind": "Player" }
      },
      {
        "instanceId": "enemy_1", "definitionId": "enemy_goblin",
        "sideId": "opposition", "controllerBinding": { "kind": "AI", "policyId": "gambit" }
      }
    ],
    "initialResourceValues": {
      "player": { "energy": 3 }
    }
  }
}
```

IDs de instância devem ser únicos, mas várias instâncias podem compartilhar a mesma definição.
Uma referência ou override desconhecido rejeita o comando inteiro.

Dentro do encontro, use:

- `GET /api/v1/combats/{combatId}` para o read model;
- `GET /api/v1/combats/{combatId}/legal-actions` para todos os comandos legais
  do ator ativo, já acompanhados do preview canônico;
- `GET /cards/evaluations` para projetar toda a mão sem N+1;
- `GET /cards/{cardInstanceId}/evaluation` para custos, alvos, upgrades,
  cálculos e fontes contextuais de uma carta;
- `POST /commands` com `PLAY_CARD`, `EXECUTE_ACTION` ou `END_TURN`;
- `GET /resolutions/{commandId}` para retomar a fila visual durável.

O payload de `PLAY_CARD` aceita `cardInstanceId`, `actorId`, `targetIds` e
`costOptionId` opcional. A definição da carta nunca é enviada pelo cliente: a
engine compila o container e aplica seus upgrades a partir da revisão fixada
na run. `EXECUTE_ACTION` fica reservado às habilidades configuradas do ator.

### Inspeção de cartas

A avaliação individual e a avaliação em lote usam os mesmos compiladores,
resolvedores de upgrade e o mesmo `LegalActionResolver` empregado pelo gateway,
pelas políticas de IA e por `PLAY_CARD`. `isPlayable` considera condição, custo, alvo, ator ativo, fase
e orçamento de ações. Quando os alvos selecionados tornam a jogada legal, a
resposta também inclui `previewSteps`, `calculations` e `previewApplications`
produzidos pela mesma transação pura de `PLAY_CARD`, sem alterar a run.

O nível é uma regra fixada no game mode. `Resolved` expõe legalidade, valores e
traces de buckets; `Full`, usado pelo sandbox, acrescenta ator, candidatos a
alvo, status, relíquias, modificadores e políticas que podem influenciar a
carta. `Disabled` bloqueia a projeção. A Godot deve tratar essa resposta como o
único read model de regras de carta e limitar-se a apresentação e input.

### Ações legais, IA e intents

O endpoint `/legal-actions` devolve candidatos em ordem determinística. Cada
candidato contém o comando que pode ser reenviado ao gateway, applications,
cálculos, steps e `resolutionFingerprint` produzidos pelo executor canônico.
O campo `costs` contém os custos resolvidos (`resourceId`, `amount` e demais
metadados de `ResolvedCardCost`) dessa mesma avaliação. Custos alternativos
permanecem em candidatos distintos; a interface deve enviar a escolha exata,
sem somar alternativas nem recalcular modificadores localmente. A prévia vale
para a versão observada, não é uma reserva de recursos: o gateway revalida o
comando com `expectedSequence` e `expectedStep`.
Habilidades que não pertencem ao componente `abilities` do ator nunca aparecem
e também são rejeitadas se enviadas manualmente.

Uma política de IA recebe exatamente essa coleção e apenas filtra/ordena seus
itens. Gambits são definições fixadas pela revisão da run; não possuem cache,
CRUD, loader ou fallback próprios. Predicados usam o runtime comum de fórmulas,
e seletores de alvo têm desempate ordinal explícito. A ação escolhida é avaliada
novamente pela mesma fronteira antes de integrar o commit automático.

Os intents no snapshot contêm `previewApplications`, `previewCalculations`,
`previewFingerprint`, `decisionFingerprint` e `previewUncertain`. O game mode
escolhe se eles são recalculados na publicação ou ficam travados até a ativação
do ator, além do comportamento `Fail`, `Recompute` ou `Hide` se um intent travado
deixar de ser legal.

A ordem de ativação também pertence ao conteúdo fixado da run. O campo
`resolvedMode.combatRules.turnOrder` descreve a estratégia e o boundary de
recálculo; `combat.turnOrderState` expõe a ordem, scores e, quando aplicável,
rolls de iniciativa ou gauges de ATB. A API não consulta configuração global do
servidor para tomar essa decisão.

### Resolução e trace

Cada resolução informa `initialCombatStateHash`, `finalCombatStateHash` e
`resolutionFingerprint`. `rootSequence` aponta para o único commit que contém
o comando e todos os seus frames. Cada frame contém quatro visões complementares:

- `effectSteps`: ordem, condição/chance, alvo, proveniência e hashes de cada efeito;
- `calculations`: buckets e contribuições numéricas usados pelo frame;
- `applications`: mudanças concretas de recurso, status, carta ou modifier.
- `cardZoneSteps`: entradas, saídas e ordenação de cartas em zonas, com IDs de
  fluxo/etapa e hashes. Movimentos disparados por efeitos também aparecem em
  `applications[].cardZoneSteps`; a engine não interpreta o propósito da zona.

O início do encontro também gera um frame `combat.initialized`, portanto
movimentos de zonas configurados para `encounter.started`, regeneração,
relíquias e status de abertura não ficam invisíveis ao cliente.
Esses dados são diagnóstico e apresentação; o estado final persistido continua
sendo a autoridade.

## Comandos de economia e recompensas

Deck, recompensas, lojas e preparação não possuem rotas próprias de mutação.
Envie-os ao gateway da run com o `payload` correspondente:

| Tipo | Payload |
| --- | --- |
| `INVOKE_CARD_ZONE_GAMEPLAY_FLOW` | `{ "flowId": "run.draw", "requestedCount": 1 }` |
| `INVOKE_CARD_ZONE_FLOW` | `{ "flowId": "tool.create-in-hand", "cardDefinitionIds": ["basic_attack"] }` |
| `CREATE_CARD_SELECTION` | `{ "selectionId": "basic_reward" }` |
| `PICK_CARD_REWARD` | `{ "selectionInstanceId": "...", "cardIds": ["..."] }` |
| `REROLL_CARD_REWARD` | `{ "selectionInstanceId": "...", "lockedCardIds": [] }` |
| `CREATE_SHOP` | `{ "shopId": "basic_shop" }` |
| `BUY_SHOP_ITEM` | `{ "shopInstanceId": "...", "itemId": "..." }` |
| `CREATE_PREPARATION` | `{ "preparationId": "basic_preparation" }` |
| `APPLY_PREPARATION_OPTION` | `{ "preparationInstanceId": "...", "optionId": "..." }` |

As coleções `/card-selections`, `/shops` e `/preparations` permanecem apenas
como read models. Alterações de conteúdo seguem o fluxo administrativo de
draft e publicação, que produz uma nova revisão imutável.

`INVOKE_CARD_ZONE_FLOW` é uma ferramenta de modo experimental/sandbox/dev:
exige `capabilityPolicy.allowCardZoneCheats` e só executa fluxos do grafo
revisionado com `allowedInvocations: ["Tool"]`. O payload pode trazer
`cardInstanceIds` para selecionar instâncias, `cardDefinitionIds` para criação
e `actorId` quando o fluxo depende de um ator. Em um passo `Create`,
`cardDefinitionId: "$input"` consome exatamente as definições enviadas; a
engine injeta `requestedCardCount` a partir do tamanho real da lista. O
cliente não escolhe zona de destino nem ordem diretamente: isso permanece na
definição JSON do fluxo. Fluxos de lifecycle ou resolução de carta não podem
ser invocados como ferramenta.

`INVOKE_CARD_ZONE_GAMEPLAY_FLOW` é a mutação genérica para um modo com zonas
configuradas. Não exige cheats e executa apenas fluxos declarados com
`allowedInvocations: ["GameplayCommand"]` e `playerInvokable: true`.
Fluxos internos de recompensa e preparação não podem ser chamados
diretamente pela API. O jogador envia `flowId`,
`requestedCount` opcional e `cardInstanceIds` de cartas visíveis; não pode
enviar `cardDefinitionIds` nem `actorId`. O fluxo JSON decide origem,
destino, seleção, capacidade e ordem. Uma execução sem transição não produz
commit. O comando é determinístico, atômico e auditável como qualquer outro
comando da run.

Em runs com grafo de zonas, `DRAW_CARDS`, `DISCARD_CARDS`, `MOVE_CARDS`,
`ADD_CARDS_TO_HAND` e `SHUFFLE_DISCARD` são recusados: seus nomes embutem
propósitos de pilha que a engine não pode presumir. Modos ainda sem grafo
mantêm esses comandos até a migração de seu conteúdo.

## Recuperação e auditoria

`GET /api/v1/runs/{runId}/card-zones` é a projeção genérica das zonas de cartas
do grafo selecionado pelo modo. Cada zona tem `zoneId`, proprietário, escopo,
metadados de apresentação, contagem e cartas visíveis. `contentsVisible` e
`orderVisible` indicam o que a interface pode mostrar: quando a ordem é oculta,
as cartas são devolvidas em ordem de identidade, não na ordem real do fluxo;
quando o conteúdo é oculto, apenas a contagem é devolvida. A Godot deve usar
`presentation.slot` para decidir onde desenhar a zona, sem inferir que `draw`,
`hand`, `discard` ou `exhaust` tenham significado especial para a engine. O
`topologyHash` permite comparar snapshots sem reconstruir as regras no cliente.

`GET /deck` e `GET /hand` ainda são projeções do modo inicial de demonstração;
clientes que pretendem admitir outros grafos de zonas devem usar `/card-zones`.

Use os seguintes recursos depois de reconectar ou para suporte:

| Objetivo | Endpoint |
| --- | --- |
| Estado completo | `GET /api/v1/runs/{runId}` |
| Histórico de comandos | `GET /api/v1/runs/{runId}/journal` |
| Commits e timeline | `/commits`, `/commits/{sequence}` e `/timeline` |
| Replay semântico | `POST /api/v1/runs/{runId}/verify` |
| Auditoria de encontro | `/api/v1/combats/{combatId}/journal` e `/verify` |

O journal é a fonte de auditoria; eventos servem como projeção. Uma verificação
de replay reexecuta comandos usando seed, versão da engine e conteúdo fixado,
comparando hashes em vez de confiar no estado enviado pelo cliente.

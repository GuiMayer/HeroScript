# Percurso circular, participantes e regras de mesa

Contrato implementado em 27/10/2026. Runtime determinístico: **34**.

## Estratégia explícita e migração

`turnOrder` ordena os atores; `flow.activationSequence` percorre essa ordem. Ambos continuam no mesmo executor de combate, com os mesmos comandos, efeitos, transações, journal e replay.

Todo JSON de regras de combate deve declarar uma estratégia:

```json
"activationSequence": { "strategy": "PerRound" }
```

`PerRound` preserva o percurso anterior: cada ator elegível age antes da próxima rodada. A ferramenta abaixo acrescenta essa declaração apenas onde estiver ausente, preservando políticas explícitas e as demais propriedades:

```powershell
python tools/migrate-activation-sequence.py
python tools/migrate-activation-sequence.py --write
python tools/migrate-activation-sequence.py caminho/para/combat-rules --write
```

Sem `--write`, apenas relata as mudanças. Uma segunda execução não altera nada. Mods externos devem migrar seus arquivos-fonte explicitamente. Não existe fallback silencioso no runtime, migração em GET ou reescrita de saves, bundles e revisões históricas. O conteúdo migrado ganha outra revisão; a execução de uma run continua vinculada à versão e revisão capturadas. Saves de runtimes anteriores não se tornam compatíveis com a versão 34 por essa transformação.

## Circular

Dentro de `flow`:

```json
"activationSequence": {
  "strategy": "Circular",
  "circular": {
    "initialDirection": "Forward",
    "firstActor": "RunOwner",
    "roundPolicy": "Disabled",
    "allowSelfSuccessor": true
  }
}
```

Direção: `Forward` ou `Reverse`. Primeiro ator: `RunOwner` ou `FirstInOrder`. A ordem precisa ser calculada em `CombatStart`; a combinação com ATB não é aceita.

`Disabled` não emite hooks de começo/fim de rodada. `ActivationCount` permite esses hooks a cada quantidade de ativações encerradas: `activationsPerRound` define a quantidade ou, ausente, usa o tamanho inicial da ordem. Saltar assentos não executa o lifecycle desses atores. Atores não elegíveis são ignorados. Zero elegíveis produz falha explícita; um único elegível só volta a agir se `allowSelfSuccessor` permitir.

O snapshot mantém direção, avanço pendente e contadores em `activationState.circular`. A navegação não consome RNG. Inversões podem retornar a um ator que já agiu; a lista de concluídos não filtra o percurso circular.

## Efeitos e alvos relativos

Efeitos de carta, status ou habilidade usam o mesmo processador:

```json
{ "type": "TURN_SEQUENCE", "target": "SELF", "sequenceMutation": { "operation": "ReverseDirection" } }
```

As operações são `ReverseDirection`, `SetDirection` (com `direction`) e `AddAdvance` (com `amount`). O avanço normal é 1; acrescentar 1 salta um participante. Essas operações exigem uma sequência circular.

Seletores de efeito/cartas: `NEXT_PARTICIPANT`, `PREVIOUS_PARTICIPANT`, `NEXT_SCHEDULED_PARTICIPANT`. Os dois primeiros aceitam `relativeOffset` positivo, opcional; o último considera o avanço já agendado. Gambits têm os equivalentes `NextParticipant`, `PreviousParticipant` e `NextScheduledParticipant`. Todos consultam o mesmo percurso. Para travar um alvo na seleção, use targeting relativo da carta e efeitos `TARGET`, seguindo o contrato de targeting existente.

## Consultas reais e condições de comando

`flow.queries` fornece aliases numéricos às fórmulas de cartas, efeitos, gambits e fluxos de zona. Exemplo:

```json
"queries": [{
  "variable": "myHand",
  "kind": "ZoneCount",
  "zones": [{ "zoneId": "hand", "ownerScope": "Actor", "actor": "Source" }]
}]
```

Tipos: `ZoneCount`, `SumZoneCounts`, `ParticipantCount`, `ActivationCommandCount` e `ActivationMovedCount`. `HasBaseLegalCard` é restrito a condições de comandos que não sejam `PLAY_CARD`, evitando recursão. Endereços duplicados são contados uma vez; zona ausente é erro, não zero. Não use contadores de mão paralelos para substituir a topologia real.

Consultas de ativação recebem `actionId`; `ActivationCommandCount` conta ações resolvidas identificadas por power/action, e `ActivationMovedCount` conta movimentos efetivos identificados pelo fluxo. Fatos são reiniciados no começo da ativação e não vazam para a previsão de outro ator. O `actor` opcional dessas consultas é `Active` por padrão; `Source` permite conferir os fatos do próprio autor. Não são contadores globais de todos os comandos passivos.

Condições configuradas valem na descoberta, na execução direta e na IA:

```json
"commandConditions": [{
  "ruleId": "example.end-after-draw",
  "commands": ["END_TURN", "PASS"],
  "actorScope": "All",
  "inputs": [{ "variable": "draws", "kind": "ActivationCommandCount", "actionId": "draw_action" }],
  "allowCondition": "draws",
  "failureCode": "DRAW_REQUIRED",
  "messageKey": "example.draw_required"
}]
```

`allowCondition` aceita expressão ou ID de fórmula; resultado positivo permite. `actionId` na própria condição restringe a uma habilidade. Isso permite compra obrigatória, escolha de cor e outras regras sem decisões duplicadas no frontend. Fluxos genéricos de gameplay da run não podem contornar as condições de um combate ativo; ferramentas de desenvolvimento continuam governadas pelas capacidades do modo.

## Distribuição explícita

Criar zonas de participantes não distribui cartas. Uma etapa opcional do fluxo declara a iteração:

```json
{
  "stepId": "deal",
  "operation": "ForEachParticipant",
  "nestedFlowId": "deal-one",
  "participants": { "order": "Ordinal", "distribution": "RoundRobin", "repetitions": 7 }
}
```

`order`: `Ordinal` ou `SeatOrder`. `distribution`: `RoundRobin` ou `BatchPerActor`. O fluxo aninhado usa `targetOwner: "IterationActor"`; o ator ativo original não é substituído. A lista é capturada antes da iteração. Falhas descartam toda a transação candidata. Existem limites de repetições, profundidade e trabalho, inclusive para fluxos sem movimentos.

Declare `sourceOwner: "RunOwner"` quando o monte pertencer à run. Em uma invocação por efeito, `FlowOwner` é o alvo desse efeito; não é sinônimo de dono do monte. `TargetActor` atende compras dirigidas, `ActiveActor` atende a mão do ator ativo e `IterationActor` atende distribuição em lote. Esses endereços são regras de conteúdo.

## Vitória individual

`flow.outcome.participantVictory` é opcional. Exemplo de condição por mão vazia:

```json
"participantVictory": {
  "ruleId": "empty-hand",
  "inputs": [{ "variable": "myHand", "kind": "ZoneCount", "zones": [{ "zoneId": "hand", "ownerScope": "Actor", "actor": "Source" }] }],
  "condition": "1 - myHand",
  "tieBreak": "ActiveActorThenOrdinal"
}
```

É avaliada após a ação resolvida, no ponto de avaliação configurado; mãos vazias durante o bootstrap não encerram a partida. Empates: `ActiveActorThenOrdinal`, `Ordinal`, `Draw` ou `All`. O resultado explicita `actorIds`, `sideIds` e `draw`. `VICTORY`/`DEFEAT` continuam sendo a projeção relativa ao jogador da run; não é necessário matar artificialmente os demais participantes. Sem essa política, o resultado anterior por lados permanece.

## Referências executáveis e API

- Setting `circular-table`, run `table_run`, modo `table_mode`, jogador `seat_0`: quatro lados, 108 cartas físicas, sete por mão, uma na mesa e 79 no monte. JSONs em `data/configs/circular-table`; gerador reproduzível em `tools/build-circular-table-content.py`.
- A mesa demonstra inversão, salto, compra, reciclagem, escolha de cor, dívida acumulada de +2, +4 imediato fora dessa corrente, declaração de uma carta, penalidade por não declarar e vitória individual. Não implementa contestação de +4. São regras dessa configuração, não regras UNO embutidas no Core.
- O duelo `uno-clash` continua com dois atores e `PerRound`. `tools/migrate-uno-command-conditions.py` transfere a antiga condição local de compra para fórmulas/condições JSON. O app web habilita o fim de turno pelas ações legais e mostra os motivos fornecidos pela API; seu layout continua sendo de duelo.
- `GET /api/v1/combats/{id}` publica `activationSequenceStrategy`, `nextScheduledActorId` e `participantVictory`.
- `GET /api/v1/combats/{id}/legal-actions?actorId=...` inclui `blockedCommands` com código, chave de mensagem e motivo opcional. O cliente apresenta; o servidor decide.
- Journal, retomada e branches usam a persistência canônica. A verificação semântica reexecuta os comandos; não existe histórico alternativo mantido pelo cliente.

Visibilidade de mãos é uma projeção de gameplay; não substitui autenticação/autorização de um serviço multiplayer. Entrada dinâmica de participantes e percurso circular com ATB ficam fora deste contrato. A certificação longa de partida completa/replay tem orçamento de CI próprio; esse tempo não representa latência de uma ação normal.

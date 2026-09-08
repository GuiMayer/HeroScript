# Fluxo de turnos e fases

**Status:** implementado pelo fluxo canônico de ativações

**Atualizado em:** 2026-09-08

## Responsabilidade

`CombatFlowPlanner` é a única autoridade que avança fases, ativações e rounds.
Ele lê a sequência de fases e as políticas fixadas na revisão da run, executa os
lifecycles aplicáveis e devolve novos snapshots. Nenhum manager singleton guarda
fase, prioridade, pilha ou cursor de turno.

O cliente não avança fases diretamente. Ele envia uma intenção (`PLAY_CARD`,
`EXECUTE_ACTION` ou `END_TURN`) e renderiza a resolução que a engine já calculou.

## Modelo configurável

Uma `PhaseSequenceDefinition` contém fases ordenadas. Cada fase declara:

- `phaseId`, `order`, nome e descrição;
- `role`: `Start`, `Middle` ou `End`;
- ações permitidas;
- próximos IDs válidos;
- metadados de transição automática e prioridade.

O grafo estrutural pode ter várias fases de cada papel. O fluxo operacional atual
aceita exatamente uma fase `Start`, uma `Middle` e uma `End`; selecionar um grafo
mais rico falha durante a compilação do cenário, em vez de cair em comportamento
parcial.

```json
{
  "sequenceId": "classic-style",
  "phases": [
    { "phaseId": "start", "role": "Start", "order": 10, "allowedActions": [] },
    { "phaseId": "main", "role": "Middle", "order": 20,
      "allowedActions": ["PLAY_CARD", "POWER"] },
    { "phaseId": "end", "role": "End", "order": 30, "allowedActions": [] }
  ]
}
```

`PhaseState` é parte de `CombatState` e registra a fase atual, seu índice e a
sequência materializada. Cada avanço substitui o snapshot; a definição usada não
é relida dos arquivos durante a ação.

## Políticas do modo

`CombatRulesDefinition`, selecionada pelo modo e fixada na revisão da run,
declara `turnOrder` e o restante das políticas de fluxo em JSON:

| Política | Decisão |
| --- | --- |
| `automaticResolution` | Até onde a engine avança sem novo input. |
| `turnOrder` | Estratégia, boundary de recálculo e desempate entre atores. |
| `actionBudget` | Limite por recurso ou quantidade fixa de ações. |
| `ai` | Gambits habilitados e encerramento automático. |
| `deckCycle` | Compra, descarte, retenção e persistência das zonas. |
| `resourceCycle` | Recurso e regra de refresh no começo da ativação. |
| `statusTiming` | Boundaries habilitados e ordem dos status. |
| `outcome` | Momento da avaliação e desempate do resultado. |
| `encounterResolution` | Confirmação do encerramento. |
| `animation` | Frames completos ou snapshots sob demanda. |
| `journal` | Granularidade do histórico durável. |
| `reactions` | Capacidade reservada; atualmente deve ser `Disabled`. |

Os scopes de ator usam ownership e controller dos lados:
`RunOwner`, `PlayerControlled`, `AiControlled` ou `All`. Não existe regra baseada
em `IsHero`.

## Ordem de uma ativação

```text
encerrar fase Middle do ator atual
  -> lifecycle EndActivation
  -> descarte/ethereal e duração de modifiers da ativação
  -> lifecycle EndRound, se a rodada terminou
  -> recalcular a ordem no boundary configurado
  -> lifecycle StartRound, se aplicável
  -> selecionar próximo ator elegível
  -> fase Start + refresh/regeneração
  -> lifecycle StartActivation + compra
  -> fase Middle
  -> publicar intents de IA ou devolver controle ao jogador
```

Status, relíquias e recursos produzem efeitos comuns nesses boundaries. A lista é
calculada dentro da mesma transação; uma falha descarta todos os snapshots e o
cursor determinístico.

## Ordem e desempates

A engine implementa cinco estratégias etiquetadas:

- `Fixed`: preserva a ordem declarada dos participantes;
- `Resource`: ordena pelo valor atual de qualquer resource, crescente ou
  decrescente;
- `Initiative`: combina um resource configurável com uma rolagem seedada;
- `Atb`: mantém gauges no snapshot e avança ticks determinísticos até existir
  ator pronto;
- `Conditional`: calcula o score com o runtime comum de fórmulas e variáveis de
  ator, sem callbacks C#.

`recalculateAt` explicita o boundary: `CombatStart`, `RoundStart`,
`ActivationEnd` ou `ContinuousTick`. Empates usam ID estável, preferência por
controller ou aleatoriedade seedada. Quando há RNG real, o contexto sucessor e a
geração do epoch ficam persistidos em `CombatState.turnOrderState` junto com
ordem, scores, rolls e gauges. Assim replay e branch retomam exatamente do mesmo
ponto.

Não existe configuração de ordem no `appsettings` nem calculadora singleton. Duas
runs no mesmo processo podem usar estratégias diferentes porque o resolvedor é
sem estado e recebe a política fixada em cada transição.

Exemplo mínimo:

```json
{
  "turnOrder": {
    "strategy": "Resource",
    "recalculateAt": "RoundStart",
    "tieBreak": { "strategy": "StableActorId" },
    "resource": {
      "resourceId": "speed",
      "direction": "Descending"
    }
  }
}
```

## Reações e prioridade

Não há `ActionStack`, `PrioritySystem` nem um segundo executor de fases. Os modos
`Immediate` e `Stack` permanecem reservados no contrato de configuração, porém
não podem ser publicados/selecionados até existir uma implementação transacional
completa. O modo suportado é `Disabled`.

## Integração REST

- comandos: `POST /api/v1/combats/{combatId}/commands`;
- snapshot: `GET /api/v1/combats/{combatId}`;
- timeline: `GET /api/v1/combats/{combatId}/timeline`;
- resolução visual: `GET /api/v1/combats/{combatId}/resolutions/{commandId}`.

`expectedSequence` e `expectedStep` protegem concorrência. A Godot anima os frames
na velocidade desejada, mas não confirma transições internas nem executa regras.

## Referências

- `src/Core/Combat/Flow/CombatFlowPlanner.cs`
- `src/Core/Combat/Flow/CombatFlowPolicyDefinitions.cs`
- `src/Core/Combat/TurnOrder/TurnOrderPolicy.cs`
- `src/Core/Combat/TurnOrder/TurnOrderResolver.cs`
- `src/Core/Combat/TurnOrder/TurnOrderState.cs`
- `src/Core/Combat/TurnPhase/PhaseSequenceDefinition.cs`
- `src/Core/Combat/TurnPhase/PhaseSequenceValidator.cs`
- `src/Core/Combat/TurnPhase/PhaseState.cs`
- `data/configs/default/Resources/combat-rules/`
- `data/configs/default/Resources/phase-sequences/`

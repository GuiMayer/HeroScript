# Fluxo de turnos e fases

**Status:** grafo configurável executável pelo fluxo canônico

**Atualizado em:** 2026-09-08

## Responsabilidade

`PhaseGraphReducer` é a autoridade sobre o cursor de fase. Ele recebe snapshot,
sequência fixada e comando, e devolve outro snapshot imutável com as transições,
efeitos e fingerprint completos. `CombatFlowPlanner` compõe esse resultado com
os boundaries de round e ativação; nenhum singleton conserva cursor ou definição.

A Godot envia somente comandos de gameplay e renderiza os receipts. Ela não
escolhe a próxima fase, não executa efeitos e não avança relógios internos.

## Contrato do grafo

Uma `PhaseSequenceDefinition` declara:

- `sequenceId`, versão e `entryPhaseId` explícita;
- `maxAutomaticTransitions`, que limita trabalho automático deterministicamente;
- qualquer quantidade de fases com papel semântico `Start`, `Middle` ou `End`.

Cada fase possui ações e/ou tags de comando permitidas, efeitos de entrada e
saída e arestas. Uma aresta declara destino, trigger, prioridade, condição e,
para arestas de comando, filtros opcionais por tipo ou tag.

```json
{
  "sequenceId": "classic-style",
  "entryPhaseId": "activation_start",
  "maxAutomaticTransitions": 16,
  "phases": [
    {
      "phaseId": "activation_start",
      "role": "Start",
      "order": 10,
      "edges": [
        {
          "edgeId": "start-to-action",
          "targetPhaseId": "action",
          "trigger": "Automatic",
          "priority": 0
        }
      ]
    },
    {
      "phaseId": "action",
      "role": "Middle",
      "order": 20,
      "allowedActions": ["PLAY_CARD", "POWER", "PASS", "END_TURN"],
      "allowedCommandTags": ["spell"],
      "edges": [
        {
          "edgeId": "action-to-end",
          "targetPhaseId": "activation_end",
          "trigger": "ActivationExit"
        }
      ]
    },
    {
      "phaseId": "activation_end",
      "role": "End",
      "order": 30,
      "edges": []
    }
  ]
}
```

Triggers executáveis:

| Trigger | Uso |
| --- | --- |
| `Automatic` | Avança sem input e conta contra o limite da sequência. |
| `Command` | Avança depois que um comando legal foi integralmente calculado. |
| `ActivationExit` | Leva a ativação atual até uma fase `End`. |

Quando mais de uma condição é verdadeira, vence a maior prioridade. Empate na
maior prioridade é erro, nunca escolha implícita. A publicação rejeita arestas
estaticamente ambíguas, destinos ausentes, fases inalcançáveis, dead ends fora de
`End`, ciclos automáticos e fases que não conseguem alcançar `End` ao encerrar a
ativação. Pelo menos um papel de cada tipo deve ser alcançável desde a entrada.

`AllowPhaseSkipping` foi removido: saltos são arestas `Command` explícitas,
auditáveis e condicionais. Não existe comportamento implícito de skip.

## Estado e determinismo

`CombatState.phaseState` guarda somente:

- `sequenceId`;
- `contentRevision`;
- `cursor`.

A definição não é duplicada no save. Ela é resolvida sempre pelo bundle imutável
da revisão da run, e divergências de sequência, revisão ou cursor falham antes da
execução. Cada transição registra edge, origem, destino, trigger, prioridade e
hashes antes/depois. Efeitos de entrada e saída usam o executor universal e
preservam steps, cálculos e aplicações no mesmo receipt.

Condições usam o runtime comum de fórmulas. O contexto inclui `turn`, `round`,
`activation`, `actions_taken`, `phase_order`, `command_type`, recursos canônicos
de source/owner/target/run e flags `tag_*`.

## Comandos legais e preview

`LegalActionResolver` resolve a fase pelo conteúdo fixado e é a única fronteira
de legalidade para jogador, IA e REST. Uma ação é permitida por tipo explícito ou
por uma tag da carta/habilidade. Depois de calcular a ação, o mesmo resolver
executa a aresta de comando e inclui o estado pós-fase no preview canônico.

Assim o candidato retornado por `GET /api/v1/combats/{combatId}/legal-actions`
é exatamente o estado que o coordinator entrega ao commit. O fingerprint cobre
ação, transições de fase, efeitos e estado final.

## Ativação e lifecycles

Uma ativação segue esta composição:

```text
PhaseGraphReducer.Exit até End
  -> deck/resource/status/relic/modifier EndActivation
  -> EndRound, quando aplicável
  -> recalcular ordem no boundary configurado
  -> selecionar próximo ator
  -> StartRound, quando aplicável
  -> refresh/compra StartActivation
  -> PhaseGraphReducer.Enter na entry phase
  -> efeitos de entry/exit e avanço Automatic
  -> devolver input ou dirigir a IA
```

Fases podem conter seus próprios `entryEffects` e `exitEffects`. Status,
relíquias, resources, deck e modifiers continuam usando os boundaries canônicos
de combate, round e ativação, todos dentro da mesma transação. A etapa de
composição posterior extrairá esses adaptadores do planner sem mudar a semântica.

## Presets suportados

O bundle padrão publica apenas:

- `classic-style`;
- `hearthstone-style`.

Os antigos presets Magic e Yu-Gi-Oh foram removidos porque anunciavam janelas de
prioridade ainda não implementadas. Eles podem voltar depois que o fluxo de
reações/stack for integralmente executável; conteúdo publicável não representa
capacidade fictícia.

## Integração REST

- comandos: `POST /api/v1/combats/{combatId}/commands`;
- snapshot: `GET /api/v1/combats/{combatId}`;
- ações legais e previews: `GET /api/v1/combats/{combatId}/legal-actions`;
- timeline: `GET /api/v1/combats/{combatId}/timeline`;
- resolução visual: `GET /api/v1/combats/{combatId}/resolutions/{commandId}`.

`expectedSequence` e `expectedStep` protegem concorrência. A engine conclui a
transação e a Godot decide apenas quando e como animar seus frames.

## Referências

- `src/Core/Combat/TurnPhase/PhaseGraphReducer.cs`
- `src/Core/Combat/TurnPhase/PhaseSequenceDefinition.cs`
- `src/Core/Combat/TurnPhase/PhaseSequenceValidator.cs`
- `src/Core/Combat/TurnPhase/PhaseState.cs`
- `src/Core/Combat/LegalActions/LegalActionResolver.cs`
- `src/Core/Combat/Flow/CombatFlowPlanner.cs`
- `data/configs/default/Resources/phase-sequences/`

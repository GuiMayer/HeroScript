# Prioridade, reações e stack

**Status:** executável no fluxo canônico

**Atualizado em:** 2026-09-09

## Autoridade e invariantes

`LegalActionResolver` continua sendo a única autoridade de legalidade e
resolução. `ReactionFlowReducer` altera somente `PriorityWindowState` e a lista
imutável de `PendingActionState`; ele nunca interpreta cartas, habilidades ou
efeitos. Assim uma resposta usa exatamente o mesmo comando, executor de efeitos,
regras de fase e cálculo que qualquer outra ação.

- proposta, passe, resolução e fizzle são transações completas da run;
- IDs de janela e ação pendente são hashes determinísticos, nunca GUIDs aleatórios;
- a ação pendente fixa a revisão de conteúdo e os dados necessários à reavaliação;
- carta proposta permanece na mão, mas fica reservada e não reaparece nas ações legais;
- efeitos e movimento da carta só ocorrem quando a ação resolve;
- cada comando gera um frame recuperável e um novo snapshot, inclusive proposta e passe;
- uma branch criada com stack aberta copia o estado, sem compartilhar estado mutável.

## Estratégias

`ReactionPolicyDefinition.strategy` aceita:

| Estratégia | Semântica |
| --- | --- |
| `Disabled` | ações resolvem diretamente e nenhuma janela pode ser aberta; |
| `Automatic` | fluxo não interativo: a ação aceita resolve na mesma transação; |
| `PriorityStack` | ações em fases com `allowPriority: true` viram propostas e resolvem após todos os elegíveis passarem. |

Uma stack configura explicitamente:

- `stackOrder`: `Lifo` ou `Fifo`;
- `eligibility`: todos os atores vivos ou apenas oponentes do proponente;
- `targetLock`: alvos normalizados na proposta ou revalidados na resolução;
- `costTiming`: pagamento na proposta ou na resolução;
- `failure`: rejeitar a transação, fizzlar mantendo o pagamento ou fizzlar reembolsando;
- `maxStackDepth`: limite determinístico de trabalho;
- `reopenAfterResolution`: atualmente deve ser `true`, garantindo nova rodada de
  prioridade entre resoluções;
- tags opcionais que filtram quais ações abrem uma janela e quais podem responder.

O validador rejeita política incompleta, `AfterResolutionStack` sem
`PriorityStack`, profundidade fora do limite e regras que usam stack sem nenhuma
fase com prioridade. Opções aceitas nunca são apenas decorativas.

## Fluxo de comandos

```text
ação normal em fase com prioridade
  -> preview canônico e validação
  -> reserva do custo, quando configurada
  -> PendingActionState + PriorityWindowState
  -> PASS_PRIORITY gira o holder
  -> todos passaram
  -> remove LIFO/FIFO
  -> reavalia pelo LegalActionResolver
  -> resolve ou aplica a política de fizzle/reembolso
  -> reabre prioridade para o próximo item ou fecha a janela
  -> avalia outcome no boundary configurado
```

IA e jogador veem o mesmo conjunto de ações legais. Um gambit pode escolher uma
resposta normal; se nenhuma regra casar, a política da IA seleciona o candidato
canônico `PASS_PRIORITY`. A engine para quando o holder pertence a um controller
de jogador.

## Contrato para a Godot

Não existe endpoint mutável `/stack`. Toda ação, resposta e passe entra por
`POST /api/v1/combats/{combatId}/commands`. O snapshot de combate expõe:

- `priorityWindow.holderActorId`, elegíveis e quantidade de passes consecutivos;
- `pendingActions` na ordem persistida, com comando, locks, custos pagos e revisão;
- `GET .../legal-actions` para o holder atual, incluindo `PASS_PRIORITY`;
- `reactionTransition` e `pendingAction` no preview;
- frames `combat.reaction.proposed`, `combat.priority.passed`,
  `combat.reaction.resolved` e `combat.reaction.fizzled`.

A UI apenas mostra a stack, coleta a escolha do holder e anima os frames. Ela não
calcula prioridade, ordem, validade, custo, alvo ou outcome.

## Conteúdo de referência

O bundle padrão inclui `priority_stack_combat` e a sequência
`priority-window` como configuração opt-in. Os modos atuais continuam usando
`standard_combat`, portanto o protótipo existente não muda de comportamento.

## Referências

- `src/Core/Combat/Reactions/ReactionFlowState.cs`
- `src/Core/Combat/Reactions/ReactionFlowReducer.cs`
- `src/Core/Combat/LegalActions/LegalActionResolver.cs`
- `src/Core/Combat/CombatRunCoordinator.cs`
- `data/configs/default/Resources/combat-rules/priority_stack_combat.json`

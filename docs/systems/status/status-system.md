# Sistema de status

**Status:** implementado como container de componentes imutáveis

**Atualizado em:** 2026-09-06

## Modelo

Um status não possui um `Type` ou `Behavior` interpretado por uma cadeia de
condicionais. A definição combina somente políticas e componentes executáveis:

- stacks e reapplication;
- duração e boundary de expiração;
- `Influences` passivas na pipeline de cálculo;
- `Triggers` de efeitos comuns;
- `ActionConstraints`;
- tags, apresentação e metadados.

DoT, HoT, força, fraqueza e stun são configurações diferentes desses mesmos
componentes, não subclasses ou casos especiais.

## Instância

`StatusEffectInstance` vive em `CombatState` e fixa:

- ID determinístico da instância;
- definição e revisão do conteúdo;
- alvo e fonte;
- stacks, duração e estado ativo;
- step/tempo lógico de aplicação e custom data defensivamente copiada.

Reaplicar usa `StackReapplyPolicy` e `DurationReapplyPolicy`. Instâncias
`Independent` coexistem; as demais políticas mesclam de modo determinístico até
`MaxStacks`.

## Triggers e duração

`CombatStatusLifecycle` processa boundaries habilitados pelo modo:
`StartRound`, `StartActivation`, `EndActivation` e `EndRound`. Status são ordenados
por prioridade e ID da instância; triggers, por prioridade e `triggerId`.

Cada trigger usa `EffectTriggerExecutor`. Um status removido antes de sua vez não
dispara; um status recém-aplicado não perde duração no mesmo snapshot inicial do
boundary. Falha em qualquer efeito aborta a transação completa.

```json
{
  "StatusId": "burning",
  "DefaultDuration": 3,
  "DefaultStacks": 1,
  "MaxStacks": 99,
  "Stacking": "Add",
  "DurationReapply": "Refresh",
  "DurationTickBoundary": "EndActivation",
  "Triggers": [
    {
      "TriggerId": "burning.tick",
      "Boundary": "EndActivation",
      "Effects": [
        { "EffectId": "burning.health", "Type": "DAMAGE", "Target": "SELF",
          "TargetResource": "health", "FormulaValue": "stacks * 3" }
      ]
    }
  ]
}
```

## Influências e restrições

Influências passivas são `ContextualInfluenceDefinition` comuns e podem usar
`stacks`/`duration` em fórmulas. Restrições de ação filtram tags e podem avaliar
uma condição. A legalidade da carta, a IA e a execução consultam a mesma função;
não há regra de stun apenas na UI.

## Remoção e dispel

`REMOVE_STATUS` seleciona um status explícito. `DISPEL_STATUS` filtra por IDs,
tags e fonte, impõe limite e declara a ordem: ID, mais antigo, mais novo ou maior
prioridade. `EffectApplicationRecord` lista todas as instâncias removidas.

## Referências

- `src/Core/StatusEffects/StatusEffectDefinition.cs`
- `src/Core/StatusEffects/StatusEffectInstance.cs`
- `src/Core/StatusEffects/StatusComponents.cs`
- `src/Core/Combat/Flow/CombatStatusLifecycle.cs`
- `data/configs/default/Resources/StatusEffects/status_effects.json`

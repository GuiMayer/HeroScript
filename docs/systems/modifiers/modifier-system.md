# Sistema de modifiers

**Status:** implementado no snapshot imutável da run

**Atualizado em:** 2026-09-06

## Papel

Modifiers representam influências contextuais que podem ser concedidas e
removidas em runtime. Eles não possuem uma pipeline paralela nem campos especiais
de dano/crítico. A definição é um container de `Influences` compartilhadas,
políticas de stack/duração, tags e metadados.

## Definição

```json
{
  "ModifierId": "glass_cannon",
  "DefaultStacks": 1,
  "MaxStacks": 4,
  "DefaultDuration": -1,
  "Stacking": "Add",
  "DurationReapply": "Preserve",
  "DurationBoundary": "Combat",
  "Influences": [
    {
      "InfluenceId": "attack-increased",
      "Scope": "Actor",
      "Channel": "effect_amount",
      "Bucket": "increased",
      "Formula": "stacks * 0.25",
      "RequiredTags": ["attack"]
    }
  ]
}
```

Filtros de tags pertencem a cada influência. Não existem `ModifierKey`,
`BaseValue` ou fórmula numérica no nível da definição que possam divergir dos
componentes executados.

## Instância e ownership

`ScriptModifierInstance` fixa ID determinístico, definição, owner tipado, fonte,
revisão, stacks, duração e estado ativo. O provider só inclui a instância quando
o owner abrange o ator/alvo calculado; um modifier de run não beneficia inimigos
por acidente.

`ModifierTransitions.Apply` é puro. Reaplicação usa as políticas comuns
`Add`, `Replace`, `Highest` ou `Independent` e `Refresh`, `Add`, `Replace`,
`Highest` ou `Preserve` para duração.

## Duração

O boundary é parte da definição:

- `Command`;
- `Activation`;
- `Round`;
- `Combat`;
- `Node`;
- `Run`.

Somente instâncias elegíveis no snapshot de entrada são decrementadas. Duração
negativa é permanente; ao atingir zero, a instância é removida do novo snapshot.
O boundary `Run` ocorre no encerramento normal do mapa. Derrota encerra o combate;
transformá-la também em encerramento da run requer uma política configurável de
progressão, não uma regra implícita do modifier.

## Efeitos e trace

`APPLY_MODIFIER` e `REMOVE_MODIFIER` atravessam `RunEffectReducer` dentro da mesma
transação do efeito. `EffectApplicationRecord` expõe:

- `modifierInstanceId`/`modifierId` para a aplicação principal;
- `removedModifierInstanceIds` para todas as remoções;
- `modifierStackChanges` com stacks anterior/atual e flag `removed` para cada
  instância afetada.

A ordem é estável e suficiente para animação, inspeção e replay.

## Referências

- `src/Core/Combat/Modifiers/ScriptModifierDefinition.cs`
- `src/Core/Combat/Modifiers/ScriptModifierInstance.cs`
- `src/Core/Combat/Modifiers/ModifierTransitions.cs`
- `src/Core/Effects/RunEffectReducer.cs`
- `src/Core/Calculations/CalculationInfluenceProviders.cs`
- `data/configs/default/Resources/Modifiers/script_modifiers.json`

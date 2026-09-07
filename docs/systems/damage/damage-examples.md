# Exemplos de cálculo e alteração de recursos

**Atualizado em:** 2026-09-06

Estes exemplos usam o caminho canônico. `DAMAGE` e `HEAL` são aliases de autoria;
o comportamento real é definido pelo recurso explícito e pela pipeline.

## Ataque simples

```json
{
  "componentType": "effect",
  "componentId": "basic-attack.damage",
  "effect": {
    "effectId": "basic-attack.damage",
    "type": "DAMAGE",
    "target": "TARGET",
    "targetResource": "health",
    "flatValue": 8,
    "calculationChannel": "effect_amount",
    "calculationPipelineId": "default_effect_amount",
    "tags": ["attack", "physical"]
  }
}
```

Uma carta com upgrade pode transformar a base de `8` para `11`. Essa mudança é
compilada antes do cálculo e aparece em `baseTrace`; não é um status.

## Scaling por status

```json
{
  "StatusId": "strength",
  "DefaultDuration": -1,
  "DefaultStacks": 1,
  "MaxStacks": 99,
  "DurationTickBoundary": "Unspecified",
  "Influences": [
    {
      "InfluenceId": "strength.attack",
      "Scope": "Actor",
      "Channel": "effect_amount",
      "Bucket": "increased",
      "Formula": "stacks * 0.25",
      "RequiredTags": ["attack"]
    }
  ]
}
```

Com uma stack, um valor base `8` passa pelo bucket `increased` com contribuição
`0.25`. O trace preserva `Status/strength` como origem, mas o redutor numérico não
tem uma ramificação específica para status.

## Dano periódico

```json
{
  "StatusId": "burning",
  "DefaultDuration": 3,
  "DurationTickBoundary": "EndActivation",
  "Triggers": [
    {
      "TriggerId": "burning.tick",
      "Boundary": "EndActivation",
      "Effects": [
        {
          "EffectId": "burning.health",
          "Type": "DAMAGE",
          "Target": "SELF",
          "TargetResource": "health",
          "FormulaValue": "stacks * 3",
          "Tags": ["fire", "dot"]
        }
      ]
    }
  ]
}
```

O lifecycle apenas decide quando disparar. O trigger é executado pelo mesmo
processador usado por cartas e relíquias.

## Dano em outro recurso

```json
{
  "effectId": "silence-by-drain",
  "type": "DAMAGE",
  "target": "TARGET",
  "targetResource": "voice",
  "flatValue": 2,
  "tags": ["curse"]
}
```

Se zerar `voice` deve silenciar ou derrotar alguém, essa consequência precisa
estar declarada em conteúdo executável. O nome `voice` não recebe semântica do
código.

## Alterar máximo sem dano

```json
{
  "effectId": "expand-focus",
  "type": "MODIFY_RESOURCE",
  "target": "SELF",
  "targetResource": "focus",
  "resourceField": "Maximum",
  "operation": "Add",
  "flatValue": 2
}
```

O redutor normaliza o valor atual conforme `canBeNegative` e `canExceedMax` da
definição do recurso.

## Relíquia e modifier

Uma relíquia pode conter a mesma influência do exemplo de força e triggers de
boundaries. Um modifier aplicado em runtime também contém `Influences`. Em ambos
os casos, ownership, revisão, stacks e duração são fixados na instância; apenas a
proveniência difere no trace.

## Resultado esperado no cliente

A Godot não recalcula esses valores. Ela lê `previewSteps` na avaliação e
`effectSteps`, `calculations` e `applications` nos frames confirmados. Cada
aplicação informa o valor anterior e posterior do recurso, permitindo animar a
transição sem duplicar regras.

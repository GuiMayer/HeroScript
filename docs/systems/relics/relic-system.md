# Sistema de relíquias

**Status:** implementado como estado de run com componentes fixados

**Atualizado em:** 2026-09-06

## Responsabilidade

Uma relíquia é um container persistente da run. Sua definição pode fornecer
influências passivas e triggers de efeitos. O sistema de relíquias decide
ownership, stacks, aquisição/remoção e boundaries; cálculo e aplicação continuam
nos processadores compartilhados.

## Definição e instância

`RelicDefinition` contém `relicId`, limite/política de stacks, apresentação,
propriedades, `Influences` e `Triggers`. Na aquisição, `RunRelicState` copia e
fixa esses dados junto com:

- `relicInstanceId` determinístico;
- owner tipado (`Entity`, `Side`, `Run` ou `Global`);
- revisão de conteúdo;
- stacks e step de aquisição.

Hot reload posterior não reescreve uma relíquia já presente na timeline.

```json
{
  "relicId": "ember_core",
  "stackLimit": 1,
  "triggers": [
    {
      "triggerId": "ember_core.combat-start",
      "boundary": "CombatStart",
      "effects": [
        { "effectId": "ember_core.energy", "type": "MODIFY_RESOURCE",
          "target": "SELF", "flatValue": 1, "targetResource": "energy",
          "operation": "ADD" }
      ]
    }
  ]
}
```

## Aquisição e stacks

`RelicTransitions.Acquire` valida a definição e owner antes de alterar o snapshot.
As políticas `Add`, `Replace`, `Highest` e `Independent` usam as mesmas primitivas
de stack das demais instâncias. Reaquisição respeita o limite e a revisão; remover
usa somente o ID da instância.

Não existe objeto global mutável de relíquia. Todos os comandos substituem o
`RunState` e entram no journal.

## Lifecycle

`CombatRelicLifecycle` executa triggers em ordem estável por ID da instância,
prioridade e `triggerId`. Os boundaries atualmente conectados incluem início/fim
de combate, ativação e round. `CombatStart` e `CombatEnd` são marcados no snapshot
para não disparar duas vezes.

Triggers são enviados ao mesmo `EffectTriggerExecutor` usado por cartas, status e
regras. Um efeito de relíquia pode alterar qualquer recurso, status, deck ou
modifier suportado sem criar um handler próprio.

## Influências

`RelicCalculationInfluenceProvider` filtra a influência por owner, scope e tags,
resolve valor/fórmula na revisão fixada e produz `CalculationInfluence`. Stacks
entram explicitamente na fórmula ou multiplicam um valor fixo.

## Referências

- `src/Core/Run/RelicState.cs`
- `src/Core/Combat/Flow/CombatRelicLifecycle.cs`
- `src/Core/Calculations/CalculationInfluenceProviders.cs`
- `data/configs/default/Resources/relics/`

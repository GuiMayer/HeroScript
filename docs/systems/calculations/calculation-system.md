# Sistema de cálculos

**Status:** implementado e compartilhado por todas as fontes de gameplay

**Atualizado em:** 2026-09-06

## Propósito

O sistema transforma um valor base usando uma pipeline de buckets configurada.
Ele não conhece dano, cartas, vida ou classes de personagem. Todo consumidor
fornece um `CalculationRequest`; todo resultado é um `CalculationResult`
determinístico, auditável e sem mutação do snapshot.

## Componentes

| Componente | Responsabilidade |
| --- | --- |
| `CalculationResolver` | Resolve fórmula/valor base, pipeline e influências. |
| `CalculationEngine` | Aplica buckets em ordem estável. |
| `ICalculationInfluenceProvider` | Projeta uma fonte em contribuições comuns. |
| `CalculationResult` | Valor final, base trace, buckets e fingerprint. |
| `ContextualInfluenceDefinition` | Componente JSON reutilizável por status, relíquias, modo, encontro e modifiers. |

## Pipeline

Cada pipeline possui um ID, um canal e buckets com `bucketId`/`order` únicos.
As operações são `Add`, `AddPercent`, `Multiply`, `Set`, `Minimum` e `Maximum`.
Bounds e arredondamento pertencem ao bucket. Conflitos de `Set` usam
`HighestPriorityWins`, `LowestPriorityWins` ou `ErrorOnMultiple`.

```json
{
  "pipelineId": "default_effect_amount",
  "channel": "effect_amount",
  "buckets": [
    { "bucketId": "flat", "order": 10, "operation": "Add" },
    { "bucketId": "increased", "order": 20, "operation": "AddPercent" },
    { "bucketId": "more", "order": 30, "operation": "Multiply" },
    { "bucketId": "final", "order": 40, "operation": "Add", "rounding": "Round" }
  ]
}
```

## Influências

Os providers atuais cobrem carta, recursos do ator/alvo, status, relíquias,
modifiers, modo e encontro. Todos retornam `CalculationInfluence` com o mesmo
formato: ID, origem, canal, bucket, valor e prioridade.

`ContextualInfluenceDefinition` pode usar valor fixo ou fórmula, nunca ambos, e
filtrar por tags requeridas/excluídas. `Scope` escolhe `Actor` ou `Target`.
Ownership decide se a instância inclui aquela entidade; não há bônus global
implícito.

Recursos só entram quando a pipeline declara um `resourceInfluenceBinding`, com
política de ausência `Ignore`, `Zero` ou `Error`. Nenhum ID de recurso recebe
scaling especial no código.

## Base, upgrade e scaling

O valor base vem do componente efetivo. Upgrades permanentes de carta são
aplicados pelo compilador e registrados em `CalculationBaseTrace`. Influências
contextuais são aplicadas depois, uma única vez, nos buckets.

```text
base original -> patches de upgrade -> base efetiva -> buckets contextuais
```

Essa separação impede que um upgrade seja confundido com força, status ou relíquia
e permite explicar exatamente de onde veio cada parcela do resultado.

## Determinismo e falhas

- buckets: `order`, depois `bucketId` ordinal;
- influências: prioridade decrescente, tipo de fonte, `sourceId`, `influenceId`;
- números não finitos e overflow falham;
- canal/bucket/recurso inexistente obedece validação/política explícita;
- o fingerprint usa JSON canônico do cálculo inteiro.

Preview e execução chamam o mesmo resolver. O trace público permite comparar
base, contribuições e resultado sem reimplementar matemática no cliente.

## Referências

- `src/Core/Calculations/CalculationEngine.cs`
- `src/Core/Calculations/CalculationResolver.cs`
- `src/Core/Calculations/CalculationInfluenceProviders.cs`
- `src/Core/Calculations/ContextualInfluencePolicies.cs`
- `data/configs/default/Resources/calculation-pipelines/`

# Sistema de cálculos

**Status:** implementado, obrigatório para efeitos numéricos e compartilhado por todas as fontes de gameplay

**Atualizado em:** 2026-10-06

## Propósito

O sistema transforma um valor base usando uma pipeline de buckets configurada.
Ele não conhece dano, cartas, vida ou classes de personagem. Todo consumidor
fornece um `CalculationRequest`; todo resultado é um `CalculationResult`
determinístico, auditável e sem mutação do snapshot.

## Componentes

| Componente | Responsabilidade |
| --- | --- |
| `CalculationResolver` | Resolve fórmula/valor base, pipeline e influências. |
| `CalculationEngine` | Aplica buckets em ordem estável e distribui quantidades capturadas sem interpretar gameplay. |
| `ICalculationInfluenceProvider` | Projeta uma fonte em contribuições comuns. |
| `CalculationSettlementPlanner` | Converte consumo calculado em mutações posteriores de recursos. |
| `CalculationResult` | Valor final, base trace, buckets e fingerprint. |
| `ContextualInfluenceDefinition` | Componente JSON reutilizável por status, relíquias, modo, encontro e modifiers. |

`ICalculationEngine.Distribute` divide um orçamento capturado, conserva seu total e mantém os receipts de stages. O contrato e seus limites estão em [distribuição de quantidades](quantity-distribution.md). É a base numérica da etapa 8a; a ligação automática com multi-hit no executor ainda não está implementada.

## Pipeline

Cada pipeline possui um ID, um canal e buckets com `bucketId`/`order` únicos.
As operações são `Add`, `AddPercent`, `Multiply`, `Set`, `Minimum`, `Maximum`,
`ConsumeCapacity` e `Formula`. Bounds e arredondamento pertencem ao bucket.
`Round` declara também a regra de desempate de ponto médio. Conflitos de `Set` usam
`HighestPriorityWins`, `LowestPriorityWins` ou `ErrorOnMultiple`.

`ConsumeCapacity` reduz o valor intermediário até o limite disponível de cada
contribuição e registra a parcela efetivamente usada. `Formula` recebe somente o
pedido, o valor intermediário e agregados das contribuições; ele não recebe acesso
ao estado da run.

```json
{
  "pipelineId": "default_effect_amount",
  "channel": "effect_amount",
  "buckets": [
    { "bucketId": "flat", "order": 10, "operation": "Add" },
    { "bucketId": "increased", "order": 20, "operation": "AddPercent" },
    { "bucketId": "more", "order": 30, "operation": "Multiply" },
    { "bucketId": "mitigation", "order": 40, "operation": "ConsumeCapacity" },
    {
      "bucketId": "final", "order": 50, "operation": "Add",
      "minimum": 0, "rounding": "Round", "midpointRounding": "AwayFromZero"
    }
  ],
  "resourceInfluenceBindings": [
    {
      "bindingId": "target.block.mitigation",
      "scope": "Target",
      "resourceId": "block",
      "channel": "effect_amount",
      "bucket": "mitigation",
      "requiredTags": ["effect.damage"],
      "missingResource": "Ignore",
      "settlement": {
        "operation": "SUBTRACT",
        "field": "Current",
        "useEffectiveValue": true,
        "scale": 1,
        "offset": 0
      }
    }
  ]
}
```

## Influências

Os providers atuais cobrem carta, recursos e stats do ator/alvo, status,
relíquias, modifiers, modo e encontro. Todos retornam `CalculationInfluence` com
o mesmo formato: ID, origem, canal, bucket, valor, prioridade e chave de ordem.

`ContextualInfluenceDefinition` pode usar valor fixo ou fórmula, nunca ambos, e
filtrar por tags requeridas/excluídas. `Scope` escolhe `Actor` ou `Target`.
Ownership decide se a instância inclui aquela entidade; não há bônus global
implícito.

Recursos só entram quando a pipeline declara um `resourceInfluenceBinding`, com
política de ausência `Ignore`, `Zero` ou `Error`. Nenhum ID de recurso recebe
scaling especial no código.

Stats também são opt-in. Um `statInfluenceBinding` declara escopo, componente,
valor, canal, bucket, escala, offset e filtros de tags. Portanto um campo chamado
`strength` só altera um cálculo quando o JSON da pipeline atribui esse significado.

## Cálculo puro e settlement

O motor numérico nunca lê nem altera entidades. O resolver monta um pedido
imutável com valor base, variáveis, tags e influências já projetadas. A saída
contém apenas o número e o trace.

Quando uma influência de recurso declara `settlement`, o planner usa a parcela
efetivamente consumida do trace para criar uma mutação separada. `scale` e
`offset` do settlement permitem converter unidades de cálculo em unidades do
recurso. A proveniência precisa coincidir exatamente com entidade, recurso,
binding e bucket; uma influência de outra origem com o mesmo ID não pode consumir
o recurso.

```text
snapshot -> contexto -> CalculationRequest
         -> CalculationEngine -> CalculationResult
         -> CalculationSettlementPlanner -> mutações de settlement
         -> mutação principal do efeito -> novo snapshot
```

Settlements e consequência principal são aplicados na mesma transação imutável.
Se qualquer mutação falhar, nenhuma delas é preservada.

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
- influências: prioridade decrescente, `orderKey`, `influenceId` e `sourceId` ordinais;
- números não finitos e overflow falham;
- canal/bucket/recurso inexistente obedece validação/política explícita;
- o fingerprint inclui revisão, fingerprint da pipeline, tags, variáveis, base e buckets;
- as coleções persistidas do resultado são concretas e imutáveis, permitindo round-trip do snapshot.

Em produção, um efeito `DAMAGE`, `HEAL` ou `MODIFY_RESOURCE` sem exatamente uma
pipeline compatível habilitada pelo modo é rejeitado. A pipeline-identidade sem
modo existe somente por opção explícita em testes unitários de baixo nível; não é
um fallback de gameplay.

Preview e execução chamam o mesmo resolver. O trace público permite comparar
base, contribuições e resultado sem reimplementar matemática no cliente.

## Referências

- `src/Core/Calculations/CalculationEngine.cs`
- `src/Core/Calculations/CalculationResolver.cs`
- `src/Core/Calculations/CalculationSettlementPlanner.cs`
- `src/Core/Calculations/CalculationInfluenceProviders.cs`
- `src/Core/Calculations/ContextualInfluencePolicies.cs`
- `data/configs/default/Resources/calculation-pipelines/`

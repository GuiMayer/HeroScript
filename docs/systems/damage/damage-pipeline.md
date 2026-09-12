# Dano e pipeline de cálculo

**Status:** implementado pela pipeline numérica e pelos recursos genéricos

**Atualizado em:** 2026-09-12

## Decisão arquitetural

A engine não possui um subsistema especial de dano. `DAMAGE` é uma forma de
autoria para subtrair uma quantidade não negativa do `targetResource` explícito.
`HEAL` adiciona e `MODIFY_RESOURCE` escolhe campo/operação diretamente. Todos
terminam no mesmo `ResourceMutationReducer`.

O cálculo da quantidade é uma responsabilidade separada. `CalculationResolver`
coleta o valor base e influências contextuais; `CalculationEngine` reduz buckets
configurados sem conhecer cartas, dano, vida ou personagens.

```text
EffectDefinition
  -> fórmula/valor base
  -> influências de todas as fontes
  -> CalculationEngine
  -> CalculationResult + trace + fingerprint
  -> CalculationSettlementPlanner
  -> mutações de capacidades consumidas
  -> mutação do targetResource
  -> ResourceMutationReducer
  -> novo snapshot + EffectApplicationRecord
```

## Pipeline por buckets

Uma pipeline declara um canal e buckets de ordem única. As operações disponíveis
são `Add`, `AddPercent`, `Multiply`, `Set`, `Minimum`, `Maximum`,
`ConsumeCapacity` e `Formula`. Cada bucket pode aplicar bounds, arredondamento e
uma política explícita para conflito de `Set`.

```json
{
  "pipelineId": "default_effect_amount",
  "channel": "effect_amount",
  "buckets": [
    { "bucketId": "flat", "order": 10, "operation": "Add" },
    { "bucketId": "increased", "order": 20, "operation": "AddPercent" },
    { "bucketId": "more", "order": 30, "operation": "Multiply" },
    { "bucketId": "mitigation", "order": 40, "operation": "ConsumeCapacity" },
    { "bucketId": "final", "order": 50, "operation": "Add", "minimum": 0 }
  ],
  "resourceInfluenceBindings": [
    {
      "bindingId": "target.block.mitigation",
      "scope": "Target",
      "resourceId": "block",
      "channel": "effect_amount",
      "bucket": "mitigation",
      "requiredTags": ["effect.damage"],
      "settlement": { "operation": "SUBTRACT", "useEffectiveValue": true }
    }
  ]
}
```

Contribuições são ordenadas por prioridade e identidade estável. `NaN`, infinito,
overflow, buckets duplicados, canal incompatível e
referências inválidas falham explicitamente.

## Capacidade defensiva sem regra de dano embutida

`block` não é conhecido pelo código. No conteúdo padrão ele entra porque um
binding do alvo projeta o valor atual no bucket `mitigation` apenas quando a tag
canônica `effect.damage` está presente. `ConsumeCapacity` calcula quanto foi
usado. Depois, o settlement subtrai exatamente essa parcela de `block`; somente o
restante é subtraído do `targetResource` do efeito.

Isso também modela barreira de mana, armadura ablativa, escudo elemental ou
qualquer outra reserva: basta outro recurso e outro binding. Um settlement pode
declarar conversão de unidade por `scale`/`offset`.

No conjunto padrão, `block` é zerado por uma fórmula no `START_TURN` de seu
proprietário. Assim ele continua existindo durante as ativações adversárias. Esse
timing é conteúdo, não comportamento especial da pipeline.

## Fontes indistinguíveis

Cartas, ator, alvo, status, relíquias, modo, encontro e modifiers geram
o mesmo `CalculationInfluence`. `SourceKind` e `SourceId` servem somente para
auditoria; não escolhem algoritmos diferentes.

Upgrades alteram o valor base efetivo da carta e aparecem em `baseTrace`.
Stats configurados e scaling temporário entram nos buckets. Portanto um upgrade de `8` para `11` de
base não é reaplicado como bônus contextual, enquanto força, vulnerabilidade ou
uma relíquia ainda podem transformar `11` conforme a configuração.

## Recursos arbitrários

```json
{
  "effectId": "drain_focus",
  "type": "DAMAGE",
  "target": "TARGET",
  "targetResource": "focus",
  "flatValue": 4,
  "calculationChannel": "effect_amount",
  "calculationPipelineId": "default_effect_amount"
}
```

O mesmo efeito pode atingir `health`, `mana`, `morale` ou qualquer recurso
publicado. O ID não ativa derrota, escudo ou resistência. Consequências de limite
vêm de `thresholdPolicies` na definição do recurso.

## Trace público

Cada cálculo aceito registra:

- valor base e transformações permanentes da carta;
- buckets na ordem executada;
- cada contribuição com origem, valor e prioridade;
- valor antes/depois de bounds;
- fingerprint canônico.

O trace aparece na avaliação da carta e nos frames de resolução. Preview e ação
real usam o mesmo caminho puro; se o snapshot e os inputs não mudarem, seus
resultados são equivalentes.

Todo efeito numérico de uma run exige uma pipeline compatível habilitada pelo
modo. Não existe aplicação escalar silenciosa quando a configuração está ausente.

## Regras de extensão

1. Não crie classes específicas para dano, bloqueio ou cura.
2. Declare sempre o recurso alvo e o canal de cálculo.
3. Modele resistência, crítico, capacidade e scaling como influências/buckets.
4. Modele derrota como política do recurso.
5. Rejeite uma pipeline incompleta durante publicação do conteúdo.
6. Preserve proveniência e ordem no trace.
7. Faça consumo de estado por settlement; nunca dentro do motor numérico.

## Referências

- [Sistema de cálculos](../calculations/calculation-system.md)
- [Sistema de recursos](../resources/resource-system.md)
- [Sistema de efeitos](../effects/effect-system.md)
- `src/Core/Calculations/CalculationEngine.cs`
- `src/Core/Calculations/CalculationResolver.cs`
- `src/Core/Calculations/CalculationSettlementPlanner.cs`
- `src/Core/Resources/ResourceMutationReducer.cs`
- `data/configs/default/Resources/calculation-pipelines/`

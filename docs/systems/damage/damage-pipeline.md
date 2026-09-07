# Dano e pipeline de cálculo

**Status:** implementado pela pipeline numérica e pelos recursos genéricos

**Atualizado em:** 2026-09-06

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
  -> ResolvedResourceMutation
  -> ResourceMutationReducer
  -> novo snapshot + EffectApplicationRecord
```

## Pipeline por buckets

Uma pipeline declara um canal e buckets de ordem única. As operações disponíveis
são `Add`, `AddPercent`, `Multiply`, `Set`, `Minimum` e `Maximum`. Cada bucket
pode aplicar bounds, arredondamento e uma política explícita para conflito de
`Set`.

```json
{
  "pipelineId": "default_effect_amount",
  "channel": "effect_amount",
  "buckets": [
    { "bucketId": "flat", "order": 10, "operation": "Add" },
    { "bucketId": "increased", "order": 20, "operation": "AddPercent" },
    { "bucketId": "more", "order": 30, "operation": "Multiply" },
    { "bucketId": "final", "order": 40, "operation": "Maximum", "minimum": 0 }
  ]
}
```

Contribuições são ordenadas por prioridade, tipo da fonte, ID da fonte e ID da
influência. `NaN`, infinito, overflow, buckets duplicados, canal incompatível e
referências inválidas falham explicitamente.

## Fontes indistinguíveis

Cartas, ator, alvo, status, relíquias, upgrades, modo, encontro e modifiers geram
o mesmo `CalculationInfluence`. `SourceKind` e `SourceId` servem somente para
auditoria; não escolhem algoritmos diferentes.

Upgrades alteram o valor base efetivo da carta e aparecem em `baseTrace`.
Scaling temporário entra nos buckets. Portanto um upgrade de `8` para `11` de
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

## Regras de extensão

1. Não crie classes específicas para dano, bloqueio ou cura.
2. Declare sempre o recurso alvo e o canal de cálculo.
3. Modele resistência, crítico e scaling como influências/buckets.
4. Modele derrota como política do recurso.
5. Rejeite uma pipeline incompleta durante publicação do conteúdo.
6. Preserve proveniência e ordem no trace.

## Referências

- [Sistema de cálculos](../calculations/calculation-system.md)
- [Sistema de recursos](../resources/resource-system.md)
- [Sistema de efeitos](../effects/effect-system.md)
- `src/Core/Calculations/CalculationEngine.cs`
- `src/Core/Calculations/CalculationResolver.cs`
- `src/Core/Resources/ResourceMutationReducer.cs`
- `data/configs/default/Resources/calculation-pipelines/`

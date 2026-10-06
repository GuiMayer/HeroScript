# Parâmetros de efeito e perfis de cálculo

Contrato executável a partir da versão 9 da engine. Implementação: etapa 3 de [CORE_GAMEPLAY_GAPS_IMPLEMENTATION_PLAN.md](../../plans/CORE_GAMEPLAY_GAPS_IMPLEMENTATION_PLAN.md).

## Responsabilidades

`CalculationResolver` é a entrada numérica comum para carta, habilidade, status, relíquia e outros triggers. A pipeline apenas calcula números e traces. O executor resolve alvos e converte o resultado para o parâmetro do efeito; os reducers aplicam a mutação.

Valores literais continuam sendo dados, não fórmulas implícitas. Um parâmetro derivado usa `EffectDefinition.parameters`; não adicionar um interpretador especial à carta nem calcular regras na Godot.

## Parâmetros executáveis

| `parameter` | Efeito compatível | Resultado |
| --- | --- | --- |
| `Amount` | DAMAGE, HEAL, MODIFY_RESOURCE | Magnitude numérica; os aliases DAMAGE/HEAL exigem resultado não negativo. |
| `StatusStacks` | APPLY_STATUS | Inteiro positivo. |
| `StatusDuration` | APPLY_STATUS | Inteiro positivo ou -1, conforme conteúdo; -1 não é inferido. |
| `ModifierStacks` | APPLY_MODIFIER, REMOVE_MODIFIER | Inteiro positivo. |
| `ModifierDuration` | APPLY_MODIFIER | Inteiro positivo ou -1. |
| `CardCount` | CARD_ZONE_FLOW | Inteiro de 1 a 4096; IDs explícitos, se fornecidos, devem ter a mesma contagem. |

Cada entrada declara `flatValue` e/ou `formulaValue`, `channel`, `unitId`, `conversion` e, opcionalmente, `pipelineId` e `stageIds`. Não pode haver dois overrides do mesmo parâmetro nem um override literal concorrente. `Amount` substitui os campos principais `flatValue/formulaValue`; os outros parâmetros substituem apenas o respectivo campo. `CardCount` calculado substitui o valor literal inicial desse campo.

`conversion.requireInteger` é obrigatório para contagens/durações. `rounding` permite None, Floor, Ceiling ou Round, com `midpointRounding` explícito. None não trunca: resultado fracionário falha. O resultado deve caber em Int32. `sign` permite Any, NonNegative ou Positive; `minimum/maximum` são limites explícitos. A conversão limita, arredonda e depois valida novamente limites/sinal/tipo. Valor inválido falha a ação inteira.

O cálculo registra `unconvertedValue`, `valuePolicy` e `remainder = unconvertedValue - value`, com sinal. Esse fato inclui a diferença causada por limites/conversão; não redistribui o resto automaticamente. Distribuição por impactos pertence à etapa de multi-hit.

Exemplo de autoria, dependente de publicar a pipeline e o status referenciados e habilitar a pipeline no modo:

```json
{
  "type": "APPLY_STATUS",
  "target": "SELF",
  "statusId": "charge",
  "parameters": [{
    "parameter": "StatusStacks",
    "flatValue": 2,
    "formulaValue": "source.resources.focus.current",
    "channel": "stack_count",
    "pipelineId": "stack_count",
    "unitId": "stacks",
    "conversion": {
      "requireInteger": true,
      "rounding": "Floor",
      "sign": "Positive",
      "minimum": 1,
      "maximum": 99
    }
  }]
}
```

```json
{
  "pipelineId": "stack_count",
  "channel": "stack_count",
  "unitId": "stacks",
  "buckets": [{ "bucketId": "base", "order": 0, "operation": "Add" }]
}
```

`unitId` é uma identidade definida pelo conteúdo, não uma lista hardcoded de recursos. Pedido, quantidade transportada e pipeline devem compartilhar a unidade; não existe conversão silenciosa. A matemática também suporta deltas com unidades de atributos, mas a operação de progressão persistente de atributos ainda depende da etapa 10.

## Estágios e quantidades transportáveis

Uma pipeline pode declarar `stages`, cada um com `stageId` e `scope` Shared, Actor ou Target. Todos os buckets de uma pipeline com estágios pertencem a um estágio declarado. Os buckets de um estágio devem ser contíguos na ordem numérica da pipeline. A engine não impõe uma ordem universal de origem/defesa; ela executa a ordem definida no JSON.

`stageIds` vazio significa todos os estágios; seleção parcial declara os IDs. Influências de outros canais/estágios não são avaliadas. Uma influência em bucket desconhecido continua sendo erro, não é descartada como fallback. Shared não exige contexto; Actor/Target recebem IDs estáveis do contexto, sem acesso a entidades dentro do reducer matemático.

Resultados expõem `checkpoints` (valor ao final de cada estágio executado) e uma `quantity` imutável com valor, unidade, revisão, fingerprint de origem e `incorporatedStages`. Uma continuação usa `CalculationSourceContext.inputQuantity`/`CalculationRequest.inputQuantity` e seleciona explicitamente os próximos estágios. Não pode fornecer simultaneamente uma nova base recalculada no resolver.

O mesmo estágio semântico, no mesmo contexto, não pode ser reaplicado — inclusive por outro perfil. Defesa já calculada para A não impede um estágio de defesa para B. Alterar o ID da pipeline não remove o histórico do estágio. IDs de estágio devem identificar a transformação numérica, não apenas sua posição. Mudança de unidade/revisão, perfil incompatível, receipts duplicados/inválidos e ausência de proveniência de estágios são rejeitados. Quantidades de pipelines sem estágios não são transportáveis.

Esses contratos são numéricos: não autorizam um ataque a saltar, não selecionam B, não consomem stacks e não determinam quando reutilizar uma quantidade. O planejamento de snapshot, condensação e continuação usa esses contratos nas etapas seguintes.

## Captura e settlements

`captureOnly` faz a pipeline produzir quantidade/trace sem autorizar settlements. `CalculationSettlementPlanner` retorna uma seleção vazia para capturas, mesmo que um estágio de defesa tenha contribuído numericamente. Contagens/durações usam captura numérica e não gastam recursos que ofereceram influência.

Para aplicações numéricas normais, settlements continuam derivados do trace exato da pipeline e somente dos buckets executados. Não recalcular defesa nem procurar bindings de estágios omitidos. Preview executa sobre snapshots e não publica a mutação candidata.

## Persistência e limites desta entrega

Traces dos parâmetros acompanham os steps e os cálculos do batch; quantidades/checkpoints/receipts participam dos fingerprints e serializam nos frames. Definições originais não são alteradas ao bindar resultados. Falha numérica descarta custo, RNG e mutações candidatas pelo fluxo transacional existente.

Saves de versões anteriores são preservados e rejeitados explicitamente pela verificação de versão; não há migração automática. Payloads acumuláveis, condensação e composição estrutural já foram integrados nas etapas seguintes. [Orçamentos de sequência](../effects/sequence-budgets.md) acrescentam `parameters[].distribution` ao executor comum na etapa 8b1; exigem stages explícitos, não alteram repeats existentes e ainda não compartilham um orçamento entre pai/filhos. Salto, API/OpenAPI consolidada e apresentação Godot das novas mecânicas continuam separados no plano.

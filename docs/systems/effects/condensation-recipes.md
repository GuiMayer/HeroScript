# Condensação de stacks em um proc

Contrato executável da etapa 5, engine versão 12. Condensação reúne uma seleção elegível, consome todos os seus stacks e ativa uma composição de efeitos **uma vez**. Não equivale a repetir dano nem a executar uma vez por stack.

## Conteúdo e responsabilidades

Receitas vivem no kind revisionado `condensation-recipes`, descoberto pelo catálogo comum de conteúdo e exposto pelos endpoints genéricos existentes. O efeito que solicita a operação é:

```json
{
  "type": "CONDENSE_STACKS",
  "target": "TARGET",
  "condensationRecipeId": "restoration",
  "chance": 1
}
```

Chance, condição e perda de alvo usam o executor comum. Seleção e consumo usam os adaptadores de status/modifiers; a camada numérica agrega e calcula; os efeitos emitidos usam os reducers normais. Não existe uma store de condensação, um endpoint de consumo direto ou uma fórmula especial de dano.

Uma definição precisa autorizar a receita em `consumption.allowedRecipeIds`. Publicação valida essas referências, o schema da receita, seus efeitos/perfis, bindings de inputs e definições selecionadas explicitamente. Falta de autorização exclui a instância; não concede capacidade por tag, nome ou periodicidade.

## Exemplo: contagem ligada a uma cura

```json
{
  "restoration": {
    "recipeId": "restoration",
    "scope": "OncePerAction",
    "consumption": "AllSelectedStacks",
    "chanceFailure": "SkipWithoutConsumption",
    "exhaustion": "RemoveInstance",
    "removalHooks": "None",
    "ownerBinding": "TargetEntity",
    "selectionTiming": "ActionStart",
    "evaluationTiming": "AfterConsumption",
    "emptySelection": "Skip",
    "conflict": "Fail",
    "zeroApplication": "Consume",
    "selection": {
      "stores": ["Status"], "definitionIds": ["regeneration"],
      "maximumInstances": 128
    },
    "aggregates": [{ "parameterId": "count", "kind": "StackCount" }],
    "effects": [{
      "type": "HEAL", "target": "TARGET", "targetResource": "vitality",
      "parameters": [{
        "parameter": "Amount", "inputQuantityId": "condensation.count",
        "channel": "stack_activation", "pipelineId": "stack_activation",
        "unitId": "stacks", "stageIds": ["activation"]
      }]
    }]
  }
}
```

O setting deve fornecer regeneration, vitality e o profile `stack_activation`, habilitado no modo e com unidade `stacks`, estágio `activation` e seus buckets. O profile pode transformar a contagem no valor aplicado ao recurso. Nomes de recursos e consequências de thresholds continuam sendo design de conteúdo.

A soma de StackCount tem unidade `stacks` e receipt Shared `stack_count`. Não inventa uma conversão para outra unidade. O profile de ativação conserva essa unidade; valores de magnitude ou contagens de StatusStacks, ModifierStacks, CardCount etc. podem consumir esse input via parâmetros tipados e sua conversão declarada. Contagens maiores que 16.777.216 são rejeitadas para evitar perder precisão inteira no contrato float.

## Payloads e continuidade do cálculo

Para aproveitar intensidades preservadas em lotes:

```json
{ "parameterId": "power", "kind": "Payload", "payloadParameterId": "potency" }
```

O efeito emitido referencia `condensation.power`. Todos os selecionados precisam fornecer esse parâmetro; o resolver usa Snapshot ou Dynamic conforme cada lote. Contribuição é ponderada por stacks, sem multiplicação implícita pela duração. Unidade, revisão, receipts e fingerprints são preservados. Misturas incompatíveis são erros; esta entrega não oferece conversão de unidades heterogêneas nem cálculo automático de potencial periódico restante.

O efeito continua pelos stages restantes. Repetir um estágio já incorporado para a mesma origem/alvo falha. Não é necessário acessar novamente a carta original para reconstruir a base capturada.

## Seleção, conflitos e momento do cálculo

- `ownerBinding`: TargetEntity, SourceEntity, Run, Explicit ou Any. Explicit exige `selection.owner`; outros bindings não aceitam esse owner redundante. Filtros de store, definição, origem e tags são os do seletor comum.
- `ActionStart` captura no snapshot recebido pela ação, antes de seus efeitos/custos internos. Aplicações posteriores não expandem essa seleção. Se uma instância capturada mudou, `conflict` Fail descarta a ação; Skip pula a condensação. Não troca silenciosamente o snapshot antigo pelo atual.
- `Current` captura os registros presentes exatamente naquele ponto da sequência.
- `BeforeConsumption` avalia payloads dinâmicos e congela as leituras numéricas dos efeitos emitidos no snapshot imediatamente anterior ao consumo, não no início da ação. Mutações continuam aplicadas ao candidato vivo. Fatos `results.*`/`parent.*` mantêm a causalidade da execução. Settlements ainda validam o recurso vivo: uma leitura congelada não autoriza gastar duas vezes a mesma capacidade.
- `AfterConsumption` remove primeiro as fontes e avalia no estado vivo candidato. Influências passivas consumidas não participam; efeitos posteriores observam mutações anteriores normalmente.

Seleção vazia é Skip ou Fail. Alvo derrotado durante a ação segue `targetLoss`; alvo inválido já na entrada continua falhando. Chance falsa não consome. `zeroApplication` Consume aceita uma ativação que não mudou o estado, por exemplo cura limitada pelo máximo; Fail rejeita a ação completa. Mudanças em duração, zonas ou outros estados contam como aplicação, não apenas deltas de recursos/stacks.

## Um proc e uma transação

Scope inicial suportado: OncePerAction, por recipeId. A primeira tentativa reserva o escopo mesmo quando condição/chance/seleção fazem skip. Invocações repetidas na mesma ação produzem `already_attempted` sem novo sorteio nem consumo. Uma ação posterior pode solicitar outra condensação.

O efeito solicitante exige um único alvo de ativação e repeat 1. Vários componentes, repetições tradicionais e alvos adicionais pertencem aos efeitos da receita. Todos compartilham o procId principal e possuem impactos/componentes distintos. Repetição tradicional de efeitos continua repetição explicitamente configurada; distribuição de um orçamento multi-hit pertence à etapa 8.

O consumo gera um application record com `condensation.selection`, inputs agregados e StackChanges Consume. Steps carregam o mesmo outcome e traces em `payloadCalculations`; frames existentes já preservam esses dados através do contrato comum. Não há eventos publicados fora do commit canônico.

Falhar no último efeito descarta consumo, recursos, zonas, novos stacks e RNG candidatos da ação. Novos stacks emitidos pela receita não entram retroativamente na seleção e não são reconsumidos no mesmo escopo.

Recursão de condensação dentro de uma receita é rejeitada, inclusive em filhos que nunca executariam. As receitas participam dos limites comuns de profundidade 32/repetição 256/passos 4.096. Seleções e lotes excessivos falham antes de expandir/agregar, sem truncamento.

Políticas iniciais suportadas para consumo/exaustão/hooks/chance failure são somente os valores mostrados acima. Outros valores não são promessas de funcionalidades futuras: são rejeitados. Consumo não dispara dispel, expiração ou abate.

## Verificação

Regressões cobrem cura, recurso arbitrário, dano como alias, compra/movimento de cartas, status/modifier, múltiplas stores/instâncias, empty/chance/conflict/zero, revisão incompatível, seleção atual/inicial, influências antes/depois, múltiplos alvos, limite de expansão, reconsumo indevido, rollback no último componente e dez execuções/serialização idênticas.

Esta entrega é de engine/contrato. Conteúdo demonstrativo, preview especializado e UI de condensação na Godot pertencem às etapas seguintes; não foram anunciados como concluídos.

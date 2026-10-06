# Leitura e consumo de stacks acumulados

Primitivas da etapa 4 do [plano do core](../../plans/CORE_GAMEPLAY_GAPS_IMPLEMENTATION_PLAN.md), versão 11 da engine. Ainda não representam a operação completa de condensação: a receita e sua ativação transacional pertencem à etapa 5.

## Autoridade e autorização

`AccumulatedStackTransitions` adapta os dois armazenamentos atuais: `CombatState.statusEffects` e `RunState.modifiers`. Não mantém um terceiro estado de stacks. Referências e planos são snapshots imutáveis derivados; nunca são usados como armazenamento vivo.

Status e modifier declaram `consumption.allowedRecipeIds`. A lista vazia não autoriza consumo. Ter stacks, efeitos periódicos ou uma tag de gameplay não concede essa capacidade implicitamente. Exemplo preparatório de capacidade na definição:

```json
{ "consumption": { "allowedRecipeIds": ["restoration_proc"] } }
```

Neste estágio, IDs identificam autorizações do contrato de consumo; o catálogo revisionado de receitas e sua validação cruzada serão integrados na etapa 5. Não existe um novo comando REST de consumo direto nem uma carta de condensação executável apenas com esse campo.

## Captura e seleção

`Capture` recebe combat/run, recipe ID e um `AccumulatedStackSelection` com filtros por store, owner exato, source, definition IDs e tags requeridas/excluídas. `maximumInstances` limita a seleção sem truncar: excesso é erro, não consumo silencioso de um subconjunto.

Uma referência registra store, owner, instância, definição, revisão, stacks, duração, origem, tags, payload lots e fingerprint de **todo** o snapshot da instância. Alterar um payload invalida uma captura anterior. A ordem é store, owner kind, owner ID ordinal e instance ID ordinal.

Definições não autorizadas e instâncias inativas não participam. O snapshot de uma instância consumível precisa ter identidade, owner/status alvo consistente, contagem positiva e revisão. Duplicações de identidade na mesma store são rejeitadas. A seleção vazia é representável; quem planeja a ativação define a política de ausência, não este reducer.

## Consumo completo e conflitos

`Consume` valida todos os snapshots antes de construir a mutação candidata. A instância precisa continuar elegível e idêntica à captura. Mudar contagem, duração, revisão, definição ou outra informação da instância invalida o plano, mesmo quando a contagem final parece igual.

Planos duplicados, uso repetido da mesma captura e instâncias esgotadas falham. Consumo sempre remove todos os stacks das instâncias selecionadas; não existe quantidade parcial implícita neste contrato.

O resultado contém combat/run candidatos e `EffectStackChange` com razão `Consume`, contagem anterior e contagem atual zero. Não dispara dispel, expiração, derrota ou ticks implicitamente. Não muda RNG, não publica eventos e não persiste nada; a transação canônica é responsável pela publicação futura desses fatos junto com a ativação.

## Lifecycle e limite da entrega

O lifecycle de status já verifica a presença ativa da instância antes de cada trigger. Se uma instância do snapshot inicial foi consumida anteriormente no mesmo boundary, ela não executa seus triggers nem gera uma expiração fictícia. Modifiers consumidos deixam a lista autoritativa, portanto não participam de ticks posteriores.

Testes cobrem ambas as stores, filtros, limites, autorização, conflitos de snapshots, rollback, repetição indevida, ordem determinística, round-trip de planos/fatos e consumo dentro de um boundary.

## Payload por parâmetro

Status e modifier podem declarar `payloadParameters` e `payloadReapply`. Cada parâmetro contém `parameterId`, `evaluation` (`Snapshot` ou `Dynamic`), `missingSource` e um `numeric` com Amount, unidade, canal, profile, stages e conversão. Amount aqui representa um número genérico por stack, não necessariamente dano.

Exemplo de captura da contribuição da origem, deixando o estágio do alvo para a ativação:

```json
{
  "payloadReapply": "PreserveLots",
  "payloadParameters": [{
    "parameterId": "potency",
    "evaluation": "Snapshot",
    "missingSource": "Fail",
    "numeric": {
      "parameter": "Amount", "flatValue": 3,
      "channel": "magnitude", "pipelineId": "staged_magnitude",
      "unitId": "scalar", "stageIds": ["source"]
    }
  }]
}
```

O profile precisa existir, estar habilitado no modo e declarar stages compatíveis. `Snapshot` passa pelo calculator ao aplicar e guarda uma quantity com revisão, unidade, fingerprint e receipts. Mudar buffs, recursos ou a carta depois não altera esse valor. A captura é `captureOnly`: não consome defesas nem executa settlements.

`Dynamic` guarda a base constitutiva e resolve a magnitude na ativação usando o estado atual da origem/alvo. Não guarda o RunState, atores ou a carta inteira. Influências constitutivas da carta efetiva e sua identidade são preservadas; influências do mundo são consultadas novamente. Fórmulas históricas `results.*`/`parent.*` não são aceitas para Dynamic. Variáveis livres do pedido original são preservadas, mas namespaces de recursos/resultados e índices gerados não são congelados.

Origem ausente ou derrotada em Dynamic exige política explícita: `Fail`, `SkipContribution` ou `UseOwner`. Esta última precisa de owner vivo. Se todas as contribuições forem omitidas, não existe quantity: a operação falha, sem inventar zero. Snapshot permanece utilizável mesmo sem origem viva.

## Binding à base efetiva e lotes

APPLY_STATUS/APPLY_MODIFIER aceitam `payloadBindings`, endereçados por parameterId. Um binding pode fornecer flat/formula, ou `cardEffectComponentId` para copiar a base numérica do componente da **carta efetiva**, incluindo upgrades. Não pode declarar ambos. Referências desconhecidas, base transportada em vez de constitutiva e perfil incompatível são erros, inclusive dentro de efeitos encadeados. Outros tipos de efeito não aceitam bindings.

Cada aplicação cria um lote determinístico dentro da instância autoritativa, com intensidade, contagem, revisão, origem, owner e parâmetros. `PreserveLots` conserva intensidades antigas; Add/Highest acrescentam somente a diferença efetivamente aceita pelo limite. Replace ou `ReplaceAll` substituem os lotes pela nova intensidade. Contagem total dos lotes sempre corresponde à contagem da instância. Máximo de 16 parâmetros por definição e 256 lotes por instância; exceder é erro, sem truncamento.

A duração continua sendo da instância e segue `durationReapply`/boundary já configurados. Não existe duração individual oculta por lote. Expirar a instância remove seus lotes. Remoção parcial comum de modifiers reduz as contribuições mais antigas primeiro. Consumo integral mantém seu motivo próprio Consume.

## Ativação numérica e observabilidade

Parâmetros de efeitos comuns podem usar `inputQuantityId: "payload.potency"` no lugar de flat/formula. O lifecycle fornece os lotes vivos; o resolver avalia Dynamic, soma a contribuição de cada lote ponderada por stacks e continua o profile configurado. Não há um efeito/proc por stack. Duração não multiplica a soma implicitamente.

A soma é responsabilidade da camada numérica e rejeita unidades/revisões ou definições de stages incompatíveis. A quantity agregada conserva os receipts: reaplicar o estágio da mesma origem/alvo falha. Um efeito de ativação deve selecionar somente seus stages restantes. Não há conversão implícita de unidades.

Capturas, avaliações e agregações aparecem em `payloadCalculations` dos steps e nos cálculos do batch. Também participam do orçamento de execução, hashes, serialização e identidade da execução. Os lotes/quantities são contexto interno confiável; não há endpoint para clientes forjarem capturas numéricas.

Grants diretos de preparação sem contexto de efeito não podem materializar payloads: uma definição que exige captura sem um lote válido é rejeitada. Para capturar uma base de gameplay, use APPLY_MODIFIER no fluxo canônico com origem explícita.

A etapa 5 permanece pendente: selecionar/consumir/ativar por receita em uma única transação ainda não está exposto como efeito executável. A referência legada permanece intocada.

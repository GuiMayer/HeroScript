# Orçamento de uma sequência de efeitos

## Estado atual

As etapas 8b1/8b2 conectam a distribuição numérica ao executor comum. Qualquer origem que use esse executor pode declarar um orçamento: carta, status, modifier, ação inimiga ou efeito emitido por condensação. Não existe um processador especial de multi-hit nem um endpoint de matemática que altera o combate.

Sem `parameters[].distribution`, `repeat` mantém o significado atual: executar novamente o efeito completo, com um novo cálculo. Com esse campo, `repeat` identifica os slots que recebem parcelas de um único orçamento numérico. A opção pertence ao parâmetro; não muda o restante da sequência implicitamente.

Suporte inicial: `Amount` de efeitos de recurso e `StatusStacks`/`ModifierStacks` de aplicações, incluindo um orçamento compartilhado com os impactos do pai. Duração, remoção de modifiers, quantidade de cartas e distribuição entre vários alvos no mesmo impacto não são suportados. Configurações incompatíveis falham, inclusive em filhos que nunca executariam.

## Fluxo

1. O executor preserva o snapshot numérico de entrada da sequência.
2. No primeiro impacto elegível, captura os stages da origem nesse snapshot, uma única vez e sem settlements. Uma sequência completamente pulada não precisa calcular seu orçamento.
3. `ICalculationEngine.Distribute` divide a quantity entre `repeat` slots ordenados. Não conhece alvos ou efeitos.
4. Cada impacto usa a parcela do seu índice e executa somente os stages restantes, com recursos e influências do alvo vivo naquele impacto.
5. O planner comum deriva settlements desse cálculo; os reducers comuns aplicam recursos, status ou modifiers no candidato transacional.

A captura não lê o primeiro alvo nem `repeat_index`/`target_index`. Fórmulas da origem podem usar atributos/recursos da origem e outros valores fornecidos no contexto de entrada. Fórmulas dependentes do alvo pertencem aos stages de impacto, não ao orçamento.

Captura e aplicação compartilham unidade, revisão e receipts. Não é permitido limpar receipts para escalar a origem novamente. Um input já influenciado por alvo é rejeitado neste modo de gameplay, mesmo que a primitiva numérica permita outros desenhos com opt-in.

## JSON de um efeito

O modo precisa habilitar a pipeline referenciada, e os recursos precisam existir no setting. Este exemplo usa nomes de recurso escolhidos pelo conteúdo; `health` e `block` não têm significado numérico especial na engine.

```json
{
  "type": "DAMAGE",
  "target": "TARGET",
  "targetResource": "health",
  "repeat": 3,
  "chance": 1,
  "chanceScope": "PerSequence",
  "targetLoss": { "policy": "StopRepeat" },
  "parameters": [
    {
      "parameter": "Amount",
      "flatValue": 10,
      "channel": "damage_amount",
      "pipelineId": "sequence_damage",
      "unitId": "points",
      "stageIds": ["impact"],
      "distribution": {
        "sourceStageIds": ["origin"],
        "allocation": {
          "mode": "Quantized",
          "quantum": 1,
          "remainderAllocation": "Earliest"
        }
      }
    }
  ]
}
```

Uma pipeline compatível:

```json
{
  "pipelineId": "sequence_damage",
  "channel": "damage_amount",
  "unitId": "points",
  "stages": [
    { "stageId": "origin", "scope": "Actor" },
    { "stageId": "impact", "scope": "Target" }
  ],
  "buckets": [
    { "bucketId": "source_flat", "stageId": "origin", "order": 10, "operation": "Add" },
    { "bucketId": "source_increased", "stageId": "origin", "order": 20, "operation": "AddPercent" },
    { "bucketId": "capacity", "stageId": "impact", "order": 30, "operation": "ConsumeCapacity" },
    { "bucketId": "final", "stageId": "impact", "order": 40, "operation": "Add", "minimum": 0 }
  ],
  "resourceInfluenceBindings": [
    {
      "bindingId": "target_capacity",
      "channel": "damage_amount",
      "bucket": "capacity",
      "scope": "Target",
      "resourceId": "block",
      "settlement": { "operation": "SUBTRACT" }
    }
  ]
}
```

`sourceStageIds` precisa selecionar um prefixo da ordem dos stages; `stageIds` seleciona o restante. Os dois conjuntos são não vazios, distintos e cobrem a pipeline completa. A origem não aceita stages Target; a aplicação não aceita stages Actor. Shared pode pertencer a qualquer parte, respeitando a ordem declarada. A pipeline default existente não foi convertida automaticamente para esse modelo.

`Continuous` divide na malha Float32 e não aceita `quantum`. `Quantized` exige múltiplo exato do quantum e define o viés do resto. O orçamento 10 com quantum 1 gera 4/3/3 com Earliest ou 3/3/4 com Latest. Uma base 10 com contribuição flat 2 na origem gera 4/4/4, não `10/3 + 2` a cada impacto.

A conservação diz respeito às parcelas **antes** dos stages de alvo. Defesa, resistência, arredondamento e bounds autorais podem modificar o resultado aplicado por impacto; a distribuição não compensa essas regras para forçar dano total igual ao orçamento.

## Stacks e contribuição zero

Stacks usam a mesma estrutura, com `parameter: StatusStacks` ou `ModifierStacks`, unidade/canal/perfil compatíveis e `conversion.requireInteger: true`. A alocação exige Quantized com quantum 1.

Um orçamento de um stack em quatro impactos gera 1/0/0/0 ou 0/0/0/1. Parcela zero gera `skipReason: zero_contribution`, sem aplicar um status/modifier inválido, capturar payloads ou executar filhos. Os stages de alvo não são usados para transformar uma parcela ausente em um stack novo. Se uma parcela não zero se tornar zero pelo cálculo de alvo, a aplicação também é pulada e o cálculo é registrado.

Amount zero continua sendo um efeito numérico válido e segue os reducers comuns. Não é reinterpretado como uma ausência automática de todos os seus efeitos filhos.

`distribution.scope` distingue `Sequence` (padrão) e `ParentSequence`. Em ParentSequence, o filho tem repeat 1, usa o snapshot de entrada do pai e recebe a parcela do índice daquele impacto. Seu orçamento é capturado uma vez para todas as chamadas; irmãos têm budgets independentes. Um stack residual em quatro acertos não é reaplicado quatro vezes. Filho sem essa opção continua sendo uma sequência própria. Root sem pai, escopos misturados no mesmo efeito e pais com múltiplos alvos por impacto falham.

## Chance, perda de alvo e condensação

- `PerEffect`: comportamento preservado, um sorteio por repetição, compartilhado pelos alvos daquela repetição.
- `PerTarget`: comportamento preservado, um sorteio por alvo elegível da repetição.
- `PerSequence`: um sorteio para esta chamada da sequência inteira. Não significa um sorteio global para todos os componentes da ação ou para toda uma árvore de efeitos.
- `PerAction`: um sorteio por nó lógico na execução do batch, reutilizado entre invocações repetidas de filhos.
- `PerProc`: um sorteio no proc pai, ou no próprio proc para efeitos raiz.

`chanceGroupId` permite compartilhar o sorteio entre nós distintos em PerAction/PerProc; sem grupo, os nós não compartilham aleatoriedade por coincidência de nome. Grupos com probabilidades conflitantes são rejeitados.

Condições continuam sendo avaliadas por impacto. Uma chance/condição falsa deixa o slot sem aplicação; não redistribui sua parcela. `executionScope` distingue EveryInvocation, OncePerAction e OncePerParentProc. O segundo/terceiro reservam a primeira tentativa, inclusive chance falsa; novas invocações registram scope_already_attempted. `executionGroupId` pode agrupar nós distintos. Action é a execução do batch de efeitos, não um estado global entre comandos ou ticks.

`childTiming` distingue AfterParentImpact e BeforeParentImpact. Efeitos anteriores executam depois da elegibilidade e antes do cálculo/aplicação, reconstroem as variáveis vivas e mantêm a cadeia de hashes. Se invalidarem o alvo, aplica-se a política do pai. Parcela zero de stacks não chama esses filhos. A gramática de composição agora fecha BeforeImpact e OncePerProc para esses mesmos contratos, sem executor alternativo.

Críticos são inputs aleatórios explícitos, não multiplicação no executor:

```json
"randomInputs": [
  { "inputId": "critical", "scope": "Action", "chance": 0.25, "groupId": "action_critical" }
]
```

Os escopos são Action, ParentProc e Impact. `rolls.critical.success` fornece 0/1 à pipeline/fórmula, e randomInputs nos steps registra valor sorteado, sucesso e identidade do escopo. Chance 0/1 não avança RNG. Cada nó tem seu input por padrão; groupId compartilha inputs Action/ParentProc entre nós. O cliente não pode fornecer o namespace rolls. O multiplicador, bucket, filtros e qualquer significado de “critical” são dados do setting. Em um budget distribuído, só inputs Action alimentam a captura; críticos por impacto pertencem aos stages de impacto, sem reaplicar origem.

Cada impacto resolve exatamente um alvo. TARGET com várias seleções, ALL_ENEMIES/ALL_ALLIES e retarget para grupos são rejeitados neste modo. Seletores automáticos de um alvo continuam usando o RNG determinístico normal, sem uma seleção extra para planejar o orçamento.

As políticas existentes de perda de alvo continuam autoritativas: Fail descarta a transação; Skip deixa o slot sem aplicação; StopRepeat interrompe os slots restantes; Retarget conserva índice/parcela e aplica os stages no novo alvo. Sem candidato, retarget interrompe. Não há redistribuição ou compensação silenciosa dos slots perdidos. Uma seleção inválida no começo continua sendo erro.

Condensação conserva OncePerAction por recipeId e o proc único de seus efeitos emitidos. A receita pode distribuir um input `condensation.*` compatível. Se usar BeforeConsumption, a captura do orçamento lê o snapshot anterior ao consumo; o opt-in de distribuição ainda usa alvo e capacidade **vivos por impacto**. Parâmetros sem distribution conservam a política numérica anterior da receita. Um erro posterior descarta também o consumo, recursos, modifiers, settlements e RNG.

## Trace, versões e limites

`sequenceBudgets` registra captura e alocação uma vez, no primeiro step que calcula o orçamento. `impactShares` referencia o orçamento e a parcela nos steps seguintes, inclusive quando o alvo foi perdido. O trace completo da alocação contém também slots que nunca foram aplicados. Um step de perda e um step de retarget podem referenciar o mesmo slot; apenas o segundo o aplica.

Capturas entram uma vez nos cálculos do batch; cálculos de alvo entram por aplicação. Os traces participam dos fingerprints e frames existentes, sem uma nova store persistente. A versão da engine é 17 após a 8b2 (16 na 8b1); saves anteriores são preservados e rejeitados quando incompatíveis, sem migração ou exclusão automática.

Profundidade 32, repeat 256 e orçamento global de 4.096 passos continuam valendo. Capturas e slots alocados participam do orçamento. Resolução numérica direta e schemas de payload rejeitam distribution não planejada, em vez de ignorá-la.

As regressões desta subetapa usam runtime fixado, pipeline/influências/settlements/reducers reais, rollback, dez execuções e serialização de definições/steps. Não substituem testes específicos de reinício/replay/branches de multi-hit no gateway persistente, que continuam pendentes antes de concluir a etapa 8.

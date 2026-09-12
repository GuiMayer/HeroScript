# Sistema de efeitos

**Status:** implementado no processador imutável canônico

**Atualizado em:** 2026-09-12

## Objetivo

Efeito é o componente reutilizável que descreve uma consequência de gameplay.
Cartas, ações, status, relíquias, regras de turno e eventos podem produzir a mesma
`EffectDefinition`. Para o processador, a origem altera proveniência e contexto,
mas não cria um algoritmo diferente.

Essa separação mantém três responsabilidades distintas:

- o proprietário decide quando e por que o efeito existe;
- o pipeline de cálculo resolve o valor;
- o processador de efeitos aplica a consequência ao snapshot.

## Fluxo canônico

```text
owner (carta/status/relíquia/regra)
  -> EffectDefinition
  -> seleção determinística de alvos
  -> condição e chance determinísticas
  -> pipeline de cálculo da revisão fixada
  -> resultado puro + settlements derivados do trace
  -> comando de efeito resolvido
  -> ImmutableEffectProcessor
  -> mutações atômicas de estado
  -> EffectApplicationRecord
  -> fila visual e journal
```

O processador não publica eventos durante uma transação. O agregado publica suas
projeções somente depois do commit completo.

## Definição

Exemplo de um efeito numérico:

```json
{
  "effectId": "lower_stability",
  "type": "DAMAGE",
  "target": "TARGET",
  "flatValue": 6,
  "formulaValue": null,
  "targetResource": "stability",
  "resourceField": "Current",
  "calculationChannel": "effect_amount",
  "calculationPipelineId": "default_effect_amount",
  "condition": "target.resources.stability.current > target.resources.stability.minimum",
  "chance": 1.0,
  "repeat": 1,
  "tags": ["attack", "physical"]
}
```

Campos essenciais:

| Campo | Função |
| --- | --- |
| `type` | Consequência a executar. |
| `target` | Política de alvo; não é um ID enviado pelo conteúdo. |
| `selectionResourceId` | Recurso usado para ordenar alvos automáticos baseados em recurso. |
| `flatValue` / `formulaValue` | Valor base autorado. |
| `targetResource` | Recurso alterado; obrigatório para efeito de recurso. |
| `resourceField` | `Current`, `Minimum` ou `Maximum`. |
| `operation` | Operação explícita de `MODIFY_RESOURCE`. |
| `calculationChannel` | Canal aceito pelo pipeline. |
| `calculationPipelineId` | Pipeline explícito opcional. |
| `condition` | Expressão booleana sobre o contexto canônico. |
| `chance` / `repeat` | Aleatoriedade seedada e repetição determinística. |
| `tags` | Contexto para filtros e influências. |

`chainedEffects` usa a mesma estrutura recursivamente; cada filho pode declarar
sua própria `condition`.
`EffectTriggerDefinition` liga uma lista de efeitos a um boundary nomeado e com
prioridade, sem acoplar o processador ao proprietário.

## Tipos e suporte

O catálogo inclui famílias para:

- recursos: `DAMAGE`, `HEAL`, `MODIFY_RESOURCE`;
- status: `APPLY_STATUS`, `REMOVE_STATUS`, `DISPEL_STATUS`;
- cartas/deck: `DRAW_CARD`, `DISCARD_CARD`, `EXHAUST_CARD`,
  `ADD_CARD_TO_HAND`;
- modifiers: `APPLY_MODIFIER`, `REMOVE_MODIFIER`.

O enum público contém somente primitivas executáveis. Condição é um campo do
efeito e composição usa `chainedEffects`; lifecycle usa triggers do proprietário.
A validação de publicação impede referências quebradas antes de uma run usar a
revisão.

## Recursos são genéricos

Os três tipos numéricos convergem para a mesma representação:

| Tipo autorado | Operação resolvida |
| --- | --- |
| `DAMAGE` | `Subtract` |
| `HEAL` | `Add` |
| `MODIFY_RESOURCE` | `Add`, `Subtract` ou `Set` conforme `operation` |

Nenhum deles seleciona `health`, `energy` ou outro ID implicitamente. Os exemplos
abaixo são equivalentes do ponto de vista do processador, mudando apenas os dados:

```json
{ "type": "DAMAGE", "targetResource": "mana", "flatValue": 3 }
```

```json
{ "type": "HEAL", "targetResource": "morale", "flatValue": 3 }
```

```json
{
  "type": "MODIFY_RESOURCE",
  "targetResource": "action_points",
  "resourceField": "Maximum",
  "operation": "ADD",
  "flatValue": 1
}
```

O `ResourceMutationReducer` aplica o lote. A condição de derrota, se houver, vem
da `thresholdPolicy` do recurso depois da transição.

## Cálculo por buckets

`flatValue` é o valor base, não necessariamente o resultado final. A definição
do efeito escolhe um canal; o modo habilita pipelines compatíveis e a revisão
fixada fornece a ordem dos buckets.

Influências de carta, stats/recursos de ator e alvo, status, relíquia, modo,
encontro e modificador são normalizadas como `CalculationInfluence`. O cálculo ordena
contribuições por prioridade e identidade, aplica os buckets e produz um trace e
fingerprint.

Um pipeline pode ligar diretamente um recurso ao cálculo:

```json
{
  "pipelineId": "default_effect_amount",
  "channel": "effect_amount",
  "buckets": [
    { "bucketId": "flat", "order": 10, "operation": "Add" },
    { "bucketId": "more", "order": 20, "operation": "Multiply" }
  ],
  "resourceInfluenceBindings": [
    {
      "bindingId": "actor.power.flat",
      "scope": "Actor",
      "resourceId": "power",
      "field": "Current",
      "channel": "effect_amount",
      "bucket": "flat",
      "scale": 1,
      "offset": 0,
      "priority": 0
    }
  ]
}
```

Upgrade de carta altera o container efetivo antes do cálculo. Status do ator,
relíquias e recursos contribuem como influências contextuais. Assim, upgrade e
scaling permanecem conceitos separados e auditáveis no trace.

Os tipos numéricos nunca ignoram a pipeline em uma run configurada. O modo deve
habilitar exatamente uma pipeline do canal do efeito, ou o efeito deve selecionar
explicitamente uma das pipelines habilitadas. Configuração ausente ou ambígua
falha antes da mutação.

Bindings de recursos podem declarar um `settlement`. Nesse caso a parcela usada
registrada no trace vira uma mutação de recurso antes da consequência principal.
O motor de cálculo continua puro; ele não sabe que a influência representa uma
capacidade, entidade ou recurso.

## Variáveis de fórmula

Recursos usam nomes explícitos e simétricos:

```text
source.resources.<id>.current|minimum|maximum|percent
target.resources.<id>.current|minimum|maximum|percent
```

Variáveis externas do boundary podem coexistir com esse namespace, mas não devem
sobrescrever silenciosamente valores canônicos. Fórmulas e condições são avaliadas
com o mesmo conteúdo fixado da execução e da prévia de carta.

## Alvos e determinismo

O alvo pode ser o próprio ator, um alvo selecionado, coleções configuradas ou uma
seleção automática. Quando a seleção depende de um recurso, o conteúdo informa
`selectionResourceId`; não existe preferência embutida por vida.

Aleatoriedade usa o contexto determinístico da run. A engine registra consumo da
seed e aplica desempates estáveis. A Godot nunca escolhe um resultado aleatório em
nome da engine.

## Atomicidade e auditoria

Antes do commit, o executor valida toda a operação: existência dos participantes,
alvos, recursos, pipelines, condições, settlements e custos. Se uma parte falha,
o snapshot não muda e nenhum evento intermediário é publicado. Consumo de uma
capacidade e aplicação do valor restante pertencem à mesma transação.

Para cada efeito aceito, `EffectApplicationRecord` registra a proveniência e a
aplicação concreta. Esses registros alimentam a fila visual, logs e inspeção de
replay sem transformar animação em regra de jogo.

Remoções de modifiers registram todas as instâncias afetadas e cada mudança de
stacks, evitando que uma operação em lote seja resumida por apenas um ID.

## Uso por cartas e outros proprietários

Uma carta é um container de componentes: custos, condições, política de alvos,
efeitos e upgrades. Ao jogar:

1. a instância é resolvida contra a definição da revisão;
2. upgrades geram a definição efetiva;
3. legalidade e custos são avaliados;
4. influências contextuais são coletadas;
5. efeitos são calculados e aplicados na transação;
6. a carta muda de zona somente se a ação inteira for aceita.

Status e relíquias entram no mesmo caminho por triggers. Regras de turno produzem
efeitos fonte-agnósticos, inclusive para regeneração.

## Contrato de prévia

Os endpoints de avaliação de carta usam os mesmos compiladores, resolvedores e
cálculos da execução real. Uma prévia legal com alvos completos deve corresponder
ao resultado determinístico, salvo mudança concorrente detectada por
`expectedSequence`/`expectedStep`.

## Regras para extensões

1. Não crie um processador por origem do efeito.
2. Não inferir recurso a partir de `EffectType`.
3. Não aplique mutações fora dos redutores canônicos.
4. Mantenha cálculo separado de aplicação.
5. Registre proveniência sem torná-la um desvio algorítmico.
6. Valide referências na publicação e novamente na fronteira de execução.
7. Use ordem estável e aleatoriedade seedada.
8. Garanta equivalência entre prévia, execução e replay.

## Referências

- [Sistema de recursos](../resources/resource-system.md)
- [Sistema de combate](../combat/combat-system.md)
- [Arquitetura de componentes de carta](../../plans/CARD_COMPONENT_ARCHITECTURE_AND_IMPLEMENTATION_PLAN.md)
- `src/Core/Effects/ImmutableEffectProcessor.cs`
- `src/Core/Calculations/CalculationEngine.cs`
- `src/Core/Run/Content/CardPlayExecutor.cs`

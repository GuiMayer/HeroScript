# Respostas de gameplay

Estado atual: contratos, compilador e coletor da etapa 5. Não é ainda autoria executável liberada; dispatcher, migrações, persistência e endpoints têm etapas próprias no [plano](../plans/GENERIC_GAMEPLAY_EVENTS_AND_RESPONSES_ARCHITECTURE_AND_PLAN.md).

O [schema JSON](../schemas/gameplay-response.schema.json) descreve a definição. A publicação também verifica o catálogo capturado e o validador canônico de efeitos. A leitura usa as mesmas opções de conteúdo do projeto, incluindo enums por nome.

```json
{
  "schemaVersion": 1,
  "responseId": "observe_resource_gain",
  "observe": {
    "kinds": { "include": ["resource.change.resolved"] },
    "subject": { "relationshipToOwner": "Enemy" },
    "resource": { "field": "Current", "direction": "Increase" }
  },
  "condition": "event.change.applied_delta > 0",
  "frequency": { "scope": "Occurrence", "maximum": 1 },
  "propagation": "RecordOnly",
  "effects": [
    { "type": "MODIFY_RESOURCE", "target": "SELF", "targetResource": "barrier", "operation": "ADD", "flatValue": 2 }
  ]
}
```

`barrier` é um recurso de exemplo que precisa existir no setting. O tipo de recurso não concede semântica de cura/dano; o filtro do exemplo observa aumentos, independentemente do motivo. Para cura, restringir também os motivos declarados pelo setting.

## Definição, registro e execução

- A definição contém filtros, bindings, disponibilidade, frequência, ordem e efeitos.
- O registro efetivo identifica provider, instância, componente, revisão capturada e dono opcional, preservando contribuidores de composição.
- A invocação congelada e sua execução pertencem ao dispatcher; compilar/coletar registros não executa efeitos, fórmulas ou RNG.

As dimensões do filtro são combinadas com AND. Inclusões de tópico são OR; exclusões vencem. Sobrepor padrões não produz múltiplas invocações. Relações são tipadas, nunca inferidas de nomes.

Bindings podem apontar para dono físico, aplicador do container, dono lógico, origem/afetado/responsável atribuído do evento ou ator explícito. O binding do dono lógico não pode apontar para ele mesmo. Um registro sem dono físico exige binding explícito; não há fallback para o primeiro ator. A política de binding ausente será Skip ou RejectAction.

## Disponibilidade de cartas

Cartas devem declarar CardZones com IDs exatos de zonas, SelectedCard ou ResolvingCard. ContainerActive é reservado aos containers cujo ciclo de vida define sua existência ativa. Uma carta não é presumida ativa no baralho inteiro.

A coleção confirma instância, endereço, definição da zona capturada e dono físico. Zonas de ator pertencem ao ator indicado; zonas da run pertencem ao jogador; cartas globais não recebem um dono físico arbitrário. Nenhuma dessas verificações concede permissão para jogar uma carta: o fluxo canônico de legalidade continua responsável por isso.

## Limites de publicação

O compilador rejeita filtros impossíveis, enum inválido, bindings incompletos, tags contraditórias e efeitos inválidos. Padrões sem correspondência no catálogo são rejeitados. Reservas por wildcard para extensões exigem permissão do host; registros sem tópico conhecido ficam dormentes e não são anunciados como executáveis.

Occurrence, Impact, Proc e RootAction são escopos transacionais do contrato inicial. Escopos maiores exigem a implementação dos contadores persistentes e não podem ser habilitados por uma flag no JSON. O schema só anuncia os escopos iniciais. A prioridade normalizada usa inteiros de 64 bits para permitir a ordem decrescente mesmo em `int.MinValue`; desempates são ordinais e explícitos.

## Providers concretos e fontes capturadas

Os providers de modo, encontro, ator, relíquia, status e carta somente materializam registros. Definições podem carregar `responses`; atores e cartas usam componentes próprios. Status e relíquias conservam a revisão capturada. O nó guarda a revisão de suas respostas; não usa automaticamente uma revisão futura. Um registro de carta usa a composição efetiva existente, com upgrades e contribuições de compartimentos, e conserva os contribuidores que afetam aquele componente.

Esses novos campos ficam ausentes na serialização quando não usados. A publicação padrão rejeita declarações ainda não executáveis. Ferramentas internas podem criar um runtime/compilador shadow explicitamente para testar a compilação; isso não habilita o conteúdo na API ou numa partida.

## Namespace numérico

A projeção recebe as referências necessárias e a fotografia declarada pelo registro. Não enumera propriedades arbitrárias. Exemplos:

- `event.change.before`, `after`, `operand`, `requested_value`, `requested_delta`, `applied_delta`, `limited_delta` quando o payload disponibiliza esses números.
- `event.stacks.before/after`, `event.duration.before/after`, `event.transport.before/after/requested_current` para seus respectivos payloads.
- `owner.<componente de atributos>.<valor>`, como `owner.stats.power`.
- `owner.resources.<recurso>.current/maximum/minimum`, sem nomes de recursos especiais.
- `event.source.<componente>.<valor>`, `event.subject...`, `event.attributed...` e os equivalentes de recursos.
- `scope.container_stacks`, `scope.container_duration`, `scope.run_sequence`, `event.ordinal`, `event.depth`.

Campo ausente, binding ausente ou valor que não cabe no contrato numérico existente produz erro quando solicitado. Não existe zero implícito ou seleção do primeiro ator. Referências não solicitadas não são calculadas; ler um payload não executa efeitos nem movimenta o RNG.

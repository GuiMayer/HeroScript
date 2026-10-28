# Respostas de gameplay

Estado atual: contratos/providers, dispatcher transacional candidato e migração das reações de recurso das relíquias (etapas 5–7). A autoria nova ainda não está liberada. Lifecycles/fases também usam o dispatcher comum (etapa 8). Os demais containers, persistência e endpoints continuam nas etapas seguintes do [plano](../plans/GENERIC_GAMEPLAY_EVENTS_AND_RESPONSES_ARCHITECTURE_AND_PLAN.md).

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
  "condition": "event.change.applied_delta",
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

## Matching e quotas transacionais (etapa 6a)

O índice de kinds produz no máximo um candidato por registro/ocorrência; exclusões vencem e as demais dimensões são conjuntivas. Bindings não substituem um ator ausente por outro. O dono lógico pode diferir do dono físico capturado. Filtros de recurso usam o payload tipado e distinguem PrimaryEffect, Settlement e DirectMutation. Direção usa o delta efetivamente aplicado. IDs de recurso seguem a comparação canônica sem diferenciar maiúsculas; canais, tags e identidades são ordinais.

As quotas locais retornam sucessores imutáveis por ação. Uma ocorrência não volta a invocar o mesmo registro mesmo com maximum maior que um. Impact e Proc exigem suas respectivas âncoras explícitas; não caem silenciosamente para ocorrência. Ultrapassar a quota declarada apenas impede a invocação; ultrapassar o orçamento global rejeita o candidato inteiro. Frequências duráveis permanecem indisponíveis. O matcher não avalia condições nem executa efeitos; o dispatcher é a próxima integração.

## Execução candidata explícita (etapa 6b)

`EffectTriggerExecutor.ExecuteResponsiveObserved` executa apenas uma candidata isolada. Compartilha o orçamento do executor comum e retorna efeitos, fatos e quotas locais; não grava nem habilita a capacidade na API. Para cada operação, registros e condições elegíveis são congelados em BeforeOccurrence/AfterOccurrence antes da primeira resposta. O corpo usa o sucessor corrente, e a revalidação de dono/registro segue a definição. RecordOnly suprime respostas descendentes, preservando os fatos. AllowResponses continua a cadeia sob os mesmos limites; qualquer falha descarta tudo.

Condições seguem a linguagem numérica canônica: resultado finito maior que zero significa verdadeiro. Use uma referência numérica, expressão aritmética aceita pelo avaliador ou fórmula JSON revisionada; operadores de comparação textuais como `>` não constituem uma nova linguagem de condições. Referências event.*, owner.* e scope.* são tipadas; campos ausentes não viram zero. Fórmulas nomeadas são resolvidas na revisão capturada, e somente parâmetros efetivamente referenciados são projetados. A passagem para publicação/autoria normal e integração com os demais produtores ainda não ocorreu.

Os fatos de respostas preservam o container, componente e revisão capturados do observador, sem substituir a origem dos efeitos seguintes. Durante um combate, bindings e projeções numéricas se restringem aos participantes do encontro; a ficha persistente somente constitui o contexto de ator quando não há combate. Observadores novos respeitam a seleção explícita BeforeOccurrence/AfterOccurrence. A derrota do dono pode invalidar uma invocação congelada conforme RevalidateOwner/RequireLivingOwner.

## Tradução histórica de reações de recursos (7a)

O provider de migração é interno e somente lê regras já capturadas nas relíquias. Ele traduz filtros de PrimaryEffect/recurso/canal/tags, dono Source/Target, alvo, prioridade e RecordOnly. O corpo original dos efeitos permanece intocado, incluindo parent.*. A tradução isolada ainda não substitui a execução antiga: a integração precisa preservar os aliases numéricos e as identidades de execução antes do cutover. Não há nova opção JSON de compatibilidade.

### Integração das reações históricas (7b)

A execução normal e observada traduz as reações capturadas e usa o dispatcher comum; o executor não mantém uma segunda coleta por relíquia. Templates internos preservam as identidades/provenance históricas e os aliases parent.* pelo mesmo executor de efeitos, para manter os resultados de replay. Eles não são campos JSON novos. Prefixes não entram no antigo slot de resposta. A observação exige o setting/root-run/branch reais.

Quando uma aplicação usa um ator adaptador para uma carteira da run, somente a tradução histórica lê seu binding original na aplicação canônica. O fato publicado continua identificando a carteira como dona e não inventa um ator sujeito; respostas novas recebem o protocolo sem essa adaptação histórica. Sete cenários de regressão comparam o hash completo da execução com a versão anterior à migração, incluindo RNG, cálculos, passos, identidades e snapshots.

## Filtro de fronteira/fase (8a)

`observe.boundary.boundaryIds` seleciona IDs do `BoundaryOccurrencePayload`, por igualdade ordinal. O tópico continua descrevendo a natureza (`phase.entered`, `activation.ended` etc.); o ID seleciona a fase/fronteira configurada, sem inferir o que ela faz. A lista deve ter 1–64 IDs únicos. Ela não pode ser combinada com o filtro Resource, pois uma ocorrência tem um único payload tipado. O filtro ausente não muda a serialização existente. Esta capacidade de seleção prepara a migração; a autoridade de timing/duração permanece nos lifecycles atuais até a integração da etapa 8.

### Timing e execução de fronteiras (8b)

Status, relíquias e fases convertem as definições capturadas em assinaturas do dispatcher comum. O módulo de lifecycle mantém o tempo, expiração e idempotência; não seleciona/executa efeitos por um segundo interpretador. Stacks, duração e payloads são lidos da instância live quando o gatilho executa, mantendo a definição/fonte capturadas. Uma instância removida deixa de executar, e uma instância criada durante a fronteira não entra retroativamente no snapshot antigo. Regeneração conserva seu timing e emite os fatos pelo executor comum de recursos.

Escopos de fronteira são privados à candidata e não são salvos. Compartilham trabalho/quotas, rejeitam rewind/substituição do contexto e não podem ser reutilizados após falha. O caminho de migração aceita um gatilho histórico vazio e preserva suas identidades; a definição JSON nova continua exigindo efeitos. Esses templates e relatórios transitórios são ignorados na serialização dos snapshots/recibos.

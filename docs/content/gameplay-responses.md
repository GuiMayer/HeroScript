# Respostas de gameplay

Estado atual (2026-10-29): autoria canônica habilitada em conteúdo schema 2 e pacotes 1.1.0. Respostas dos containers, PriorityStack, migração, lote causal no commit, consultas REST e laboratório Godot estão integrados. O [relatório de certificação](../architecture/GAMEPLAY_EVENTS_CERTIFICATION_REPORT.md) registra testes, medições e limitações; o [plano](../plans/GENERIC_GAMEPLAY_EVENTS_AND_RESPONSES_ARCHITECTURE_AND_PLAN.md) conserva as decisões de arquitetura.

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

Esses novos campos ficam ausentes na serialização quando não usados. A publicação schema 2 aceita declarações canônicas validadas e rejeita hooks históricos misturados. Schema 1 é reservado a bundles capturados; o registro histórico não habilita autoria nova. Ferramentas de migração validam o grafo candidato antes de publicar.

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

As quotas locais retornam sucessores imutáveis por ação. Uma ocorrência não volta a invocar o mesmo registro mesmo com maximum maior que um. Impact e Proc exigem suas respectivas âncoras explícitas; não caem silenciosamente para ocorrência. Ultrapassar a quota declarada apenas impede a invocação; ultrapassar o orçamento global rejeita o candidato inteiro. PriorityWindow é durável; Activation, Round, Combat e Run permanecem rejeitados. O matcher não avalia condições nem executa efeitos; isso pertence ao dispatcher comum.

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

### Componentes de entidade (9a)

O materializador reconhece o componente `responses` e captura suas declarações no estado imutável do ator. O validador estrutural rejeita arrays não inicializados, mais de 16 declarações e IDs ausentes/duplicados; o compilador de respostas mantém a validação semântica/referencial antes de executar. A identidade, lado e binding de um ator continuam fixos durante sua vida no combate. O teste de integração dos providers verifica um terceiro ator que observa a cura entre dois outros, mantendo origem/alvo próprios em cada fato e a cadeia causal.

### Execução normal pós-fato (9)

O executor normal detecta declarações tipadas no estado e no runtime imutável e usa os mesmos providers do caminho observado. A identidade de efeito deriva da causa de domínio; acrescentar observação não deve mudar o efeito. Testes cobrem buff de atributo, custo de recurso e um terceiro observador, comparando a saída completa dos caminhos normal/observado. A presença por revisão é apenas metadado derivado; não contém contadores/RNG e não é compartilhada globalmente por nomes de revisão. O broadcast único de combate/rodada/ativação já pertence ao executor geral do fluxo; fases também despacham sua própria fronteira uma vez. Os sub-lifecycles conservam as oportunidades capturadas históricas para não duplicar a assinatura nova.


### Composição, continuidade e produtores externos (9c–9h)

A compilação de cartas preserva o catálogo nas resoluções de upgrades, bundles e contribuições de compartimentos. A zona real determina a disponibilidade; um componente concedido usa os mesmos providers e processador de efeitos. Cartas não ganham autoridade para publicar fatos.

As quotas e o trabalho de respostas seguem por uma continuidade imutável dentro da candidata. Forks de simulação não compartilham contadores mutáveis. O despacho de fatos já produzidos exige snapshots reais, prefixo causal append-only e o mesmo agregado/revisão. Os contadores RootAction continuam locais à candidata. PriorityWindow possui contador imutável/bounded na continuação serializada da janela, preservado entre comandos, save/load e branches. Activation, Round, Combat e Run continuam rejeitados enquanto não houver sua implementação durável.

Status que reduzem duração/expiram e modificadores que expiram/reduzem duração nas fronteiras de combate também produzem respostas. O snapshot de status e os IDs elegíveis de modificadores são capturados antes das respostas da fronteira, evitando que instâncias recém-criadas sejam processadas retroativamente. O refresh primitivo de recursos conserva a política configurada e incorpora os sucessores da run e do combate, incluindo aplicações/passos das respostas.

**Limite atual:** fluxos externos de cartas, comandos fora de combate, ação raiz e criação da run integram o dispatcher comum. Transporte/projeção de recursos e ativação de revisão registram seus contratos específicos, sem inferir dano/cura ou habilitar hooks arbitrários na troca de revisão. Não há comando para injetar fatos nem prevenção/cancelamento arbitrários.

### Pilha e frequência entre comandos (10)

`action.proposed` descreve uma proposta autorizada pela PriorityStack existente. `action.resolved` descreve resolução real, e `action.cancelled` descreve fizzle, mantendo a política configurada de custo/reembolso. Esses fatos não abrem uma segunda pilha nem desfazem fatos resolvidos. A proposta conserva sua âncora causal; a continuação usa o pai local quando ainda está no mesmo comando, ou a referência histórica quando está em outro. A ancestralidade autorizada é explícita e limitada.

`frequency.scope: PriorityWindow` limita as execuções por registro na janela lógica, inclusive propostas aninhadas e comandos novos. A janela armazena somente contadores derivados da execução autoritativa; consultas não os consomem. Branches copiam o estado imutável e evoluem independentemente. IDs de combate rebased não resetam os contadores de observadores do encontro.

### Migração de conteúdo e corte de autoria (11)

O campo `timing` tem valor padrão `PostFact`. `ContainerBoundary` agenda a mesma definição no ponto já existente do lifecycle de status/relíquia/fase, separado do broadcast global. O host deve suportar explicitamente essa capacidade; o compilador genérico não a concede. A agenda exige frequência `Occurrence` e fronteira explícita. Fases usam `phase.entered`/`phase.exited` com seu ID de fase; status/relíquias usam os IDs canônicos de fronteira do ciclo. Source, owner, payloads e variáveis capturados continuam no executor universal.

Os conversores puros de `GameplayBoundaryResponseMigration` devolvem candidatas e não editam arquivos, publicam revisões ou alteram bundles de runs salvas. Eles cobrem triggers de status/relíquias, entryEffects/exitEffects de fases e reações de recurso com referências inline suportadas. A mistura ambígua entre a forma histórica e a canônica é rejeitada.

`order.group` ordena grupos antes da identidade do container, preservando prioridades entre relíquias. As referências inline `parent.requested_change`, `parent.applied_change` e `parent.limited_change` migram para `event.change.requested_delta`, `event.change.applied_delta` e `event.change.limited_delta`; `stacks` migra para `scope.container_stacks` em reações de recurso. Não se deduz dano pelo nome do recurso ou pelo sinal do número.

Referências externas não expandidas e corpos estocásticos/continuations cuja identidade ainda não tem migração certificada permanecem rejeitados. A ferramenta de arquivos/diff, migração dos pacotes e corte de autoria ainda não estão concluídos. Os templates históricos continuam necessários até esse corte e para bundles históricos suportados.

## Persistência causal

O commit schema 4 pode conter `GameplayEvents` (lote schema 1), com scope, occurrences e invocations. A candidata é gravada uma única vez antes de publicar o estado. O lote não faz parte do hash do estado nem constitui uma fila a executar ao carregar. As quotas de PriorityWindow permanecem no estado autoritativo da janela; quotas de ação são efêmeras. Commits anteriores sem esse campo são válidos e exibem ausência de diagnóstico, sem reconstrução heurística. Status e ações pendentes usam suas âncoras causais capturadas, inclusive entre branches autorizadas. Nova revisão não reescreve o conteúdo histórico.

## Descoberta e inspeção REST

- `GET /api/v1/content/{settingId}/revisions/{revision}/gameplay-events/catalog`: tópicos, motivos e convenções da revisão publicada.
- `GET /api/v1/content/{settingId}/revisions/{revision}/gameplay-responses/schema`: schema de autoria vinculado ao mesmo escopo.
- `GET /api/v1/runs/{runId}/commits/{sequence}/gameplay-events?contentRevision={revision}`: lote causal gravado. O parâmetro de revisão é opcional e valida a revisão da ação histórica, não a mais recente.

Inspeção causal completa exige `timeline.inspect_state` e `debug.card_inspection.full`, intersectados com as permissões capturadas no modo. Sem ambas, 403 antes de ler os fatos; não se vaza identidade de cartas ocultas pela timeline normal/recibos. Histórico sem lote devolve `recorded: false`, não executa efeitos para fabricar fatos. Não existe POST para injetar ocorrência. Preview, simulação e IA continuam usando o planejador/dispatcher do gameplay; a API nova é somente consulta. O cliente Godot usa o gateway de comunicação existente.

## Projeção e laboratório Godot

Abra `examples/godot-engine-showcase/scenes/gameplay_event_lab.tscn` para comparar os dois fixtures de apresentação (Ascendant e mesa circular), alternando inglês/português. Esses fixtures são explicitamente offline e não definem regras nem representam saves. `gameplay_event_view.gd` e seu presenter são os mesmos componentes usados pela timeline autorizada. A árvore permite selecionar a ocorrência e ver natureza/valor solicitado/aplicado/limitado; origem atrasada aponta branch e ocorrência anteriores. Fallback: motivo exato → ancestrais do motivo → tipo exato → ancestrais do tipo → mensagem genérica, consultando inglês quando faltar tradução. Nomes ausentes mantêm IDs; fonte ausente é Environment/Ambiente. Texto localizado nunca retorna à engine como comando/regra.

## Custo e caches derivados

O matcher cruza índices de tipo, motivo e fotografia de disponibilidade antes de revalidar atores/recursos. Compilações e fingerprints imutáveis têm cache por identidade do catálogo (setting/revisão), definição e capacidades, limitado a 4096 entradas por catálogo, com descarte integral ao atingir o limite e retenção fraca do catálogo. Cada sessão mantém no máximo duas coletas, reutilizadas somente para as mesmas referências imutáveis de run/combate. Mudança de composição/estado exige nova coleta. Nenhum cache guarda RNG, decisões de condição ou quotas autoritativas.

`OperationalTiming` separa response_compile, response_collect, response_match, response_execute, response_dispatch e commit_serialize, além das métricas existentes de commit e REST. Tempos de despacho/execução aninhados são inclusivos; não se deve somá-los como fases exclusivas. Diagnóstico não entra nos hashes determinísticos.

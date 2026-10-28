# Ocorrências de gameplay — contratos internos

Estado: infraestrutura de contratos; ainda não habilita respostas novas em conteúdo publicado. A migração do executor é uma etapa posterior do [plano](../plans/GENERIC_GAMEPLAY_EVENTS_AND_RESPONSES_ARCHITECTURE_AND_PLAN.md).

`GameplayOccurrence` é um registro imutável com identidade causal, escopo de setting/revisão/run/branch/ação/comando, participantes opcionais, proveniência, tags e payload discriminado. Não depende da classe da origem para escolher o processamento. `SourceActorId` pode estar ausente; `AttributedActorId` é uma atribuição explícita, não o primeiro ator do combate.

Payloads suportados pelo contrato: recurso, atributo, status, movimento de carta, ação, fronteira e resultado de regra. Não existe payload livre que possa executar código. A classe do payload deve corresponder ao tipo declarado no catálogo. Fatos derivados de mudanças de recurso distinguem o operando, o valor pedido ao redutor, antes/depois e deltas computados em double a partir dos floats canônicos. Não arredondam números para apresentação.

`ResourceChangePayload`: subtrair 12 de 8, limitado a zero, significa `Operand=12`, `Before=8`, `RequestedValue=-4`, `After=0`, `RequestedDelta=-12` e `AppliedDelta=-8`. Subtrair 8 após uma absorção de 4 é outro caso: o operando do redutor é 8 e o settlement tem referência própria. `Set(20)` a partir de 15 pede delta +5; não +20.

`GameplayEventCatalog` é privado a um setting e uma revisão. Seus contratos de publisher são fornecidos pela composição confiável da engine, não por JSON de comandos/equipamentos. O conteúdo pode declarar tipos e motivos compatíveis com publishers suportados; não pode se autorizar a emitir uma mutação de recurso ou abrir uma janela não implementada. Referência de autoria: [schema do catálogo](../schemas/gameplay-event-catalog.schema.json).

Aliases são validados e resolvidos no catálogo; ciclos, destinos inexistentes e colisão com identificadores canônicos são inválidos. Uma ocorrência pronta para execução usa IDs canônicos, nunca o alias. Isso impede duplicar fatos como efeito colateral da migração. Wildcards seguem o [contrato de hierarquias](gameplay-event-hierarchies.md).

Nada nesta etapa publica ocorrências, adiciona campos a snapshots existentes, muda a engineVersion ou instala um segundo journal. A validação do envelope não equivale a aceitar fatos arbitrários do cliente: o gateway continua sendo a única entrada de comandos.

## Causalidade entre ações

`GameplayEventTransaction` cria sucessores imutáveis. IDs usam escopo, publisher, caminho do produtor e ordinal, sem RNG ou relógio. Append inválido não consome ordinal, contador nem contexto aleatório. O publisher precisa corresponder à capacidade do tipo no catálogo; aliases são resolvidos uma vez antes da validação da ocorrência.

`ParentEventId` e `RootEventId` descrevem a execução na ação atual. Um efeito atrasado pode ter `OriginCause`, uma referência histórica separada à ação que o instalou, com responsável atribuído e revisão. Isso permite seguir a cadeia entre ações sem transformar a idade de um status em profundidade de recursão. A referência histórica não pode ser usada para forjar um pai inexistente da ação atual. Outra branch só é aceita quando o chamador fornece a lista de ancestrais autorizados.

`StatusEffectInstance`, `StackPayloadLot` e `PendingActionState` possuem campo opcional `OriginCause`, omitido quando nulo. A serialização dos estados existentes continua sem esse campo; não há preenchimento automático nem migração silenciosa. O primeiro produtor que tornar esse estado novo autoritativo precisará de corte explícito de engine/schema, baseline de replay e certificação. Até esse corte, engineVersion 34 e seus consumidores continuam executando o comportamento anterior.

## Primeiros produtores em observação shadow

`GameplayResourceFactProducer` converte a entrada e o resultado do redutor canônico em um draft tipado. O operando vem do comando original: não pode ser inferido do delta arredondado. Para efeitos, é usado `ResolvedEffectCommand.ResolvedValue`; settlements exigem ligação inequívoca ao seu input próprio. O produtor valida identidades, alvo, recurso e campo antes de aceitar o fato.

`ObservedResourceTransitions` executa `ResourceSet.Apply` ou `ResourceCostTransitions.Spend` e acrescenta fatos ao sucessor privado da transação de eventos. Não substitui essas regras, não publica no event bus e não modifica o estado de entrada. Falha do redutor ou da observação retorna falha sem entregar um sucessor parcial. O motivo é explícito: duas subtrações iguais podem representar custo e dano distintos.

O payload pode identificar o dono por `GameplayObjectReference`, permitindo recursos de ator, run, tabuleiro ou outro agregado sem inventar um ator. Essa referência identifica o afetado; não é proveniência do executor.

Essa etapa é opt-in interno para testes/shadow. Os consumidores atuais ainda não foram migrados, não há dispatcher genérico em produção e não se deve declarar concluída a cobertura de status, atributos, cartas, fronteiras ou comandos. Essa ligação é a continuação da etapa 4, antes da habilitação de respostas novas.

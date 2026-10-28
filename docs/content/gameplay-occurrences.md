# Ocorrências de gameplay — contratos internos

Estado: infraestrutura de contratos; ainda não habilita respostas novas em conteúdo publicado. A migração do executor é uma etapa posterior do [plano](../plans/GENERIC_GAMEPLAY_EVENTS_AND_RESPONSES_ARCHITECTURE_AND_PLAN.md).

`GameplayOccurrence` é um registro imutável com identidade causal, escopo de setting/revisão/run/branch/ação/comando, participantes opcionais, proveniência, tags e payload discriminado. Não depende da classe da origem para escolher o processamento. `SourceActorId` pode estar ausente; `AttributedActorId` é uma atribuição explícita, não o primeiro ator do combate.

Payloads suportados pelo contrato: recurso, atributo, status, movimento de carta, ação, fronteira e resultado de regra. Não existe payload livre que possa executar código. A classe do payload deve corresponder ao tipo declarado no catálogo. Fatos derivados de mudanças de recurso distinguem o operando, o valor pedido ao redutor, antes/depois e deltas computados em double a partir dos floats canônicos. Não arredondam números para apresentação.

`ResourceChangePayload`: subtrair 12 de 8, limitado a zero, significa `Operand=12`, `Before=8`, `RequestedValue=-4`, `After=0`, `RequestedDelta=-12` e `AppliedDelta=-8`. Subtrair 8 após uma absorção de 4 é outro caso: o operando do redutor é 8 e o settlement tem referência própria. `Set(20)` a partir de 15 pede delta +5; não +20.

`GameplayEventCatalog` é privado a um setting e uma revisão. Seus contratos de publisher são fornecidos pela composição confiável da engine, não por JSON de comandos/equipamentos. O conteúdo pode declarar tipos e motivos compatíveis com publishers suportados; não pode se autorizar a emitir uma mutação de recurso ou abrir uma janela não implementada. Referência de autoria: [schema do catálogo](../schemas/gameplay-event-catalog.schema.json).

Aliases são validados e resolvidos no catálogo; ciclos, destinos inexistentes e colisão com identificadores canônicos são inválidos. Uma ocorrência pronta para execução usa IDs canônicos, nunca o alias. Isso impede duplicar fatos como efeito colateral da migração. Wildcards seguem o [contrato de hierarquias](gameplay-event-hierarchies.md).

Nada nesta etapa publica ocorrências, adiciona campos a snapshots existentes, muda a engineVersion ou instala um segundo journal. A validação do envelope não equivale a aceitar fatos arbitrários do cliente: o gateway continua sendo a única entrada de comandos.

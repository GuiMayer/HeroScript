# Ocorrências de gameplay — contratos internos

Estado: infraestrutura de contratos; ainda não habilita respostas novas em conteúdo publicado. A migração do executor é uma etapa posterior do [plano](../plans/GENERIC_GAMEPLAY_EVENTS_AND_RESPONSES_ARCHITECTURE_AND_PLAN.md).

`GameplayOccurrence` é um registro imutável com identidade causal, escopo de setting/revisão/run/branch/ação/comando, participantes opcionais, proveniência, tags e payload discriminado. Não depende da classe da origem para escolher o processamento. `SourceActorId` pode estar ausente; `AttributedActorId` é uma atribuição explícita, não o primeiro ator do combate.

Payloads suportados pelo contrato: recurso, atributo, status, movimento de carta, ordenação de zona, ação, fronteira e resultado de regra. Não existe payload livre que possa executar código. A classe do payload deve corresponder ao tipo declarado no catálogo. Fatos derivados de mudanças de recurso distinguem o operando, o valor pedido ao redutor, antes/depois e deltas computados em double a partir dos floats canônicos. Não arredondam números para apresentação.

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

## Status, atributos, cartas e fronteiras (etapa 4b)

`GameplayEntityFactProducer` consome resultados canônicos de atributos e stack changes de status, conferindo as instâncias antes/depois. Duração é um fato separado de quantidade de stacks; a expiração tem classificação própria. A fonte imediata continua explícita e não é substituída pelo aplicador histórico. Fronteiras usam um enum fechado e o sequenciamento já resolvido pelo lifecycle: o produtor não avança fases nem executa efeitos.

`GameplayCardFactProducer` projeta somente os passos primitivos efetivos do fluxo. Seleção de cartas e agregados de controle não duplicam movimentos. Criação e destruição têm apenas um endpoint; movimento tem dois. Shuffle/reorder usam `ZoneOrderPayload`, preservando a permutação real em vez de inventar movimentos. Referências das zonas vêm do conteúdo resolvido, sem dividir endereços por delimitadores ou inferir o ownerScope pelo nome.

Esses adaptadores ainda não instrumentam todos os consumidores em runtime. A etapa 4 permanece aberta; respostas genéricas de conteúdo não estão habilitadas.

## Observação no executor canônico (etapa 4c)

`EffectTriggerExecutor.ExecuteObserved` é uma entrada interna opt-in de shadow. Usa o mesmo executor e os mesmos redutores de `Execute`, inclusive prefixos, respostas atuais e efeitos encadeados. Os resultados de recurso, atributo e status são observados na fronteira da aplicação, antes das respostas existentes. Respostas irmãs mantêm o mesmo pai imediato. O contexto de eventos é um sucessor imutável local, não um coletor global nem um canal de publicação.

A observação não entra na serialização de `EffectBatchResult` nem em seu fingerprint. A API normal continua usando `Execute`, sem criação de fatos. Falha de observação não entrega estado/eventos parciais e não altera entradas. A revisão do contexto precisa coincidir com a execução.

`Unobserved` registra explicitamente as famílias ainda sem projeção completa (incluindo condensação, mesmo que suas stacks já tenham um fato). Não é aceitável tratar essa lista como cobertura concluída ou habilitar respostas autoritativas enquanto houver lacunas. O motivo de shadow é fornecido explicitamente pelo chamador; não converte automaticamente operações de recurso em dano/cura.

## Famílias de regras e integração das zonas (etapa 4d)

A observação agora recebe os snapshots de run junto aos de combate. Isso permite projetar modificadores e passos primitivos das zonas sem perder o dono real (ator, lado, run ou global). `ModifierChangePayload` conserva owner, stacks, duração e natureza da operação; não confunde modificador com status nem o dono com o alvo do efeito que o concedeu.

`ResourceThresholdPayload` copia apenas os fatos que o resultado canônico já resolveu. A seleção por prioridade não é repetida pelo observador. `ActorDefeatChangePayload` descreve transições reais de derrota/retorno e se liga como filho à alteração de recurso; nenhum nome de recurso é interpretado como letal. Cálculo, proc e impacto permanecem referenciados.

`SequenceChangePayload` conserva a operação e a sequência circular antes/depois, validando a correspondência entre os três. A observação não avança o turno. Os passos de cartas consultam endereços completos no snapshot de topologia e a definição de cada zona na revisão capturada; não interpretam nomes como mão/baralho/descarte.

Ainda falta a cobertura completa da condensação e a ligação dos lifecycles/fronteiras/ações ao contexto da transação. O dispatcher e os componentes JSON continuam desabilitados.

## Vínculos entre aplicações e condensação (etapa 4e)

A mudança primária de recurso referencia os eventos dos settlements reais em `RelatedFactIds`, sem somar recursos de unidades diferentes. Âncoras imutáveis por impactId preservam a relação entre pai e efeitos filhos. Tanto o catálogo quanto o payload `RuleResult` tipam o resultado `effect.condensation.resolved`: ele identifica a receita, enquanto os fatos de status/modificador registram quais stacks foram consumidas. Os efeitos ativados continuam passando pelo executor comum.

Ao consumir stacks de vários atores ou donos globais, cada stack usa o dono canônico do registro, não o alvo escolhido para ativar a receita. O fato da receita é o pai dos fatos de consumo e da aplicação que ela ativou. Nenhuma quantidade é reconstruída a partir de texto, lore ou nome de status.

A condensação deixa de ser uma lacuna no executor observado. A etapa 4 ainda requer a integração dos lifecycles, fronteiras e ações, antes de habilitar autoria de respostas genéricas.

## Lifecycle de status observado (etapa 4f)

`CombatStatusLifecycle.ProcessObserved` usa o mesmo ciclo de `Process`. Os triggers passam por `IEffectTriggerExecutor.ExecuteObserved`; duração e expiração são emitidas somente pelo lifecycle que já decide esses momentos. Não há um segundo relógio de status, assinatura retroativa ou chamada extra de remoção. A fotografia de elegibilidade e as ordens atuais continuam intactas.

O decurso de duração tem origem de regra e não transforma o aplicador histórico em atacante. Se houver uma âncora histórica válida, ela continua em `OriginCause`. O campo interno de observação no resultado é ignorado na serialização. A entrada normal de gameplay continua sem observação; o suporte novo da interface é opt-in e falha explicitamente se um executor alternativo não o implementar.

## Regeneração e relíquias observadas (etapa 4g)

`CombatResourceLifecycle.ProcessObserved` e `CombatRelicLifecycle.ProcessObserved` seguem os ciclos canônicos existentes. Regeneração mantém sinais, exclusões e o ator escolhido. Relíquias mantêm a revisão capturada, ordenação, seleção de donos e a marca de execução única para CombatStart/CombatEnd. A segunda passagem de uma fronteira já concluída não adiciona eventos. Nenhuma dessas entradas instala callbacks no event bus ou habilita respostas por JSON.

A revisão da **ação** deve corresponder à transação de observação. Ela é distinta da revisão de **definição** de uma relíquia/status/efeito capturado antes de uma mudança de conteúdo: essa revisão fica em `Origin.ContentRevision` e continua sendo resolvida pelo executor canônico. Não substituir definições antigas pela revisão atual para facilitar a observação.

Ainda não foram conectados o executor geral de fronteiras, o gateway de ações e todos os caminhos de recursos/cartas fora desses executores. A etapa 4 continua aberta; o índice de respostas e o dispatcher são as etapas seguintes.

## Grafo de fases observado (etapa 4h)

`PhaseGraphReducer` possui entradas opt-in de observação para entrada, comando, saída e avanço automático. Todas usam os mesmos métodos internos e o mesmo acumulador imutável do caminho normal. O próprio `ApplyBoundary` emite `phase.entered`/`phase.exited`, inclusive quando a definição não tem efeitos, e os efeitos dessa fronteira recebem seu evento como pai. O contexto pai anterior é restaurado ao sair da fronteira.

Não são emitidas entradas extras em avanços automáticos sem transição. Regras de seleção de arestas, cursores, permissões, limite de transições e fingerprints não foram alterados. Falha de efeito, ciclo ou orçamento não entrega um candidato parcial. `BoundaryOccurrencePayload.Sequence` identifica o ordinal da marca dentro da observação da ação, não uma nova contagem persistida de turnos.

## Composição das fronteiras de combate (etapa 4i)

`CombatBoundaryExecutor.InitializeObserved`, `AdvanceObserved` e `CompleteObserved` percorrem os mesmos métodos internos das entradas normais. A composição reúne a observação de status, relíquias, regeneração e fases. As marcas `activation.started`/`activation.ended` e `round.started`/`round.ended` correspondem às fronteiras de lifecycle já escolhidas pelo fluxo, sem antecipar nem criar uma nova ativação. Um combate terminal não emite a fronteira do próximo ator. `combat.ended` respeita a execução única já existente.

Um cursor privado de uma única chamada mantém apenas referências a sucessores imutáveis da observação. Ele não é persistido, compartilhado entre ações ou entregue ao chamador. Falha interrompe a composição sem retornar esse cursor nem candidatos parciais. Cada escopo de fronteira restaura o pai anterior. As entradas normais não criam esse cursor nem publicam ocorrências. Implementações alternativas dos serviços podem recusar observação explicitamente; não se simula cobertura por fallback.

O refresh de recurso conserva o operando `Set` efetivamente enviado ao redutor, inclusive quando o valor foi escolhido por uma política Add/ResetToMax. Não reconstrói o operando por diferença de floats. Condições/derrota continuam usando o resolvedor canônico de recursos. Fluxos de cartas fora de efeitos usam `GameplayCardFlowObservation`, com cópia limitada dos passos e referências resolvidas pela topologia, sem duplicar seleção ou agregados.

Ticks de modificadores são observados depois da chamada canônica de `ModifierTransitions.Tick`, com a mesma lista de elegibilidade. `InstanceDurationPayload` distingue duração de stacks; remoção por expiração conserva o motivo e o dono. Não se atribui o passar do tempo ao ator que instalou o modificador.

Ainda faltam a ação raiz/candidato do gateway e a ligação dos caminhos de custo e outros produtores externos aos contextos. Não há autoria nova de respostas habilitada, corte de versão, novo journal ou modificação de saves nesta etapa.

## Candidatos de carta e habilidade (etapa 4j)

`CardPlayExecutor.ExecuteObserved` e `AbilityExecutor.ExecuteObserved` usam o mesmo core das entradas normais, retornando a observação junto ao candidato privado. Repetir ou descartar um candidato não modifica a observação de entrada. O resolvedor de legalidade ainda precisa escolher qual desses candidatos será encaminhado; não é correto registrar globalmente todos os previews executados.

`CostReasonId` é uma classificação explícita da composição confiável. `ActionEffectCosts` a vincula aos comandos de custo já avaliados, antes dos efeitos. O vínculo interno é ignorado na serialização e não muda os fingerprints existentes. Uma subtração do corpo da carta não vira custo, e um custo não vira dano por usar o mesmo recurso/operação. Motivos ausentes do catálogo continuam falhando sem entregar um candidato parcial.

## Legalidade e alternativas descartadas (etapa 4k)

`LegalActionResolver.EvaluateObserved` executa uma instância isolada do resolvedor, sem consultar/popular seus caches normais de preview. Cada execução recebe a mesma base imutável e só o candidato escolhido devolve seu sucessor de observação. A observação passa por carta/habilidade e pelas transições de fase desse candidato.

A validação especulativa de uma ação que será proposta à PriorityStack usa o caminho canônico sem coletar fatos dessa resolução descartada. Se a política paga na proposta, apenas a reserva realmente aplicada entra no candidato. Pagamento e reembolso recebem motivos explícitos independentes. Isso evita consumir orçamento de observação com um dano hipotético ou registrar como executado um efeito que aguarda prioridade.

## Composição do comando de combate (etapa 4l)

`CombatCommandHandler.HandleObserved` mantém a mesma validação de input, legalidade e redução. Um comando candidato possui `action.started` e `action.completed`; a última ocorrência referencia os fatos produzidos por ele. `action.completed` significa conclusão do processamento do comando, inclusive uma proposta aceita; não significa que uma ação pendente da PriorityStack já resolveu. As ocorrências específicas de proposta/resolução continuam sendo a integração da etapa 10.

O redutor observa somente o fluxo efetivo de disposição da carta e os ticks canônicos de modificadores por comando. A projeção de duração foi compartilhada com as fronteiras, sem duplicar a regra de tempo. Mesmo se o orçamento terminar ao fechar o comando depois da redução, a entrada permanece intacta e nenhum candidato parcial é entregue. O caminho normal continua sem observação e com a mesma serialização/fingerprint.

## Continuação automática observada (etapa 4m)

`CombatFlowPlanner` e `AutomaticFlowDriver` possuem entradas observadas que usam os mesmos cores de composição e loop. A observação escolhida passa entre comandos efetivos, transições de ativação, prioridade e finalização. Decisões/intents continuam sendo consultas sem coletar suas alternativas no candidato confirmado. Não há contexto ambiente nem modificação do contexto de entrada: somente a variável privada da execução recebe sucessores imutáveis.

O driver conserva o limite de passos e a parada por input/prioridade do jogador. Falhar depois de calcular fronteiras não devolve snapshots nem observação parcial. Parar sem trabalho não emite ocorrências. O planner mantém a publicação canônica de intents, sem transformá-la em regra de despacho ou segunda autoridade de ações.

## Candidato da porta de combate (etapa 4n)

`CombatRunCoordinator.PlanObservedAction` calcula o mesmo plano de commit de uma ação e de sua continuação automática sob a trava do coordenador, sem chamar o committer ou publicar telemetry. O caminho normal e o observado compartilham `PrepareActionCandidate`. Identidade, rota, agregado, encontro e versão são conferidos antes de produzir o candidato.

O catálogo fechado `GameplayCoreCatalog` fornece capacidades confiáveis e os tipos já suportados; não recebe fatos do cliente. `command.started`/`command.completed` delimitam a porta externa e diferem das ações internas (inclusive ações automáticas). O escopo usa o ID real do comando, revisão/setting capturados, run raiz lógica e ID da branch atual. Essa entrada ainda é uma ferramenta interna de plano; não é um endpoint de injeção ou commit de fatos.

## Candidatos fora do combate (etapa 4o)

`RunSessionCoordinator.PlanObserved` usa o codec, gate e handler canônicos e um motor isolado por candidato. Não grava commits nem recibos. A observação existe apenas na composição privada e não faz parte do snapshot. Pagamentos e recompensas preservam os operandos enviados ao redutor; consultas de disponibilidade não produzem fatos. O adaptador de atividades vincula explicitamente o ator sintético à carteira real quando configurado como RunWallet, sem atribuir a mudança a um recurso do personagem. O vínculo é restaurado ao terminar o batch. O caminho normal permanece sem observação; essa entrada ainda não habilita respostas por JSON.

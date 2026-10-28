# Implementação — eventos e respostas genéricas

Guia: [arquitetura e plano](GENERIC_GAMEPLAY_EVENTS_AND_RESPONSES_ARCHITECTURE_AND_PLAN.md).

Início: 2026-10-28. Base: `7dc3292`, engineVersion `34`.

## Preservação e método

O checkout tinha 171 arquivos rastreados modificados e outros arquivos não rastreados antes do início. Essas alterações não pertencem automaticamente a este plano. Commits incluem apenas os arquivos/trechos da etapa; artefatos de teste ficam em `.runtime/generic-gameplay-events`. Não alterar saves, API em uso ou revisões históricas para obter testes verdes.

O registro de uma etapa só muda para concluída depois de implementação e teste. Contratos internos isolados não equivalem a funcionalidade liberada para autoria JSON. O sistema existente continua autoritativo enquanto a família correspondente não foi migrada.

## Inventário da etapa 0

| Produtor/consumidor | Autoridade atual | Fronteira/ordem a preservar | Referência de regressão |
|---|---|---|---|
| Efeito e alteração primária | `EffectTriggerExecutor`, `ImmutableEffectProcessor` | Por repetição/alvo; efeito antes de respostas e filhos; passos contíguos. | `EffectTransactionTests`, `GameplayResponseBaselineTests` |
| Reações de recurso | `EffectReactionDefinition`, coleta em relíquias | Prioridade crescente, instância da relíquia, reactionId; antes dos filhos; descendentes sem novas reações. | `EffectReactionTests`, baseline novo |
| Absorção, clamp, derrota e multi-hit | Pipeline, settlements e outcomes canônicos | Pedidos, perda real e orçamentos de sequência não são recalculados pelo observador. | `EffectReactionTests`, `EffectSequenceBudgetTests` |
| Status periódico/expiração | `CombatStatusLifecycle` | Captura de instâncias antes da fronteira; ator, prioridade de status decrescente, instanceId, prioridade de trigger decrescente e triggerId; revalidação da instância ativa. | `CombatStatusLifecycleTests` |
| Relíquia de fronteira | `CombatRelicLifecycle` | RelicInstanceId, prioridade de trigger decrescente, triggerId, ownerId; início/fim idempotentes. | `CombatRelicLifecycleTests` |
| Fases e fronteiras | `PhaseGraphReducer`, `CombatBoundaryExecutor` | Transição e momento de execução pertencem ao grafo/política, não ao event bus. | `PhaseGraphReducerTests` |
| Recursos fora de efeitos | `ResourceMutationReducer`, `ResourceCostTransitions`, lifecycles | Custo/refund, regeneração, máximo/mínimo, restrição e threshold têm motivos próprios. | `ResourceMutationReducerTests`, `ActorResourceLifecycleTests` |
| Cartas/zonas | `CardZoneFlowExecutor`, transições e lifecycle | IDs, endereços, RNG de shuffle, visibilidade e ordenação definidos pelo fluxo. | `CardZoneTransitionsTests`, `ParticipantDealingTests` |
| Resposta por input | `ReactionFlowReducer`, `LegalActionResolver` | PriorityStack, pagamento, locks, passagem e revalidação existentes. | `ReactionFlowReducerTests` |
| Persistência e publicação | Gateway/commit canônicos e event bus operacional | Nenhum efeito por consulta; publicação após commit; retry não executa novamente. | Suítes de persistência e jornada na certificação final. |

## Etapas

| Etapa | Estado |
|---|---|
| 0 — Inventário e baseline | Concluída |
| 1 — Hierarquias e matcher | Concluída |
| 2 — Ocorrências e catálogo | Concluída |
| 3 — Causalidade e identidade | Concluída |
| 4 — Produtores canônicos | Concluída em modo opt-in; migração de consumidores nas etapas seguintes |
| 5 — Definições e providers | Concluída; publicação executável bloqueada até o cutover |
| 6 — Dispatcher transacional | Concluída para execução candidata; integração/cutover nas etapas seguintes |
| 7 — Migração das reações de recurso | Em andamento: tradução isolada validada, cutover pendente |
| 8 — Migração de lifecycle/fases | Não iniciada |
| 9 — Containers e respostas pós-fato | Não iniciada |
| 10 — Intenções e janelas | Não iniciada |
| 11 — Migração de conteúdo | Não iniciada |
| 12 — Persistência/replay/timeline | Não iniciada |
| 13 — REST e inspeção | Não iniciada |
| 14 — Mensagens e laboratório | Não iniciada |
| 15 — Certificação | Não iniciada |

## Verificações

As contagens abaixo devem registrar somente execuções realizadas durante este plano. Não reutilizar números de relatórios anteriores como nova execução.

Próximo bloco obrigatório: definições e providers efetivos da etapa 5; em seguida dispatcher transacional e migrações, mantendo o corte de autoria e persistência nas etapas correspondentes.

- Etapa 0: 107 testes focados Core passaram em `.runtime/generic-gameplay-events`, incluindo as duas novas regressões de baseline. As fixtures foram ajustadas ao pipeline real configurado; nenhum código de gameplay foi alterado.

- Etapa 1: parser ordinal, seletores exatos/um nível/descendentes, inclusão/exclusão e trie imutável implementados. 26 testes focados passaram (24 de hierarquia + 2 de baseline). Nenhum consumidor de gameplay foi alterado. Contrato em `docs/content/gameplay-event-hierarchies.md`.

- Etapa 2: envelope/payloads discriminados, escopo, validação numérica, catálogo por revisão, capacidades confiáveis de publisher e aliases validados implementados. 31 testes focados passaram. Há contrato e schema de autoria; não há publicação/runtime novo habilitado nem alteração de hashes existentes.

- Etapa 3: transação de ocorrências imutável, identidade sem RNG, atribuição explícita e âncora histórica separada da profundidade da ação. Campos opcionais nulos não são serializados em status, lotes e propostas; a engineVersion continua 34 e produtores ainda não os preenchem. 55 testes focados passaram.
- A execução Core completa do checkout registrou 1974 aprovações e seis falhas em `UnoClashParityTests`, arquivo não rastreado anterior ao plano; as falhas leem diretamente chaves ausentes dos JSONs locais de UNO. O teste/conteúdo não foi alterado nem excluído. Certificação independente do índice em andamento.

- Certificação da etapa 3: exportado o índice para `.runtime/generic-events-stage3-index` e executada a suíte Core completa com build/artefatos dentro da própria cópia, usando o conteúdo versionado dessa cópia: **1970 testes aprovados, zero falhas e zero exclusões**. A execução anterior de fontes do índice também passou em 1970 testes; a repetição dentro da cópia garante isolamento da descoberta de recursos. Os seis erros do checkout não estão presentes na árvore versionada.

- Etapa 4a: produtores canônicos de fato de recurso, vínculo ao operando real de efeitos/settlements e observação explícita de mutação/custo implementados. `GameplayObjectReference` permite dono de recurso não ator. 62 testes focados passaram, incluindo os anteriores e sete casos novos. A observação ainda é shadow e a etapa 4 permanece aberta.

- Certificação da etapa 4a: índice exportado para `.runtime/generic-events-stage4a-index`, com build/artefatos e conteúdo isolados. **1977 testes Core passaram, sem exclusões**, repetidos após a proteção final de cópias das listas de entrada. **31 testes API focados passaram**, cobrindo controllers, sandbox e jornada de compartimentos. Não equivale à suíte API inteira nem à certificação do dispatcher ainda não implementado. No checkout completo permanecem as mesmas seis falhas de JSON do teste de UNO não rastreado, com 1981 aprovações.

- Etapa 4b: adaptadores tipados de atributos, status/duração, fronteiras, movimentos e ordenação de zonas. 59 testes focados passaram, incluindo lifecycles existentes e dez casos novos. Não há migração dos consumidores nem dispatcher habilitado.

- Etapa 4c: `ExecuteObserved` usa o executor canônico e contexto shadow imutável, antes de respostas/filhos. Quatro regressões novas conferem igualdade de toda a serialização, RNG, causas de respostas irmãs, prefixos de status, rollback e lacunas explícitas de condensação. 117 testes focados passaram antes da regressão final de cobertura; as quatro regressões passaram em seguida.
- Certificação 4c: árvore exata do índice exportada para `.runtime/generic-events-stage4c-index`, compilada e testada com conteúdo/artefatos dentro da cópia. **1991 testes Core passaram, sem falhas ou exclusões**. Checkout original: 1995 aprovações e as mesmas seis falhas anteriores do teste UNO não rastreado. Nenhuma resposta genérica foi habilitada, nenhum lifecycle substituído e engineVersion permanece 34.

- Etapa 4d: observação de modificadores com dono real (incluindo global), movimentos de cartas via topologia/definições capturadas, sequência circular e fatos de políticas de recurso/derrota. Novas regressões de igualdade com o executor normal e de precedência da política não letal. **149 testes focados passaram**. Pendentes na etapa 4: condensação completa, ligação dos lifecycles/fronteiras/ações e vínculos complementares de settlements.

- Certificação 4d: **1997 testes Core aprovados, zero falhas/exclusões**, sobre a árvore exata do índice em `.runtime/generic-events-stage4d-index`, com descoberta de conteúdo e build isolados dentro da cópia.

- Etapa 4e: vínculos dos settlements à mudança primária, âncoras por impacto e fato da receita de condensação; consumo de status de vários atores e modificador global mantém seus donos. **151 testes focados passaram**, incluindo novas regressões de absorção e condensação entre donos/stores. Ainda pendente: ligação de lifecycles, fronteiras e ações ao contexto; etapa 4 não concluída.

- Etapa 4f: observação opt-in no lifecycle canônico de status, incluindo seus triggers, redução de duração e expiração. **69 testes focados passaram**, com duas novas regressões: igualdade completa de resultados/ordem/ator elegível em repetições e rollback se o orçamento terminar depois do tick. Entrada normal continua sem observação. Pendentes: demais lifecycles, fronteiras e ações; etapa 4 segue em execução.

- Certificação 4f (inclui 4e): **2001 testes Core aprovados, zero falhas e zero exclusões**, sobre a árvore preparada para commit em `.runtime/generic-events-stage4f-index`, com compilação e conteúdo isolados.

- Etapa 4g: observação opt-in na regeneração e nas relíquias, preservando exclusões, sinais, ordem/donos e idempotência. Corrigida a validação interna para separar revisão da ação e revisão capturada da definição; regressão usa um efeito de revisão antiga sem substituir sua origem. **78 testes focados passaram**. Pendentes: executor geral de fronteiras, gateway de ações e caminhos externos de recursos/cartas; nenhuma habilitação de observers novos por JSON.

- Certificação 4g: árvore exata do índice em `.runtime/generic-events-stage4g-index`; **2005 testes Core aprovados sem exclusões**, mais **31 testes API focados aprovados** (controllers, sandbox e jornada de compartimentos), com builds/recursos dentro da cópia. Não é certificação das etapas 5–15 ainda não implementadas.

- Etapa 4h: entrada/saída de fase observada no próprio interpretador, inclusive sem efeitos; entrada, comando, saída e avanço automático usam os mesmos cores e acumulador imutável. Efeitos da fronteira têm pai próprio, e no-op não duplica eventos. **79 testes focados passaram**, incluindo quatro casos novos de igualdade serializada, repetição, rollback/ciclo e caminhos por comando/saída. Pendentes: composição geral das fronteiras de combate e gateway/caminhos externos; etapa 4 continua em execução.

- Etapa 4i: composição opt-in das fronteiras reais de combate, ator e rodada, incluindo fases/lifecycles, refresh de recurso, fluxos de cartas externos a efeitos e ticks/expiração de modificadores. Cursor privado não escapa e contém apenas sucessores imutáveis; caminho normal não o cria. **138 testes focados passaram**, com oito novos casos (inclui quatro combinações de duração/fronteira). Cobertos dois atores/volta ao jogador, fluxo configurado de compra/descarte/exílio, término por status antes do próximo ator, rollback após trabalho parcial, repetição determinística e finalização idempotente.
- Certificação 4i: árvore exata do índice exportada para `.runtime/generic-events-stage4i-index`; **2017 testes Core aprovados sem exclusões**, mais **31 testes API focados aprovados** (controllers, sandbox e jornada de compartimentos), com conteúdo e builds isolados. Inclui a etapa 4h.
- A etapa 4 permanece aberta: próxima ligação é ação raiz/candidato do gateway, custos e caminhos externos de recursos/cartas. Etapas 5–15 ainda não iniciadas; respostas novas por JSON continuam desabilitadas e engineVersion permanece 34.

- Etapa 4j: cartas e habilidades possuem entrada observada por candidato, conservando custos antes dos efeitos e classificando pagamento por vínculo explícito de composição, sem inferência por nomes. **92 testes focados passaram**, com duas novas regressões de repetição, RNG, igualdade completa e rollback depois do custo. Ainda necessário: propagar somente o candidato escolhido pela legalidade até a fronteira de comando; não registrar previews em coletor global.

- Etapa 4k: legalidade observada por candidato, com instância isolada e sem compartilhar caches; propagação de carta/habilidade/fases e preservação de proposta/reserva/reembolso. **106 testes focados passaram**. Novas regressões verificam cache normal previamente populado, duas bases distintas, descarte da resolução especulativa e orçamento de uma ocorrência para proposta com custo de vida (não classificado como dano). Próximo: ação/candidato no handler e comando raiz.

- Etapa 4l: comando de combate observado pela mesma composição de validação/legalidade/redução, com raiz e fechamento do comando, disposição real da carta e duração dos modificadores. Projeção de modificadores compartilhada com fronteiras. **106 testes focados passaram**, incluindo duas combinações novas jogador/controle automático, cinco repetições, referências dos filhos e rollback se o orçamento acabar ao fechar o comando após a redução. Próximo: composição das ativações automáticas e ligação do comando externo/candidato.

- Etapa 4m: observação nas composições de planner/driver automático, propagada entre ações selecionadas e fronteiras; manutenção de intents, prioridade, limites e parada por input. Os testes focados iniciais passaram em 90 casos; depois **5 testes de driver passaram**, incluindo dois cenários novos com planner/fronteiras reais, turno automático completo com retorno ao jogador e rollback por limite. Repetição compara todo resultado serializado, fatos e entrada original. Próximo: candidato na porta externa e observação de comandos fora do combate.

- Etapa 4n: plano observado na porta de combate, compartilhando a composição normal antes do committer. Escopo com identidade real de comando/setting/revisão/branch; catálogo fechado de capacidades. **104 testes focados passaram**, incluindo repetição do candidato completo, versão/rota/agregado inválidos e ausência de commit/mutação. Ainda pendente: comandos externos ao combate e conclusão do inventário; autoria de respostas não está habilitada.

- Certificação 4n: a suíte completa identificou excesso dos limites de tamanho na composição. Os adaptadores observados foram separados em arquivos próprios e o redutor de estado, já uma classe distinta, foi movido para seu arquivo de responsabilidade. Nenhum orçamento/teste foi relaxado. A árvore final do índice em `.runtime/generic-events-stage4n-final-index` passou em **2026 testes Core, sem exclusões ou falhas**. Os **31 testes API focados** passaram na árvore 4n anterior à separação estrutural; a certificação final voltará a abranger API.

- Etapa 4o: plano observado de comandos fora do combate, com motor isolado por candidato, carteira real da run, custos/recompensas dos submódulos e efeitos de atividades pelo executor comum. Não há coletor ambiente, publicação ou alteração de recibos no planejamento. **85 testes focados passaram**, incluindo três regressões novas de identidade/rollback/repetição e distinção entre carteira e ator. Ainda faltam movimentos externos de cartas e o fechamento do inventário da etapa 4; etapas 5–15 continuam pendentes.

- Certificação 4o: **2029 testes Core aprovados, zero falhas/exclusões**, árvore exata do índice em `.runtime/generic-events-stage4o-index`, build `.runtime/build`. A primeira tentativa usou profundidade de artefatos incompatível com fixtures antigas de caminho relativo e falhou em 12 fixtures; a execução correta preservou todos os testes e conteúdo.

- Etapa 4p: os fluxos externos de cartas no motor isolado agora observam os passos primitivos reais do executor configurado, inclusive concessões dos submódulos e fronteiras da run. Não há nova seleção, movimentação ou RNG. **41 testes focados passaram**, incluindo duas combinações novas de equivalência e rollback por orçamento durante concessão de duas cartas. Etapa 4 permanece em execução para cobrir criação/resolução dos encontros e fechamento do inventário.

- Etapa 4q: concessão/reaplicação de modificadores de preparação, ticks sequenciais Node/Run e transição real de término da run observados nos próprios caminhos canônicos. **38 testes focados passaram**, incluindo três casos novos de preparação/ordem/limite; depois **18 testes** passaram com o cenário adicional de abandono e os limites arquiteturais. A entrada e o contexto inicial permanecem imutáveis. Etapa 4 ainda aberta; migração de consumidores não iniciada.

- Etapa 4r: transporte de recursos de entrada/saída recebe payload próprio, preservando política, pedido e limites sem inventar uma mutação de dano/cura. Inicialização canônica observada compartilha materialização e planner reais antes da anexação. **36 testes focados passaram**; depois **7 testes** passaram incluindo os cinco casos novos de transporte e duas combinações da inicialização real, com isolamento de setting. Ainda falta concluir o inventário de criação/resolução e ativação de conteúdo; respostas genéricas seguem desabilitadas.

- Certificação 4r: **2039 testes Core passaram**, sem exclusões/falhas, árvore versionada em `.runtime/generic-events-stage4r-index`. A API ampla aprovou 243 casos, falhou em dois e atingiu o timeout global: o probe entre processos exige build da solução; a jornada espera vida 65 mas obtém 67. A mesma jornada foi executada no commit anterior `b837d10` (antes de 4o) com build completo e reproduziu a mesma falha/vida 67. Não foi alterada a jogabilidade nem a expectativa para esconder esse diagnóstico.

- Etapa 4s: criação observada pelo mesmo core, com seed/commandId explícitos, fatos de run e fluxos iniciais, efeitos de entrada e normalização compartilhada com commit. Nenhum save, recibo, cache de criação ou evento externo é publicado pelo plano. O bootstrap declarativo constitui o estado gênese, sem operações fictícias de dano/custo. **56 testes focados passaram**, incluindo cinco repetições da nova regressão de equivalência completa, falha por orçamento e verificação de ausência de append. Etapa 4 ainda aberta para resolução e revisão final dos produtores.

- Etapa 4t: plano completo de resolução do encontro compartilha limpeza de zonas, transporte, atividade de saída, ticks Node/Run e progressão com a confirmação normal. Fatos e snapshots são descartáveis até o commit. **69 testes focados passaram**, incluindo regressão nova com cinco repetições, comparação completa com o comando normal e falha depois de trabalho parcial por limite. Identidade/versão/estratégia inválidas são rejeitadas. Ainda pendente o fechamento da observação na ativação de revisão e do inventário da etapa 4.

- Complemento da certificação 4r: após build da solução exigido pelo probe, **32 testes API focados passaram**, incluindo a prova em processos independentes além de controllers/sandbox/compartimentos. A falha de expectativa 65/67 da jornada ampla permanece documentada e reproduzida na base.

- Etapa 4u: ativação selecionada de revisão e rebind das definições de recursos possuem payloads próprios, conservando revisão de origem da ação e revisão de destino. Previews não publicam fatos; regras de recurso não são reinterpretadas como dano/cura. **21 testes focados passaram**, incluindo regressão nova com cinco repetições, equivalência com comando normal, clamp real, encadeamento causal e rollback por orçamento. Próximo: auditoria final de aplicações sem alteração e fechamento do inventário da etapa 4.

- Etapa 4v: remoção/dispelação sem instância correspondente agora registra tentativa resolvida explícita, sem inventar alteração de stacks e sem marcar falsamente uma lacuna de produtor. **25 testes focados passaram**, com três regressões novas para status, dispel e modificador. Restante da conferência: materialização de restrições e sincronização após atributos persistentes, antes de concluir a etapa 4.

- Etapa 4w: materialização de restrições em relíquias, inicialização de efeitos e sincronização persistente observadas. A regra registra o limite real e a chamada Set com seu operando original, sem recalcular o teto. A alteração de atributo precede o transporte dependente, conservando o vínculo causal. **48 testes focados passaram**, incluindo cinco casos novos de ordem/equivalência, rollback, distinção entre ator persistente/encontro e histórico intocado. Certificação final da etapa 4 em andamento antes de marcá-la concluída.

- Fechamento da etapa 4: inventário final em `docs/architecture/GAMEPLAY_OCCURRENCE_PRODUCER_INVENTORY.md`. Árvore de código exata do índice `53d014d0d717270448e8bcb461d437ab282476e5`, exportada para `.runtime/generic-events-stage4-final-index`, com build completo da solução e artefatos/conteúdo dentro da cópia: **2050 testes Core e 32 testes API focados aprovados**, sem exclusões. O build não emitiu warnings/erros. A etapa 4 encerra a instrumentação opt-in; não anuncia dispatcher, autoria executável, journal novo ou âncoras históricas preenchidas antes das etapas correspondentes. A divergência prévia 65/67 da jornada ampla continua registrada.

- Complemento 4x, na preparação dos filtros da etapa 5: fatos de efeitos capturam as tags efetivas canônicas e o canal, com papel explícito PrimaryEffect/Settlement/DirectMutation. Custos preservam seu motivo independente. Campos de transporte internos são ignorados na serialização dos comandos. **129 testes focados passaram**, incluindo regressão nova de tags/custo e equivalência completa; acrescidas asserções de papel na regressão existente de absorção. Não há cálculo de tags/observação no caminho normal sem observação.

- Etapa 5a: contratos JSON de respostas/filtros/bindings/disponibilidade, compilador por catálogo e coletor puro com ordem total ordinal. Cartas exigem disponibilidade explícita e propriedade física da zona; providers não executam matching/efeitos. Frequências duráveis ainda indisponíveis são rejeitadas; wildcards de extensão exigem capacidade confiável e ficam dormentes sem tópicos conhecidos. Schema em `docs/schemas/gameplay-response.schema.json`. **111 testes focados passaram**, com oito novos casos de compilação/JSON/coleção/escopo/disponibilidade. Providers concretos e namespaces numéricos ainda serão ligados antes de concluir a etapa 5.

- Etapa 5b: providers concretos de modo/encontro/ator/relíquia/status/carta, revisão capturada e proveniência de upgrades/compartimentos. Componentes de resposta passam pelo compilador/composição canônicos em modo shadow explícito; a publicação padrão recusa essas declarações até a execução estar integrada. Campos opcionais nulos não alteram snapshots antigos. Namespace numérico tipado projeta somente referências pedidas e distingue responsável, afetado e dono, sem reflexão/defaults silenciosos. **16 testes focados passaram**, incluindo cinco casos novos de providers, publicação, números, zonas/remoção e contribuição real por compartimento. Certificação completa da etapa 5 em preparação.

- Fechamento da etapa 5: árvore de código `73e38c316e1234a3820c3bf45eb4e0d7e4819149` em `.runtime/generic-events-stage5-index`, build completo sem warnings/erros e artefatos/conteúdo isolados: **2064 testes Core e 32 testes API focados aprovados**, sem exclusões. Depois, **3 testes** passaram com asserções adicionais que distinguem numericamente recursos de dono e responsável no namespace de fórmulas. Não há dispatcher habilitado nem conteúdo antigo migrado antes das etapas seguintes.

- Etapa 6a: matcher indexado puro com bindings explícitos, filtros de recursos/tags/relações e seleção de snapshot. Contadores transacionais imutáveis distinguem ocorrência, impacto, proc e ação raiz, rejeitam âncoras ausentes e isolam branches; padrões sobrepostos não duplicam candidatos. **21 testes focados aprovados**, incluindo oito novos casos/variações de matching, identidade, quotas e rollback dos contadores. Esta parte congela candidatos: ainda não executa condições/efeitos nem habilita autoria em produção. A etapa 6 continua em implementação.

- Etapa 6b: execução candidata explícita de respostas pelo mesmo executor de efeitos, com orçamento compartilhado entre ação, respostas e descendentes. Elegibilidade/condições são congeladas por operação; registro ativo e dono podem ser revalidados conforme política. RecordOnly registra fatos sem disparar descendentes; AllowResponses usa as mesmas quotas/profundidade/limites. Condições e fórmulas leem a revisão capturada, sem parser paralelo. **166 testes focados aprovados**, incluindo nove novos casos/variações de execução, encadeamento, rollback, números tipados, elegibilidade e prova de orçamento compartilhado. A autoria/publicação padrão continua bloqueada; migrações e integração dos produtores externos ainda pertencem às etapas seguintes. Certificação completa deste incremento em preparação.

- Certificação 6b: árvore exata do índice `0ba031504e1dec9620364d3169aa9f07ca398f4e`, exportada para `.runtime/generic-events-stage6b-index`. Build da solução sem warnings/erros; **2081 testes Core e 32 testes API focados aprovados**, sem falhas ou exclusões. O teste API amplo 65/67 previamente reproduzido na base não foi incluído nesta seleção focada. Ainda serão conferidos os casos de criação de observador/derrota e a proveniência das respostas antes do fechamento da etapa 6.

- Fechamento da etapa 6: proveniência capturada das respostas, criação de observadores conforme BeforeOccurrence/AfterOccurrence, revalidação explícita após derrota, alteração zero e falha tardia/ciclo cobertos. Bindings/valores de combate não consultam atores persistentes fora do roster; atividades sem combate continuam podendo consultar a ficha persistente. **175 testes focados aprovados**. Árvore de código certificada `6ca1ef6c3c930b72939929c46bf6d81dd78d3590`, em `.runtime/generic-events-stage6-certified-index`: build completo sem warnings/erros, **2090 testes Core e 32 testes API focados aprovados**, sem falhas ou exclusões. A execução concorrente das duas suítes levou 2m20/2m43; não é uma medição de latência da engine. A etapa 6 fecha o dispatcher candidato, não anuncia migração de reações/lifecycles, publicação JSON, persistência de frequência durável ou REST antes das etapas correspondentes.

- Etapa 7a: tradução isolada/read-only das reações capturadas de relíquias para registros de resposta, sem substituir a execução antiga. Mantém Source/Target, propriedade, revisão, prioridade anterior à identidade da relíquia e supressão de descendentes. Tags duplicadas conservam semântica; combinação histórica contraditória continua nunca elegível. Custos/refunds/settlements/canais diferentes continuam excluídos. **7 testes novos aprovados**. A troca do executor fica condicionada à prova dos aliases parent.*, IDs, snapshots e ordem completa; esta etapa ainda não está concluída.

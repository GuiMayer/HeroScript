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
| 4 — Produção de fatos | Em execução: observação integrada ao executor; completar famílias e lifecycles |
| 5 — Respostas e registros | Não iniciada |
| 6 — Dispatcher transacional | Não iniciada |
| 7 — Migração das reações de recurso | Não iniciada |
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

Próximo bloco obrigatório: completar a instrumentação da etapa 4 (efeitos/status/atributos/zonas/fronteiras e integração dos produtores), sem saltar diretamente para liberar componentes JSON. Os primeiros adaptadores de recurso/custo ainda não são cobertura completa dessa etapa.

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

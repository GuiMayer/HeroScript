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
| 3 — Causalidade e identidade | Não iniciada |
| 4 — Produção de fatos | Não iniciada |
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

- Etapa 0: 107 testes focados Core passaram em `.runtime/generic-gameplay-events`, incluindo as duas novas regressões de baseline. As fixtures foram ajustadas ao pipeline real configurado; nenhum código de gameplay foi alterado.

- Etapa 1: parser ordinal, seletores exatos/um nível/descendentes, inclusão/exclusão e trie imutável implementados. 26 testes focados passaram (24 de hierarquia + 2 de baseline). Nenhum consumidor de gameplay foi alterado. Contrato em `docs/content/gameplay-event-hierarchies.md`.

- Etapa 2: envelope/payloads discriminados, escopo, validação numérica, catálogo por revisão, capacidades confiáveis de publisher e aliases validados implementados. 31 testes focados passaram. Há contrato e schema de autoria; não há publicação/runtime novo habilitado nem alteração de hashes existentes.

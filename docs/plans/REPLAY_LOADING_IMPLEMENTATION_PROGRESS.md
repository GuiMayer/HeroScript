# Replay loading — progresso

Guia: [plano](REPLAY_LOADING_ARCHITECTURE_AND_PERFORMANCE_PLAN.md).
Início: 2026-10-13. Sem alteração de regras/checkpoints/schema de gravação.

1. Baseline: benchmark read-only e geração isolada de fixtures de armazenamento.
2. Envelopes paginados: head fixo, fronteira/cadeia/identidade validadas, bytes capturados sob gate e CPU fora dele; sem materializar deltas.
3. Timeline compacta: DTOs de cabeçalhos, query separada, contrato canônico com hasMore/throughSequence e escopo; sem estado/cálculos na resposta.
4. Apresentação de comando: resumo mínimo e transcrição compartilhada do domínio, materializador canônico e autorização histórica preservada.
5. Transporte cancelável: tickets com conclusão única, cancelamento antes/durante HTTP, epoch de endpoint e métricas de bytes/transferência/parsing.
6. Coordenação/view: ReplayController + presenter + view passiva; screen apenas adapta intents, navegação/verify e contexto.
7. Paginação incremental: primeira página/primeiro comando imediatos, Load More e next/autoplay atravessando páginas com head fixo, sem teto de 4.000 ou consulta vazia.
8. Cache e prioridades: LRU de 32 apresentações/8 MiB serializados, uma seleção em voo com coalescência/cancelamento, escopo completo e inspeção técnica explícita. Sem prefetch especulativo.
9. Materialização: segmento capturado uma vez, sem reler deltas; checkpoint/deltas validados por bytes, CPU fora do gate em leituras públicas. Sem novo cache de estados ou mudança de checkpoint.
10. Certificação: pendente.

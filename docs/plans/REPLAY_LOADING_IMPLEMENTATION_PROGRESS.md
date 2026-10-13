# Replay loading — progresso

Guia: [plano](REPLAY_LOADING_ARCHITECTURE_AND_PERFORMANCE_PLAN.md).
Início: 2026-10-13. Sem alteração de regras/checkpoints/schema de gravação.

1. Baseline: benchmark read-only e geração isolada de fixtures de armazenamento.
2. Envelopes paginados: head fixo, fronteira/cadeia/identidade validadas, bytes capturados sob gate e CPU fora dele; sem materializar deltas.
3. Timeline compacta: DTOs de cabeçalhos, query separada, contrato canônico com hasMore/throughSequence e escopo; sem estado/cálculos na resposta.
4. Apresentação de comando: pendente.
5. Transporte cancelável: pendente.
6. Coordenação/view: pendente.
7. Paginação incremental: pendente.
8. Cache e prioridades: pendente.
9. Materialização: pendente.
10. Certificação: pendente.

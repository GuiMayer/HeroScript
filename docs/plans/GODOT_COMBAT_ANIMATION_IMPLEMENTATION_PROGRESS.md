# Apresentação de combate — progresso

Guia: [arquitetura e plano](GODOT_COMBAT_ANIMATION_ARCHITECTURE_AND_PLAN.md).

## Preparação

- Correção anterior das cartas preservada separadamente: `e96008a`.
- Plano versionado: `aaba89f`.
- Nenhum push solicitado nesta implementação.

## Etapas

1. Baseline: regressões mantidas em `animation_contracts.gd`; o modo diagnóstico
   `--animation-baseline` registra defeitos conhecidos. O modo normal exige os
   comportamentos corretos e será incluído na suíte após a implementação.
2. Envelope contextual: implementado como projeção imutável com validação de
   identidade/sequence e filtragem de transferências visuais ocultas. Sem alteração
   de contrato da engine nesta etapa.
3. Planner causal: pendente.
4. Director/handles: pendente.
5. Integração com combate/mão: pendente.
6. Pause/cancelamento/pós-combate: pendente.
7. Feedback semântico/resources: pendente.
8. Zonas de cartas: pendente.
9. Perfis/acessibilidade/áudio: pendente.
10. Replay: pendente.
11. Limites/desempenho: pendente.
12. Certificação/limpeza: pendente.

## Limites de validação

Os probes da auditoria demonstraram recebimento antes da sincronização, input
liberado durante voo e cenário animando no pause. Esses cenários serão
convertidos em testes de integração do novo fluxo nas etapas 5–6. Testes
offline não substituem certificação online nem inspeção de imagens renderizadas.

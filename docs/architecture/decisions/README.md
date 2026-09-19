# Registros de decisão de arquitetura

As decisões desta pasta são normativas para o runtime de gameplay. Elas existem
para que determinismo e imutabilidade sejam propriedades verificáveis do
sistema, e não convenções locais de cada módulo.

| ADR | Decisão |
| --- | --- |
| [0001](0001-command-and-gameplay-authority.md) | comandos e autoridade de gameplay |
| [0002](0002-authoritative-run-commits.md) | histórico autoritativo por `RunCommit` |
| [0003](0003-published-content-boundary.md) | conteúdo publicado e fixado |
| [0004](0004-immutable-actor-model.md) | atores imutáveis e component-driven |
| [0005](0005-executable-combat-flow.md) | fluxo de combate executável |
| [0006](0006-operational-telemetry.md) | telemetria não autoritativa |
| [0007](0007-migration-baseline.md) | baseline de migração sem compatibilidade legada |
| [0008](0008-purpose-free-card-zones.md) | zonas de cartas sem finalidade embutida |

O plano de migração correspondente está em
[`docs/plans/REMAINING_CORE_SYSTEMS_CONSOLIDATION_PLAN.md`](../../plans/REMAINING_CORE_SYSTEMS_CONSOLIDATION_PLAN.md).

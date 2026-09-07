# ADR 0007 — Baseline da consolidação

**Status:** aceito, temporário

Em 2026-09-07, antes da consolidação dos sistemas restantes:

- `dotnet build HeroScript.slnx --no-restore`: aprovado, sem warnings;
- `Core.Tests`: 1.099 aprovados;
- `API.Tests`: 148 aprovados;
- cenário vertical canônico: `DeterministicPrototypeFlowTests`;
- authorities legadas toleradas temporariamente pelos testes arquiteturais:
  `JsonFileRunStateRepository`, `VersionedRunStateRepository`, `Entity`,
  `ComponentBase`, `ResourceComponent`, `StatsComponent` e
  `InventoryComponent`.

Cada allowlist deve diminuir na etapa que substitui a autoridade correspondente.
Ela nunca pode receber um item novo.


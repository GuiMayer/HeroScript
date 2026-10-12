# Implementação Marfim da run e diálogos

Plano: [GODOT_IVORY_RUN_AND_DIALOGUE_PLAN.md](GODOT_IVORY_RUN_AND_DIALOGUE_PLAN.md). Início: 2026-10-12.

Snapshot anterior: `bce6921`, enviado para `origin/main` antes da implementação.

| Etapa | Estado |
| --- | --- |
| 0 | API QA isolada em 5293, sem dados pessoais. Diálogo REST: 32 checks; mão online nos settings: 150 checks aprovados. Capturas anteriores preservadas. |
| 1 | Implementada: identidade contextual completa, Choice/Continue explícitos e metadata opcional de speaker. Core DialogueTests: 8 testes; Godot dialogue_contracts: 6 checks aprovados. Defaults de metadata omitidos da serialização para preservar snapshots sem esses campos. |
| 2 | Implementada: cena/controller próprios, início publicado único, conclusão pelo RESOLVE_NODE publicado e fronteira de retorno ao mapa por contexto. Diálogo REST: 32 checks; reconciliação de atividade: 7; troca de composição: 13. |
| 3–10 | Pendentes; registrar evidências e commits por entrega. |

Os testes usam cópia da Godot com user directory próprio e persistência da API em `.runtime/ivory-run-qa`. Não foi utilizado o servidor das partidas pessoais.

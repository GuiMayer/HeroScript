# Implementação Marfim da run e diálogos

Plano: [GODOT_IVORY_RUN_AND_DIALOGUE_PLAN.md](GODOT_IVORY_RUN_AND_DIALOGUE_PLAN.md). Início: 2026-10-12.

Snapshot anterior: `bce6921`, enviado para `origin/main` antes da implementação.

| Etapa | Estado |
| --- | --- |
| 0 | API QA isolada em 5293, sem dados pessoais. Diálogo REST: 32 checks; mão online nos settings: 150 checks aprovados. Capturas anteriores preservadas. |
| 1 | Implementada: identidade contextual completa, Choice/Continue explícitos e metadata opcional de speaker. Core DialogueTests: 8 testes; Godot dialogue_contracts: 6 checks aprovados. Defaults de metadata omitidos da serialização para preservar snapshots sem esses campos. |
| 2 | Implementada: cena/controller próprios, início publicado único, conclusão pelo RESOLVE_NODE publicado e fronteira de retorno ao mapa por contexto. Diálogo REST: 32 checks; reconciliação de atividade: 7; troca de composição: 13. |
| 3 | Implementada: mapa dedicado, inspector, inspeção de qualquer nó visível sem comando e ação explícita por referência atual. Atividades não possuem mapa embutido. Estado de leitura por run/branch; 6 checks de seleção, 7 de atividade e diálogo REST aprovado. |
| 4 | Implementada: perfis de UI/cenário e manifesto Marfim; cartas/fontes do laboratório reutilizadas, atores/fundo SVG próprios. Contextos: 8, tema: 10, custos: 35; nova importação isolada e ivory_run_contracts: 7 checks aprovados. |
| 5 | Implementada: renderer único de nós inspecionáveis, estados/ícones/texto, mapa amplo e inspector Marfim. Layout SCC estável mantém saída de ciclos em camadas posteriores. 10 checks de geometria, 12 de atualizações, 6 de seleção e 37 checks/18 capturas do marco visual. |
| 6 | Implementada: pan com limiar, centralização, próximas escolhas, visão compacta, destaque por tipo sem ocultar nós e legenda somente de leitura. Seleção/scroll/overview/filtro são locais e restauráveis; testes de seleção e câmera adicionados. |
| 7–10 | Pendentes; registrar evidências e commits por entrega. |

Os testes usam cópia da Godot com user directory próprio e persistência da API em `.runtime/ivory-run-qa`. Não foi utilizado o servidor das partidas pessoais.

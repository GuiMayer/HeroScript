# Implementação do combate de referência

Guia: [plano](GODOT_COMBAT_REFERENCE_LAYOUT_HAND_AND_PILES_PLAN.md). Início: 2026-10-12.

1. Baseline: fixture offline de 0/1/2/5/10/20 cartas; capturas anteriores preservadas em `.runtime/reference-combat-before`.
2. Composição proporcional: política de largura central limitada, métricas puras e área segura dos atores; altura de arte ampliada no perfil. Dock será separado na etapa 5.
3. Adaptive fan: curvatura independente de compressão, mínimo de 4° com duas cartas, máximo de 8° e passo proporcional. Reading mantém cartas retas; hitboxes/reserva permanecem no layout compartilhado.
4. Ícones/chrome: catálogo SVG monocromático e keylines angulares em controles nativos; header/turn controls usam a família sem perder labels acessíveis ou comandos.
5. Remoção de overview/dock: pendente.
6. Browser de pilhas: pendente.
7. Feedback: pendente.
8. Certificação: pendente.

Regras, cartas e pilhas permanecem autoritativas na engine. QA não usa runs pessoais.

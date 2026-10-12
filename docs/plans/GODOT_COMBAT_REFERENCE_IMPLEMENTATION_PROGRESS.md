# Implementação do combate de referência

Guia: [plano](GODOT_COMBAT_REFERENCE_LAYOUT_HAND_AND_PILES_PLAN.md). Início: 2026-10-12.

1. Baseline: fixture offline de 0/1/2/5/10/20 cartas; capturas anteriores preservadas em `.runtime/reference-combat-before`.
2. Composição proporcional: política de largura central limitada, métricas puras e área segura dos atores; altura de arte ampliada no perfil. Dock será separado na etapa 5.
3. Adaptive fan: curvatura independente de compressão, mínimo de 4° com duas cartas, máximo de 8° e passo proporcional. Reading mantém cartas retas; hitboxes/reserva permanecem no layout compartilhado.
4. Ícones/chrome: catálogo SVG monocromático e keylines angulares em controles nativos; header/turn controls usam a família sem perder labels acessíveis ou comandos.
5. Remoção de overview/dock: overview removido do combate/atalhos públicos; permanece somente o consumidor real do laboratório. Pilhas e ferramentas ficam no dock independente da geometria da mão, com identidade por endereço.
6. Browser de pilhas: grade compartilhada de CardView, busca/ordenação local, visibilidade/ordem explícitas e endereço completo; avaliações individuais sob demanda com cache/contexto e concorrência limitada.
7. Feedback: HUD de atores compacto sem remover recursos/estados ativos, silhuetas maiores, ícones de intenção configuráveis com fallback e preview independente; foco desenhado sem reflow. Modais bloqueiam atalhos de combate.
8. Certificação: suíte offline padrão aprovada; geometria 21, coleções offline 9/online 25, mão online 150, combate 441, seleção 22 e configurações 82 checks aprovados. Matriz visual 42 capturas/85 checks, EN/PT e três resoluções. Regras da engine não foram alteradas.

Regras, cartas e pilhas permanecem autoritativas na engine. QA não usa runs pessoais.

[Certificação e capturas](../design/reference-combat/2026-10-12/README.md). Foram corrigidos o reset de fonte do contador de pilha e o crescimento inicial do popup que ocultava sua ação de fechar. Métodos de leitura preservaram snapshots/seleção/replay nos três settings. Avisos residuais de encerramento são documentados separadamente.

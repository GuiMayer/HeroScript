# Auditoria inicial — animações e usabilidade do combate MVP

Amostra visual: cena `tests/combat_mvp_preview.gd`, projeção offline em português com herói, dois inimigos, cinco cartas, quatro pilhas e seleção de alvo; capturas realizadas em 1280×720, 1280×800 e 1920×1080. O teste também exercita a mão vazia e quantidades variadas de cartas. A inspeção não substitui uma partida completa conectada ao servidor.

## Existente

- Mão: leque, elevação, redistribuição animada, leitura ampliada, arraste e cancelamento. As cartas permanecem selecionáveis para leitura mesmo quando indisponíveis; a informação visual da disponibilidade ainda é pouco explícita.
- Alvos: botões habilitados apenas quando há candidatos publicados, indicação por cor, prévia ao apontar e cancelamento. Melhorar a identificação sem depender apenas de cor e o foco por teclado.
- Ações: `CombatBeatOrchestrator` coordena movimento entre zonas e uso; `CombatFeedback` já apresenta textos flutuantes e impactos, e `ResourceView` anima o preenchimento. Preservar esses sistemas.
- A prévia de ações usa candidatos fornecidos pelo jogo. A amostra apresenta efeitos genéricos quando não há valores de prévia; não calcular valores localmente.
- Movimento reduzido já é uma preferência, aplicada pelo perfil de animações, e precisa continuar respeitado.

## Pontos a verificar nas próximas etapas

A amostra visual revela cartas bastante sobrepostas e texto pequeno em 1280×720; preservar tamanho e disposição aprovados e priorizar inspeção e feedback de disponibilidade. Validar também bloqueio, cura, status, múltiplos efeitos, foco, modo reduzido e interrupções com testes específicos. Fotografias de eventos animados e uma sessão conectada ainda são necessárias para avaliar ritmo real.

# Marfim — primeiro marco visual

2026-10-11. Capturas renderizadas na Godot 4.7.2, usando a cena e os mesmos componentes do jogo. Fixtures offline: não representam uma run executada pela engine.

Verificação final: **380 checks em dez suites offline + 39 checks na captura renderizada**, zero falhas funcionais/de script. Importação da fonte também concluída sem diagnósticos. Relatório completo inclui os avisos de encerramento, quando presentes.

Stretch de produção `1440×900 / canvas_items / expand`; dimensões finais 1280×720 e 2560×1080. Inglês a 100%, português a 120%. [Inventário gerado](captures.json), [relatório de testes](validation.json) e [guia do laboratório](../../IVORY_CARD_LAB.md).

| Estado | Inglês 1280×720 | Português 2560×1080 |
| --- | --- | --- |
| Mão inicial | [captura](hand-en-1280x720.png) | [captura](hand-pt_BR-2560x1080.png) |
| Custos e modificações | [captura](scaling-en-1280x720.png) | [captura](scaling-pt_BR-2560x1080.png) |
| Inspeção persistente | [captura](inspection-en-1280x720.png) | [captura](inspection-pt_BR-2560x1080.png) |
| Mão vazia | [captura](empty-en-1280x720.png) | [captura](empty-pt_BR-2560x1080.png) |

Também foram capturados os mesmos estados nas outras combinações de idioma/resolução, [alto contraste](high-contrast-en-1280x720.png) e [visão geral de 100 cartas](overview-100-en-1280x720.png): 18 PNGs ao todo.

Aprovação artística ainda pendente. Não é a repaginação completa do jogo: combate, jornada e menus continuam na direção anterior. A cena não acessa HTTP nem altera runs, e seus testes comparam o arquivo de preferências e a restauração do perfil após sair. O runner usa uma cópia com `user://` exclusivo.

As fontes variáveis usam tags OpenType numéricas: além do peso declarado, o teste consulta o cache nativo para provar que ele foi aplicado. Comparações numéricas usam a mesma política branco/vermelho/azul e uma superfície invertida também no alto contraste. Há testes de acentos, mãos até 100 cartas, escala, raridade, tooltip sem scrollbar inacessível e inspeção persistente.

Alto contraste conserva as cores e superfícies dos chips de custo/badges, evitando texto branco sobre branco; os indicadores da toolbar refletem os valores efetivamente usados. Os arquivos antigos de baseline/certificação não foram sobrescritos.

Os avisos de áudio no encerramento imediato de alguns testes headless permanecem no relatório, não foram ocultados. As verificações funcionais e a revisão visual são complementares; não há certificação de todos os dispositivos físicos ou de qualquer conteúdo futuro.

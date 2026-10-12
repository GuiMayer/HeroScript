# Comparação visual da arquitetura da run

Data: 2026-10-12. Referência anterior preservada em [run-graphics-before](../../run-graphics-before/2026-10-11/README.md).

## Capturas

72 PNGs: EN/PT × 1280×720, 1920×1080 e 2560×1080 × 12 estados. Combate com mão cheia/vazia, preview, sidebar, pausa, terceira equipe/recurso desconhecido; jornada, recompensa, loja, preparação, diálogo e término. O teste usa os componentes de produção e gateway sintético; não cria uma run real nem grava preferências.

Galeria de amostra:

- [Combate 1280×720](en-combat-hand-1280x720.png)
- [Mão vazia 1280×720](en-combat-empty-1280x720.png)
- [Terceira equipe 1920×1080](en-combat-third-team-1920x1080.png)
- [Jornada PT ultrawide](pt_BR-journey-2560x1080.png)
- [Diálogo PT 1280×720](pt_BR-dialogue-1280x720.png)
- [Pausa PT 1280×720](pt_BR-pause-1280x720.png)

Inspeção visual de amostras confirmou a composição preservada; a alteração visível deliberada é o mapa passar a geometria por topologia e atores neutros terem grupo/legenda próprios. Isto não é o redesign Marfim do gameplay.

## Evidências

- Rodada ampla: 20 suites, 1.218 checks, incluindo 146 checks da captura; resultados em validation-round-1.json. Rodadas posteriores repetiram os testes impactados, em vez de somar resultados repetidos como se fossem cobertura inédita.
- A matriz anterior e posterior tiveram **6 leituras de inspeção da fixture**, para 72 capturas cada. Essas contagens não são chamadas reais de rede.
- O teste de arquitetura executou 20 refreshes iguais: zero substituições de nodes, zero relayouts da mão e zero inspeções adicionais. Média síncrona observada entre aproximadamente 0,18–0,37 ms por refresh nas rodadas locais. Não é benchmark de GPU, latência da API ou tempo clique→resultado.
- MapGeometry cobre diamante, array reordenado, recursos/disponibilidade, desconexões, ciclos/self edges e 100 nós. MapGraphView valida overflow sem reduzir fontes, foco e preservação por ID em 1280×720 e 2560×1080.
- Testes adicionais verificam pausa/modal, seleção limpa após uso, composição lateral/retorno, contexto de estilo independente, descarte de awaits e liberação de cenário externo.
- A importação final do projeto original terminou com exit code 0 e nenhuma linha de erro; logs em source-import.log/source-import-errors.log. git diff --check também não apontou erros.

Os relatórios preservam avisos nativos de recursos/ObjectDB separadamente. Eles não são prova de ausência de vazamentos; o resultado funcional também não os apaga.

## Limite da entrega

As capturas são fixtures, não uma run online certificada. O teste de diálogo online não conectou à API; iniciar QA isolado foi bloqueado pela ferramenta. **Falta a etapa de integração REST completa nos settings default/ascendant/volatile.** Nenhum teste mutante foi redirecionado à API das partidas pessoais.

Contratos e andamento: [GODOT_RUN_GRAPHICS_CONTRACTS](../../GODOT_RUN_GRAPHICS_CONTRACTS.md), [progresso](../../../plans/GODOT_RUN_GRAPHICS_IMPLEMENTATION_PROGRESS.md).

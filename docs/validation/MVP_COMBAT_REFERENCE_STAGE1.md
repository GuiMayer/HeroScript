# Composição MVP — etapa 1: base e mão inferior

Data: 2026-11-05. **Implementada, aguardando aprovação visual. Etapas 2–7 não iniciadas.**

Guia: [plano de composição](../plans/MVP_COMBAT_REFERENCE_HUD_REBUILD_PLAN.md). Dados da execução: [resultados e arquivos protegidos](mvp-reference-stage1.json).

## Entrega

- O MVP usa `CanvasHost/ReferenceCanvas`, sem a árvore anterior `CombatBody` ou o nó compensador `Balance`.
- A mão ocupa `HandHost/HandSection` no centro inferior. Os controles atuais continuam funcionais em `TemporaryControls`, um irmão independente, até suas etapas de validação visual.
- Bindings são caminhos explícitos editáveis no Inspector. O adaptador e os controladores existentes continuam processando a sessão/input; não foi criado outro controlador ou mecanismo de animação.
- O retângulo da composição é ajustado a 16:9 e centralizado no espaço útil. O stretch uniforme já configurado na aplicação é preservado; não se adiciona outra escala aos controles das cartas, evitando alterar tamanho e coordenadas de input.
- Tema, fontes, materiais, cartas e algoritmos/durações de animação não foram alterados. As mudanças locais preexistentes no tema MVP foram preservadas e ficam fora do commit desta etapa.
- Cabeçalho, pilhas, energia e placas ainda estão na apresentação provisória atual. Não representam a conclusão das etapas seguintes.

## Validação

Godot 4.7.2, cópia importada isolada com dados/preferências de QA. **19 scripts, 1.046 verificações aprovadas**, todos com exit 0 e sem diagnósticos no relatório final. Inclui 55 montagens no editor e 89 verificações da nova base, além dos testes compartilhados de combate, mão, hover, apresentação, custo, compra, ciclo de turno, sinais, pagamentos inline e pause.

O teste novo verifica 0/1/2/5/12 cartas em áreas 1920×1080, 1280×720, 1280×800 e 2560×1080: centralização, limites, dimensões, destino do fan com duas cartas, hit testing e estabilidade após refresh. A cena genérica continua coberta por 515 verificações de layout. Onze arquivos de carta/mão/movimento protegidos foram comparados com a base e permanecem iguais, normalizando apenas finais de linha.

Capturas renderizadas foram produzidas em 1920×1080, 1280×800 e 2560×1080. Foram inspecionadas visualmente as capturas 1920×1080 com cinco cartas, duas cartas e mão vazia. Evidências locais em `.runtime/mvp-reference-stage1/before/` e `after/`; esses arquivos locais não são distribuídos pelo Git. A fixture usa o router real para selecionar a cena MVP, com snapshots locais e gateway de inspeção substituído. **Não foi executada uma run com a API real nem uma nova certificação de importação limpa.**

## Correção da sincronização de um teste existente

A primeira execução de `turn_hand_cycle` falhou ao comparar a pose retida. A falha também foi reproduzida no código anterior desta etapa: duas de três execuções falharam; três de três falharam na variante nova no mesmo ensaio. O teste aguardava 20 frames, o que não garante que o tween tenha terminado em headless.

A correção acrescenta ao helper de espera a duração configurada da transição mais 50 ms, mantendo todas as asserções. Nenhum script de animação da aplicação foi alterado. O teste passou isoladamente e na rodada final.

## Reprodução

Executar `tools/test-godot-ui.ps1 -Tests mvp_reference_base,ui_editability,ui_standalone_preview,editor_tool_scenes,combat_mvp_layout,combat_stage_layout,hand_selection,hand_hover_motion,card_presentation,card_cost_layout,animation_contracts,turn_hand_cycle,card_draw_pose,ui_authored_signals,combat_controller,inline_card_selection,card_payment,pause_layers`.

Para capturas, acrescentar `-VisualTest combat_mvp_preview -VisualOutputDirectory <pasta-de-saida>`. O runner usa cópia isolada, sem abrir saves reais.

## Próximo gate

Apresentar a captura e aguardar aprovação do usuário antes de iniciar a etapa 2, dedicada ao cabeçalho. Ao executar no editor com uma instância antiga já aberta, reexecutar a cena para montar a nova árvore.

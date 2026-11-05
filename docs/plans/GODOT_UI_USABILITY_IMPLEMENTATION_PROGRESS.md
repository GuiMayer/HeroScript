# Execução — usabilidade da interface Godot

Guia: GODOT_UI_USABILITY_AND_INTERFACE_PLAN.md.

## Estado

Implementadas as seis etapas no checkout principal. Sem commit ou push nesta execução. Alterações locais anteriores preservadas. A integração de sessão, comandos REST, validação de regras e persistência continua nos adaptadores existentes; nenhuma alteração na engine ou nos payloads dos comandos foi necessária.

## Etapa 1 — Foco, retorno e notificações

Anéis de foco nos recursos autorados e no estilo aplicado em runtime; Escape nas cinco telas secundárias usa a ação existente de retorno e respeita modais. Foco inicial nos botões de retorno. Toast agora é PanelContainer com Label interno, quebra de linha, largura baseada no viewport e indicação textual de erro. Adaptadores e testes adotam o contrato do componente. ModalFocus captura foco na abertura e restaura uma referência válida ao esconder/desmontar confirmation, attribute sources e inspection; conversation history herda o shell de inspection; o diálogo dinâmico de overview também usa o mesmo helper. Pausa mantém sua restauração existente.

Testes aprovados: usability_navigation (10), pause_layers (24), shared_overlay_scenes (9), ivory_menu (41), settings_interaction (86), visual_authority_contract (18). Teste novo de usabilidade verifica também restauração de foco ao fechar modal e mensagem de erro independente de cor.

## Etapa 2 — Combate

ActorPanel encaminha cliques do corpo ao sinal de alvo apenas quando o botão de alvo está habilitado, mantendo botões filhos e tooltips. Instrução de seleção destaca cancelamento por Escape/clique direito. Cabeçalho mostra atalhos mapeados de timeline/pausa e rótulos em larguras a partir de 1920; consulta de viewport protegida durante montagem fora da árvore. Drawer tem alvo mínimo 44×48 e descrição acessível. Foco aplicado por CombatChrome permanece visível.

Testes aprovados: combat_controller (13), combat_collaborators (12), combat_input_soak (80), combat_attributes_drawer (23), hand_ux (38), hand_selection (19). Um processo de teste encerrou nativamente após seus checks; a repetição concluiu com exit 0. Não confundir checks impressos com execução bem-sucedida.

## Etapa 3 — Mapa

Inspector diferencia tipografia de tipo/risco/benefício, com papéis semânticos para risco e benefício. Ações foram movidas estaticamente para fora da rolagem e bindings atualizados. Botão Centralizar e tecla Home usam a câmera existente com transição suave. Toggle de detalhes possui tooltip e nome acessível.

Testes aprovados: map_selection (26), map_geometry (12), map_graph_view (10), map_camera_motion (51), editable_map_scenes (8). Baseline geométrica do mapa preservada nos cenários comparados.

## Etapa 4 — Atividades e diálogo

Verificação de replay compactada no header. Stage do diálogo deixa de consumir expansão vertical; retrato é ocultado quando não há ID de arte. Escolhas 1–9 emitem o mesmo comando publicado e respeitam bloqueio; Space avança leitura sem escolher uma opção. A paginação exibe dica de teclado e teclas numéricas aparecem nas opções. Contexto somente leitura e inputs de texto são respeitados.

Testes aprovados: activity_reconciliation (21), activity_ingress (11), activity_map_completion (12), dialogue_contracts (6), dialogue_coordination (6), dialogue_reading (38). Teste novo comprova comando intacto por atalho e bloqueio em leitura.

## Etapa 5 — Loadout

Abas recebem contagem de inscrições equipadas/slots, cartas e relíquias a partir dos modelos existentes; sub-abas mostram quantidades disponíveis. Deck e inscrições possuem limpeza de filtros e estado sem resultados. Ferramentas do deck usam HFlowContainer para quebra em largura reduzida. Rodapé diferencia sincronizado, salvando e recuperação pendente, usando estado do controlador sem introduzir operações de gravação paralelas.

Testes aprovados: unified_loadout_shell (9), unified_loadout_layout (48), unified_loadout_autosave (9), unified_loadout_recovery (9), loadout_input (6), support_loadout (12), loadout_editorial (187). Corrigida diferença de 3 pixels no painel de atributos: o status de salvamento agora usa a mesma tipografia do rodapé; a baseline anterior passou sem alteração dos valores esperados.

## Etapa 6 — Codex, histórico e timeline

Codex tem busca, contagem, aba ativa, detalhes formatados e toggle de JSON bruto. O índice filtrado é separado da lista-fonte, evitando abrir a definição errada. Detalhes e preview ficam num único host rolável, preservando os dois filhos do SplitContainer; catálogo offline atualizado. Preview reutiliza CardView em modo descritivo, com custos desconhecidos explícitos: a consulta de definições não fornece a avaliação efetiva de combate, que não é reconstruída localmente.

Histórico usa ações em HFlowContainer e destaque para continuar/replay segundo o ciclo da run. Timeline apresenta nomes legíveis e um controle de quadro anterior. Retorno reconstrói apenas a inspeção local a partir dos snapshots registrados, sem enviar comandos, mudar head ou editar commit. Não oferece retorno anterior ao primeiro quadro registrado, pois esse estado não pode ser inventado.

Testes aprovados: tools_editable (8), replay_editable (6), historical_presentation (6), replay_lifecycle (11), screen_context_pilot (14), screen_catalog_full (49). usability_components (12) cobre busca/seleção, empty state, modal, toast, ações fixas, atalhos de diálogo e busca de snapshot anterior preservando commit/sessão. Novos testes foram incluídos na seleção padrão do runner.

## Certificação e limites

`ui_decoupling_baseline`: 74 verificações aprovadas, 1280×720, 1920×1080 e 2560×1080, escalas 1,0 e 1,6. Durações de compra/uso/descarte/exílio/retorno/hover/fan/movimento reduzido, poses representativas e transição do mapa passaram na comparação existente. Estado da sessão, preferências e ausência de requests na captura foram verificados pelo teste.

Validação em cópia isolada `.runtime/ui-refactor/a2ea9bf8ed584201a18d872dc5b0b717`, com saves separados. A importação limpa anterior sofreu encerramento nativo em SourceSans3; as execuções utilizaram cache de assets copiado do projeto original para a cópia de testes. Avisos de UID com fallback para caminho foram preservados nos logs. Não houve certificação nova de importação limpa, inspeção visual de screenshots ou execução de uma run completa com API real nesta entrega. Os testes de integração de UI usam fixtures/adaptadores reais com consultas interceptadas; isso não equivale a testar o servidor em execução.

As comparações geométricas cobrem os nós e estados definidos pela baseline, não todo estado possível de todos os settings. A prévia de definição no Codex não deve ser anunciada como prévia numérica efetiva. A aprovação estética das novas composições permanece uma verificação manual.

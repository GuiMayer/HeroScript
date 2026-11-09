# Composição MVP — etapa 5: energia central e Encerrar turno no canto inferior direito

Data: 2026-11-09. **Etapas 1, 2, 3 e 4 aprovadas. Etapa 5 implementada, aguardando aprovação visual. Etapas 6–7 não iniciadas.**

Guia: [plano de composição](../plans/MVP_COMBAT_REFERENCE_HUD_REBUILD_PLAN.md). Evidências: [testes, arquivos protegidos e capturas](mvp-reference-stage5.json).

## Entrega visual

Na variante MVP (`combat_mvp_view.tscn` e `combat_mvp_view.gd`), o indicador de energia e o grupo de ações de turno deixam a organização anterior e ocupam seus hosts dedicados na faixa inferior:

- **Energia central acima da mão (`EnergyIndicator`):**
  - Movida de `HandHost/HandSection/Energy` para um nó próprio `CanvasHost/ReferenceCanvas/EnergyIndicator` (`anchor_left = 0.36`, `anchor_right = 0.64`, `anchor_top = 1.0`, `anchor_bottom = 1.0`), vinculado por `energy_indicator_path` no Inspector.
  - Posicionada centralizada horizontalmente no `ReferenceCanvas`, imediatamente acima da mão (`HandHost`), mantendo o glifo e a tipografia atuais (`◇  Energy  current / maximum`).
  - Exibe fielmente o recurso `energy` publicado do ator de input (`_input_actor`), incluindo valores iguais a zero (`0 / 3`), excedentes acima do máximo (`5 / 3`), máximos customizados (`7 / 5`) e recursos sem `maximum` publicado (`4` sem barra `/3` fixa). Quando o ator de input não publica `energy`, `EnergyIndicator` é ocultado de forma limpa.
  - Atualiza também durante snapshots intermediários de resolução (`present_combat_snapshot`) sem mutar o snapshot autoritativo de combate.
- **Encerrar turno e ações contextuais no canto inferior direito (`EndTurnAndContextActions`):**
  - O host provisório `TemporaryControls` foi substituído pelo nó definitivo `CanvasHost/ReferenceCanvas/EndTurnAndContextActions` (sem metadados provisórios `stage_note`), vinculado por `turn_controls_path`.
  - Isolado no canto inferior direito de `ReferenceCanvas`, à direita de `HandHost`, sem sobrepor a mão ou interceptar o centro de clique/hover da carta mais à direita.
  - Mantém o botão `EndTurnButton` atual sem redesenho de skin, preservando disponibilidade (`END_TURN` publicado nas ações legais e input desbloqueado), estilo (`CombatChrome.apply(..., "arrow", ...)`), comando e atalho remapeado (`visual_context.action_label("end_turn")`).
  - Mantém `NextAnimation`, `SkipPresentationButton` e `CancelSelection` acessíveis exclusivamente em seus estados aplicáveis (`_has_frames()` ou `not selected_card.is_empty()`), ocultando `HandTools` e `EndTurnGap` quando nenhum botão contextual/paginação está visível e mantendo `ActionPreviewView` e `Pass` ocultos no MVP.
  - Dimensões, estilo e fan das cartas permanecem inalterados (`mvp_card_style.tres`).

## Validação

Godot 4.7.2, cópia de QA isolada. Foram executados **31 scripts de validação na bateria principal, totalizando 1.940 verificações aprovadas**, todas com `exit_code = 0`, além de uma execução adicional de captura em português com mais 23 verificações aprovadas.

O novo teste `mvp_reference_energy_and_turn` cobre 122 verificações:
- estrutura estática em `combat_mvp_view.tscn` (`EnergyIndicator` e `EndTurnAndContextActions` filhos diretos de `ReferenceCanvas`, fora de `HandHost`, e remoção do placeholder `TemporaryControls`/`stage_note`);
- honestidade de dados em `EnergyIndicator`: estado padrão (`3 / 3`), zero (`0 / 3`), excedente acima do máximo (`5 / 3`), máximo customizado (`7 / 5`), sem `maximum` publicado (`4` sem `/`) e recurso `energy` ausente (oculto), além de atualização em `present_combat_snapshot` sem mutar `_combat_snapshot`;
- visibilidade contextual em `EndTurnAndContextActions`: `EndTurnButton` habilitado/desabilitado conforme `END_TURN` publicado e estado `locked`/`busy`, exibição de `CancelSelection` apenas com carta selecionada, exibição de `NextAnimation` e `SkipPresentationButton` apenas com quadros de apresentação ativos, e `ActionPreviewView`/`Pass`/`HandTools` ocultos quando inaplicáveis;
- geometria multi-resolução (`1920×1080`, `1280×720`, `1280×800` e `2560×1080`), em `en` e `pt_BR`, com `text_scale` `1.0` e `1.6`: centralização horizontal de `EnergyIndicator`, posicionamento acima das cartas da mão, ausência de sobreposição entre `EndTurnAndContextActions` e `HandHost`, e isolamento de `EndTurnButton` no canto inferior direito;
- integração em `CombatScreen`: hover e seleção da carta mais à direita próxima ao botão `EndTurnButton` sem colisão, prioridade de `Escape` (primeiro `Escape` cancela a seleção ativa da carta sem abrir `Pause`; segundo `Escape` abre `Pause`), abertura de `Pause` durante resolução de quadros e execução de fim de turno via atalho remapeado (`KEY_R`).

Os 11 arquivos protegidos de carta, mão e movimento permanecem 100% inalterados.

Capturas renderizadas em `1920×1080`, `1280×800`, `2560×1080`, `0` cartas e `2` cartas foram geradas em `.runtime/mvp-reference-stage5/after/` (EN) e `.runtime/mvp-reference-stage5/pt-BR/` (PT-BR), com inspeção visual direta das imagens.

## Reprodução e próximo gate

Executar `tools/test-godot-ui.ps1 -Tests mvp_reference_energy_and_turn,mvp_reference_battlefield,mvp_reference_piles,mvp_reference_header,mvp_reference_base,ui_editability,ui_authored_signals,ui_standalone_preview,combat_mvp_layout,editor_tool_scenes` para a bateria focada da etapa 5, acrescentando `-VisualTest combat_mvp_preview -VisualOutputDirectory <pasta>` para regerar capturas.

Aguardar aprovação visual do usuário sobre a energia central e o botão Encerrar turno antes de iniciar a Etapa 6 (Janelas e overlays alinhados à nova composição).

# Execução do Desacoplamento e Editabilidade da Interface Godot

Plano: [`GODOT_UI_DECOUPLING_AND_EDITABILITY_PLAN.md`](GODOT_UI_DECOUPLING_AND_EDITABILITY_PLAN.md) · Início: 2026-11-03.

## Etapa 0 — Inventário e baseline

- Cópia isolada de referência preservada em `.runtime/ui-decoupling/baseline-source` (1.290 arquivos) com hashes SHA-256 em `.runtime/ui-decoupling/baseline-hashes.json` e registro do status Git inicial em `.runtime/ui-decoupling/initial-status.txt`.
- Inventário completo documentado em [`docs/architecture/GODOT_UI_DECOUPLING_INVENTORY.md`](../architecture/GODOT_UI_DECOUPLING_INVENTORY.md): rotas de `scene_registry.gd`, subcenas, coleções variáveis, referências a autoloads e `router`, propriedades visuais escritas em código, acessos profundos entre componentes e cobertura de testes.
- Baseline geométrica (`1280×720`, `1920×1080`, `2560×1080` × escalas `1.0` e `1.6`), matriz de mãos (`0, 1, 2, 5, 10` cartas e `2, 3` atores) e baseline de poses/durações de animação (`draw`, `use`, `discard`, `staged_discard`, `exile`, `retained`, `hover`, `fan_retarget`, `reduced_motion`, cancelamento e transição de câmera do mapa) capturadas e validadas por `tests/ui_decoupling_baseline.gd` (74 checks aprovados, zero HTTP, zero escrita de preferências).
- Dados versionados em `docs/architecture/baselines/godot-ui-decoupling/baseline.before.json` e `examples/godot-engine-showcase/data/baselines/ui_decoupling_baseline.json`.
- Validação base aprovada em projeto isolado: `ui_decoupling_baseline` (74), `screen_catalog_full` (49), `editable_ui_nodes` (14), `card_draw_pose` (106) e `map_camera_motion` (51).

## Etapa 1 — Contrato de autoridade visual

- Matriz completa de autoridade visual documentada em [`docs/architecture/GODOT_SCENE_OWNERSHIP_CONTRACT.md`](../architecture/GODOT_SCENE_OWNERSHIP_CONTRACT.md) para menu, carta, mão, combate, mapa e overlays (mapeando propriedade → proprietário → momento de escrita → justificativa → restauração pós-animação, além da lista canônica de templates de coleções variáveis).
- Constantes responsivas residuais migradas para recursos exportados mantendo exatamente os mesmos defaults:
  - `MenuVisualStyle`: `compact_gap`, `minimum_scroll_width`, `scroll_padding` + `MainMenuView.use_profile_metrics`.
  - `CardVisualStyle`: `requirement_min_width`, `inline_font_size`, `zero_cost_font_size`, `change_badge_font_size`.
  - `CombatLayoutProfile`: `floating_preview_height`, `floating_dock_gap`, `toolbar_min_size`, `reconnect_button_size`, `choice_button_height`.
  - `MapLayoutProfile`: `caption_height`, `caption_gap`, `node_font_size`, `inspector_compact_breakpoint`, `inspector_min_width`, `inspector_max_width`, `inspector_width_ratio`, `inspector_compact_height`, `inspector_compact_bottom_offset`, `inspector_side_min_height`, `inspector_side_max_height`, `inspector_toggle_size`, `inspector_margin`.
  - `ActorPanel`: `battlefield_intent_height`, `battlefield_margin_vertical`, `compact_margin_vertical`, `battlefield_content_gap` e preservação de `_authored_intent_height` no modo completo.
- Novo teste `tests/visual_authority_contract.gd` cobrindo edição observável de propriedades locais (`.tscn`) e de recursos exportados sem mudança na baseline visual (`ui_decoupling_baseline`).

## Etapa 2 — Composição e contexto de tela

- Contratos estreitos criados em `examples/godot-engine-showcase/scripts/ui/context/`:
  - `navigation_port.gd` (`NavigationPort`): encapsula navegação de rotas, estado de leitura de mapa/diálogo e leases de cenário sem expor `router.shell` nem métodos privados do router.
  - `session_port.gd` (`SessionPort`): adaptador estreito de leitura e comando sobre a sessão autoritativa (`GameSession`) ou sobre um stub em memória para testes isolados, sem criar um segundo serviço de sessão.
  - `feedback_port.gd` (`FeedbackPort`): encapsula toasts, erros e eventos cosméticos de áudio (`GameAudio`).
  - `playback_port.gd` (`PlaybackPort`): encapsula o cursor de apresentação (`Playback`).
  - `screen_context.gd` (`ScreenContext`): agrega `visual_context`, `navigation`, `session`, `feedback`, `playback` e `i18n`, com fábricas `ScreenContext.ensure()`, `ScreenContext.from_router()` e `ScreenContext.isolated()`.
- `VisualContext.English` estendido com `presentation()` e `error()` para satisfazer integralmente o contrato de tradução offline/isolado.
- Tela piloto `scripts/ui/codex_screen.gd` migrada para consumir `ScreenContext` sem referenciar `GameSession`, `I18n` ou `GameAudio` diretamente, preservando compatibilidade com `setup(owner)`.
- Novo teste `tests/screen_context_pilot.gd` validando abertura isolada com serviços falsos, cancelamento assíncrono (`request_epoch`) e execução em runtime com serviços reais.

## Etapa 3 — Modelos de apresentação e validação de fronteiras

- Modelos tipados (`RefCounted`) consolidados em `examples/godot-engine-showcase/scripts/presentation/view_models.gd`:
  - `SessionIdentity`, `Card`, `Actor`, `ResourceValue`, `LegalAction`, `ActionOption`, `Zone` e `InteractionState`.
  - Preservação explícita de cópias defensivas e campos desconhecidos/extensões por setting (`extensions()`).
- Validador e adaptador de fronteira criado em `examples/godot-engine-showcase/scripts/contracts/presentation_boundary.gd`:
  - Validação de UUIDs (`is_valid_uuid`), identificadores (`is_valid_id`), identidade de sessão (`validate_session_identity`) e contexto de revisão (`matches_revision_context`).
  - Adaptadores `adapt_actors`, `adapt_cards`, `adapt_legal_actions` (que rejeita candidatos inválidos sem fabricar `actionType` ou `cardInstanceId` sintéticos) e `adapt_zones`.
  - `StateKeeper` para preservar o último snapshot válido diante de respostas malformadas, truncadas ou fora de ordem (`runSequence`/`combatStep` atrasados ou mistura de `runId`).
- Integração nos adaptadores `snapshot_projection.gd`, `combat_presenter.gd` e `card_zone_presenter.gd`.
- Novo teste `tests/presentation_boundaries.gd` (20 checks) aprovado junto com `ui_contracts`, `card_presentation`, `combat_collaborators` e `ui_decoupling_baseline`.

## Etapa 4 — Componentes visuais e editabilidade

- Desacoplamento de acessos privados e caminhos profundos entre componentes visuais:
  - `CharacterAttributesView` (`scripts/ui/loadout/character_attributes_view.gd`) e `CombatAttributesDrawer` (`scripts/ui/components/combat_attributes_drawer.gd`): exposta propriedade pública `scroll_container`, parametrizadas reservas/limites de altura em `@export` e validados bindings `%UniqueName` no início.
  - `CardView` (`scripts/ui/card_view.gd`), `CardHandView` (`scripts/ui/card_hand_view.gd`) e `HandHitSurface` (`scripts/ui/hand_hit_surface.gd`): adicionadas APIs públicas `CardView.create_tooltip_panel()` e `CardHandView.is_card_pickable()`, eliminando chamadas externas a `_make_custom_tooltip` e `_pickable`, além de vínculo direto a `%CardArtwork` em `CardView.artwork()`.
  - `ActorPanel` (`scripts/ui/actor_panel.gd`): adicionados seletores públicos `resource_view()`, `resource_counter()` e `status_badge()` sobre os containers autorados `%ActorResources`, `%ActorCounters` e `%ActorStatusBadges`.
  - `ActivityOptionsView` (`scripts/ui/components/activity_options_view.gd`), `JourneyView` (`scripts/ui/journey_view.gd`) e `activity_screen.gd`: substituído acesso a `_choice_label` pela API pública `choice_label()` e vinculado `_action_scroll` diretamente em `_bind_nodes()`.
- Suíte de editabilidade, catálogo e laboratórios validada (`editable_ui_nodes`, `editable_card_inspection`, `editable_character_attributes`, `combat_attributes_drawer`, `editable_activity_scenes`, `card_style_canonical`, `workbench_smoke`, `screen_catalog_full`).

## Etapa 5 — Layout responsivo do mapa e combate

- Cálculo puro de geometria do inspetor flutuante e extensão do canvas do mapa consolidado em `MapLayout` (`scripts/presentation/map_layout.gd`: `inspector_geometry` e `canvas_extent`).
- Encapsulamento do estado geométrico de `RouteView` (`scripts/ui/route_view.gd`) por meio das APIs públicas `geometry_snapshot()`, `has_node_rect()`, `node_rect()`, `node_button_center()` e `set_initial_focus_node()`, removendo acessos privados a `route._geometry` e mutação externa de metadados de foco em `MapView` (`scripts/ui/map_view.gd`).
- Cálculos responsivos de offsets de dock flutuante, painel de candidatos, preview flutuante e rodapé de campo de batalha concentrados em `CombatLayoutMetrics.stage_layout()` (`scripts/presentation/combat_layout_metrics.gd`) e consumidos por `CombatView._layout_gameplay()` (`scripts/ui/combat_view.gd`).
- Validação aprovada sem deriva na baseline: `map_geometry`, `map_graph_view`, `map_camera`, `map_camera_motion`, `editable_map_scenes`, `editable_combat_shell`, `visual_authority_contract` e `ui_decoupling_baseline`.

## Etapa 6 — Mão: geometria, input e apresentação

- Colaboradores coesos extraídos de `CardHandView` (`scripts/ui/card_hand_view.gd`), mantendo uma única implementação canônica compartilhada entre combate e laboratório:
  - `HandLayout.presentation_layout()` (`scripts/ui/hand_layout.gd`): cálculo puro de posições e rotações de leque durante apresentação e alinhamento de chegadas.
  - `HandCardReconciler` (`scripts/ui/hand_card_reconciler.gd`): reconciliação de slots (`hand_card_slot.tscn`) e instâncias de `CardView` por `cardInstanceId` sem recriar cartas sobreviventes.
  - `HandPointerCoordinator` (`scripts/ui/hand_pointer_coordinator.gd`): resolução pura de face visível sob rotação/sobreposição (`pick_global`, `is_pickable`, `contains_card`, `is_above`) e direção de auto-scroll em overflow (`auto_scroll_direction`).
- APIs públicas adicionadas em `CardHandView` (`update_counter()`, `set_overview_button_visible()`, `relayout_now()`, `begin_drag_at()`, `drag_to()`, `return_drag_ghost()`, `check_reorder_settled()`), eliminando chamadas privadas a partir de `combat_view.gd`, `hand_overview_dialog.gd` e `hand_reorder_lab.gd`.
- Validação aprovada: `hand_selection`, `reference_hand_geometry`, `hand_arrangement`, `hand_reorder_flow`, `hand_reorder_drop`, `hand_reorder_preview`, `hand_reorder_overflow`, `hand_reorder_interruptions`, `card_draw_pose` e `ui_decoupling_baseline`.

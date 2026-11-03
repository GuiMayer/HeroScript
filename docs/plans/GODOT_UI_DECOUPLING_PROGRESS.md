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

## Etapa 7 — Orquestração de combate e playback

- Extraído `CombatBeatOrchestrator` (`scripts/ui/controllers/combat_beat_orchestrator.gd`) para isolar validação de partida de recibo (`matches_receipt_departure`), preparação de envelope/modelos de transferência (`prepare_envelope_presentation`), anotações de pagamento (`build_payment_annotations`), extração de etapas de zonas (`extract_card_zone_steps`) e resolução de animabilidade (`can_animate_beat`).
- Substituídas todas as chamadas a métodos privados de `CombatView` e `CombatPresenter` em `combat_screen.gd` por APIs públicas (`clear_choices()`, `show_preview()`, `toggle_character_sidebar()`, `hand_scope()`, `pile_buttons()`, `presenter.candidates()`, `presenter.targets()`, `presenter.system_candidate()`, `presenter.appearance()`).
- Lease de cenário de combate (`acquire_combat_scenery`/`release_combat_scenery`) e navegação (`show_timeline`, `toggle_pause`, `open_game`, `navigation_epoch`) roteados via `ScreenContext.navigation`, eliminando acesso direto a `router.shell.run_scenery` em `combat_screen.gd`.
- Validação aprovada: `combat_controller`, `combat_collaborators`, `combat_input_soak`, `presentation_director`, `presentation_envelope`, `presentation_planner`, `animation_contracts`, `animation_live`, `turn_hand_cycle` e `ui_decoupling_baseline`.

## Etapa 8 — Sessão e comunicação

- Colaboradores extraídos e compostos sob `GameSession` (`scripts/application/game_session.gd`), preservando sua interface pública e de sinais:
  - `SessionSnapshotStore` (`scripts/application/session_snapshot_store.gd`): validação atômica do bundle compacto de sessão (`validate_session_bundle`), resolução de ator de input (`input_actor_id`), contexto de avaliação (`evaluation_context`) e captura de snapshot privado imutável (`capture_view_snapshot`).
  - `SessionInspectionService` (`scripts/application/session_inspection_service.gd`): validação de contexto de loadout (`loadout_context_matches`, `validate_loadout_info`), pagamento (`payment_context_matches`) e preview de transformação/modificadores (`preview_transformation`).
  - `SessionReplayService` (`scripts/application/session_replay_service.gd`): resolução de bookmark/histórico para retomada de jornada (`find_resume`) e respostas padronizadas de capacidades (`tool_denied`).
  - `SessionCommandDispatcher` (`scripts/application/session_command_dispatcher.gd`): mapeamento de tipo de comando de candidato (`candidate_command_type`), normalização defensiva de recibo 2xx malformado (`normalize_receipt_response`) e política de limpeza de comando pendente (`should_clear_pending_on_failure`).
- Validação aprovada: `compact_session`, `http_transport_pool`, `rest_transport_latency`, `connection_epoch`, `api_request_numbers`, `replay_transport`, `replay_progressive` e `ui_decoupling_baseline`.

## Etapa 9 — Router, atividades e demais telas

- Criado `GameplayRouteResolver` (`scripts/ui/gameplay_route_resolver.gd`) para isolar a resolução pura da rota ativa (`combat`, `dialogue`, `map`, `activity`) e detecção de atividades de diálogo sem depender da cena principal.
- Atualizado `main.gd` (`scripts/ui/main.gd`) com `screen_context = ScreenContext.from_router(self)`, método público `start_new_campaign()` e delegação para `GameplayRouteResolver`.
- Atualizados `NavigationPort` (`scripts/ui/context/navigation_port.gd`) e `FeedbackPort` (`scripts/ui/context/feedback_port.gd`) com política LRU de 16 entradas para `save_map_reading_state` / `save_dialogue_reading_state` e despacho compatível com `show_toast` de 1 ou 2 parâmetros.
- Migradas `activity_screen.gd`, `dialogue_screen.gd`, `map_screen.gd`, `timeline_screen.gd`, `run_history_screen.gd`, `run_replay_screen.gd`, `sandbox_screen.gd` e `settings_screen.gd` para consumir `ScreenContext` (eliminando `router._new_campaign()`, mutações diretas em `router.dialogue_reading_states` / `router.map_reading_states` e leituras diretas de `router.presentation`).
- Validação aprovada: `screen_scene_registry`, `run_screen_lifecycle`, `replay_lifecycle`, `dialogue_contracts`, `dialogue_coordination`, `dialogue_reading`, `activity_reconciliation`, `activity_ingress`, `activity_map_completion`, `map_selection`, `settings_contracts`, `settings_interaction`, `settings_recovery`, `run_loadout`, `loadout_editorial`, `loadout_input`, `screen_context_pilot` e `ui_decoupling_baseline`.

## Etapa 10 — Temas, acessibilidade e ferramentas de autoria

- Classificados explicitamente em `ViewStyleFactory` (`scripts/ui/styles/view_style_factory.gd`) os construtores dinâmicos (`DYNAMIC_BUILDER_CLASSIFICATION`: `title`, `muted`, `caption`, `wrap`, `button`, `panel`) para uso restrito a conteúdo variável e laboratórios isolados, preservando a autoridade das cenas canônicas e recursos de estilo.
- Expandidos `ScreenCatalogWorkbench` (`scripts/ui/workbenches/screen_catalog_workbench.gd`), `UICatalogWorkbench` (`scripts/ui/workbenches/ui_catalog_workbench.gd`) e `ScreenCatalogFixtures` (`scripts/ui/workbenches/screen_catalog_fixtures.gd`) para cobrir explicitamente os estados `full`, `empty`, `error` e `long_text` sobre os componentes reais com cópias defensivas (`.duplicate(true)`), garantindo zero mutação nos dicionários/recursos compartilhados (`preview_actor`, `preview_data`, `preview_state`, `preview_status`).
- Tornada determinística a amostragem de `custom_step` do tween de câmera do mapa em `tests/ui_decoupling_baseline.gd` descontando `get_total_elapsed_time()` do frame inicial.
- Validação aprovada: `screen_catalog_full`, `workbench_smoke`, `scene_preview_profiles`, `visual_authority_contract`, `ivory_menu`, `ivory_run_contracts`, `ivory_lab` e `ui_decoupling_baseline`.

## Etapa 11 — Regressão de ponta a ponta e desempenho

- Adicionada guarda defensiva em `CardHandView._drag_visual()` (`scripts/ui/card_hand_view.gd`) quando `interaction.pressed` não corresponde a uma carta ativa (cobrindo manipulação direta de `interaction.dragging` em `audio_contracts`).
- Adicionado fallback de instanciação da estrutura de `art_effect_lab.tscn` em `ArtEffectLab.setup()` (`scripts/labs/art_effect_lab.gd`) quando instanciado via `.new()` direto em `art_effects`.
- Atualizada a condição de espera `idle(screen)` em `tests/unified_loadout_online.gd` para aguardar também `screen.controller._support_reading` ao alternar abas em modo headless.
- Bateria completa de regressão executada e aprovada:
  - 75 suítes offline completas (`ui_contracts`, `layers`, `card_presentation`, `card_modifier_items`, `card_preview_separation`, `card_refresh_stability`, `animation_contracts`, `animation_responsiveness`, `card_motion_polish`, `presentation_envelope`, `presentation_planner`, `presentation_director`, `animation_live`, `semantic_cues`, `card_zone_motion`, `turn_hand_cycle`, `motion_policy`, `historical_presentation`, `animation_limits`, `settings_contracts`, `settings_interaction`, `settings_recovery`, `audio_contracts`, `replay_transport`, `replay_progressive`, `connection_epoch`, `run_graphics_updates`, `run_visual_contexts`, `combat_collaborators`, `map_geometry`, `map_graph_view`, `run_graphics_architecture`, `activity_reconciliation`, `map_selection`, `dialogue_contracts`, `dialogue_coordination`, `dialogue_reading`, `activity_ingress`, `ivory_run_contracts`, `hand_selection`, `reference_hand_geometry`, `card_collections`, `http_transport_pool`, `compact_session`, `rest_transport_latency`, `inscription_loadout_lab`, `inscription_rest_workspace`, `run_loadout`, `loadout_editorial`, `loadout_input`, `api_request_numbers`, `combat_controller`, `combat_input_soak`, `card_cost_layout`, `window_monitor`, `local_server`, `card_transit_counter`, `shared_canvas_theme`, `combat_attributes_drawer`, `run_screen_lifecycle`, `replay_lifecycle`, `activity_map_completion`, `map_auto_completion`, `map_completion_status`, `map_topology_refresh`, `map_camera_motion`, `first_combat_map_transition`, `art_effects`, `art_effect_lab`, `card_draw_pose`, `tactical_pieces`, `visual_authority_contract`, `screen_context_pilot`, `presentation_boundaries`, `ui_decoupling_baseline`) — todas com `exit=0`.
  - 5 suítes de integração REST online com API e persistência isoladas (`map_rest_flow`: 443 checks, `animation_online`: 29 checks, `scene_history_online`: 12 checks na porta `61987`; `unified_loadout_online`: 23 checks e `support_loadout_online`: 17 checks na porta `61989`) — todas com `exit=0` e zero diagnósticos.
  - Baseline final gerado em `docs/architecture/baselines/godot-ui-decoupling/baseline.after.json` e validado contra `baseline.before.json` (74 verificações geométricas e de movimento aprovadas).

## Etapa 12 — Limpeza e documentação

- Atualizado o guia de edição na Godot (`docs/guides/godot-ui-scene-editing.md`) com as novas portas de `ScreenContext`, os modelos e validadores de fronteira (`ViewModels` / `PresentationBoundary`), os colaboradores extraídos de combate/mapa/mão/sessão, o estado `long_text` dos catálogos e os comandos de validação isolados.
- Atualizada a auditoria arquitetural (`docs/architecture/GODOT_EDITABLE_UI_SCENES_AUDIT.md`) e mantida a matriz de autoridade visual e lista canônica dos 25 templates dinâmicos em `docs/architecture/GODOT_SCENE_OWNERSHIP_CONTRACT.md`.
- Conferência final de checkout e Git: todas as modificações independentes pré-existentes do workspace (arquivos C# da engine, docs de API/design, assets de áudio e correções anteriores) foram preservadas intactas, sem mistura nos commits das Etapas 0–12.

### Relatório Final de Certificação

1. **Verificado automaticamente (offline):**
   - 75 suítes completas executadas via `tools/test-godot-ui.ps1` em cópia isolada (`HeroScript-ui-refactor-*`), cobrindo contratos de UI, camadas, mão de cartas, poses, animações, diálogos, atividades, mapa, configurações, áudio, replay, sessão compacta, contratos de autoridade visual (`visual_authority_contract`), isolamento de tela (`screen_context_pilot`), fronteiras de apresentação (`presentation_boundaries`) e baseline geométrico/movimento (`ui_decoupling_baseline`).
2. **Verificado automaticamente (integração REST online isolada):**
   - 5 suítes de integração REST (`map_rest_flow`, `animation_online`, `scene_history_online`, `unified_loadout_online`, `support_loadout_online` — 524 verificações no total) contra instâncias dedicadas da API nas portas `61987` e `61989` com diretórios de persistência descartáveis, sem tocar nos saves reais do jogador.
3. **Verificado por baseline geométrico e de movimento:**
   - Equivalência entre `docs/architecture/baselines/godot-ui-decoupling/baseline.before.json` e `baseline.after.json` nas resoluções `1280x720`, `1920x1080` e `2560x1080` e escalas `1.0` e `1.6`, além de durações e poses em trânsito (`draw`, `use`, `discard`, `exile`, `retained`, `hover`, `fan` e transição de câmera do mapa).
4. **Exceções deliberadas e limites explícitos:**
   - Coleções de cardinalidade variável (cartas, atores, escolhas, ofertas, nós do grafo, traces de atributos), itens nativos de `Tree`/`ItemList`, arcos/fan da mão, câmera/grafo do mapa e shaders/keylines permanecem governados por seus templates e algoritmos dedicados conforme a matriz de autoridade.
   - Não se declara cobertura do produto cartesiano de todas as combinações possíveis de resolução, locale, escala e conteúdo de mods externos.

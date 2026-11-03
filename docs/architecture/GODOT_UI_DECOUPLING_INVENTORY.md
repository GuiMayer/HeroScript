# Inventário e Baseline — Desacoplamento e Editabilidade da Interface Godot

Data: 2026-11-03 · Revisão Git base (`HEAD`): `151b766c` · Engine: Godot `4.7.2.stable.steam`

Guia de referência: [`docs/plans/GODOT_UI_DECOUPLING_AND_EDITABILITY_PLAN.md`](../plans/GODOT_UI_DECOUPLING_AND_EDITABILITY_PLAN.md).

---

## 1. Estado do Checkout e Alterações Locais Preexistentes

O checkout principal contém alterações locais não commitadas que precedem este plano e **devem ser preservadas integralmente**, sem publicação automática de mudanças alheias:

- **Engine C# / Conteúdo / Ferramentas externas ao escopo visual (`src/`, `tests/`, `data/configs/`, `openapi/`, `docs/`, `Start-UnoClash.*`):**
  - Novos projetores e testes de sinergia/transformação de cartas (`CardRuleReferenceProjector.cs`, `UnoClashParityTests.cs`, etc.).
  - Documentação de API, áudio e arte programática.
- **Alterações locais na Godot (`examples/godot-engine-showcase`) já em execução no workspace:**
  - **Auditoria de ciclo de vida de input/apresentação (`CARD_INPUT_LIFECYCLE_AUDIT.md`):** `game_session.gd` (`presentation_discarded`, validação defensiva de envelopes/recibos), `combat_screen.gd` (`_settled_generation`, `_on_presentation_discarded`, retomada deferida), `combat_controller.gd` (`blocking_reasons()`, limpeza de `submitting` no rebind), `presentation_director.gd` e `presentation_handle.gd` (`lost_owned_motion()`), `presentation_planner.gd` e `playback.gd` (normalização defensiva de arrays).
  - **Preferências, monitor e servidor local:** `preferences.gd` (migração de diretório de usuário, persistência de `window_monitor`), `bootstrap.gd` e `scripts/engine/local_server.gd` (`ensure_local_engine()`), `main.gd` (probe com `ensure_local_engine()` e `Preferences.save(false)` no quit).
  - **Assets de áudio e importações:** `assets/audio/kenney-cards/*`, `data/audio_manifest.json`, `.import` de SVGs em `assets/programmer-art/`, e testes (`card_cost_layout.gd`, `card_face_rows.gd`, `combat_input_online.gd`, `combat_input_soak.gd`, `local_server.gd`, `window_monitor.gd`).

Uma cópia integral do estado inicial de `examples/godot-engine-showcase` (1.290 arquivos) e seus hashes SHA-256 foram preservados em `.runtime/ui-decoupling/baseline-source` e `.runtime/ui-decoupling/baseline-hashes.json`, junto com `.runtime/ui-decoupling/initial-status.txt`.

---

## 2. Cenas Registradas, Subcenas e Nós Dinâmicos

Todas as rotas da aplicação são centralizadas em [`scripts/ui/scene_registry.gd`](../../examples/godot-engine-showcase/scripts/ui/scene_registry.gd):

| Rota | Cena Canônica (`.tscn`) | Coordenador / Script Raiz | Subcenas e Componentes | Coleções Dinâmicas Legítimas |
| :--- | :--- | :--- | :--- | :--- |
| `(root)` | `main.tscn` | `scripts/ui/main.gd` | `scenes/ui/shell/application_shell.tscn` (`application_shell.gd`), `pause_menu.tscn`, `toast_banner.tscn`, `confirmation_dialog.tscn`, `inspection_dialog.tscn` | Pilha de modais/diálogos em `OverlayLayer` |
| `menu` | `scenes/ui/screens/main_menu.tscn` | `scripts/ui/main_menu_view.gd` | Hierarquia própria (`Masthead`, `HeroColumn`, `NavigationColumn`, `Footer`) | Opções de `SettingSelector` (`OptionButton`) |
| `settings` | `scenes/ui/screens/settings.tscn` | `scripts/ui/settings_screen.gd` | 6 páginas estáticas (`General`, `Audio`, `Display`, `Accessibility`, `Controls`, `Connection`) | Opções de idioma/resolução (`OptionButton`) |
| `run_loadout` | `scenes/ui/screens/unified_loadout.tscn` | `scripts/ui/unified_loadout_screen.gd` | `loadout_page.tscn`, `inscriptions_editorial.tscn`, `loadout_summary.tscn`, `deck_module.tscn`, `support_workbench.tscn`, `character_attributes_view.tscn`, `card_modifier_inventory.tscn` | Abas de módulos (`loadout_tab_button.tscn`), slots/inventário de inscrições (`inscription_tile.tscn`), cartas (`card_view.tscn`), linhas de atributos (`attribute_row.tscn`) |
| `loadout_lab` | `scenes/unified_loadout_lab.tscn` | `scripts/labs/unified_loadout_lab.gd` | Mesma `loadout_page.tscn` e módulos canônicos + `deck_composition.tscn` | Mesmas coleções reconciliadas por identidade |
| `combat` | `scenes/ui/screens/combat_screen.tscn` | `scripts/ui/combat_screen.gd` | `combat_view.tscn`, `combat_header.tscn`, `battlefield.tscn`, `card_hand_view.tscn`, `turn_controls.tscn`, `combat_feedback.tscn`, `floating_combat_hud.tscn`, `combat_attributes_drawer.tscn`, `action_preview.tscn`, `card_zone_controls.tscn` | Cartas na mão (`card_hand_slot.tscn` + `card_view.tscn`), atores (`actor_panel.tscn`), recursos (`resource_view.tscn`), statuses (`status_badge.tscn`), pilhas (`pile_button.tscn`), escolhas de candidato (`candidate_choice_button.tscn`), popups de dano (`floating_feedback_label.tscn`) |
| `map` | `scenes/ui/screens/map_screen.tscn` | `scripts/ui/map_screen.gd` | `map_view.tscn`, `route_view.tscn`, `map_node.tscn` | Nós do grafo (`map_node.tscn`), botões de ação do inspetor (`map_action_button.tscn`), conexões vetoriais (`_draw`) |
| `activity` | `scenes/ui/screens/activity_screen.tscn` | `scripts/ui/activity_screen.gd` | `journey_view.tscn`, `activity_header.tscn`, `activity_summary.tscn`, `encounter_attempt.tscn`, `activity_options.tscn`, `activity_ending.tscn`, `transformation_confirm_dialog.tscn` | Ofertas de carta (`activity_card_offer.tscn` + `card_view.tscn`), ofertas genéricas (`activity_offer_entry.tscn`), botões de ação (`activity_choice_button.tscn`) |
| `dialogue` | `scenes/ui/screens/dialogue_screen.tscn` | `scripts/ui/dialogue_screen.gd` | `dialogue_scene.tscn` (`dialogue_view.gd`), `dialogue_panel.tscn` (`dialogue_panel.gd`) | Opções de diálogo (`dialogue_choice_item.tscn`) |
| `run_history` | `scenes/ui/screens/run_history.tscn` | `scripts/ui/run_history_screen.gd` | Hierarquia própria com `ItemList` nativo | Itens nativos de `ItemList` |
| `timeline` | `scenes/ui/screens/timeline.tscn` | `scripts/ui/timeline_screen.gd` | `historical_beat_view.tscn`, `gameplay_event_view.tscn` | Itens nativos de `ItemList`/`Tree` e linhas de eventos (`gameplay_event_row.tscn`) |
| `run_replay` | `scenes/ui/screens/run_replay.tscn` | `scripts/ui/run_replay_screen.gd` | `run_replay_view.tscn`, `historical_beat_view.tscn`, `gameplay_event_view.tscn` | Itens nativos de `ItemList` e visualizador histórico |
| `sandbox` | `scenes/ui/screens/sandbox.tscn` | `scripts/ui/sandbox_screen.gd` | Hierarquia própria (`ModesPanel`, `ScenarioPanel`) | Botões de modo (`sandbox_mode_button.tscn`) |
| `codex` | `scenes/ui/screens/codex.tscn` | `scripts/ui/codex_screen.gd` | Hierarquia própria (`TabsScroll`, `List`, `Detail`) | Abas de categoria (`codex_tab_button.tscn`) e itens nativos de `ItemList` |

---

## 3. Dependências de Autoloads, Router e Caminhos Internos

### 3.1. Autoloads (`GameSession`, `Preferences`, `Playback`, `GameAudio`, `I18n`, `GameServices`)
- **Componentes visuais passivos (`combat_view.gd`, `map_view.gd`, `route_view.gd`, `card_view.gd`, `card_hand_view.gd`, `actor_panel.gd`, `resource_view.gd`, `status_badge.gd`, `main_menu_view.gd`, `pause_menu_view.gd`, `journey_view.gd`):** Já estão livres de referências diretas a autoloads e consomem `VisualContext`.
- **Coordenadores de tela (`*_screen.gd`, `main.gd`, `deck_compartment_editor.gd`) e laboratórios (`scripts/labs/*.gd`):** Acessam diretamente os singletons globais (`GameSession`, `Preferences`, `Playback`, `GameAudio`, `I18n`, `GameServices`), dificultando instanciação isolada com serviços falsos sem mutar singletons globais.
- **Dependência cruzada em `preferences.gd`:** `next_campaign_seed()` lê `GameSession.selected_setting` e `GameSession.selected_setting_id` diretamente.

### 3.2. Acoplamentos ao `router` (`main.gd`)
- `combat_screen.gd`: acessa `router.shell.run_scenery.acquire_combat()` / `release_combat()`, `router.navigation_epoch` e `router.get_tree().paused`.
- `activity_screen.gd`: chama método privado `router._new_campaign()`, lê `router.current_screen` e `router.dialogue_return_barrier`.
- `dialogue_screen.gd`: muta `router.dialogue_return_barrier` e `router.dialogue_reading_states`.
- `map_screen.gd`: lê `router.current_screen`, `router.dialogue_return_barrier` e muta `router.map_reading_states`.
- `run_replay_screen.gd` e `timeline_screen.gd`: leem `router.presentation`.

### 3.3. Acessos a Membros Privados e Caminhos Profundos entre Componentes
- `map_view.gd` → lê `route._geometry` (linhas 73, 75, 318–319) e manipula metadados diretamente em `route.buttons[id]`.
- `combat_screen.gd` → chama métodos privados de `CombatView` (`_clear_choices`, `_show_preview`, `_toggle_character_sidebar`, `_candidates`, `_targets`, `_system_candidate`, `_hand_scope`), acessa `presenter._appearance` e navega por `view.hand.interaction.*`, `view.battlefield_view.panels` e `find_children("Pile_*")`.
- `combat_view.gd` → reparenta `hand.toolbar` para fora de `CardHandView`, oculta `hand.counter`, define `hand.toolbar_external = true` e chama `hand._update_counter()`.
- `combat_attributes_drawer.gd` → acessa `attributes_view._scroll`.
- `hand_hit_surface.gd` / `hand_overview_dialog.gd` / `hand_reorder_lab.gd` → chamam métodos privados de `CardHandView` (`_pickable`, `_drag_visual`, `_return_ghost`, `_relayout`, `_check_reorder_settled`) e `CardView` (`_make_custom_tooltip`).
- `activity_screen.gd` / `journey_view.gd` → chamam `_choice_label` em `activity_options_view.gd`.
- `unified_loadout_screen.gd` / `loadout_page_view.gd` / `loadout_view.gd` / `settings_screen.gd` / `timeline_screen.gd` / `run_replay_view.gd` / `run_history_screen.gd` / `sandbox_screen.gd` / `card_details_panel.gd` → usam caminhos relativos profundos (`get_node("A/B/C/...")`) ou buscas repetidas por `find_child(..., true, false)`.

---

## 4. Cobertura de Testes Existentes e Lacunas Identificadas

### Suítes Existentes (`examples/godot-engine-showcase/tests/`)
- **Editabilidade e Catálogo:** `editable_ui_nodes`, `editable_main_menu`, `editable_settings`, `editable_loadout_shell`, `editable_inscriptions`, `editable_loadout_modules`, `editable_combat_shell`, `editable_battlefield_hosts`, `editable_map_scenes`, `editable_activity_scenes`, `dialogue_editable`, `archive_editable`, `replay_editable`, `tools_editable`, `editable_card_inspection`, `editable_character_attributes`, `editable_application_shell`, `screen_scene_registry`, `screen_catalog_full`, `scene_preview_profiles`.
- **Combate, Mão e Animações:** `combat_controller`, `combat_collaborators`, `combat_input_soak`, `combat_input_online`, `animation_contracts`, `animation_live`, `animation_limits`, `animation_responsiveness`, `card_motion_polish`, `card_draw_pose`, `card_zone_motion`, `card_transit_counter`, `turn_hand_cycle`, `hand_selection`, `hand_arrangement`, `hand_reorder_flow`, `hand_reorder_drop`, `hand_reorder_preview`, `hand_reorder_overflow`, `hand_reorder_interruptions`, `reference_hand_geometry`, `uno_motion_*`.
- **Mapa, Atividades e Diálogo:** `map_geometry`, `map_graph_view`, `map_selection`, `map_camera`, `map_camera_motion`, `map_topology_refresh`, `map_completion_status`, `map_auto_completion`, `first_combat_map_transition`, `activity_reconciliation`, `activity_ingress`, `activity_map_completion`, `dialogue_contracts`, `dialogue_coordination`, `dialogue_reading`.
- **Sessão, Transporte, Loadout e Replay:** `compact_session`, `http_transport_pool`, `rest_transport_latency`, `connection_epoch`, `api_request_numbers`, `run_loadout`, `unified_loadout_autosave`, `unified_loadout_recovery`, `unified_loadout_layout`, `loadout_editorial`, `loadout_input`, `deck_compartments`, `support_loadout`, `replay_transport`, `replay_progressive`, `replay_lifecycle`, `run_screen_lifecycle`.

### Lacunas que Serão Cobertas nas Etapas 1–12
1. **Matriz de autoridade visual e testes de edição de recursos exportados:** verificar que parâmetros de layout responsivo (mapa, combate, menu, carta) não têm constantes mágicas competindo no código e que edições no editor/recurso produzem efeito previsível sem sobrescrita indevida.
2. **Abertura isolada de telas de aplicação com contexto injetado (`ScreenContext`):** abrir telas de aplicação (`codex`, `sandbox`, `run_history`, `timeline`, `run_replay`, `activity`, `dialogue`, `map`, `combat`, `settings`, `unified_loadout`) com adaptadores/serviços falsos sem depender de estado global nos singletons.
3. **Validação estrita nas fronteiras de apresentação:** modelos tipados e adaptadores que rejeitam/sinalizam payloads truncados, tipos inválidos, UUIDs ausentes ou revisões incompatíveis sem fabricar ações ilegais.
4. **Testes unitários dedicados para colaboradores extraídos:** layout de mapa/combate (Etapa 5), colaboradores de geometria/input/reconciliação da mão (Etapa 6), coordenadores de combate/playback (Etapa 7) e colaboradores de sessão (Etapa 8).

---

## 5. Baseline Funcional, Geométrica e de Movimento (Etapa 0)

- **Script de captura e verificação:** [`examples/godot-engine-showcase/tests/ui_decoupling_baseline.gd`](../../examples/godot-engine-showcase/tests/ui_decoupling_baseline.gd)
- **Dados versionados:**
  - [`docs/architecture/baselines/godot-ui-decoupling/baseline.before.json`](baselines/godot-ui-decoupling/baseline.before.json)
  - [`examples/godot-engine-showcase/data/baselines/ui_decoupling_baseline.json`](../../examples/godot-engine-showcase/data/baselines/ui_decoupling_baseline.json)
- **Cobertura da baseline:**
  - **Resoluções:** `1280×720`, `1920×1080`, `2560×1080`
  - **Escalas de texto:** `1.0` e `1.6`
  - **Telas e cenas:** `menu`, `settings`, `map`, `activity`, `dialogue`, `loadout`, `combat` (incluindo matriz de mãos com 0, 1, 2, 5 e 10 cartas e 2/3 atores), `replay`
  - **Movimento e durações:** `draw`, `use`, `discard`, `staged_discard`, `exile`, `retained`, `hover`, `fan_retarget`, `reduced_motion`, pose em repouso, pose em hover, pose de compra a $t = 0.5$, cancelamento de compra sem resíduos no overlay e ponto médio/alvo de transição de câmera do mapa.

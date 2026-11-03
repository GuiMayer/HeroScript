# Contrato de Autoridade Visual e Propriedade das Cenas Godot

A cena (`.tscn`) possui a árvore estática, âncoras de base, margens estáticas, flags de container e hierarquia. Recursos exportados (`Theme`, `VisualThemeProfile`, `CardVisualStyle`, `HandVisualStyle`, `CombatLayoutProfile`, `MapLayoutProfile`, `MenuVisualStyle`) possuem métricas responsivas e tokens semânticos. Controladores de layout calculam posições derivadas da área disponível. Animações aplicam poses transitórias e restauram o estado canônico ao concluir ou cancelar.

Nenhuma propriedade pode ter duas autoridades concorrentes no mesmo modo de operação.

---

## 1. Regras Gerais de Montagem e Ciclo de Vida

1. **Instanciação canônica:** `scripts/ui/scene_registry.gd` centraliza as rotas da aplicação. Componentes reutilizáveis (`CardView`, `ActorPanel`, `ResourceView`, `StatusBadge`) possuem uma única `.tscn` canônica usada por produção, laboratórios e previews.
2. **Montagem idempotente:** Se `setup`/`mount`/`apply_projection` ocorrer antes de `add_child`, resolver referências por `_bind_nodes()` idempotente sem depender de `@onready`. Sinais são conectados uma única vez.
3. **Preservação de edições do Inspector:**
   - Em `CardView` e `MainMenuView`, `use_profile_metrics = true` (default de produção) delega dimensões responsivas ao recurso de estilo (`CardVisualStyle`, `MenuVisualStyle`). Desativar `use_profile_metrics = false` preserva integralmente `custom_minimum_size` e tamanhos definidos no `.tscn`.
   - Em `ActorPanel`, `StatusBadge`, `ResourceView` e `PauseMenuView`, `StyleBoxFlat` e margens/tamanhos autorados no `.tscn` são capturados antes de aplicar cores semânticas de estado, preservando padding, bordas e tipografia editados no Inspector.
   - `ViewStyleFactory.apply_view_preferences()` registra `base_font_size` e `base_style_*` na primeira leitura para aplicar `text_scale` e `high_contrast` sem perder o tamanho-base editado no `.tscn`.
4. **Restauração pós-animação:** Todo tween ou pose transitória (`CardMotionPose`, `CardZoneMotion`, `HandRetargetMotion`, `EffectMotion`, `ResourceView._value_motion`, `MapView._camera_tween`) restaura a propriedade ao estado de repouso ou remove o nó transitório (`ghost` / popup) ao concluir, ser substituído ou cancelar (`dispose()` / `cancel_gesture()`).

---

## 2. Matriz de Autoridade Visual por Área

### 2.1. Menu Principal (`main_menu.tscn`, `main_menu_view.gd`, `MenuVisualStyle`)

| Propriedade / Nó | Proprietário (Autoridade) | Momento de Escrita | Justificativa | Restauração / Observação |
| :--- | :--- | :--- | :--- | :--- |
| Hierarquia, âncoras, margens de `MenuScroll`, `Masthead`, `Footer` | `.tscn` (`main_menu.tscn`) | Carregamento da cena | Composição estática editável no Inspector | Permanente |
| `%MenuBody.vertical`, `custom_minimum_size.x`, `"separation"`, `%JourneyColumn.custom_minimum_size.x`, `%NavigationColumn.custom_minimum_size.x` | Layout responsivo via `MenuVisualStyle` (`content_width`, `compact_width`, `hero_min_width`, `navigation_width`, `column_gap`, `compact_gap`, `minimum_scroll_width`, `scroll_padding`) quando `use_profile_metrics=true`; `.tscn` quando `false` | `_layout()` em `resized` / `apply_model()` | Adaptação entre layout de 2 colunas e coluna única compacta | Recalculado deterministicamente a partir de `_scroll.size.x` |
| `Button.custom_minimum_size.y` (ações/links) e `%SettingTitle` `font_size` | `MenuVisualStyle` (`action_height`, `link_height`, `title_size`) quando `use_profile_metrics=true`; `.tscn` quando `false` | `apply_model()` | Padronização tipográfica do perfil editorial | Inalterado quando `use_profile_metrics=false` |
| `%EngineStatus` `font_color`, `Label` `font_color` (por meta `text_role`), `HSeparator` color | Conteúdo de apresentação / `VisualThemeProfile` | `apply_model()` / `apply_visual_profile()` | Comunica estado online/offline da engine e papéis semânticos do tema | Atualizado a cada mudança de estado/perfil |

### 2.2. Carta (`card_view.tscn`, `card_view.gd`, `CardVisualStyle`)

| Propriedade / Nó | Proprietário (Autoridade) | Momento de Escrita | Justificativa | Restauração / Observação |
| :--- | :--- | :--- | :--- | :--- |
| Hierarquia interna (`%CardFaceMargin`, `%CardHeader`, `%CostLine`, `%Art`, `%EffectPanel`, `%Requirements`, `%Changes`) e margens de `%CardFaceMargin` | `.tscn` (`card_view.tscn`) | Carregamento da cena | Estrutura canônica da face da carta | Nunca sobrescrita pelo script |
| `CardView.custom_minimum_size`, `%CardHeader.custom_minimum_size.y`, `%Art.custom_minimum_size.y` | `CardVisualStyle` (`effective_size()`, `title_line_height`, `art_height`) quando `use_profile_metrics=true`; `.tscn` quando `false` | `configure()` | Escala responsiva da carta com `text_scale` | Com `use_profile_metrics=false`, valores do `.tscn` permanecem intactos |
| `StyleBox` de estados (`normal`, `hover`, `pressed`, `disabled`, `focus`), `%Art`, `%EffectPanel`, chips de custo e badges | `.tscn` (geometria/margens da `StyleBox`) + `CardVisualStyle`/`VisualThemeProfile` (cores de raridade, tom, superfície e `face_radius`) | `configure()` | Duplica a `StyleBox` autorada preservando margens e aplica cor semântica de raridade/tom | Reaplicado apenas quando `presentation_key` muda |
| Linhas de requisito (`requirement_min_width`), textos inline (`inline_font_size`, `zero_cost_font_size`, `change_badge_font_size`) | `CardVisualStyle` + conteúdo publicado pela engine | `configure()` / `_cost_zone()` / `_change_badges()` | Coleções variáveis de custos, requisitos e modificadores da carta | Nós filhos reconciliados quando os dados mudam |
| `position`, `rotation`, `scale`, `pivot_offset`, `modulate.a`, `z_index` | Animação (`CardView` entry/emphasis/fly_to ou `CardHandView` quando `hand_managed=true`) | `_ready()`, `set_emphasis()`, `fly_to()` ou pose da mão | Feedback de entrada, hover/seleção e voo entre zonas | `reset_visual_state()` / `set_emphasis(false)` restaura `scale=ONE`, `modulate.a=1.0`, `z_index=0`; `fly_to` remove o ghost no término/cancelamento |

### 2.3. Mão (`card_hand_view.tscn`, `card_hand_view.gd`, `HandVisualStyle`)

| Propriedade / Nó | Proprietário (Autoridade) | Momento de Escrita | Justificativa | Restauração / Observação |
| :--- | :--- | :--- | :--- | :--- |
| Hierarquia base (`Toolbar`, `HandScroll`, `HandSurface`, `EmptyHandState`, `HitSurface`) | `.tscn` (`card_hand_view.tscn`) | Carregamento da cena | Estrutura estática do componente de mão | Permanente |
| Ordem de `Toolbar` (`move_child`) | Configuração `@export var toolbar_at_bottom` / `toolbar_external` | `_ready()` / montagem no combate | Permite barra no topo (laboratório) ou dock inferior (combate) | Determinístico pela configuração |
| `custom_minimum_size` de `CardHandView`, `HandScroll`, `HandSurface`; `slot.position`, `slot.size`, `card.size`, `hit_surface.global_position/size` | Layout responsivo (`hand_layout.gd` + `HandVisualStyle` + `CardVisualStyle`) | `_relayout()` / `position_hit_surface()` | Cálculo geométrico do arco/leque (fan), modo leitura e área de hit-testing conforme número de cartas | Recalculado em `sync_models()`, resize ou fim de apresentação |
| `slot.z_index`, `card.position`, `card.rotation`, `card.scale`, `card.modulate.a` | Animação e pose interativa (`HandRetargetMotion`, `_apply_emphasis()`, `prepare_card()`, `reject_card()`, `CardTransitRegistry`) | Hover, seleção, compra, descarte, drag/reorder | Elevação de hover, abertura de espaço para compra/reordenação e empréstimo de trânsito | `end_presentation()`, `clear_prepared_card()`, `cancel_gesture()` e `Retarget.reset(card)` restauram a pose exata do slot |

### 2.4. Combate (`combat_view.tscn`, `combat_view.gd`, `CombatLayoutProfile`, `ActorPanel`, `ResourceView`, `StatusBadge`)

| Propriedade / Nó | Proprietário (Autoridade) | Momento de Escrita | Justificativa | Restauração / Observação |
| :--- | :--- | :--- | :--- | :--- |
| Hierarquia de `CombatHeader`, `GameplayColumn`, `Battlefield`, `CombatBottomHUD`, `FloatingCombatHUD` | `.tscn` (`combat_view.tscn` e subcenas) | Carregamento da cena | Estrutura de camadas do combate | Permanente |
| Montagem de slots do dock/HUD (`move_child` / `reparent` de `OverlayDefinitions` para `TurnControls` e `FloatingCombatHUD`) | Composição estrutural de `CombatView` + `combat_composition.tscn` | `_build_header()`, `_build_battlefield()`, `_build_footer()`, `_try_apply_composition()` | Permite composições alternativas sem duplicar cenas de controles | Executado uma vez na vinculação ou ao trocar `composition_scene` |
| `hand_section.custom_minimum_size.x`, `bottom_hud.offset_top`, `battlefield_actors.offset_bottom`, offsets de `candidate_panel`, `_feedback_column`, `reconnect_button` e `character_sidebar` | Layout responsivo (`combat_layout_metrics.gd` + `CombatLayoutProfile`: `hand_width_ratio`, `hand_max_width`, `safe_margin`, `candidate_width/height`, `preview_width`, `floating_preview_height`, `floating_dock_gap`, `sidebar_width/height`, `toolbar_min_size`, `reconnect_button_size`, `choice_button_height`) | `_layout_gameplay()` | Garante safe area entre atores e mão elevada em qualquer resolução/escala | Recalculado em `resized` e `minimum_size_changed` |
| `ActorPanel` (`_content.custom_minimum_size`, `target_button.custom_minimum_size`, margens de `_surface`, `_intent.custom_minimum_size.y`) | `.tscn` (`actor_panel.tscn`) no modo completo; `@export` de `ActorPanel` (`compact_*`, `battlefield_*`) e `fit_battlefield_height()` nos modos compacto/battlefield | `setup()` / `fit_battlefield_height()` | Mesmo componente canônico atende inspeção completa, resumo compacto e palco de combate | Captura `_authored_*` no `_bind_nodes()` e restaura ao alternar `_layout_mode` |
| `ResourceView` (`bar.value`, `_fill_style.bg_color`) e `StatusBadge` (`_panel_style.bg_color/border_color`) | `.tscn` (alturas, margens e bordas) + `VisualContext` (cores semânticas) + Animação (`_value_motion`) | `setup()` / `apply_status()` / `present_change()` | Preserva geometria editada no `.tscn` e anima apenas o valor numérico do recurso | `settle_motion()` fixa `bar.value = model.state.current` |

### 2.5. Mapa (`map_view.tscn`, `route_view.tscn`, `map_view.gd`, `route_view.gd`, `MapLayoutProfile`)

| Propriedade / Nó | Proprietário (Autoridade) | Momento de Escrita | Justificativa | Restauração / Observação |
| :--- | :--- | :--- | :--- | :--- |
| Hierarquia de `Header`, `MapWorkspace`, `MapScroll`, `MapCanvas`, `RouteView`, `MapFloatingInspector` | `.tscn` (`map_view.tscn`, `route_view.tscn`, `map_node.tscn`) | Carregamento da cena | Estrutura estática da tela de mapa | Permanente |
| `RouteView.custom_minimum_size`, `button.position`, `button.size`, `caption.position`, `caption.size`, `art` offsets, `glyph.position/size` | Layout responsivo (`map_layout.gd` + `MapLayoutProfile`: `node_size`, `hit_size`, `symbol_size`, `auxiliary_symbol_size`, `caption_height`, `caption_gap`, `node_font_size`, `padding`, `lane_gap`, `layer_gap`) | `RouteView.setup()` | Disposição algorítmica de grafos arbitrários publicados pela engine | Recalculado quando topologia ou `layout_profile` muda |
| `MapFloatingInspector` `position`/`size` e `MapInspectorToggle` `position`/`size` | Layout responsivo (`MapView._layout_inspector()` + `MapLayoutProfile`: `inspector_*`) | `_layout_inspector()` em `_body.resized` e toggle | Inspetor flutuante lateral (`>= 1000px`) ou inferior (`< 1000px`) sem redimensionar o `MapScroll` | Recalculado em resize/toggle |
| `_scroll.scroll_horizontal`, `_scroll.scroll_vertical` | Animação / Câmera (`map_camera.gd` + `_camera_tween`, `MapLayoutProfile.camera_transition_seconds`) | `_center_current()`, `_reveal_node()`, drag de ponteiro | Interpolação suave entre nós do mapa e pan manual | `_stop_camera_motion(true)` ou input de ponteiro/dispose encerra o tween de forma limpa |

### 2.6. Overlays e Modais (`pause_menu.tscn`, `inspection_dialog.tscn`, `confirmation_dialog.tscn`, `toast_banner.tscn`, `card_payment.tscn`)

| Propriedade / Nó | Proprietário (Autoridade) | Momento de Escrita | Justificativa | Restauração / Observação |
| :--- | :--- | :--- | :--- | :--- |
| Hierarquia, tamanhos mínimos, margens internas de `StyleBox` e layout de botões | `.tscn` respectivo de cada overlay | Carregamento da cena | Composição autoral no editor | Margens originais da `StyleBox` preservadas ao duplicar |
| Cores de scrim, fundo de superfície e borda (`surface.modal_scrim`, `surface.popup`, `surface.toast`, `stroke.panel`) | `VisualThemeProfile` / `VisualContext` | `setup()` / `show_toast()` | Consistência cromática e suporte a alto contraste | Aplicado por instância |
| Escala e opacidade de abertura (`PanelMotion.pop_in`) | Animação (`panel_motion.gd`) | Abertura do modal | Transição curta de entrada | Finaliza em `scale = Vector2.ONE`, `modulate.a = 1.0` |

---

## 3. Controles Criados Dinamicamente por Coleção

Apenas coleções cuja cardinalidade depende do estado publicado pela engine instanciam templates em runtime e reconciliam por identidade:

1. **Mão e Zonas de Cartas:** `card_hand_slot.tscn` + `card_view.tscn` (em `CardHandView`), `card_view.tscn` (em `CardCollectionView`, `DeckModuleView`, `DeckCompositionView`), `pile_button.tscn` (em `CardZoneControlsView`).
2. ** face da Carta:** `card_cost_symbol.gd` / `card_cost_chip.tscn` (em `%CostLine`), `card_inline_label.tscn` (em `%Requirements` e separadores de custo), `card_change_badge.tscn` (em `%Changes`), `card_details_section.tscn` e `card_details_row.tscn` (em `CardDetailsPanel`).
3. **Campo de Batalha e Atores:** `battlefield_group.tscn` e `actor_panel.tscn` (em `BattlefieldView`), `resource_view.tscn`, `resource_counter.tscn` e `status_badge.tscn` (em `ActorPanel`), `candidate_choice_button.tscn` (em `CombatView`), `floating_feedback_label.tscn` (em `CombatFeedback`).
4. **Mapa, Atividades e Diálogo:** `map_node.tscn` (em `RouteView`), `map_action_button.tscn` (em `MapView`), `activity_card_offer.tscn`, `activity_offer_entry.tscn` e `activity_choice_button.tscn` (em `ActivityOptionsView`), `dialogue_choice_item.tscn` (em `DialoguePanel`).
5. **Loadout, Eventos, Sandbox e Codex:** `loadout_tab_button.tscn` (em `LoadoutPageView`), `inscription_tile.tscn` (em `LoadoutView`), `card_modifier_entry.tscn` (em `CardModifierInventoryView`), `attribute_group.tscn` e `attribute_row.tscn` (em `CharacterAttributesView`), `gameplay_event_row.tscn` (em `GameplayEventView`), `sandbox_mode_button.tscn` (em `SandboxScreen`), `codex_tab_button.tscn` (em `CodexScreen`).

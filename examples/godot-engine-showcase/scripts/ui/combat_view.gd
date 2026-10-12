extends "res://scripts/ui/screen_view.gd"

signal intent_requested(action: String, arguments: Array)
const Presenter = preload("res://scripts/presentation/combat_presenter.gd")
const CardZonePresenter = preload("res://scripts/presentation/card_zone_presenter.gd")
const HandView = preload("res://scripts/ui/card_hand_view.gd")
var presenter
var zone_presenter
var presentation: Dictionary
var scenery_model: Dictionary = {}
var scenery
@export var composition_scene: PackedScene = preload("res://scenes/ui/combat_composition.tscn")
@export var layout_profile: Resource = preload("res://data/themes/bottom_preview.tres")
var _pending_composition: Dictionary = {}
var _feedback_column: Control
var _interaction: Dictionary = {}
var _input_actor := ""
var _scope := ""
var _setting: Dictionary = {}
var _setting_id := ""
var selected_target: String:
	get: return str(_interaction.get("selected_target", ""))
var selected_card: String:
	get: return str(_interaction.get("selected_card", ""))
var hand
var hand_section: VBoxContainer
var _projection_key: Array = []
var _run_snapshot: Dictionary
var _combat_snapshot: Dictionary
var _action_snapshot: Array
var _zone_snapshot: Dictionary
var header_view
var battlefield_view
var turn_view
var _turn_key: Array = []
var _hand_chrome_key: Array = []
var detail_label: RichTextLabel
var queue_label: Label
var next_frame_button: Button
var actor_portraits: Dictionary = {}
var target_buttons: Dictionary = {}
var card_buttons: Dictionary = {}
var action_buttons: Array[Button] = []
var choices: VBoxContainer
var hint_label: Label
var selection_label: Label
var feedback
var submitting: bool:
	get: return bool(_interaction.get("submitting", false))
var reconnect_button: Button
var selection_actions: HBoxContainer
var animate_card_entry := true
var render_epoch := 0
var gameplay_column: Control
var bottom_hud: VBoxContainer
var battlefield_actors: ScrollContainer
var floating_hud
var candidate_panel: PanelContainer
var _gameplay_layout_pending := false
var character_sidebar: PanelContainer
var character_sidebar_content: ScrollContainer
var character_sidebar_title: Label
var character_sidebar_reopen: Button
var character_sidebar_expanded := false

func _ready() -> void:
	visibility_changed.connect(func():
		if is_instance_valid(floating_hud): floating_hud.visible = is_visible_in_tree())
	if is_instance_valid(floating_hud): floating_hud.visible = is_visible_in_tree()
	_queue_gameplay_layout()


func _refresh_structure() -> void:
	_build_header()
	_build_battlefield()
	_build_hand()
	_build_footer()
	_build_character_sidebar()
	visual_context.styles.apply_view_preferences(self)
	_update_controls()
	_queue_gameplay_layout()


func _ensure_bottom_hud() -> void:
	if is_instance_valid(bottom_hud): return
	bottom_hud = VBoxContainer.new()
	bottom_hud.name = "CombatBottomHUD"
	bottom_hud.mouse_filter = Control.MOUSE_FILTER_IGNORE
	bottom_hud.add_theme_constant_override("separation", 4)
	gameplay_column.add_child(bottom_hud)
	bottom_hud.set_anchors_and_offsets_preset(Control.PRESET_BOTTOM_WIDE)
	bottom_hud.minimum_size_changed.connect(_queue_gameplay_layout)


func _ensure_floating_hud() -> void:
	if is_instance_valid(floating_hud): return
	floating_hud = preload("res://scripts/ui/floating_hud.gd").new()
	add_child(floating_hud)
	floating_hud.visible = is_visible_in_tree()


func _queue_gameplay_layout() -> void:
	if _gameplay_layout_pending: return
	_gameplay_layout_pending = true
	_layout_gameplay.call_deferred()


func _layout_gameplay() -> void:
	_gameplay_layout_pending = false
	if not is_inside_tree() or not is_instance_valid(bottom_hud): return
	var height := bottom_hud.get_combined_minimum_size().y
	if is_instance_valid(hand_section):
		var metrics: Dictionary = preload("res://scripts/presentation/combat_layout_metrics.gd").calculate(gameplay_column.size, Vector2(0, hand_section.get_combined_minimum_size().y), height - hand_section.get_combined_minimum_size().y, layout_profile)
		hand_section.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
		hand_section.custom_minimum_size.x = metrics.hand_width
	bottom_hud.offset_top = -height
	if is_instance_valid(floating_hud):
		floating_hud.fit_to(gameplay_column.get_global_rect(), visual_context.theme)
		if is_instance_valid(character_sidebar):
			character_sidebar.offset_bottom = minf(layout_profile.sidebar_height, gameplay_column.size.y - 84)
			character_sidebar.offset_left = -layout_profile.sidebar_width
		if is_instance_valid(candidate_panel):
			var dock_height: float = hand.toolbar.get_combined_minimum_size().y + bottom_hud.get_node("TurnControls").get_combined_minimum_size().y + 10
			candidate_panel.offset_bottom = -dock_height
			candidate_panel.offset_top = -dock_height - layout_profile.candidate_height
			candidate_panel.offset_right = layout_profile.candidate_width
	if is_instance_valid(_feedback_column) and layout_profile.preview_placement == "floating_left":
		_feedback_column.set_anchors_and_offsets_preset(Control.PRESET_BOTTOM_LEFT)
		var dock_height: float = hand.toolbar.get_combined_minimum_size().y + turn_view.get_combined_minimum_size().y + 10
		_feedback_column.offset_right = layout_profile.preview_width
		_feedback_column.offset_bottom = -dock_height
		_feedback_column.offset_top = -dock_height - 110
	# The scenery extends behind the floating hand, but actor information stays
	# above even the raised cards. No rules or card count limits affect this layout.
	if is_instance_valid(battlefield_actors):
		battlefield_actors.offset_bottom = -height - layout_profile.safe_margin
		var available := battlefield_actors.size.y
		if battlefield_actors.get_h_scroll_bar().visible: available -= battlefield_actors.get_h_scroll_bar().size.y
		for actor in battlefield_actors.find_children("ActorView_*", "PanelContainer", true, false):
			actor.fit_battlefield_height(available)


func _build_header() -> void:
	if not is_instance_valid(header_view):
		header_view = preload("res://scripts/ui/components/combat_header_view.gd").new()
		header_view.name = "CombatHeader"
		header_view.timeline_requested.connect(func(): intent_requested.emit("timeline", []))
		header_view.tools_requested.connect(_open_zone_tools)
		header_view.pause_requested.connect(func(): intent_requested.emit("pause", []))
		add_child(header_view)
		move_child(header_view, 0)
		action_buttons.append(header_view.tool_button) if is_instance_valid(header_view.tool_button) else null
	var row = header_view
	row.visual_context = visual_context
	row.apply_model(presenter.header_model({"open_timeline": visual_context.action_label("open_timeline")}).to_dictionary())
	if row.tool_button not in action_buttons: action_buttons.append(row.tool_button)


func _build_battlefield() -> void:
	if not is_instance_valid(battlefield_view):
		battlefield_view = preload("res://scripts/ui/components/battlefield_view.gd").new()
		battlefield_view.target_selected.connect(_select_target)
		battlefield_view.target_previewed.connect(_show_target_preview)
		battlefield_view.preview_left.connect(func(): if not selected_card.is_empty(): _show_preview(selected_card))
		battlefield_view.layout_requested.connect(_queue_gameplay_layout)
		gameplay_column.add_child(battlefield_view)
		battlefield_view.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
		gameplay_column.move_child(battlefield_view, 0)
	var stage = battlefield_view
	var actors: Array = presenter.combat_model(_input_actor).read("actors")
	stage.visual_context = visual_context
	stage.apply_model(actors, presentation)
	if is_instance_valid(scenery):
		scenery.apply_model(actors, str(scenery_model.get("backgroundId", "combat")), visual_context)
		scenery.bind_stage(stage)
		scenery.bind_anchors(stage.targets)
	battlefield_actors = stage.actor_scroll
	actor_portraits = scenery.portraits if is_instance_valid(scenery) else {}
	target_buttons = stage.targets


func _build_hand() -> void:
	var section := hand_section if is_instance_valid(hand_section) else VBoxContainer.new()
	hand_section = section
	var chrome_key: Array = [zone_presenter.auxiliary_zones(), visual_context.translator.locale]
	if is_instance_valid(hand) and _hand_chrome_key == chrome_key:
		_sync_hand_models()
		return
	_hand_chrome_key = chrome_key.duplicate(true)
	section.name = "HandSection"
	for child in section.get_children():
		if child != hand: section.remove_child(child); child.queue_free()
	if is_instance_valid(hand):
		var prior_heading: Node = hand.toolbar.get_node_or_null("HandHeading")
		if is_instance_valid(prior_heading): hand.toolbar.remove_child(prior_heading); prior_heading.queue_free()
	section.add_theme_constant_override("separation", 4)
	var heading := HBoxContainer.new()
	heading.name = "HandHeading"
	heading.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	heading.add_theme_constant_override("separation", 10)
	hint_label = visual_context.styles.caption(visual_context.translator.text("Select a card, then choose a highlighted target."), 13, 260)
	hint_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	# The footer already presents this hint; keep the same data hook without
	# reserving a second line above the cards.
	hint_label.hide()
	section.add_child(hint_label)
	var zones_scroll := ScrollContainer.new()
	zones_scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_AUTO
	zones_scroll.vertical_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	zones_scroll.custom_minimum_size = Vector2(360, 40)
	zones_scroll.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	var zones := HBoxContainer.new()
	zones.add_theme_constant_override("separation", 6)
	for zone in zone_presenter.auxiliary_zones():
		var zone_id := str(zone.get("zoneId", ""))
		var label: String = zone_presenter.zone_label(zone)
		var pile := _button("%s · %s" % [label, int(zone.get("count", 0))],
			func(): _inspect_pile(label, zone_id))
		pile.add_theme_font_size_override("font_size", 13)
		pile.custom_minimum_size = Vector2(136, 34)
		pile.clip_text = true
		pile.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
		pile.tooltip_text = "%s · %s" % [label, int(zone.get("count", 0))]
		zones.add_child(pile)
	zones_scroll.add_child(zones)
	heading.add_child(zones_scroll)
	if not is_instance_valid(hand):
		hand = HandView.new(visual_context)
		hand.card_activated.connect(_choose_card)
		hand.card_previewed.connect(_show_preview)
		hand.inspection_requested.connect(_inspect_instance)
		hand.selection_requested.connect(func(id):
			if _locked() or id not in card_buttons: return
			intent_requested.emit("select_card", [id]))
		hand.drop_requested.connect(_drop_card)
		section.add_child(hand)
	hand.toolbar.add_child(heading)
	hand.toolbar.move_child(heading, 0)
	hand.set_tools_below_cards(true)
	hand.counter.size_flags_horizontal = Control.SIZE_FILL
	_sync_hand_models()


func _sync_hand_models() -> void:
	var models: Array = []
	for card in zone_presenter.playable_cards():
		models.append(presenter.card_view_model(str(card.get("cardInstanceId", ""))))
	hand.sync_models(models, _hand_scope(), visual_context.translator.text("Your hand is empty."))
	card_buttons = hand.cards()
	for id in hand.order:
		if card_buttons[id].has_meta("initial_focus"): card_buttons[id].remove_meta("initial_focus")
	_ensure_bottom_hud()
	if hand_section.get_parent() == null: bottom_hud.add_child(hand_section)


func _empty_hand_state() -> Control:
	return HandView.empty_state(visual_context.translator.text("Your hand is empty."), visual_context)


func _build_footer() -> void:
	_ensure_floating_hud()
	var key: Array = [_system_candidate(["PASS", "PASS_PRIORITY"]), visual_context.translator.locale, visual_context.action_label("end_turn"), visual_context.action_label("confirm_action")]
	if is_instance_valid(turn_view) and key == _turn_key: return
	_turn_key = key.duplicate(true)
	if is_instance_valid(turn_view):
		for button in turn_view.buttons: action_buttons.erase(button)
		if is_instance_valid(_feedback_column) and _feedback_column.get_parent() != turn_view:
			floating_hud.release_slot("preview")
		bottom_hud.remove_child(turn_view); turn_view.queue_free()
	var row = preload("res://scripts/ui/components/turn_controls_view.gd").new()
	turn_view = row
	row.visual_context = visual_context
	var feedback = preload("res://scripts/ui/components/action_preview_view.gd").new()
	_feedback_column = feedback
	feedback.visual_context = visual_context
	feedback.mount(visual_context.translator.text("Select a card, then choose a highlighted target."))
	row.add_child(feedback)
	detail_label = feedback.detail_label; selection_label = feedback.selection_label; queue_label = feedback.queue_label
	row.next_requested.connect(_next_frame)
	row.cancel_requested.connect(_cancel_selection)
	row.end_requested.connect(_end_turn)
	row.system_requested.connect(_execute_system)
	row.mount(visual_context.translator, {"confirm_action": visual_context.action_label("confirm_action"), "end_turn": visual_context.action_label("end_turn")},
		_system_candidate(["PASS", "PASS_PRIORITY"]))
	next_frame_button = row.next_frame_button; selection_actions = row.selection_actions
	reconnect_button = _button(visual_context.translator.text("RECONNECT"), _reconnect, 130)
	floating_hud.replace_slot("reconnect", reconnect_button)
	reconnect_button.set_anchors_and_offsets_preset(Control.PRESET_TOP_LEFT)
	reconnect_button.offset_right = 150
	reconnect_button.offset_bottom = 44
	action_buttons.append_array(row.buttons)
	choices = VBoxContainer.new()
	candidate_panel = PanelContainer.new()
	candidate_panel.name = "CandidatePanel"
	candidate_panel.mouse_filter = Control.MOUSE_FILTER_STOP
	candidate_panel.add_theme_stylebox_override("panel", visual_context.styles.box(visual_context.styles.PANEL, 10, visual_context.styles.GOLD, 1))
	floating_hud.replace_slot("candidates", candidate_panel)
	candidate_panel.set_anchors_and_offsets_preset(Control.PRESET_BOTTOM_LEFT)
	candidate_panel.offset_right = 600
	candidate_panel.hide()
	var alternatives := ScrollContainer.new()
	alternatives.name = "CandidateChoicesSlot"
	alternatives.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	choices.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	alternatives.add_child(choices)
	candidate_panel.add_child(alternatives)
	bottom_hud.add_child(row)
	_apply_preview_composition()


func _build_character_sidebar() -> Control:
	_ensure_floating_hud()
	if not is_instance_valid(character_sidebar_reopen):
		character_sidebar_reopen = _button("‹", _toggle_character_sidebar, 36)
		character_sidebar_reopen.name = "CharacterSidebarReopen"
		character_sidebar_reopen.add_theme_font_size_override("font_size", 18)
		character_sidebar_reopen.clip_text = true
		for state in ["normal", "hover", "pressed", "disabled", "focus"]:
			var style: StyleBox = visual_context.theme.get_stylebox(state, "Button").duplicate()
			for side in [SIDE_LEFT, SIDE_TOP, SIDE_RIGHT, SIDE_BOTTOM]: style.set_content_margin(side, 6)
			character_sidebar_reopen.add_theme_stylebox_override(state, style)
		character_sidebar_reopen.z_index = 1
		floating_hud.replace_slot("sidebar_handle", character_sidebar_reopen)
		character_sidebar_reopen.set_anchors_and_offsets_preset(Control.PRESET_TOP_RIGHT)
		character_sidebar_reopen.offset_left = -layout_profile.handle_width
		character_sidebar_reopen.offset_bottom = 44
	var view = character_sidebar
	if not is_instance_valid(view):
		view = preload("res://scripts/ui/components/character_sidebar_view.gd").new()
		view.collapse_requested.connect(_toggle_character_sidebar)
		floating_hud.replace_slot("sidebar", view)
	view.visual_context = visual_context
	view.apply_model({"presentation": presentation, "run": _run_snapshot, "combat": _combat_snapshot,
		"actors": presenter.actors(), "input_actor": _input_actor, "zones": _zone_snapshot,
		"setting": _setting, "setting_id": _setting_id}, visual_context.translator)
	character_sidebar = view
	view.set_anchors_and_offsets_preset(Control.PRESET_TOP_RIGHT)
	view.offset_left = -layout_profile.sidebar_width
	view.offset_bottom = layout_profile.sidebar_height
	character_sidebar_content = view.content_scroll
	character_sidebar_title = view.title_label
	_apply_character_sidebar_state()
	return view


func _toggle_character_sidebar() -> void:
	character_sidebar_expanded = not character_sidebar_expanded
	_apply_character_sidebar_state()


func _apply_character_sidebar_state() -> void:
	if not is_instance_valid(character_sidebar) or not is_instance_valid(character_sidebar_content):
		return
	var toggle: Button = character_sidebar.toggle_button
	var transfer_focus := (is_instance_valid(toggle) and toggle.has_focus()) or character_sidebar_reopen.has_focus()
	character_sidebar.custom_minimum_size.x = 328
	character_sidebar.visible = character_sidebar_expanded
	character_sidebar_reopen.visible = not character_sidebar_expanded
	character_sidebar_reopen.tooltip_text = visual_context.translator.text("EXPAND CHARACTER PANEL")
	character_sidebar_content.visible = character_sidebar_expanded
	character_sidebar_title.visible = character_sidebar_expanded
	if is_instance_valid(toggle):
		toggle.tooltip_text = visual_context.translator.text("COLLAPSE CHARACTER PANEL")
	if transfer_focus and is_inside_tree():
		(toggle if character_sidebar_expanded else character_sidebar_reopen).grab_focus()
	preload("res://scripts/ui/focus_navigation.gd").queue(self)


func _input(event: InputEvent) -> void:
	# Consume cancel before the router handles pause; the first Escape cancels targeting.
	if not get_tree().paused and not selected_card.is_empty() and event.is_action_pressed("ui_cancel"):
		_cancel_selection()
		get_viewport().set_input_as_handled()


func _unhandled_input(event: InputEvent) -> void:
	if get_tree().paused or not is_visible_in_tree():
		return
	if event.is_action_pressed("inspect_card") and not event.is_echo():
		_inspect_instance(hand.active_card_id())
		get_viewport().set_input_as_handled()
	elif event.is_action_pressed("hand_overview") and not event.is_echo():
		hand.show_overview()
		get_viewport().set_input_as_handled()
	elif event.is_action_pressed("end_turn") and not event.is_echo():
		_end_turn()
		get_viewport().set_input_as_handled()
	elif event.is_action_pressed("confirm_action") and not event.is_echo():
		_next_frame()
		get_viewport().set_input_as_handled()
	elif event.is_action_pressed("ui_cancel") or (event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_RIGHT):
		_cancel_selection()


func _queue_text() -> String:
	var remaining := int(_interaction.get("remaining_frames", 0))
	return visual_context.translator.text("%s animation(s) remaining") % maxi(remaining, 0)


func _candidate_for_card(instance_id: String, target_id := "") -> Dictionary:
	return presenter.candidate_for_card(instance_id, target_id)


func _system_candidate(action_types: Array) -> Dictionary:
	return presenter.system_candidate(action_types)


func _preview(candidate: Dictionary) -> String:
	return presenter.preview(candidate)


func _card_instance(instance_id: String) -> Dictionary:
	return presenter.card_instance(instance_id)


func _intent_for(actor_id: String) -> String:
	return presenter.intent_for(actor_id)


func _button(text: String, action: Callable, width := 0) -> Button:
	var value := visual_context.styles.button(text, width)
	value.custom_minimum_size.y = 44
	value.pressed.connect(action)
	return value

func _process(_delta: float) -> void:
	_try_apply_composition()


func _has_frames() -> bool:
	return bool(_interaction.get("has_frames", false))


func _locked() -> bool:
	return bool(_interaction.get("locked", true))


func _candidates(id: String) -> Array:
	return presenter.candidates(id)


func _targets(candidate: Dictionary) -> Array:
	return presenter.targets(candidate)


func _clear_choices() -> void:
	if is_instance_valid(candidate_panel): candidate_panel.hide()
	for child in choices.get_children():
		choices.remove_child(child)
		child.queue_free()


func _show_preview(id: String) -> void:
	if not is_instance_valid(detail_label) or _has_frames():
		return
	detail_label.text = visual_context.translator.text("Action preview: %s") % presenter.action_preview(id, selected_target if id == selected_card else "")
	detail_label.tooltip_text = detail_label.text


func _show_target_preview(actor_id: String) -> void:
	if _locked() or selected_card.is_empty() or not _candidates(selected_card).any(func(candidate): return actor_id in _targets(candidate)):
		return
	detail_label.text = visual_context.translator.text("Action preview: %s") % presenter.action_preview(selected_card, actor_id)
	detail_label.tooltip_text = detail_label.text


func _update_controls() -> void:
	preload("res://scripts/ui/focus_navigation.gd").wire.call_deferred(self)
	if not is_instance_valid(queue_label):
		return
	var locked := _locked()
	detail_label.tooltip_text = detail_label.text
	hand.set_read_only(locked)
	hand.set_selection(selected_card)
	reconnect_button.visible = not bool(_interaction.get("synchronized", false))
	queue_label.text = _queue_text() if _has_frames() else ""
	queue_label.tooltip_text = queue_label.text
	next_frame_button.visible = _has_frames()
	selection_actions.visible = not selected_card.is_empty() and not _has_frames()
	selection_label.text = visual_context.translator.text("PROCESSING…") if bool(_interaction.get("busy", false)) or submitting else (
		visual_context.translator.text("Choose a highlighted target.") if not selected_card.is_empty() else "")
	selection_label.tooltip_text = selection_label.text
	if bool(_interaction.get("busy", false)) or submitting: queue_label.text = selection_label.text
	queue_label.tooltip_text = queue_label.text
	for id in card_buttons:
		var card: CardView = card_buttons[id]
		card.disabled = false # Still focusable for reading; activation is gated by the hand/screen.
		# Availability is explicit text; tinting the entire card corrupts semantic colors.
		card.modulate = Color.WHITE
		card.select_card(id == selected_card)
	for button in action_buttons:
		button.disabled = locked or (button.has_meta("selection_only") and selected_card.is_empty()) or \
			(button.has_meta("requires_end") and _system_candidate(["END_TURN"]).is_empty())
	for id in target_buttons:
		var allowed := not selected_card.is_empty() and _candidates(selected_card).any(func(candidate): return id in _targets(candidate))
		target_buttons[id].disabled = locked or not allowed
		target_buttons[id].button_pressed = selected_target == id
		target_buttons[id].modulate = visual_context.styles.GOLD if allowed else Color.WHITE


func _actor_name(id: String) -> String:
	return presenter.actor_name(id)


func _cost_text(candidate: Dictionary) -> String:
	return presenter.cost_text(candidate)


func _inspect_card() -> void:
	_inspect_instance(selected_card)


func _inspect_instance(id: String) -> void:
	if id not in card_buttons: return
	var dialog := preload("res://scripts/ui/inspection_dialog.gd").new(visual_context)
	dialog.setup(visual_context.translator.text("Card inspection"), presenter.inspection_text(id), presenter.inspection_data(id), presenter.card_view_model(id), card_buttons[id].visual_style)
	dialog.tree_exiting.connect(func(): if id in card_buttons: card_buttons[id].grab_focus())
	add_child(dialog)
	hand.track_reading_modal(dialog)
	dialog.popup_centered()


func _inspect_pile(label: String, zone: String) -> void:
	var lines: Array[String] = []
	for card in presenter.pile_cards(zone):
		lines.append(visual_context.translator.content_name(str(card.get("definitionId", ""))) + (" +" if not card.get("upgrades", []).is_empty() else ""))
	var dialog := preload("res://scripts/ui/inspection_dialog.gd").new(visual_context)
	dialog.setup(visual_context.translator.text(label), "\n".join(lines) if not lines.is_empty() else visual_context.translator.text("Empty"))
	add_child(dialog)
	hand.track_reading_modal(dialog)
	dialog.popup_centered()


func _open_zone_tools() -> void:
	if _locked(): return
	var flows: Array = CardZonePresenter.tool_flows(_run_snapshot)
	if flows.is_empty(): return
	var dialog := preload("res://scripts/ui/card_zone_tool_dialog.gd").new()
	dialog.visual_context = visual_context
	dialog.setup(flows, selected_card)
	dialog.flow_requested.connect(_execute_zone_flow)
	add_child(dialog)
	hand.track_reading_modal(dialog)
	dialog.popup_centered()


func capture_local_state() -> Dictionary:
	var focused := get_viewport().gui_get_focus_owner()
	return {"sidebar": character_sidebar_expanded, "hand_scroll": hand.scroll.scroll_horizontal,
		"focus": weakref(focused) if is_instance_valid(focused) else null}


func restore_local_state(state: Dictionary) -> void:
	character_sidebar_expanded = bool(state.get("sidebar", false))
	_apply_character_sidebar_state()
	hand.scroll.scroll_horizontal = int(state.get("hand_scroll", 0))
	var focus = state.get("focus")
	if focus and is_instance_valid(focus.get_ref()) and focus.get_ref().is_inside_tree(): focus.get_ref().grab_focus()


func replace_composition(scene: PackedScene, profile: Resource) -> void:
	if scene == null or profile == null or not scene.can_instantiate(): return
	var probe: Node = scene.instantiate()
	var valid := probe is Control and probe.get_node_or_null("CombatGameplay") is Control
	probe.free()
	if not valid:
		push_warning("Combat composition requires a Control root and CombatGameplay slot.")
		return
	_pending_composition = {"scene": scene, "profile": profile}
	_try_apply_composition()


func _try_apply_composition() -> void:
	if _pending_composition.is_empty() or not is_inside_tree() or not is_instance_valid(hand) or submitting or bool(_interaction.get("busy", false)) or not hand.interaction.pressed.is_empty(): return
	var state := capture_local_state()
	hand.cancel_gesture()
	var body: Control = _pending_composition.scene.instantiate()
	var replacement: Control = body.get_node("CombatGameplay")
	var previous := gameplay_column.get_parent()
	var index := previous.get_index()
	gameplay_column = replacement
	remove_child(previous)
	add_child(body)
	move_child(body, index)
	battlefield_view.reparent(replacement, false)
	battlefield_view.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	bottom_hud.reparent(replacement, false)
	bottom_hud.set_anchors_and_offsets_preset(Control.PRESET_BOTTOM_WIDE)
	gameplay_column.resized.connect(_queue_gameplay_layout)
	previous.queue_free()
	composition_scene = _pending_composition.scene
	layout_profile = _pending_composition.profile
	_pending_composition.clear()
	_apply_preview_composition()
	restore_local_state(state)
	_queue_gameplay_layout()
	preload("res://scripts/ui/focus_navigation.gd").queue(self)


func _apply_preview_composition() -> void:
	if not is_instance_valid(_feedback_column): return
	var parent: Control = floating_hud.host if layout_profile.preview_placement == "floating_left" else turn_view
	if parent == floating_hud.host: floating_hud.replace_slot("preview", _feedback_column)
	else:
		floating_hud.take_slot("preview")
		if _feedback_column.get_parent() != parent: _feedback_column.reparent(parent, false)
	if parent == turn_view:
		_feedback_column.set_anchors_and_offsets_preset(Control.PRESET_TOP_LEFT)
		turn_view.move_child(_feedback_column, 0)


func _drop_card(id: String, point: Vector2) -> void:
	intent_requested.emit("_drop_card", [id, point])

func _select_target(actor_id: String) -> void:
	intent_requested.emit("_select_target", [actor_id])

func _play_candidate(candidate: Dictionary) -> void:
	var id := str(candidate.get("command", {}).get("cardInstanceId", ""))
	for option in presenter.action_options(id):
		if option.read("candidate") == candidate:
			intent_requested.emit("_play_option", [option.reference, id])
			return

func _end_turn() -> void:
	intent_requested.emit("_end_turn", [])

func _execute_system(candidate: Dictionary) -> void:
	_play_candidate(candidate)

func _next_frame() -> void:
	intent_requested.emit("_next_frame", [])

func _choose_card(id: String) -> void:
	intent_requested.emit("_choose_card", [id])

func _cancel_selection() -> void:
	intent_requested.emit("_cancel_selection", [])

func _reconnect() -> void:
	intent_requested.emit("_reconnect", [])

func _execute_zone_flow(payload: Dictionary) -> void:
	intent_requested.emit("_execute_zone_flow", [payload])

func _hand_scope() -> String: return _scope

func apply_projection(data: Dictionary, combat_presenter, cards_presenter, facts: Dictionary, context) -> void:
	mount(context)
	theme = context.theme
	presenter = combat_presenter
	zone_presenter = cards_presenter
	_run_snapshot = facts.run.duplicate(true)
	_combat_snapshot = facts.combat.duplicate(true)
	_action_snapshot = facts.actions.duplicate(true)
	_zone_snapshot = facts.zones.duplicate(true)
	_input_actor = facts.input_actor
	_scope = facts.scope
	_setting = facts.setting.duplicate(true)
	_setting_id = facts.setting_id
	scenery_model = facts.get("scenery", {}).duplicate(true)
	presentation = data.duplicate(true)
	if is_instance_valid(hand):
		hand.cancel_gesture()
		_refresh_structure()
		return
	feedback = preload("res://scripts/ui/components/combat_feedback.gd").new(visual_context)
	add_child(feedback)
	add_theme_constant_override("separation", 8)
	_build_header()
	var body: HBoxContainer = composition_scene.instantiate()
	gameplay_column = body.get_node("CombatGameplay")
	gameplay_column.resized.connect(_queue_gameplay_layout)
	_ensure_bottom_hud()
	_build_battlefield()
	_build_hand()
	_build_footer()
	add_child(body)
	_ensure_floating_hud()
	_build_character_sidebar()
	visual_context.styles.apply_view_preferences(self)
	_update_controls()
	_queue_gameplay_layout()

func apply_interaction(state) -> void:
	_interaction = state.duplicate(true)
	_update_controls()

func present_frame(frame: Dictionary, index: int, total: int) -> void:
	if not is_instance_valid(detail_label): return
	detail_label.text = feedback.present(frame, index, total, presenter, actor_portraits)
	detail_label.tooltip_text = detail_label.text

func show_options(candidates: Array) -> void:
	_clear_choices()
	for candidate in candidates:
		var label := "%s  •  %s  •  %s" % [
			visual_context.translator.text("PLAY"), _cost_text(candidate), _preview(candidate)]
		var option := _button(label, func(): _play_candidate(candidate))
		option.clip_text = true
		option.tooltip_text = label
		choices.add_child(option)
	candidate_panel.visible = not candidates.is_empty()
	preload("res://scripts/ui/focus_navigation.gd").wire.call_deferred(self)

func refresh_cards() -> void:
	_sync_hand_models()
	if not selected_card.is_empty(): _show_preview(selected_card)

func reset_preview() -> void:
	_clear_choices()
	detail_label.text = visual_context.translator.text("Select a card, then choose a highlighted target.")
	detail_label.tooltip_text = detail_label.text

func target_at(point: Vector2) -> String:
	for id in target_buttons:
		if target_buttons[id].get_global_rect().has_point(point): return str(id)
	return ""

func contains_battlefield_point(point: Vector2) -> bool:
	return is_instance_valid(battlefield_view) and battlefield_view.get_global_rect().has_point(point)

func animate_card_use(id: String, target_ids: Array, still_present: bool) -> void:
	hand.clear_choice()
	reset_preview()
	var destination := size * .5 + global_position
	if not target_ids.is_empty() and actor_portraits.has(target_ids[0]):
		var portrait: Control = actor_portraits[target_ids[0]]
		destination = portrait.global_position + portrait.size * .5
	if card_buttons.has(id):
		if not still_present: card_buttons[id].modulate.a = 0
		await card_buttons[id].fly_to(destination, feedback.effects)

func dispose() -> void:
	if feedback: feedback.dispose()

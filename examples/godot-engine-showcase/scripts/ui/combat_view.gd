@tool
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
@export_file("*.json") var preview_document := "res://data/labs/ui_editor_preview.json"
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
var _attribute_snapshot: Dictionary = {}
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
var bottom_hud: HBoxContainer
var battlefield_actors: ScrollContainer
var floating_hud
var candidate_panel: PanelContainer
var _gameplay_layout_pending := false
var character_sidebar: PanelContainer
var character_sidebar_content: ScrollContainer
var character_sidebar_title: Label
var character_sidebar_expanded := false
var pile_controls
var right_pile_controls

var _footer_bound := false
static func create():
	return (load("res://scenes/ui/components/combat_view.tscn") as PackedScene).instantiate()
func _ready() -> void:
	_preview_if_isolated.call_deferred()
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
	bottom_hud = gameplay_column.get_node("CombatBottomHUD")
	bottom_hud.minimum_size_changed.connect(_queue_gameplay_layout)


func _ensure_floating_hud() -> void:
	if is_instance_valid(floating_hud): return
	floating_hud = get_node("FloatingCombatHUD")
	floating_hud.visible = is_visible_in_tree()


func _queue_gameplay_layout() -> void:
	if _gameplay_layout_pending: return
	_gameplay_layout_pending = true
	_layout_gameplay.call_deferred()


func _layout_gameplay() -> void:
	_gameplay_layout_pending = false
	if not is_inside_tree() or not is_instance_valid(bottom_hud): return
	var height := bottom_hud.get_combined_minimum_size().y
	var hand_min_height := hand_section.get_combined_minimum_size().y if is_instance_valid(hand_section) else 0.0
	var toolbar_height: float = hand.toolbar.get_combined_minimum_size().y if is_instance_valid(hand) and is_instance_valid(hand.toolbar) else 0.0
	var turn_controls_node: Control = bottom_hud.get_node_or_null("TurnControls")
	var turn_height: float = turn_controls_node.get_combined_minimum_size().y if is_instance_valid(turn_controls_node) else 0.0
	var stage_metrics: Dictionary = preload("res://scripts/presentation/combat_layout_metrics.gd").stage_layout(
		gameplay_column.size, height, hand_min_height, toolbar_height, turn_height, layout_profile)
	if is_instance_valid(hand_section):
		hand_section.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		hand_section.custom_minimum_size.x = 0.0
	bottom_hud.offset_top = float(stage_metrics.get("bottom_hud_top", -height))
	if is_instance_valid(floating_hud):
		floating_hud.fit_to(gameplay_column.get_global_rect(), visual_context.theme)
		if is_instance_valid(character_sidebar):
			character_sidebar.fit_bounds(gameplay_column.get_global_rect(), layout_profile.sidebar_width, layout_profile.sidebar_height)
		if is_instance_valid(candidate_panel):
			candidate_panel.offset_bottom = float(stage_metrics.get("candidate_bottom", 0.0))
			candidate_panel.offset_top = float(stage_metrics.get("candidate_top", 0.0))
			candidate_panel.offset_right = float(stage_metrics.get("candidate_right", layout_profile.candidate_width))
	if is_instance_valid(_feedback_column) and layout_profile.preview_placement == "floating_left":
		_feedback_column.set_anchors_and_offsets_preset(Control.PRESET_BOTTOM_LEFT)
		var preview_turn_height: float = turn_view.get_combined_minimum_size().y if is_instance_valid(turn_view) else turn_height
		var preview_metrics: Dictionary = preload("res://scripts/presentation/combat_layout_metrics.gd").stage_layout(
			gameplay_column.size, height, hand_min_height, toolbar_height, preview_turn_height, layout_profile)
		_feedback_column.offset_right = float(preview_metrics.get("floating_preview_right", layout_profile.preview_width))
		_feedback_column.offset_bottom = float(preview_metrics.get("floating_preview_bottom", 0.0))
		_feedback_column.offset_top = float(preview_metrics.get("floating_preview_top", 0.0))
	# The scenery extends behind the floating hand, but actor information stays
	# above even the raised cards. No rules or card count limits affect this layout.
	if is_instance_valid(battlefield_actors):
		battlefield_actors.offset_bottom = float(stage_metrics.get("battlefield_bottom", -height - layout_profile.safe_margin))
		var available := battlefield_actors.size.y
		if battlefield_actors.get_h_scroll_bar().visible: available -= battlefield_actors.get_h_scroll_bar().size.y
		for actor in battlefield_actors.find_children("ActorView_*", "PanelContainer", true, false):
			actor.fit_battlefield_height(available)


func _build_header() -> void:
	if not is_instance_valid(header_view):
		header_view = get_node("CombatHeader")
		header_view.name = "CombatHeader"
		header_view.timeline_requested.connect(func(): intent_requested.emit("timeline", []))
		header_view.tools_requested.connect(_open_zone_tools)
		header_view.pause_requested.connect(func(): intent_requested.emit("pause", []))
		move_child(header_view, 0)
		if is_instance_valid(header_view.tool_button): action_buttons.append(header_view.tool_button)
	var row = header_view
	row.visual_context = visual_context
	row.apply_model(presenter.header_model({"open_timeline": visual_context.action_label("open_timeline")}).to_dictionary())
	var support_label: Label = row.get_node("Spacer/SupportStatus")
	var support = _run_snapshot.get("supportCombat")
	support_label.visible = support is Dictionary
	if support is Dictionary:
		support_label.text = visual_context.translator.text("%s · %s activations remaining") % [visual_context.translator.content_name(str(support.get("supportId", ""))), str(int(support.get("remainingOwnerActivations", 0)))]
		support_label.tooltip_text = visual_context.translator.content_name(str(support.get("routineId", "")))
	if row.tool_button not in action_buttons: action_buttons.append(row.tool_button)


func _build_battlefield() -> void:
	if not is_instance_valid(battlefield_view):
		battlefield_view = gameplay_column.get_node("Battlefield")
		battlefield_view.target_selected.connect(_select_target)
		battlefield_view.target_previewed.connect(_show_target_preview)
		battlefield_view.preview_left.connect(func(): if not selected_card.is_empty(): _show_preview(selected_card))
		battlefield_view.layout_requested.connect(_queue_gameplay_layout)
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
	if not is_instance_valid(hand_section):
		hand_section = bottom_hud.get_node("HandSection")
		hint_label = hand_section.get_node("Hint")
	if not is_instance_valid(hand):
		hand = hand_section.get_node("CardHandView"); hand.mount(visual_context)
		hand.disable_overview(); hand.toolbar_external = true
		hand.counter.hide(); hand.toolbar.custom_minimum_size = layout_profile.toolbar_min_size
		hand.card_activated.connect(_choose_card)
		hand.card_previewed.connect(_show_preview)
		hand.card_hovered.connect(func(id): intent_requested.emit("card_hover", [id]))
		hand.card_hover_entered.connect(func(id): intent_requested.emit("card_hover_entered", [id]))
		hand.inspection_requested.connect(_inspect_instance)
		hand.selection_requested.connect(func(id):
			if _locked() or id not in card_buttons: return
			intent_requested.emit("select_card", [id]))
		hand.drop_requested.connect(_drop_card)
		hand.drag_previewed.connect(_preview_drag_targets)
		hand.drag_ended.connect(_restore_drag_targets)
		hand.reorder_drop_filter = _hand_drop_allowed
		hand.visual_order_changed.connect(func(_ids): intent_requested.emit("_cancel_selection", []))
	if not is_instance_valid(pile_controls):
		pile_controls = get_node("CombatBody/CombatGameplay/CombatBottomHUD/TurnControls/PileControls")
		pile_controls.zone_requested.connect(func(address): _inspect_pile("", str(address.zoneId), address))
	if not is_instance_valid(right_pile_controls):
		right_pile_controls = get_node("CombatBody/CombatGameplay/CombatBottomHUD/TurnControls/RightPileControls")
		right_pile_controls.name = "RightPileControls"
		right_pile_controls.zone_requested.connect(func(address): _inspect_pile("", str(address.zoneId), address))
	var zones: Array = zone_presenter.auxiliary_zones()
	var right: Array = zones.filter(func(zone): return str(zone.get("zoneId", "")) == "discard")
	pile_controls.apply_model(zones.filter(func(zone): return zone not in right), visual_context)
	right_pile_controls.apply_model(right, visual_context); right_pile_controls.visible = not right.is_empty()
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
	var pass_option := _visible_pass_candidate()
	var key: Array = [pass_option, visual_context.translator.locale, visual_context.action_label("end_turn"), visual_context.action_label("confirm_action")]
	if _footer_bound and key == _turn_key: return
	_turn_key = key.duplicate(true)
	if not _footer_bound:
		turn_view = bottom_hud.get_node("TurnControls")
		turn_view.alignment = BoxContainer.ALIGNMENT_END
		_feedback_column = get_node("CombatBody/CombatGameplay/CombatBottomHUD/TurnControls/ActionPreviewView")
		_feedback_column.visual_context = visual_context
		_feedback_column.mount(visual_context.translator.text("Select a card, then choose a highlighted target."))
		_feedback_column.hide()
		detail_label = _feedback_column.detail_label; selection_label = _feedback_column.selection_label; queue_label = _feedback_column.queue_label
		turn_view.next_requested.connect(_next_frame)
		turn_view.skip_requested.connect(func(): intent_requested.emit("skip_presentation", []))
		turn_view.cancel_requested.connect(_cancel_selection); turn_view.end_requested.connect(_end_turn)
		turn_view.system_requested.connect(_execute_system)
		hand.toolbar_external = true; hand.update_counter()
		reconnect_button = get_node("FloatingCombatHUD/FloatingHUDHost/Reconnect")
		reconnect_button.pressed.connect(_reconnect)
		preload("res://scripts/ui/combat_chrome.gd").apply(reconnect_button, "tools", visual_context.translator.text("RECONNECT"), visual_context)
		floating_hud.replace_slot("reconnect", reconnect_button)
		reconnect_button.set_anchors_and_offsets_preset(Control.PRESET_TOP_LEFT); reconnect_button.offset_right = layout_profile.reconnect_button_size.x; reconnect_button.offset_bottom = layout_profile.reconnect_button_size.y
		candidate_panel = get_node("FloatingCombatHUD/FloatingHUDHost/CandidatePanel")
		var surface: StyleBoxFlat = candidate_panel.get_theme_stylebox("panel").duplicate()
		surface.bg_color = visual_context.styles.PANEL; surface.border_color = visual_context.styles.GOLD
		candidate_panel.add_theme_stylebox_override("panel", surface)
		choices = candidate_panel.get_node("CandidateChoicesSlot/Choices")
		floating_hud.replace_slot("candidates", candidate_panel)
		candidate_panel.set_anchors_and_offsets_preset(Control.PRESET_BOTTOM_LEFT); candidate_panel.offset_right = layout_profile.candidate_width
		_footer_bound = true
	for button in turn_view.buttons: action_buttons.erase(button)
	turn_view.visual_context = visual_context
	turn_view.mount(visual_context.translator, {"confirm_action": visual_context.action_label("confirm_action"), "end_turn": visual_context.action_label("end_turn")}, pass_option)
	next_frame_button = turn_view.next_frame_button; selection_actions = turn_view.selection_actions
	action_buttons.append_array(turn_view.buttons); _apply_preview_composition()


func _build_character_sidebar() -> Control:
	_ensure_floating_hud()
	var view = character_sidebar
	if not is_instance_valid(view):
		view = get_node("FloatingCombatHUD/FloatingHUDHost/CombatAttributesDrawer")
		view.mount(visual_context)
		view.open_changed.connect(func(value): character_sidebar_expanded = value)
		floating_hud.replace_slot("sidebar", view)
	view.visual_context = visual_context
	view.present({"presentation": presentation, "run": _run_snapshot, "combat": _combat_snapshot,
		"actors": presenter.actors(), "input_actor": _input_actor, "zones": _zone_snapshot,
		"setting": _setting, "setting_id": _setting_id, "attributes": _attribute_snapshot}, visual_context)
	character_sidebar = view
	view.fit_bounds(gameplay_column.get_global_rect(), layout_profile.sidebar_width, layout_profile.sidebar_height)
	character_sidebar_content = view.content_scroll
	character_sidebar_title = view.title_label
	_apply_character_sidebar_state()
	return view


func toggle_character_sidebar() -> void:
	_toggle_character_sidebar()


func _toggle_character_sidebar() -> void:
	if is_instance_valid(character_sidebar): character_sidebar.set_open(not character_sidebar_expanded)

func _apply_character_sidebar_state() -> void:
	if is_instance_valid(character_sidebar): character_sidebar.set_open(character_sidebar_expanded)


func _input(event: InputEvent) -> void:
	if not is_inside_tree() or get_tree().paused or not is_visible_in_tree() or hand.reading_modal: return
	if event is InputEventMouseMotion and not hand.interaction.dragging and is_instance_valid(character_sidebar):
		character_sidebar.pointer_moved(event.position)
	if event.is_action_pressed("open_character") and not event.is_echo():
		var focused := get_viewport().gui_get_focus_owner()
		if focused is LineEdit or focused is TextEdit: return
		_toggle_character_sidebar()
		get_viewport().set_input_as_handled()
		return
	if character_sidebar_expanded and event.is_action_pressed("ui_cancel"):
		character_sidebar.set_open(false)
		get_viewport().set_input_as_handled()
		return
	# Consume cancel before the router handles pause; the first Escape cancels targeting.
	if not get_tree().paused and not hand.reading_modal and not selected_card.is_empty() and event.is_action_pressed("ui_cancel"):
		_cancel_selection()
		get_viewport().set_input_as_handled()


func _unhandled_input(event: InputEvent) -> void:
	if get_tree().paused or not is_visible_in_tree() or hand.reading_modal:
		return
	if event.is_action_pressed("inspect_card") and not event.is_echo():
		_inspect_instance(hand.active_card_id())
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

func _visible_pass_candidate() -> Dictionary:
	var hidden: Array = presentation.get("hidden_system_actions", {}).get(visual_context.setting_id, [])
	var actions: Array = ["PASS", "PASS_PRIORITY"].filter(func(action): return action not in hidden)
	return _system_candidate(actions) if not actions.is_empty() else {}


func _preview(candidate: Dictionary) -> String:
	return presenter.preview(candidate)


func _card_instance(instance_id: String) -> Dictionary:
	return presenter.card_instance(instance_id)


func _intent_for(actor_id: String) -> String:
	return presenter.intent_for(actor_id)


func _button(text: String, action: Callable, width := 0) -> Button:
	var value: Button = preload("res://scenes/ui/components/combat_button.tscn").instantiate()
	value.text = text; value.custom_minimum_size.x = width
	value.custom_minimum_size.y = layout_profile.choice_button_height
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


func clear_choices() -> void:
	_clear_choices()


func _clear_choices() -> void:
	if is_instance_valid(candidate_panel): candidate_panel.hide()
	for child in choices.get_children():
		choices.remove_child(child)
		child.queue_free()


func show_preview(id: String) -> void:
	_show_preview(id)


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
	preload("res://scripts/ui/focus_navigation.gd").queue(self)
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
	turn_view.skip_button.visible = _has_frames()
	turn_view.skip_button.disabled = bool(_interaction.get("busy", false)) or submitting
	selection_actions.visible = not selected_card.is_empty() and not _has_frames()
	turn_view.get_node("ContextActionSlot").visible = next_frame_button.visible or selection_actions.visible
	selection_label.text = visual_context.translator.text("PROCESSING…") if bool(_interaction.get("busy", false)) or submitting else (
		visual_context.translator.text("Choose a highlighted target. [Esc / Right-click to cancel]") if not selected_card.is_empty() else "")
	selection_label.tooltip_text = selection_label.text
	selection_label.add_theme_color_override("font_color", visual_context.styles.color("accent.primary" if not selected_card.is_empty() else "text.secondary"))
	if bool(_interaction.get("busy", false)) or submitting: queue_label.text = selection_label.text
	queue_label.tooltip_text = queue_label.text
	for id in card_buttons:
		var card: CardView = card_buttons[id]
		card.disabled = false # Still focusable for reading; activation is gated by the hand/screen.
		# Playability is explained in preview/inspection; preserve the face's semantic colors.
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
	var dialog := preload("res://scripts/ui/inspection_dialog.gd").create(visual_context)
	dialog.setup(visual_context.translator.text("Card inspection"), presenter.inspection_text(id), presenter.inspection_data(id), presenter.card_view_model(id), card_buttons[id].visual_style)
	dialog.tree_exiting.connect(func(): if id in card_buttons: card_buttons[id].grab_focus())
	add_child(dialog)
	hand.track_reading_modal(dialog)
	dialog.popup_centered()


func _inspect_pile(_label: String, zone: String, address: Dictionary = {}) -> void:
	if address.is_empty():
		for item in zone_presenter.auxiliary_zones():
			if str(item.get("zoneId", "")) == zone: address = preload("res://scripts/presentation/card_collection_presenter.gd").address(item); break
	if not address.is_empty(): intent_requested.emit("inspect_zone", [address])


func _open_zone_tools() -> void:
	if _locked(): return
	var flows: Array = CardZonePresenter.tool_flows(_run_snapshot)
	if flows.is_empty(): return
	var dialog := preload("res://scripts/ui/card_zone_tool_dialog.gd").create()
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
	bottom_hud.owner = null; bottom_hud.reparent(replacement, false)
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
	floating_hud.take_slot("preview")
	if _feedback_column.get_parent() != turn_view: _feedback_column.reparent(turn_view, false)
	_feedback_column.hide()


func _drop_card(id: String, point: Vector2) -> void:
	intent_requested.emit("_drop_card", [id, point])

func _preview_drag_targets(card_id: String, point: Vector2) -> void:
	if hand.contains_reorder_point(point): _restore_drag_targets(); return
	# Only decorate targets from published candidates; do not select or submit.
	var hovered := target_at(point)
	for id in target_buttons:
		var allowed := _candidates(card_id).any(func(candidate): return id in _targets(candidate))
		target_buttons[id].modulate = (visual_context.styles.GOLD if id == hovered else visual_context.styles.GOLD.lightened(.18)) if allowed else Color.WHITE

func _restore_drag_targets() -> void:
	for id in target_buttons:
		var allowed := not selected_card.is_empty() and _candidates(selected_card).any(func(candidate): return id in _targets(candidate))
		target_buttons[id].modulate = visual_context.styles.GOLD if allowed else Color.WHITE

func _hand_drop_allowed(point: Vector2) -> bool:
	for control in [header_view, pile_controls, right_pile_controls, turn_view, candidate_panel, character_sidebar, _feedback_column]:
		if is_instance_valid(control) and control is Control and control.is_visible_in_tree() and control.get_global_rect().has_point(point): return false
	return true

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

func hand_scope() -> String: return _scope
func _hand_scope() -> String: return _scope

func pile_buttons() -> Array[Button]:
	var result: Array[Button] = []
	for button in find_children("Pile_*", "Button", true, false):
		if button is Button: result.append(button)
	return result

func apply_projection(data: Dictionary, combat_presenter, cards_presenter, facts: Dictionary, context) -> void:
	mount(context)
	theme = context.theme
	presenter = combat_presenter
	zone_presenter = cards_presenter
	_run_snapshot = facts.run.duplicate(true)
	_combat_snapshot = facts.combat.duplicate(true)
	_action_snapshot = facts.actions.duplicate(true)
	_zone_snapshot = facts.zones.duplicate(true)
	_attribute_snapshot = facts.get("attributes", {}).duplicate(true)
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
	feedback = get_node("CombatFeedback"); feedback.mount(visual_context)
	_build_header()
	var body: HBoxContainer = get_node("CombatBody")
	gameplay_column = body.get_node("CombatGameplay")
	gameplay_column.resized.connect(_queue_gameplay_layout)
	_ensure_bottom_hud()
	_build_battlefield()
	_build_hand()
	_build_footer()
	_ensure_floating_hud()
	_build_character_sidebar()
	visual_context.styles.apply_view_preferences(self)
	_update_controls()
	_queue_gameplay_layout()
	if body.scene_file_path != composition_scene.resource_path:
		replace_composition.call_deferred(composition_scene, layout_profile)

func apply_interaction(state) -> void:
	_interaction = state.duplicate(true)
	_update_controls()

func present_frame(frame: Dictionary, index: int, total: int) -> void:
	if not is_instance_valid(detail_label): return
	detail_label.text = feedback.present(frame, index, total, presenter, actor_portraits)
	detail_label.tooltip_text = detail_label.text

func present_combat_snapshot(state: Dictionary) -> void:
	# This is an engine-published intermediate snapshot, never a reconstructed state.
	var projection = preload("res://scripts/presentation/combat_presenter.gd").new(_run_snapshot, state, [], visual_context.translator, presentation)
	var actors: Array = projection.combat_model(_input_actor).read("actors")
	battlefield_view.apply_model(actors, presentation)
	if is_instance_valid(scenery):
		scenery.apply_model(actors, str(scenery_model.get("backgroundId", "combat")), visual_context)
		scenery.bind_anchors(battlefield_view.targets)
	actor_portraits = scenery.portraits if is_instance_valid(scenery) else {}
	target_buttons = battlefield_view.targets

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
	preload("res://scripts/ui/focus_navigation.gd").queue(self)

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

func dispose() -> void:
	if feedback: feedback.dispose()

func _preview_if_isolated() -> void:
	if presenter == null and (Engine.is_editor_hint() or get_tree().current_scene == self): show_editor_preview()
func show_editor_preview() -> void:
	var data = JSON.parse_string(FileAccess.get_file_as_string(preview_document))
	if not data is Dictionary: return
	var context = ContextType.new()
	var projection = Presenter.new(data.run, data.combat, [], context.translator, data.presentation, data.zones)
	var zones = CardZonePresenter.new(data.zones, context.translator)
	get_node("CombatBody/CombatGameplay/Battlefield").embedded_artwork = true
	apply_projection(data.presentation, projection, zones, {"run": data.run, "combat": data.combat, "actions": [], "zones": data.zones, "input_actor": "hero", "scope": "editor-preview", "setting": {}, "setting_id": "prototype-test"}, context)

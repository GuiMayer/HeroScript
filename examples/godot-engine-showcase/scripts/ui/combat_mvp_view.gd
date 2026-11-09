@tool
extends "res://scripts/ui/combat_view.gd"
## Dedicated MVP composition; the shared controller retains all rule authority.
@export var combat_theme: Resource = preload("res://data/themes/mvp_combat_theme.tres")
@export_node_path("Label") var energy_indicator_path := NodePath("CanvasHost/ReferenceCanvas/EnergyIndicator")
@export var arena_gap := 20.0
@export var hand_bottom_inset := 8.0
@export var energy_hand_gap := 10.0
@export var battlefield_top_gap := 12.0
@export var battlefield_left_gap := 20.0
@export var battlefield_right_inset := 24.0
@export var turn_right_inset := 24.0
@export var turn_bottom_inset := 18.0
var energy_indicator: Label
var _log_entries: Array[String] = []
var _log_return_focus: WeakRef
var _drawer_return_focus: WeakRef

func apply_projection(data: Dictionary, combat_presenter, cards_presenter, facts: Dictionary, context) -> void:
	var local_context = context.derive(combat_theme)
	local_context.hand_arrangements = context.hand_arrangements
	super.apply_projection(data, combat_presenter, cards_presenter, facts, local_context)

func _build_header() -> void:
	_present_header(presenter, _combat_snapshot)

func _present_header(projection, combat_state: Dictionary) -> void:
	if not is_instance_valid(header_view):
		header_view = get_node(header_path)
		header_view.minimum_size_changed.connect(_queue_gameplay_layout)
	var active: String = str(combat_state.get("activation", {}).get("activeActorId", combat_state.get("activeActorId", "")))
	var identity: Dictionary = {}
	for actor in projection.actors():
		if str(actor.get("instanceId", "")) == (active if not active.is_empty() else _input_actor): identity = actor; break
	var support = _run_snapshot.get("supportCombat")
	var support_text := ""
	var support_hint := ""
	if support is Dictionary and not str(support.get("supportId", "")).is_empty() and (support.get("remainingOwnerActivations") is float or support.get("remainingOwnerActivations") is int):
		support_text = visual_context.translator.text("%s · %s activations remaining") % [visual_context.translator.content_name(str(support.get("supportId", ""))), str(int(support.remainingOwnerActivations))]
		support_hint = visual_context.translator.content_name(str(support.get("routineId", "")))
	var model: Dictionary = projection.header_model({}).to_dictionary()
	header_view.apply_model({"actorName": projection.actor_name(str(identity.get("instanceId", ""))),
		"actorDefinitionId": str(identity.get("definitionId", "")), "round": model.round,
		"turn": visual_context.translator.text("Turn: %s") % projection.actor_name(active) if not active.is_empty() else "",
		"support": support_text, "supportHint": support_hint, "log": visual_context.translator.text("Combat log"),
		"pause": "%s [%s]" % [str(model.pause), visual_context.action_label("pause_game")],
		"inspect": "%s [%s]" % [visual_context.translator.text("Inspect card"), visual_context.action_label("inspect_card")]}, visual_context)
	_refresh_header_inspection()

func _refresh_header_inspection(_id := "") -> void:
	if not is_instance_valid(header_view): return
	var id: String = hand.active_card_id() if is_instance_valid(hand) else ""
	header_view.set_inspection_available(not id.is_empty() and hand.is_card_pickable(id))

func _on_header_pause_requested() -> void: intent_requested.emit("pause", [])
func _on_header_inspection_requested() -> void:
	if is_instance_valid(hand) and not hand.reading_modal and not hand.interaction.dragging:
		_inspect_instance(hand.active_card_id())

func _update_controls() -> void:
	super._update_controls()
	_refresh_header_inspection()
	_sync_turn_dock_visibility()

func _sync_turn_dock_visibility() -> void:
	if not is_instance_valid(hand) or not is_instance_valid(hand.toolbar): return
	var has_visible_tool := false
	for child in hand.toolbar.get_children():
		if child is Control and child.visible:
			has_visible_tool = true
			break
	hand.toolbar.visible = has_visible_tool
	if is_instance_valid(turn_view):
		var gap: Control = turn_view.get_node_or_null("EndTurnGap")
		if is_instance_valid(gap):
			gap.visible = has_visible_tool or turn_view.get_node("ContextActionSlot").visible or turn_view.skip_button.visible or turn_view.get_node("SystemActionSlot").visible
		_layout_turn_controls()

func _build_piles() -> void:
	if not is_instance_valid(pile_controls):
		pile_controls = get_node(primary_piles_path)
		pile_controls.zone_requested.connect(func(address): _inspect_pile("", str(address.zoneId), address))
	var dock: Node = get_node_or_null(turn_controls_path)
	if is_instance_valid(dock):
		for legacy in ["PileControls", "RightPileControls"]:
			var node: Control = dock.get_node_or_null(legacy)
			if is_instance_valid(node): node.hide()
	var raw: Array = zone_presenter.auxiliary_zones()
	var ordered: Array = []
	for rank in 5:
		for zone in raw:
			if _pile_rank(zone) == rank: ordered.append(zone)
	pile_controls.apply_model(ordered, visual_context)
	pile_controls.visible = not ordered.is_empty()

static func _pile_rank(zone: Dictionary) -> int:
	var presentation: Dictionary = zone.get("presentation", {}) if zone.get("presentation") is Dictionary else {}
	var slot := str(presentation.get("slot", ""))
	var id := str(zone.get("zoneId", ""))
	if slot == "draw_pile" or id in ["draw", "deck"]: return 0
	if slot == "exile_pile" or id in ["exile", "banish"]: return 1
	if slot == "discard_pile" or id == "discard": return 2
	if slot == "owned_deck" or id == "owned": return 3
	return 4

func _input(event: InputEvent) -> void:
	if not is_inside_tree() or get_tree().paused or not is_visible_in_tree() or hand.reading_modal: return
	if event is InputEventMouseMotion and is_instance_valid(pile_controls) and pile_controls.is_visible_in_tree():
		if is_instance_valid(character_sidebar) and not character_sidebar.expanded and event.position.y >= pile_controls.global_position.y - 12.0:
			return
	super._input(event)

func _build_hand() -> void:
	super._build_hand()
	# The same inspection action is now in the header, not duplicated in the dock.
	hand.toolbar.get_node("HandInspectButton").hide()
	if not hand.card_previewed.is_connected(_refresh_header_inspection): hand.card_previewed.connect(_refresh_header_inspection)
	if not hand.card_hovered.is_connected(_refresh_header_inspection): hand.card_hovered.connect(_refresh_header_inspection)
	_refresh_header_inspection()
	_present_energy(_combat_snapshot)
	_sync_turn_dock_visibility()

func _present_energy(combat_state: Dictionary) -> void:
	if not is_instance_valid(energy_indicator):
		energy_indicator = get_node(energy_indicator_path)
		energy_indicator.minimum_size_changed.connect(_queue_gameplay_layout)
	energy_indicator.text = ""
	energy_indicator.tooltip_text = ""
	for actor in combat_state.get("actors", []):
		if str(actor.get("instanceId", "")) != _input_actor: continue
		var resources: Dictionary = actor.get("resources", {})
		if resources.get("energy") is Dictionary:
			var value: Dictionary = resources.energy
			var current_text: String = visual_context.translator.number(float(value.get("current", 0)))
			var name_text: String = visual_context.translator.content_name("energy")
			energy_indicator.text = "◇  %s  %s" % [name_text, current_text]
			if value.get("maximum") is float or value.get("maximum") is int:
				energy_indicator.text += " / " + visual_context.translator.number(float(value.maximum))
			energy_indicator.tooltip_text = energy_indicator.text.trim_prefix("◇  ")
		break
	energy_indicator.visible = not energy_indicator.text.is_empty()

func _layout_turn_controls() -> void:
	if not is_instance_valid(turn_view) or not is_instance_valid(gameplay_column): return
	var min_size: Vector2 = turn_view.get_combined_minimum_size()
	var inset_x := minf(turn_right_inset, maxf(10.0, gameplay_column.size.x * 0.0125))
	var inset_y := minf(turn_bottom_inset, maxf(8.0, gameplay_column.size.y * 0.016))
	if is_instance_valid(bottom_hud):
		var available_right := gameplay_column.size.x * (1.0 - bottom_hud.anchor_right)
		if min_size.x + inset_x + 4.0 > available_right:
			inset_x = maxf(4.0, available_right - min_size.x - 4.0)
	turn_view.offset_right = -inset_x
	turn_view.offset_bottom = -inset_y
	turn_view.offset_left = -min_size.x - inset_x
	turn_view.offset_top = -min_size.y - inset_y

func _bind_composition() -> Control:
	gameplay_column = get_node("CanvasHost/ReferenceCanvas")
	get_node("CanvasHost").resized.connect(_queue_gameplay_layout)
	gameplay_column.resized.connect(_queue_gameplay_layout)
	return null # This authored composition has no replaceable legacy body.

func _layout_gameplay() -> void:
	_gameplay_layout_pending = false
	if not is_inside_tree() or not is_instance_valid(gameplay_column): return
	var available: Vector2 = get_node("CanvasHost").size
	var fitted := Vector2(minf(available.x, available.y * 16.0 / 9.0), 0)
	fitted.y = fitted.x * 9.0 / 16.0
	# Godot already uniformly scales the application's canvas_items viewport.
	# Fit only the composition bounds here, avoiding a second scale on cards/input.
	gameplay_column.size = fitted
	gameplay_column.position = (available - fitted) * .5
	if not is_instance_valid(bottom_hud) or not is_instance_valid(hand_section): return
	var height: float = hand_section.get_combined_minimum_size().y
	bottom_hud.offset_top = -height - hand_bottom_inset
	bottom_hud.offset_bottom = -hand_bottom_inset
	var first_card_top := fitted.y + bottom_hud.offset_top
	if is_instance_valid(hand) and is_instance_valid(hand.scroll):
		first_card_top = (hand.scroll.global_position.y - gameplay_column.global_position.y) + hand.visual_style.padding
	if is_instance_valid(header_view):
		header_view.offset_left = 0.0
		header_view.offset_right = 0.0
		header_view.offset_top = 0.0
		header_view.offset_bottom = header_view.get_combined_minimum_size().y
	var min_stage_height := 180.0
	if is_instance_valid(battlefield_actors):
		for actor in battlefield_actors.find_children("ActorView_*", "PanelContainer", true, false):
			if actor.has_method("minimum_battlefield_height"):
				min_stage_height = maxf(min_stage_height, actor.minimum_battlefield_height())
	var energy_top := bottom_hud.offset_top
	if is_instance_valid(energy_indicator):
		var energy_height := maxf(energy_indicator.get_combined_minimum_size().y, 26.0)
		var gap := 2.0 if fitted.y < 860.0 else energy_hand_gap
		var energy_base := bottom_hud.offset_top
		if fitted.y < 860.0:
			energy_base = maxf(bottom_hud.offset_top, first_card_top - fitted.y + 4.0)
		energy_indicator.offset_bottom = energy_base - gap
		energy_indicator.offset_top = energy_indicator.offset_bottom - energy_height
		if energy_indicator.visible:
			energy_top = energy_indicator.offset_top
	var rail_right := 18.0
	if is_instance_valid(pile_controls):
		var rail_width := maxf(pile_controls.get_combined_minimum_size().x, 148.0)
		pile_controls.offset_left = 18.0
		pile_controls.offset_right = maxf(0.0, rail_width + 18.0 - fitted.x * pile_controls.anchor_right)
		pile_controls.offset_top = maxf(0.0, header_view.offset_bottom + battlefield_top_gap - fitted.y * pile_controls.anchor_top)
		pile_controls.offset_bottom = -hand_bottom_inset + fitted.y * (1.0 - pile_controls.anchor_bottom)
		if pile_controls.visible:
			rail_right = maxf(fitted.x * pile_controls.anchor_right + pile_controls.offset_right, 18.0 + rail_width)
	if is_instance_valid(battlefield_actors):
		var top_gap := 0.0 if fitted.y < 860.0 else battlefield_top_gap
		battlefield_actors.offset_left = rail_right + battlefield_left_gap
		battlefield_actors.offset_right = -battlefield_right_inset
		battlefield_actors.offset_top = header_view.offset_bottom + top_gap
		var bottom_limit := minf(energy_top - arena_gap, first_card_top - fitted.y - 8.0)
		var hard_ceiling := first_card_top - fitted.y - 2.0
		if fitted.y + bottom_limit - battlefield_actors.offset_top < min_stage_height:
			bottom_limit = minf(hard_ceiling, battlefield_actors.offset_top + min_stage_height - fitted.y)
		battlefield_actors.offset_bottom = bottom_limit
		var available_height := fitted.y + battlefield_actors.offset_bottom - battlefield_actors.offset_top
		if battlefield_actors.get_h_scroll_bar().visible: available_height -= battlefield_actors.get_h_scroll_bar().size.y
		for actor in battlefield_actors.find_children("ActorView_*", "PanelContainer", true, false):
			actor.fit_battlefield_height(available_height)
	_sync_turn_dock_visibility()
	if is_instance_valid(floating_hud):
		floating_hud.fit_to(gameplay_column.get_global_rect(), visual_context.theme)
		if is_instance_valid(character_sidebar):
			character_sidebar.fit_bounds(gameplay_column.get_global_rect(), layout_profile.sidebar_width, layout_profile.sidebar_height)
		if is_instance_valid(reconnect_button):
			var rec_width := maxf(layout_profile.reconnect_button_size.x, reconnect_button.get_combined_minimum_size().x)
			var rec_height := maxf(layout_profile.reconnect_button_size.y, reconnect_button.get_combined_minimum_size().y)
			var rec_top: float = (float(header_view.offset_bottom) if is_instance_valid(header_view) else 80.0) + 8.0
			reconnect_button.set_anchors_and_offsets_preset(Control.PRESET_TOP_LEFT)
			reconnect_button.offset_left = maxf(rail_right + battlefield_left_gap, fitted.x - battlefield_right_inset - rec_width)
			reconnect_button.offset_right = reconnect_button.offset_left + rec_width
			reconnect_button.offset_top = rec_top
			reconnect_button.offset_bottom = rec_top + rec_height
		if is_instance_valid(candidate_panel):
			var cand_left_min: float = rail_right + battlefield_left_gap
			var cand_right_max: float = fitted.x * bottom_hud.anchor_right if is_instance_valid(bottom_hud) else (fitted.x - 220.0)
			var cand_avail: float = maxf(240.0, cand_right_max - cand_left_min)
			var cand_width: float = minf(layout_profile.candidate_width, cand_avail)
			var cand_left: float = clampf((fitted.x - cand_width) * 0.5, cand_left_min, maxf(cand_left_min, cand_right_max - cand_width))
			var cand_bottom: float = minf(energy_top - 8.0, first_card_top - fitted.y - 8.0)
			candidate_panel.set_anchors_and_offsets_preset(Control.PRESET_BOTTOM_LEFT)
			candidate_panel.offset_left = cand_left
			candidate_panel.offset_right = cand_left + cand_width
			candidate_panel.offset_bottom = cand_bottom
			candidate_panel.offset_top = cand_bottom - layout_profile.candidate_height

func _on_sidebar_open_changed(value: bool) -> void:
	super._on_sidebar_open_changed(value)
	var viewport := get_viewport()
	if viewport == null: return
	if value:
		var focused := viewport.gui_get_focus_owner()
		if is_instance_valid(focused) and (not is_instance_valid(character_sidebar) or not character_sidebar.is_ancestor_of(focused)):
			_drawer_return_focus = weakref(focused)
	else:
		var target: Control = _drawer_return_focus.get_ref() if _drawer_return_focus != null else null
		_drawer_return_focus = null
		var current_focus := viewport.gui_get_focus_owner()
		if current_focus == null and is_instance_valid(target) and target.is_inside_tree() and target.is_visible_in_tree():
			target.grab_focus()

func present_combat_snapshot(state: Dictionary) -> void:
	super.present_combat_snapshot(state)
	var projection = Presenter.new(_run_snapshot, state, [], visual_context.translator, presentation)
	_present_header(projection, state)
	_present_energy(state)

func present_frame(frame: Dictionary, index: int, total: int) -> void:
	super.present_frame(frame, index, total)
	if is_instance_valid(detail_label) and not detail_label.text.is_empty():
		_log_entries.append(detail_label.text)
		if _log_entries.size() > 200: _log_entries.pop_front()

func _open_combat_log() -> void:
	if not is_inside_tree() or get_tree().paused or (is_instance_valid(hand) and hand.reading_modal): return
	var dialog: AcceptDialog = get_node("CombatLog")
	var viewport := get_viewport()
	var current_focus := viewport.gui_get_focus_owner() if viewport != null else null
	if is_instance_valid(current_focus):
		_log_return_focus = weakref(current_focus)
	elif is_instance_valid(header_view) and is_instance_valid(header_view.log_button):
		_log_return_focus = weakref(header_view.log_button)
	else:
		_log_return_focus = null
	if not dialog.has_meta("mvp_log_bound"):
		dialog.set_meta("mvp_log_bound", true)
		dialog.visibility_changed.connect(func():
			if not dialog.visible: _restore_combat_log_focus.call_deferred())
	dialog.theme = visual_context.theme
	dialog.title = visual_context.translator.text("Combat log")
	dialog.ok_button_text = visual_context.translator.text("CLOSE")
	dialog.get_node("Scroll/Events").text = "\n\n".join(_log_entries) if not _log_entries.is_empty() else visual_context.translator.text("No events recorded in this view.")
	if is_instance_valid(hand): hand.track_reading_modal(dialog)
	dialog.popup_centered_clamped(Vector2i(700, 480), .9)

func _restore_combat_log_focus() -> void:
	if not is_inside_tree() or get_tree().paused: return
	var target: Control = _log_return_focus.get_ref() if _log_return_focus != null else null
	_log_return_focus = null
	if is_instance_valid(target) and target.is_inside_tree() and target.is_visible_in_tree():
		target.grab_focus()
	elif is_instance_valid(header_view) and is_instance_valid(header_view.log_button) and header_view.log_button.is_inside_tree():
		header_view.log_button.grab_focus()

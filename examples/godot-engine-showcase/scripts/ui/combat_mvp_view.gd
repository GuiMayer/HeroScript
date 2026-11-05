@tool
extends "res://scripts/ui/combat_view.gd"
## Dedicated MVP composition; the shared controller retains all rule authority.
@export var combat_theme: Resource = preload("res://data/themes/mvp_combat_theme.tres")
@export var arena_gap := 20.0
@export var hand_bottom_inset := 8.0
@export var battlefield_top_gap := 12.0
var _log_entries: Array[String] = []

func apply_projection(data: Dictionary, combat_presenter, cards_presenter, facts: Dictionary, context) -> void:
	var local_context = context.derive(combat_theme)
	local_context.hand_arrangements = context.hand_arrangements
	super.apply_projection(data, combat_presenter, cards_presenter, facts, local_context)

func _build_header() -> void:
	super._build_header()
	header_view.get_node("HeroName").text = presenter.actor_name(_input_actor)
	header_view.get_node("TimelineButton").hide()
	var active: String = str(_combat_snapshot.get("activation", {}).get("activeActorId", _combat_snapshot.get("activeActorId", "")))
	header_view.get_node("ActiveTurn").text = visual_context.translator.text("Turn: %s") % presenter.actor_name(active) if not active.is_empty() else ""
	var button: Button = header_view.get_node("LogButton")
	button.text = visual_context.translator.text("Combat log")

func _build_hand() -> void:
	super._build_hand()
	var energy: Label = hand_section.get_node("Energy")
	energy.text = ""
	for actor in _combat_snapshot.get("actors", []):
		if str(actor.get("instanceId", "")) != _input_actor: continue
		var resources: Dictionary = actor.get("resources", {})
		if resources.get("energy") is Dictionary:
			var value: Dictionary = resources.energy
			energy.text = "◇  %s  %s" % [visual_context.translator.content_name("energy"), visual_context.translator.number(value.get("current", 0))]
			if value.get("maximum") is float or value.get("maximum") is int: energy.text += " / " + visual_context.translator.number(value.maximum)
	energy.visible = not energy.text.is_empty()

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
	if is_instance_valid(header_view):
		header_view.position = Vector2.ZERO
		header_view.size = Vector2(fitted.x, header_view.get_combined_minimum_size().y)
	if is_instance_valid(battlefield_actors):
		battlefield_actors.offset_top = header_view.size.y + battlefield_top_gap
		battlefield_actors.offset_bottom = bottom_hud.offset_top - arena_gap
		for actor in battlefield_actors.find_children("ActorView_*", "PanelContainer", true, false):
			actor.fit_battlefield_height(battlefield_actors.size.y)
	if is_instance_valid(turn_view):
		turn_view.offset_left = -turn_view.get_combined_minimum_size().x
		turn_view.offset_top = -turn_view.get_combined_minimum_size().y
	if is_instance_valid(floating_hud):
		floating_hud.fit_to(gameplay_column.get_global_rect(), visual_context.theme)
		if is_instance_valid(character_sidebar):
			character_sidebar.fit_bounds(gameplay_column.get_global_rect(), layout_profile.sidebar_width, layout_profile.sidebar_height)
		if is_instance_valid(candidate_panel):
			candidate_panel.offset_bottom = -height
			candidate_panel.offset_top = -height - layout_profile.candidate_height
			candidate_panel.offset_right = layout_profile.candidate_width

func present_frame(frame: Dictionary, index: int, total: int) -> void:
	super.present_frame(frame, index, total)
	if is_instance_valid(detail_label) and not detail_label.text.is_empty():
		_log_entries.append(detail_label.text)
		if _log_entries.size() > 200: _log_entries.pop_front()

func _open_combat_log() -> void:
	var dialog: AcceptDialog = get_node("CombatLog")
	dialog.theme = visual_context.theme
	dialog.title = visual_context.translator.text("Combat log")
	dialog.get_node("Scroll/Events").text = "\n\n".join(_log_entries) if not _log_entries.is_empty() else visual_context.translator.text("No events recorded in this view.")
	dialog.popup_centered_clamped(Vector2i(700, 480), .9)

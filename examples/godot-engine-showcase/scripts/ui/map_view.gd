@tool
extends "res://scripts/ui/screen_view.gd"
## Read-only map inspection and explicit semantic actions. No rule evaluation.
signal intent_requested(action: String, arguments: Array)
const MapLayout = preload("res://scripts/presentation/map_layout.gd")
@export_file("*.json") var preview_document := "res://data/labs/ui_editor_preview.json"
var route
var _scroll: ScrollContainer
var _inspector: VBoxContainer
var _body: Control
var _header: Label
var _selected := ""
var _run: Dictionary
var _choices: Array
var _presenter
var _model_key: Array = []
var _locked := false
var _action_buttons: Array[Button] = []
var _inspector_key: Array = []
var _resource_badges: Label
var camera = preload("res://scripts/ui/map_camera.gd").new()
var navigation = preload("res://scripts/presentation/map_navigation_model.gd").new()
var _canvas: Control
var _framed := false
var _inspector_panel: PanelContainer
var _inspector_toggle: Button
var _inspector_open := false
var _last_current := ""
var _resume_button: Button
var _compartments_button: Button

static func create():
	return (load("res://scenes/ui/components/map_view.tscn") as PackedScene).instantiate()
func _ready() -> void:
	_preview_if_isolated.call_deferred()
	if visual_context == null:
		mount(ContextType.new()); theme = visual_context.theme
	if not is_instance_valid(route): _build()
func set_compartments_available(value: bool) -> void:
	# The unified page is available for inspection even when this module is disabled.
	pass
var _camera_tween: Tween
var _camera_target := Vector2.INF
var _camera_restore_ticket := 0
var _camera_restore_from := ""
func apply_projection(run: Dictionary, choices: Array, presenter, context: ContextType) -> void:
	mount(context); theme = context.theme
	if context.reduced_motion and _camera_tween != null and _camera_tween.is_valid():
		_stop_camera_motion(true)
	_run = run.duplicate(true); _choices = choices.duplicate(true); _presenter = presenter
	var current := str(run.get("currentNodeId", ""))
	var progressed := not _last_current.is_empty() and _last_current != current
	_last_current = current
	if not is_instance_valid(route): _build()
	_header.text = context.translator.content_name(str(run.get("runDefinitionId", "")), str(run.get("metadata", {}).get("displayName", context.translator.text("JOURNEY"))))
	var counters: Array[String] = []
	for resource_id in run.get("resources", {}):
		counters.append("%s %s" % [context.translator.number(float(run.resources[resource_id].get("current", 0))), context.translator.content_name(str(resource_id))])
	for resource_id in run.get("characterResources", {}).get("resources", {}):
		var value: Dictionary = run.characterResources.resources[resource_id]
		counters.append("%s %s" % [context.translator.number(float(value.get("current", 0))), context.translator.content_name(str(resource_id))])
	_resource_badges.text = "  ·  ".join(counters)
	var nodes: Array = presenter.route()
	# Optional published display metadata, never inferred from prose or effects.
	var metadata: Dictionary = {}
	for source in run.get("map", {}).get("nodes", []): metadata[str(source.get("nodeId", ""))] = source.get("metadata", {})
	for node in nodes:
		var display: Dictionary = metadata.get(str(node.id), {})
		node["benefit"] = str(display.get("reward", ""))
	navigation.update(nodes)
	var key: Array = [nodes, choices, context.presentation_key()]
	var geometry_changed := false
	if key != _model_key:
		_model_key = key.duplicate(true)
		route.visual_context = context
		var previous_geometry: Dictionary = route.geometry_snapshot()
		route.setup(nodes)
		var next_geometry: Dictionary = route.geometry_snapshot()
		geometry_changed = next_geometry.get("rects", {}) != previous_geometry.get("rects", {}) or next_geometry.get("bounds", Vector2.ZERO) != previous_geometry.get("bounds", Vector2.ZERO)
		_update_extent()
	if progressed or not nodes.any(func(node): return node.id == _selected): _selected = current
	if _selected.is_empty() and not nodes.is_empty(): _selected = str(nodes[0].id)
	_inspect(_selected)
	context.styles.apply_view_preferences(self)
	# A compact combat projection can expand without changing currentNodeId.
	# Reframe actual geometry changes, not ordinary choice/resource refreshes,
	# so intentional panning and floating inspector interactions remain stable.
	if not _framed or progressed or geometry_changed:
		_framed = true
		if progressed:
			_camera_restore_ticket += 1
			_camera_restore_from = ""
		_center_current.call_deferred(progressed)
func _build() -> void:
	get_node("Header/CenterCurrent").text = visual_context.translator.text("Center")
	get_node("Header/CenterCurrent").pressed.connect(func(): cancel_gesture(); _center_current(true))
	_header = get_node("Header/MapTitle"); _resource_badges = get_node("MapResources")
	_compartments_button = get_node("Header/UnifiedLoadoutButton")
	_compartments_button.text = visual_context.translator.text("LOADOUT")
	_compartments_button.pressed.connect(func(): intent_requested.emit("loadout", []))
	_body = get_node("MapWorkspace"); _scroll = _body.get_node("MapScroll")
	_canvas = _scroll.get_node("MapCanvas"); route = _canvas.get_node("RouteView")
	route.inspection_requested.connect(func(id): _inspect(id); _set_inspector_open(true))
	route.focus_requested.connect(func(id):
		navigation.focused = id
		if not camera.pressed: _inspect(id); _reveal_node(id))
	route.hover_changed.connect(func(id): navigation.hovered = id; route.set_hover(id))
	_scroll.resized.connect(_resize_camera)
	_inspector_panel = _body.get_node("MapFloatingInspector")
	var surface: StyleBoxFlat = _inspector_panel.get_theme_stylebox("panel").duplicate()
	surface.bg_color = visual_context.styles.color("surface.route_current"); surface.border_color = visual_context.styles.color("text.primary")
	_inspector_panel.add_theme_stylebox_override("panel", surface)
	_inspector = _inspector_panel.get_node("Margin/Stack/Scroll/Body")
	_resume_button = _inspector_panel.get_node("Margin/Stack/MapResumeRun")
	_resume_button.pressed.connect(func(): intent_requested.emit("resume_run", []))
	get_node("MapWorkspace/MapFloatingInspector/Margin/Stack/Actions/MapResumeDialogue").pressed.connect(func(): intent_requested.emit("resume_dialogue", []))
	_inspector_toggle = _body.get_node("MapInspectorToggle")
	_inspector_toggle.pressed.connect(func(): _set_inspector_open(not _inspector_open))
	_body.resized.connect(_layout_inspector); _layout_inspector.call_deferred()

func _set_inspector_open(value: bool) -> void:
	if _inspector_open == value: return
	_inspector_open = value; _layout_inspector()
	if is_inside_tree() and not value and route.buttons.has(_selected): route.buttons[_selected].grab_focus()

func _layout_inspector() -> void:
	if not is_instance_valid(_inspector_panel): return
	_inspector_panel.visible = _inspector_open
	_inspector_toggle.text = "›" if _inspector_open else "‹"
	var profile = route.layout_profile if is_instance_valid(route) and route.layout_profile != null else preload("res://scripts/ui/styles/map_layout_profile.gd").new()
	var metrics := MapLayout.inspector_geometry(_body.size, profile)
	_inspector_panel.position = metrics.panel_position
	_inspector_panel.size = metrics.panel_size
	_inspector_toggle.position = metrics.toggle_position
	_inspector_toggle.size = metrics.toggle_size
	_update_extent()

func _usable_size() -> Vector2: return _scroll.size

func set_resume_target(target: String) -> void:
	_resume_button.visible = target in ["activity", "combat", "dialogue"]
	var label := "Resume conversation" if target == "dialogue" else ("Return to combat" if target == "combat" else "Return to activity")
	_resume_button.text = visual_context.translator.text(label)

func _inspect(id: String) -> void:
	_selected = id
	navigation.inspect(id)
	route.set_selection(id)
	var projection: Array = [id, route.nodes, _choices, _run.get("dialogues", []), visual_context.presentation_key()]
	if projection == _inspector_key: return
	_inspector_key = projection.duplicate(true)
	# Screens are projected before the router attaches them to the scene tree.
	# Focus is optional during that construction phase, not a data dependency.
	var viewport := get_viewport() if is_inside_tree() else null
	var old_focus: Control = viewport.gui_get_focus_owner() if viewport != null else null
	var focus_name := str(old_focus.name) if old_focus != null and _inspector.is_ancestor_of(old_focus) else ""
	var actions = get_node("MapWorkspace/MapFloatingInspector/Margin/Stack/Actions")
	for child in actions.get_children():
		if child.has_meta("map_action"): actions.remove_child(child); child.queue_free()
	var information = _inspector.get_node("Information")
	information.hide(); actions.hide(); actions.get_node("MapResumeDialogue").hide()
	_action_buttons.clear()
	var matches: Array = route.nodes.filter(func(node): return str(node.id) == id)
	if matches.is_empty(): return
	var node: Dictionary = matches[0]
	information.show()
	information.get_node("Title").text = str(node.name)
	information.get_node("Kind").text = visual_context.translator.content_name(str(node.kind))
	information.get_node("Risk").text = visual_context.translator.text("Risk: %s") % str(node.get("risk", ""))
	information.get_node("Risk").visible = not str(node.get("risk", "")).is_empty()
	information.get_node("Benefit").text = str(node.get("benefit", ""))
	information.get_node("Benefit").visible = not str(node.get("benefit", "")).is_empty()
	information.get_node("Description").text = str(node.get("description", ""))
	var statuses: Array[String] = []
	if node.current: statuses.append(visual_context.translator.text("YOU ARE HERE"))
	if node.resolved: statuses.append(visual_context.translator.text("COMPLETED"))
	if statuses.is_empty(): statuses.append(visual_context.translator.text("Available" if not node.travel.is_empty() else "Unavailable"))
	information.get_node("Status").text = " · ".join(statuses)
	if not node.travel.is_empty(): _action(visual_context.translator.text("TRAVEL"), node.travel)
	if node.current:
		for choice in _choices:
			var type := str(choice.get("type", ""))
			if type in ["ADVANCE_NODE", "ABANDON_RUN"]: continue
			if type == "CHOOSE_DIALOGUE_OPTION": continue
			_action(_presenter.choice_label(choice), choice)
		var active_dialogue: bool = _run.get("dialogues", []).any(func(item): return str(item.get("activityNodeId", "")) == id and not bool(item.get("completed", false)))
		if str(node.kind) == "Dialogue" and not node.resolved and active_dialogue:
			actions.get_node("MapResumeDialogue").text = visual_context.translator.text("Resume conversation")
			actions.get_node("MapResumeDialogue").show()
	actions.visible = not _action_buttons.is_empty() or actions.get_node("MapResumeDialogue").visible
	visual_context.styles.apply_view_preferences(_inspector)
	if not focus_name.is_empty():
		var replacement := _inspector.find_child(focus_name, true, false)
		if replacement is Control: replacement.grab_focus.call_deferred()
func _action(label: String, choice: Dictionary) -> void:
	var reference: String = _presenter.option_reference(choice)
	var button: Button = preload("res://scenes/ui/components/map_action.tscn").instantiate()
	button.text = label
	button.name = "MapAction_" + str(choice.get("type", ""))
	button.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	button.disabled = _locked
	button.pressed.connect(func(): intent_requested.emit("command", [reference]))
	var actions = get_node("MapWorkspace/MapFloatingInspector/Margin/Stack/Actions")
	actions.add_child(button); actions.move_child(button, _action_buttons.size()); _action_buttons.append(button)
func set_read_only(value: bool) -> void:
	_locked = value
	for button in _action_buttons:
		if is_instance_valid(button): button.disabled = value
func capture_local_state() -> Dictionary:
	return {"version": 3, "selected": _selected, "current": navigation.current, "inspector_open": _inspector_open}
func restore_local_state(state: Dictionary) -> void:
	_camera_restore_ticket += 1
	var previous := str(state.get("current", navigation.current))
	_camera_restore_from = previous if previous != navigation.current and route.buttons.has(previous) else ""
	if not state.is_empty():
		_inspector_open = bool(state.get("inspector_open", false)); _layout_inspector()
		var id := str(state.get("selected", navigation.current))
		if str(state.get("current", navigation.current)) != navigation.current: id = navigation.current
		if route.buttons.has(id): _inspect(id)
	if previous != navigation.current and route.buttons.has(previous):
		_restore_progress_frame.call_deferred(previous, navigation.current, _camera_restore_ticket)
	else: _center_current.call_deferred()

func _restore_progress_frame(previous: String, current: String, ticket: int) -> void:
	if not is_inside_tree(): return
	# A recreated map has no old scroll offset. Use its previous current-node
	# anchor after containers settle, then interpolate to the new current node.
	await get_tree().process_frame
	await get_tree().process_frame
	if not is_inside_tree() or ticket != _camera_restore_ticket or current != navigation.current: return
	if not route.buttons.has(previous) or not route.buttons.has(current): return
	_update_extent()
	_camera_restore_from = ""
	_scroll_to(route.node_button_center(previous))
	_scroll_to(route.node_button_center(current), true)

func _update_extent() -> void:
	if not is_instance_valid(_canvas): return
	# Symmetric camera gutters allow even first/last nodes to occupy screen center.
	# Inspector visibility never participates in map extent, scale or framing.
	var extent := MapLayout.canvas_extent(route.custom_minimum_size, _scroll.size)
	route.position = extent.route_position
	_canvas.custom_minimum_size = extent.canvas_minimum_size
	route.set_initial_focus_node(navigation.current)

func _scroll_to(point: Vector2, smooth := false) -> void:
	var offset := camera.offset_for(point + route.position, _scroll.size, _canvas.custom_minimum_size)
	var policy = visual_context.capture_motion()
	var seconds: float = 0 if policy.instant or policy.reduced else route.layout_profile.camera_transition_seconds / policy.speed
	# Duplicate deferred focus/layout requests must not restart the same pan.
	if seconds > 0 and _camera_tween != null and _camera_tween.is_valid() and _camera_target.is_equal_approx(offset): return
	_stop_camera_motion()
	_camera_target = offset
	var origin := Vector2(_scroll.scroll_horizontal, _scroll.scroll_vertical)
	if not smooth or seconds <= 0 or origin.distance_to(offset) < 1:
		_set_camera_offset(offset); return
	_camera_tween = create_tween()
	_camera_tween.tween_method(func(weight: float): _set_camera_offset(origin.lerp(offset, weight)), 0., 1., seconds).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)
func _set_camera_offset(value: Vector2) -> void:
	_scroll.scroll_horizontal = roundi(value.x); _scroll.scroll_vertical = roundi(value.y)
func _stop_camera_motion(finish := false) -> void:
	if _camera_tween != null and _camera_tween.is_valid(): _camera_tween.kill()
	_camera_tween = null
	if finish and _camera_target.is_finite(): _set_camera_offset(_camera_target)
func _place_frame(point: Vector2, smooth := false) -> void:
	if is_inside_tree(): _scroll_to(point, smooth)
func _resize_camera() -> void:
	if not is_instance_valid(route) or route.nodes.is_empty(): return
	_update_extent(); _center_current.call_deferred()

func _input(event: InputEvent) -> void:
	if not is_inside_tree() or not is_instance_valid(_scroll): return
	if get_tree().paused or get_tree().root.find_children("*", "Window", true, false).any(func(window): return window.visible):
		cancel_gesture(); return
	if event.is_action_pressed("pause_game"):
		cancel_gesture(); return
	if event.is_action_pressed("ui_cancel") and _inspector_open:
		cancel_gesture(); _set_inspector_open(false); get_viewport().set_input_as_handled(); return
	var focused := get_viewport().gui_get_focus_owner()
	if focused == null and ["ui_left","ui_right","ui_up","ui_down"].any(func(action): return event.is_action_pressed(action)):
		if route.buttons.has(navigation.current):
			route.buttons[navigation.current].grab_focus(); get_viewport().set_input_as_handled(); return
	if focused in route.buttons.values() and (event.is_action_pressed("ui_focus_next") or event.is_action_pressed("ui_focus_prev")):
		if not _action_buttons.is_empty(): _action_buttons[0].grab_focus()
		else: _inspector_toggle.grab_focus()
		get_viewport().set_input_as_handled(); return
	if event is InputEventMouseButton and event.button_index in [MOUSE_BUTTON_LEFT, MOUSE_BUTTON_MIDDLE]:
		if event.pressed and _scroll.get_global_rect().has_point(event.global_position) and not _pointer_over_inspector(event.global_position):
			_camera_restore_ticket += 1
			_camera_restore_from = ""
			_stop_camera_motion()
			camera.begin(event.global_position, Vector2(_scroll.scroll_horizontal, _scroll.scroll_vertical))
		elif not event.pressed and camera.pressed:
			if camera.dragged: get_viewport().set_input_as_handled()
			cancel_gesture.call_deferred()
	elif event is InputEventMouseMotion and camera.pressed:
		var target: Vector2 = camera.motion(event.global_position)
		if camera.dragged:
			route.suppress_activation = true
			_scroll.scroll_horizontal = int(target.x); _scroll.scroll_vertical = int(target.y)
			get_viewport().set_input_as_handled()
	elif event.is_action_pressed("confirm_action"):
		var focus := get_viewport().gui_get_focus_owner()
		if focus in route.buttons.values():
			_inspect(str(focus.get_meta("node_id", ""))); _set_inspector_open(true); get_viewport().set_input_as_handled()

func _pointer_over_inspector(point: Vector2) -> bool:
	return (_inspector_open and _inspector_panel.get_global_rect().has_point(point)) or _inspector_toggle.get_global_rect().has_point(point)

func cancel_gesture() -> void:
	camera.cancel()
	if is_instance_valid(route): route.suppress_activation = false

func _notification(what: int) -> void:
	if what in [NOTIFICATION_WM_WINDOW_FOCUS_OUT, NOTIFICATION_PAUSED]: cancel_gesture()

func _reveal_node(id: String) -> void:
	if not _camera_restore_from.is_empty(): return
	if id == navigation.current and _camera_tween != null and _camera_tween.is_valid(): return
	if not route.has_node_rect(id): return
	var rect: Rect2 = route.node_rect(id)
	var visible_rect := Rect2(Vector2(_scroll.scroll_horizontal, _scroll.scroll_vertical) - route.position, _usable_size())
	if not visible_rect.encloses(rect): _place_frame.call_deferred(rect.get_center(), true)
func _center_node(id: String, smooth := false) -> void:
	if not is_inside_tree() or not route.buttons.has(id): return
	_update_extent()
	_place_frame.call_deferred(route.node_button_center(id), smooth)
func _center_current(smooth := false) -> void:
	# Initial layout/focus callbacks must not flash the new stop for a frame
	# before the restored transition begins at the previous stop.
	_center_node(_camera_restore_from if not _camera_restore_from.is_empty() else navigation.current, smooth and _camera_restore_from.is_empty())
func dispose() -> void:
	_camera_restore_ticket += 1
	_camera_restore_from = ""
	_stop_camera_motion()
	cancel_gesture()

func _unhandled_key_input(event: InputEvent) -> void:
	if Engine.is_editor_hint() or not is_visible_in_tree() or not event is InputEventKey or not event.pressed or event.echo: return
	if event.keycode == KEY_HOME:
		cancel_gesture(); _center_current(true); get_viewport().set_input_as_handled()

func _preview_if_isolated() -> void:
	if _presenter == null and (Engine.is_editor_hint() or get_tree().current_scene == self): show_editor_preview()
func show_editor_preview() -> void:
	var data = JSON.parse_string(FileAccess.get_file_as_string(preview_document))
	if not data is Dictionary: return
	var context = ContextType.new()
	var projection = preload("res://scripts/presentation/activity_presenter.gd").new(data.run, [], data.presentation, context.translator)
	apply_projection(data.run, [], projection, context)

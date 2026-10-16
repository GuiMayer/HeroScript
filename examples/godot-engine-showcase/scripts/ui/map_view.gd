extends "res://scripts/ui/screen_view.gd"
## Read-only map inspection and explicit semantic actions. No rule evaluation.
signal intent_requested(action: String, arguments: Array)
var route
var _scroll: ScrollContainer
var _inspector: VBoxContainer
var _body: HBoxContainer
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
var _overview := false
var _base_layout: Resource
var _filter: OptionButton
var _filter_kinds: Array[String] = []
var navigation = preload("res://scripts/presentation/map_navigation_model.gd").new()
var _canvas: Control
var _zoom := 1.0
var _reading: Dictionary = {}
var _framed := false
func apply_projection(run: Dictionary, choices: Array, presenter, context: ContextType) -> void:
	mount(context); theme = context.theme
	_run = run.duplicate(true); _choices = choices.duplicate(true); _presenter = presenter
	if not is_instance_valid(route): _build()
	_header.text = context.translator.content_name(str(run.get("runDefinitionId", "")), str(run.get("metadata", {}).get("displayName", context.translator.text("JOURNEY"))))
	find_child("MapPauseButton", true, false).text = context.translator.text("PAUSE")
	var counters: Array[String] = []
	for resource_id in run.get("resources", {}):
		counters.append("%s %s" % [context.translator.number(float(run.resources[resource_id].get("current", 0))), context.translator.content_name(str(resource_id))])
	for resource_id in run.get("characterResources", {}).get("resources", {}):
		var value: Dictionary = run.characterResources.resources[resource_id]
		counters.append("%s %s" % [context.translator.number(float(value.get("current", 0))), context.translator.content_name(str(resource_id))])
	_resource_badges.text = "  ·  ".join(counters)
	for button in find_children("MapTool_*", "Button", true, false): button.text = context.translator.text(str(button.get_meta("label_key")))
	var nodes: Array = presenter.route()
	navigation.update(nodes)
	var key: Array = [nodes, choices, context.presentation_key()]
	if key != _model_key:
		_model_key = key.duplicate(true)
		route.visual_context = context
		route.setup(nodes)
		_update_filter(nodes)
		_update_extent()
	if not nodes.any(func(node): return node.id == _selected): _selected = str(run.get("currentNodeId", ""))
	if _selected.is_empty() and not nodes.is_empty(): _selected = str(nodes[0].id)
	_inspect(_selected)
	context.styles.apply_view_preferences(self)
	if not _framed:
		_framed = true
		_next_choices.call_deferred()
func _build() -> void:
	add_theme_constant_override("separation", 16)
	var header := HBoxContainer.new(); add_child(header)
	_header = visual_context.styles.title("", 27); _header.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	header.add_child(_header)
	_resource_badges = visual_context.styles.muted("", 15)
	_resource_badges.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	add_child(_resource_badges)
	var toolbar := HFlowContainer.new(); toolbar.add_theme_constant_override("h_separation", 8)
	for tool in [["Center current", _center_current], ["Next choices", _next_choices], ["Overview", _toggle_overview], ["Legend", _legend]]:
		var button := visual_context.styles.button(visual_context.translator.text(tool[0]))
		button.name = "MapTool_" + str(tool[0]).replace(" ", "")
		button.set_meta("label_key", tool[0]); button.pressed.connect(tool[1]); toolbar.add_child(button)
	_filter = OptionButton.new(); _filter.name = "MapTypeFilter"; _filter.custom_minimum_size.y = 44
	_filter.item_selected.connect(func(index): route.highlight_kind(_filter_kinds[index]))
	toolbar.add_child(_filter); add_child(toolbar)
	var pause := visual_context.styles.button(visual_context.translator.text("PAUSE"), 100)
	pause.name = "MapPauseButton"
	pause.pressed.connect(func(): intent_requested.emit("pause", [])); header.add_child(pause)
	var items := visual_context.styles.button(visual_context.translator.text("MODIFIERS"), 140)
	items.pressed.connect(func(): intent_requested.emit("modifier_items", [])); header.add_child(items)
	if _run.get("loadout") is Dictionary:
		var inscriptions := visual_context.styles.button(visual_context.translator.text("INSCRIPTIONS"), 140)
		inscriptions.pressed.connect(func(): intent_requested.emit("inscriptions", [])); header.add_child(inscriptions)
	_body = HBoxContainer.new(); _body.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_body.add_theme_constant_override("separation", 20); add_child(_body)
	_scroll = ScrollContainer.new(); _scroll.name = "MapScroll"
	_scroll.size_flags_horizontal = Control.SIZE_EXPAND_FILL; _scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_AUTO; _scroll.follow_focus = false
	_scroll.resized.connect(_resize_camera)
	_scroll.gui_input.connect(_pan_input)
	route = preload("res://scripts/ui/route_view.gd").new()
	_base_layout = route.layout_profile.duplicate(true)
	route.mouse_filter = Control.MOUSE_FILTER_PASS
	route.inspection_requested.connect(_inspect)
	_canvas = Control.new(); _canvas.name = "MapCanvas"; _canvas.mouse_filter = Control.MOUSE_FILTER_PASS
	_canvas.add_child(route); _scroll.add_child(_canvas); _body.add_child(_scroll)
	var inspector_scroll := ScrollContainer.new(); inspector_scroll.custom_minimum_size.x = 300
	inspector_scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	_inspector = VBoxContainer.new(); _inspector.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_inspector.add_theme_constant_override("separation", 12)
	inspector_scroll.add_child(_inspector); _body.add_child(inspector_scroll)
func _inspect(id: String) -> void:
	_selected = id
	navigation.inspect(id)
	route.set_selection(id)
	var projection: Array = [id, route.nodes, _choices, _run.get("dialogues", []), visual_context.presentation_key()]
	if projection == _inspector_key: return
	_inspector_key = projection.duplicate(true)
	for child in _inspector.get_children(): _inspector.remove_child(child); child.queue_free()
	_action_buttons.clear()
	var matches: Array = route.nodes.filter(func(node): return str(node.id) == id)
	if matches.is_empty(): return
	var node: Dictionary = matches[0]
	_inspector.add_child(visual_context.styles.wrap(visual_context.styles.title(str(node.name), 25), 260))
	_inspector.add_child(visual_context.styles.muted(visual_context.translator.content_name(str(node.kind)), 16, 260))
	if not str(node.get("risk", "")).is_empty(): _inspector.add_child(visual_context.styles.muted(visual_context.translator.text("Risk: %s") % str(node.risk), 15, 260))
	_inspector.add_child(visual_context.styles.muted(str(node.get("description", "")), 17, 260))
	var status := "YOU ARE HERE" if node.current else ("COMPLETED" if node.resolved else ("Available" if not node.travel.is_empty() else "Unavailable"))
	_inspector.add_child(visual_context.styles.title(visual_context.translator.text(status), 16))
	if not node.travel.is_empty(): _action(visual_context.translator.text("TRAVEL"), node.travel)
	if node.current:
		for choice in _choices:
			var type := str(choice.get("type", ""))
			if type in ["ADVANCE_NODE", "ABANDON_RUN"]: continue
			if type == "CHOOSE_DIALOGUE_OPTION": continue
			_action(_presenter.choice_label(choice), choice)
		var active_dialogue: bool = _run.get("dialogues", []).any(func(item): return str(item.get("activityNodeId", "")) == id and not bool(item.get("completed", false)))
		if str(node.kind) == "Dialogue" and not node.resolved and active_dialogue:
			var resume := visual_context.styles.button(visual_context.translator.text("Resume conversation"))
			resume.name = "MapResumeDialogue"
			resume.pressed.connect(func(): intent_requested.emit("resume_dialogue", [])); _inspector.add_child(resume)
	visual_context.styles.apply_view_preferences(_inspector)
func _action(label: String, choice: Dictionary) -> void:
	var reference: String = _presenter.option_reference(choice)
	var button := visual_context.styles.button(label)
	button.name = "MapAction_" + str(choice.get("type", ""))
	button.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	button.disabled = _locked
	button.pressed.connect(func(): intent_requested.emit("command", [reference]))
	_inspector.add_child(button); _action_buttons.append(button)
func set_read_only(value: bool) -> void:
	_locked = value
	for button in _action_buttons:
		if is_instance_valid(button): button.disabled = value
func capture_local_state() -> Dictionary:
	var point: Vector2 = (Vector2(_scroll.scroll_horizontal, _scroll.scroll_vertical) + _scroll.size * .5 - route.position) / _zoom
	var anchor := _selected
	var distance := INF
	for id in route._geometry.rects:
		var candidate: float = route._geometry.rects[id].get_center().distance_squared_to(point)
		if candidate < distance: distance = candidate; anchor = str(id)
	return {"version": 2, "anchor": anchor, "offset": point - route._geometry.rects.get(anchor, Rect2()).get_center(), "zoom": _zoom,
		"selected": _selected, "horizontal": _scroll.scroll_horizontal, "vertical": _scroll.scroll_vertical, "overview": _overview,
		"filter": _filter_kinds[_filter.selected] if _filter.selected >= 0 else ""}
func restore_local_state(state: Dictionary) -> void:
	if state.is_empty(): return
	if bool(state.get("overview", false)) != _overview: _toggle_overview()
	var filter_id := _filter_kinds.find(str(state.get("filter", "")))
	if filter_id >= 0: _filter.select(filter_id); route.highlight_kind(_filter_kinds[filter_id])
	var id := str(state.get("selected", ""))
	if route.nodes.any(func(node): return str(node.id) == id): _inspect(id)
	_restore_camera.call_deferred(state.duplicate(true))

func _restore_camera(state: Dictionary) -> void:
	if _overview: _frame(route.buttons.keys(), true); return
	var anchor := str(state.get("anchor", ""))
	if route._geometry.rects.has(anchor):
		_zoom = clampf(float(state.get("zoom", 1)), .6, 1)
		_update_extent()
		var offset: Vector2 = state.get("offset", Vector2.ZERO)
		_scroll_to(route._geometry.rects[anchor].get_center() + offset)
	else: _next_choices()
	_reading = capture_local_state()

func _update_extent() -> void:
	if not is_instance_valid(_canvas): return
	route.scale = Vector2.ONE * _zoom
	_canvas.custom_minimum_size = route.custom_minimum_size * _zoom
	route.position.x = maxf(0, (_scroll.size.x - _canvas.custom_minimum_size.x) * .5)
	for button in route.buttons.values():
		button.mouse_filter = Control.MOUSE_FILTER_IGNORE if _overview else Control.MOUSE_FILTER_STOP
		button.focus_mode = Control.FOCUS_NONE if _overview else Control.FOCUS_ALL
		button.get_node("Caption").visible = _zoom >= .65

func _scroll_to(point: Vector2) -> void:
	var offset := camera.offset_for(point * _zoom + route.position, _scroll.size, _canvas.custom_minimum_size.max(_scroll.size))
	_scroll.scroll_horizontal = int(offset.x); _scroll.scroll_vertical = int(offset.y)

func _frame(ids: Array, whole := false) -> void:
	var bounds := Rect2(); var first := true
	for id in ids:
		if not route._geometry.rects.has(id): continue
		var rect: Rect2 = route._geometry.rects[id]
		bounds = rect if first else bounds.merge(rect); first = false
	if first: return
	_zoom = camera.fit_scale(bounds, _scroll.size, .01 if whole else .6)
	_update_extent()
	# Scroll ranges are updated by the container on the next frame.
	_place_frame.call_deferred(bounds.get_center())

func _place_frame(point: Vector2) -> void:
	_scroll_to(point); _reading = capture_local_state()

func _resize_camera() -> void:
	if not is_instance_valid(route) or route.nodes.is_empty(): return
	_update_extent()
	if not _reading.is_empty(): _restore_camera.call_deferred(_reading.duplicate(true))

func _pan_input(event: InputEvent) -> void:
	if _overview and event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT and event.pressed:
		var point: Vector2 = route.get_global_transform_with_canvas().affine_inverse() * event.global_position
		var nearest := ""; var distance := INF
		for id in route._geometry.rects:
			var value: float = route._geometry.rects[id].get_center().distance_squared_to(point)
			if value < distance: nearest = str(id); distance = value
		if not nearest.is_empty(): _center_node(nearest)
		_scroll.accept_event(); return
	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT:
		if event.pressed: camera.begin(event.global_position, Vector2(_scroll.scroll_horizontal, _scroll.scroll_vertical))
		else: camera.cancel()
	elif event is InputEventMouseMotion and camera.pressed:
		var target := camera.motion(event.global_position)
		if camera.dragged:
			_scroll.scroll_horizontal = int(target.x); _scroll.scroll_vertical = int(target.y)
			_scroll.accept_event()
func _input(event: InputEvent) -> void:
	if event is InputEventMouseButton and not event.pressed and event.button_index == MOUSE_BUTTON_LEFT: camera.cancel()
func _center_node(id: String) -> void:
	if not route.buttons.has(id): return
	_overview = false; _zoom = 1; _update_extent()
	_place_frame.call_deferred(route._geometry.rects[id].get_center())
func _center_current() -> void: _center_node(str(_run.get("currentNodeId", "")))
func _next_choices() -> void:
	_overview = false
	_frame(navigation.decision_ids())
func _toggle_overview() -> void:
	_overview = not _overview
	if _overview: _frame(route.buttons.keys(), true)
	else: _next_choices()
func _update_filter(nodes: Array) -> void:
	var previous := _filter_kinds[_filter.selected] if _filter.selected >= 0 and not _filter_kinds.is_empty() else ""
	_filter_kinds = [""]
	for node in nodes:
		if str(node.kind) not in _filter_kinds: _filter_kinds.append(str(node.kind))
	_filter.clear()
	for kind in _filter_kinds: _filter.add_item(visual_context.translator.text("All node types") if kind.is_empty() else visual_context.translator.content_name(kind))
	_filter.select(maxi(0, _filter_kinds.find(previous)))
	route.highlight_kind(_filter_kinds[_filter.selected])
func _legend() -> void:
	var dialog = preload("res://scripts/ui/inspection_dialog.gd").new(visual_context)
	var lines: Array[String] = [visual_context.translator.text("Selecting a node only inspects it. Travel requires a published option."), ""]
	for key in ["YOU ARE HERE", "COMPLETED", "Available", "Unavailable"]: lines.append(visual_context.translator.text(key))
	lines.append("")
	for kind in _filter_kinds:
		if not kind.is_empty(): lines.append(visual_context.translator.content_name(kind))
	dialog.setup(visual_context.translator.text("Legend"), "\n".join(lines)); add_child(dialog); dialog.popup_centered()

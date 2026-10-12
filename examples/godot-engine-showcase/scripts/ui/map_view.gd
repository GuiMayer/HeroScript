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
func apply_projection(run: Dictionary, choices: Array, presenter, context: ContextType) -> void:
	mount(context); theme = context.theme
	_run = run.duplicate(true); _choices = choices.duplicate(true); _presenter = presenter
	if not is_instance_valid(route): _build()
	_header.text = context.translator.content_name(str(run.get("runDefinitionId", "")), str(run.get("metadata", {}).get("displayName", context.translator.text("JOURNEY"))))
	var nodes: Array = presenter.route()
	var key: Array = [nodes, choices, context.presentation_key()]
	if key != _model_key:
		_model_key = key.duplicate(true)
		route.visual_context = context
		route.setup(nodes)
	if not nodes.any(func(node): return node.id == _selected): _selected = str(run.get("currentNodeId", ""))
	if _selected.is_empty() and not nodes.is_empty(): _selected = str(nodes[0].id)
	_inspect(_selected)
	context.styles.apply_view_preferences(self)
func _build() -> void:
	add_theme_constant_override("separation", 16)
	var header := HBoxContainer.new(); add_child(header)
	_header = visual_context.styles.title("", 27); _header.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	header.add_child(_header)
	var pause := visual_context.styles.button(visual_context.translator.text("PAUSE"), 100)
	pause.name = "MapPauseButton"
	pause.pressed.connect(func(): intent_requested.emit("pause", [])); header.add_child(pause)
	_body = HBoxContainer.new(); _body.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_body.add_theme_constant_override("separation", 20); add_child(_body)
	_scroll = ScrollContainer.new(); _scroll.name = "MapScroll"
	_scroll.size_flags_horizontal = Control.SIZE_EXPAND_FILL; _scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_AUTO; _scroll.follow_focus = true
	route = preload("res://scripts/ui/map_route_view.gd").new()
	route.inspection_requested.connect(_inspect)
	_scroll.add_child(route); _body.add_child(_scroll)
	var inspector_scroll := ScrollContainer.new(); inspector_scroll.custom_minimum_size.x = 300
	inspector_scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	_inspector = VBoxContainer.new(); _inspector.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_inspector.add_theme_constant_override("separation", 12)
	inspector_scroll.add_child(_inspector); _body.add_child(inspector_scroll)
func _inspect(id: String) -> void:
	_selected = id
	route.set_selection(id)
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
			var label: String = {"START_ENCOUNTER": "ENTER COMBAT", "START_DIALOGUE": "TALK", "RESOLVE_NODE": "COMPLETE STAGE"}.get(type, type.replace("_", " "))
			_action(visual_context.translator.text(label), choice)
		if str(node.kind) == "Dialogue" and not _run.get("dialogues", []).is_empty():
			var resume := visual_context.styles.button(visual_context.translator.text("Resume conversation"))
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
	return {"selected": _selected, "horizontal": _scroll.scroll_horizontal, "vertical": _scroll.scroll_vertical}
func restore_local_state(state: Dictionary) -> void:
	if state.is_empty(): return
	var id := str(state.get("selected", ""))
	if route.nodes.any(func(node): return str(node.id) == id): _inspect(id)
	_scroll.set_deferred("scroll_horizontal", int(state.get("horizontal", 0)))
	_scroll.set_deferred("scroll_vertical", int(state.get("vertical", 0)))

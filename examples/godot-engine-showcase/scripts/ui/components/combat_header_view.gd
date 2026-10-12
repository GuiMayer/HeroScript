extends HBoxContainer
const ContextType = preload("res://scripts/ui/visual_context.gd")
var visual_context: ContextType = ContextType.new()
signal timeline_requested
signal tools_requested
signal pause_requested
var tool_button: Button
var _model := {}
var _context_key := ""
func presentation_model() -> Dictionary: return _model.duplicate(true)
func reconcile_from(other) -> void:
	visual_context = other.visual_context
	apply_model(other.presentation_model())
func apply_model(model: Dictionary) -> void:
	theme = visual_context.theme
	var context_key := visual_context.presentation_key()
	if _model == model and _context_key == context_key: return
	_context_key = context_key
	_model = model.duplicate(true)
	if get_child_count() > 0:
		var desired = get_script().new()
		desired.visual_context = visual_context
		desired.apply_model(model)
		preload("res://scripts/ui/view_reconciler.gd").patch_contents(self, desired)
		desired.free()
		return
	var title := visual_context.styles.title(str(model.round), 24)
	title.tooltip_text = str(model.context)
	add_child(title)
	var spacer := Control.new()
	spacer.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	add_child(spacer)
	var timeline := visual_context.styles.button(str(model.timeline), 165)
	timeline.name = "TimelineButton"
	timeline.custom_minimum_size.y = 44
	preload("res://scripts/ui/combat_chrome.gd").apply(timeline, "timeline", str(model.timeline), visual_context, false, true)
	timeline.custom_minimum_size.x = 44
	timeline.pressed.connect(func(): timeline_requested.emit())
	add_child(timeline)
	tool_button = visual_context.styles.button(str(model.tools), 125)
	tool_button.name = "ZoneToolsButton"
	tool_button.custom_minimum_size.y = 44
	preload("res://scripts/ui/combat_chrome.gd").apply(tool_button, "tools", str(model.tools), visual_context, false, true)
	tool_button.custom_minimum_size.x = 44
	tool_button.visible = bool(model.allow_tools)
	tool_button.pressed.connect(func(): tools_requested.emit())
	add_child(tool_button)
	var pause := visual_context.styles.button(str(model.pause), 100)
	pause.name = "PauseButton"
	pause.custom_minimum_size.y = 44
	preload("res://scripts/ui/combat_chrome.gd").apply(pause, "pause", str(model.pause), visual_context)
	pause.pressed.connect(func(): pause_requested.emit())
	add_child(pause)

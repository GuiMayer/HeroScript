@tool
class_name CombatHeaderView
extends HBoxContainer
const ContextType = preload("res://scripts/ui/visual_context.gd")
var visual_context: ContextType = ContextType.new()
signal timeline_requested
signal tools_requested
signal pause_requested
var tool_button: Button
var _model := {}
var _context_key := ""
var _bound := false
static func create() -> CombatHeaderView:
	return (load("res://scenes/ui/components/combat_header.tscn") as PackedScene).instantiate()
func _ready() -> void:
	resized.connect(func(): if not _model.is_empty(): apply_model(_model.duplicate(true)))
	if _model.is_empty(): apply_model({"round":"ROUND 1","context":"Combat","timeline":"Timeline","tools":"Zone tools","pause":"Pause","allow_tools":true})
func presentation_model() -> Dictionary: return _model.duplicate(true)
func reconcile_from(other) -> void:
	visual_context = other.visual_context; apply_model(other.presentation_model())
func apply_model(model: Dictionary) -> void:
	theme = visual_context.theme
	var wide: bool = is_inside_tree() and get_viewport_rect().size.x >= 1920
	var context_key := visual_context.presentation_key() + str(wide)
	if _model == model and _context_key == context_key: return
	_context_key = context_key; _model = model.duplicate(true)
	tool_button = get_node("ZoneToolsButton")
	get_node("Round").text = str(model.round); get_node("Round").tooltip_text = str(model.context)
	for entry in [["TimelineButton", "timeline", str(model.timeline) + " [" + visual_context.action_label("open_timeline") + "]", true], ["ZoneToolsButton", "tools", str(model.tools), true], ["PauseButton", "pause", str(model.pause) + " [" + visual_context.action_label("pause_game") + "]", false]]:
		if not has_node(entry[0]): continue
		preload("res://scripts/ui/combat_chrome.gd").apply(get_node(entry[0]), entry[1], entry[2], visual_context, false, entry[3] and not wide)
	tool_button.visible = bool(model.allow_tools)

func _on_timeline_pressed() -> void: timeline_requested.emit()
func _on_tools_pressed() -> void: tools_requested.emit()
func _on_pause_pressed() -> void: pause_requested.emit()

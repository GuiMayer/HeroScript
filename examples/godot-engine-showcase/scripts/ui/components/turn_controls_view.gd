@tool
class_name TurnControlsView
extends VBoxContainer
const ContextType = preload("res://scripts/ui/visual_context.gd")
var visual_context: ContextType = ContextType.new()
signal next_requested
signal skip_requested
signal cancel_requested
signal end_requested
signal system_requested(candidate: Dictionary)
var next_frame_button: Button
var skip_button: Button
var selection_actions: HBoxContainer
var buttons: Array[Button] = []
var _bound := false
var _pass_option := {}
static func create() -> TurnControlsView:
	return (load("res://scenes/ui/components/turn_controls.tscn") as PackedScene).instantiate()
func _ready() -> void:
	if not _bound: mount(visual_context.translator,{"confirm_action":"Space","end_turn":"E"},{})
func mount(translator, labels: Dictionary, pass_option: Dictionary) -> void:
	theme = visual_context.theme; _pass_option = pass_option.duplicate(true)
	next_frame_button = get_node("ContextActionSlot/NextAnimation")
	skip_button = get_node("SkipPresentationButton")
	selection_actions = get_node("ContextActionSlot/SelectionActions")
	var cancel: Button = selection_actions.get_node("CancelSelection")
	var pass_button: Button = get_node("SystemActionSlot/Pass")
	var end: Button = get_node("EndTurnButton")
	_bound = true
	buttons.assign([cancel, end]); pass_button.visible = not _pass_option.is_empty()
	get_node("SystemActionSlot").visible = pass_button.visible
	if not _pass_option.is_empty(): buttons.insert(1,pass_button)
	var pass_label := "PASS PRIORITY" if str(_pass_option.get("command", {}).get("actionType", "")) == "PASS_PRIORITY" else "PASS"
	for entry in [[next_frame_button,"advance",translator.text("NEXT ANIMATION  [%s]") % str(labels["confirm_action"]),false,true], [cancel,"cancel",translator.text("CANCEL SELECTION"),false,true], [skip_button,"skip",translator.text("Skip presentation"),false,true], [pass_button,"arrow",translator.text(pass_label),false,true], [end,"arrow",translator.text("END TURN  [%s]") % str(labels["end_turn"]),true,false]]:
		preload("res://scripts/ui/combat_chrome.gd").apply(entry[0],entry[1],entry[2],visual_context,entry[3],entry[4])

func _on_next_pressed() -> void: next_requested.emit()
func _on_skip_pressed() -> void: skip_requested.emit()
func _on_cancel_pressed() -> void: cancel_requested.emit()
func _on_end_pressed() -> void: end_requested.emit()
func _on_pass_pressed() -> void: system_requested.emit(_pass_option.duplicate(true))

extends HBoxContainer
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
func mount(translator, labels: Dictionary, pass_option: Dictionary) -> void:
	name = "TurnControls"
	add_theme_constant_override("separation", 8)
	var context_slot := CenterContainer.new()
	context_slot.name = "ContextActionSlot"
	context_slot.custom_minimum_size = Vector2(48, 44)
	next_frame_button = _button(translator.text("NEXT ANIMATION  [%s]") % str(labels["confirm_action"]), next_requested.emit, 180)
	next_frame_button.clip_text = true
	preload("res://scripts/ui/combat_chrome.gd").apply(next_frame_button, "advance", next_frame_button.text, visual_context, false, true)
	next_frame_button.custom_minimum_size.x = 44
	context_slot.add_child(next_frame_button)
	selection_actions = HBoxContainer.new()
	selection_actions.add_theme_constant_override("separation", 6)
	var cancel := _button(translator.text("CANCEL SELECTION"), cancel_requested.emit, 180)
	cancel.set_meta("selection_only", true)
	preload("res://scripts/ui/combat_chrome.gd").apply(cancel, "cancel", cancel.text, visual_context, false, true)
	cancel.custom_minimum_size.x = 44
	buttons.append(cancel)
	selection_actions.add_child(cancel)
	# Inspection remains in the hand toolbar; do not duplicate it in the turn dock.
	cancel.clip_text = true
	context_slot.add_child(selection_actions)
	add_child(context_slot)
	skip_button = _button(translator.text("Skip presentation"), skip_requested.emit, 44)
	skip_button.name = "SkipPresentationButton"
	preload("res://scripts/ui/combat_chrome.gd").apply(skip_button, "advance", skip_button.text, visual_context, false, true)
	skip_button.custom_minimum_size.x = 44
	add_child(skip_button)
	var system_slot := CenterContainer.new()
	system_slot.name = "SystemActionSlot"
	system_slot.custom_minimum_size = Vector2(48, 44)
	var pass_candidate := pass_option
	if not pass_candidate.is_empty():
		var pass_label := "PASS PRIORITY" if str(pass_candidate.command.get("actionType", "")) == "PASS_PRIORITY" else "PASS"
		var pass_button := _button(translator.text(pass_label), func(): system_requested.emit(pass_candidate), 100)
		pass_button.clip_text = true
		preload("res://scripts/ui/combat_chrome.gd").apply(pass_button, "arrow", pass_button.text, visual_context, false, true)
		pass_button.custom_minimum_size.x = 44
		pass_button.tooltip_text = pass_button.text
		buttons.append(pass_button)
		system_slot.add_child(pass_button)
	add_child(system_slot)
	var end := _button(translator.text("END TURN  [%s]") % str(labels["end_turn"]), end_requested.emit, 195)
	end.name = "EndTurnButton"
	end.theme_type_variation = "PrimaryAction"
	preload("res://scripts/ui/combat_chrome.gd").apply(end, "arrow", end.text, visual_context, true)
	end.icon_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	end.set_meta("requires_end", true)
	buttons.append(end)
	add_child(end)

func _button(text: String, action: Callable, width := 0) -> Button:
	var button := visual_context.styles.button(text, width)
	button.custom_minimum_size.y = 44
	button.pressed.connect(action)
	return button

extends HBoxContainer
signal next_requested
signal cancel_requested
signal end_requested
signal reconnect_requested
signal system_requested(candidate: Dictionary)
var next_frame_button: Button
var selection_actions: HBoxContainer
var reconnect_button: Button
var buttons: Array[Button] = []
func mount(translator, labels: Dictionary, pass_option: Dictionary, overlay_host: Control) -> void:
	name = "TurnControls"
	add_theme_constant_override("separation", 8)
	var context_slot := CenterContainer.new()
	context_slot.name = "ContextActionSlot"
	context_slot.custom_minimum_size = Vector2(190, 44)
	next_frame_button = _button(translator.text("NEXT ANIMATION  [%s]") % str(labels["confirm_action"]), next_requested.emit, 180)
	next_frame_button.clip_text = true
	next_frame_button.tooltip_text = next_frame_button.text
	context_slot.add_child(next_frame_button)
	selection_actions = HBoxContainer.new()
	selection_actions.add_theme_constant_override("separation", 6)
	var cancel := _button(translator.text("CANCEL SELECTION"), cancel_requested.emit, 180)
	cancel.set_meta("selection_only", true)
	buttons.append(cancel)
	selection_actions.add_child(cancel)
	# Inspection remains in the hand toolbar; do not duplicate it in the turn dock.
	cancel.clip_text = true
	cancel.tooltip_text = cancel.text
	context_slot.add_child(selection_actions)
	add_child(context_slot)
	var system_slot := CenterContainer.new()
	system_slot.name = "SystemActionSlot"
	system_slot.custom_minimum_size = Vector2(100, 44)
	var pass_candidate := pass_option
	if not pass_candidate.is_empty():
		var pass_label := "PASS PRIORITY" if str(pass_candidate.command.get("actionType", "")) == "PASS_PRIORITY" else "PASS"
		var pass_button := _button(translator.text(pass_label), func(): system_requested.emit(pass_candidate), 100)
		pass_button.clip_text = true
		pass_button.tooltip_text = pass_button.text
		buttons.append(pass_button)
		system_slot.add_child(pass_button)
	add_child(system_slot)
	var end := _button(translator.text("END TURN  [%s]") % str(labels["end_turn"]), end_requested.emit, 195)
	end.name = "EndTurnButton"
	end.theme_type_variation = "PrimaryAction"
	end.set_meta("requires_end", true)
	buttons.append(end)
	add_child(end)
	reconnect_button = _button(translator.text("RECONNECT"), reconnect_requested.emit, 130)
	overlay_host.add_child(reconnect_button)
	reconnect_button.set_anchors_and_offsets_preset(Control.PRESET_TOP_LEFT)
	reconnect_button.offset_right = 150
	reconnect_button.offset_bottom = 44

func _button(text: String, action: Callable, width := 0) -> Button:
	var button := AppTheme.button(text, width)
	button.custom_minimum_size.y = 44
	button.pressed.connect(action)
	return button

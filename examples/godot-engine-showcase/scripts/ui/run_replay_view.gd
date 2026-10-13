extends VBoxContainer
## Passive replay interface. No session, transport, commands or playback authority.
signal intent_requested(action: String, arguments: Array)
const ContextType = preload("res://scripts/ui/visual_context.gd")
var visual_context: ContextType
var list: ItemList
var heading: Label
var progress: ProgressBar
var state_text: RichTextLabel
var frame_text: RichTextLabel
var play_button: Button
var previous_button: Button
var next_button: Button
var verify_button: Button
var more_button: Button
var inspect_button: Button
var loaded_count: Label
var _last_selected := -2

func setup(context, from_history := false) -> void:
	visual_context = context
	theme = visual_context.theme
	var return_to_history: bool = from_history
	add_theme_constant_override("separation", 10)
	var header := HBoxContainer.new()
	header.add_child(visual_context.styles.title(visual_context.translator.text("JOURNEY REPLAY"), 30))
	var push := Control.new()
	push.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	header.add_child(push)
	header.add_child(_button(visual_context.translator.text("BACK TO HISTORY") if return_to_history else visual_context.translator.text("BACK TO RESULT"), "back"))
	add_child(header)
	add_child(visual_context.styles.muted(visual_context.translator.text("This is the immutable recording produced by the engine. Playback only moves a local viewing cursor.")))
	var body := HSplitContainer.new()
	body.size_flags_vertical = Control.SIZE_EXPAND_FILL
	add_child(body)
	var left := VBoxContainer.new()
	left.custom_minimum_size.x = 390
	left.add_child(visual_context.styles.muted(visual_context.translator.text("COMMAND TIMELINE"), 13))
	list = ItemList.new()
	list.name = "ReplayCommandList"
	list.size_flags_vertical = Control.SIZE_EXPAND_FILL
	list.item_selected.connect(func(index): intent_requested.emit("select", [index]))
	left.add_child(list)
	loaded_count = visual_context.styles.muted("", 13)
	left.add_child(loaded_count)
	more_button = _button(visual_context.translator.text("LOAD MORE"), "more")
	left.add_child(more_button)
	body.add_child(visual_context.styles.panel(left))
	var right := VBoxContainer.new()
	right.add_theme_constant_override("separation", 8)
	right.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	heading = visual_context.styles.title(visual_context.translator.text("Loading replay…"), 24, visual_context.styles.GOLD)
	right.add_child(heading)
	progress = ProgressBar.new()
	progress.name = "ReplayProgress"
	progress.show_percentage = false
	right.add_child(progress)
	state_text = RichTextLabel.new()
	state_text.bbcode_enabled = true
	state_text.fit_content = true
	state_text.custom_minimum_size.y = 170
	right.add_child(state_text)
	frame_text = RichTextLabel.new()
	frame_text.bbcode_enabled = true
	frame_text.size_flags_vertical = Control.SIZE_EXPAND_FILL
	frame_text.custom_minimum_size.y = 160
	right.add_child(frame_text)
	var controls := HFlowContainer.new()
	controls.add_child(_button(visual_context.translator.text("RESTART"), "restart"))
	previous_button = _button(visual_context.translator.text("PREVIOUS"), "previous")
	controls.add_child(previous_button)
	play_button = _button(visual_context.translator.text("PLAY"), "toggle_play")
	play_button.name = "ReplayPlayButton"
	controls.add_child(play_button)
	next_button = _button(visual_context.translator.text("NEXT"), "next")
	controls.add_child(next_button)
	verify_button = _button(visual_context.translator.text("VERIFY INTEGRITY"), "verify")
	controls.add_child(verify_button)
	inspect_button = _button(visual_context.translator.text("Show engine details"), "inspect")
	controls.add_child(inspect_button)
	right.add_child(controls)
	body.add_child(visual_context.styles.panel(right))
	previous_button.disabled = true; next_button.disabled = true; play_button.disabled = true


func apply_model(model: Dictionary) -> void:
	var entries: Array = model.entries
	while list.item_count < entries.size():
		var entry: Dictionary = entries[list.item_count]
		list.add_item("#%03d  •  %s  ·  %s %s" % [int(entry.sequence),
			visual_context.translator.text(str(entry.get("commandType", "")).replace("_", " ").capitalize()), visual_context.translator.text("Step"), int(entry.get("step", 0))])
		list.set_item_tooltip(list.item_count - 1, list.get_item_text(list.item_count - 1))
	if model.selected_index >= 0 and model.selected_index != _last_selected:
		list.select(model.selected_index); list.ensure_current_is_visible()
		_last_selected = model.selected_index
	progress.max_value = maxi(int(model.through_sequence) - 1, 1)
	progress.value = maxi(model.selected_index, 0)
	heading.text = visual_context.translator.text("Loading replay…") if entries.is_empty() else visual_context.translator.text("Command %s of %s") % [model.selected_index + 1, model.through_sequence]
	if model.page_error: heading.text = visual_context.translator.text("Replay unavailable")
	state_text.clear(); state_text.add_text(model.state_text)
	frame_text.clear(); frame_text.add_text(model.frame_text)
	previous_button.disabled = model.selected_index <= 0
	next_button.disabled = entries.is_empty() or (not model.has_more and model.selected_index >= entries.size() - 1)
	play_button.disabled = entries.is_empty()
	play_button.text = visual_context.translator.text("PAUSE REPLAY" if model.playing else "PLAY")
	verify_button.disabled = entries.is_empty() or model.verifying
	more_button.visible = model.has_more
	more_button.disabled = model.page_loading
	loaded_count.text = visual_context.translator.text("%s commands loaded of %s") % [entries.size(), model.through_sequence]
	inspect_button.visible = model.inspection_allowed
	inspect_button.disabled = model.presentation_loading or model.selected_index < 0
func _button(text: String, action: String) -> Button:
	var value := visual_context.styles.button(text)
	value.pressed.connect(func(): intent_requested.emit(action, []))
	return value

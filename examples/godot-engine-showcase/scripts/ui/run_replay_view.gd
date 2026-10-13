extends VBoxContainer
## Passive replay interface. No session, transport, commands or playback authority.
signal intent_requested(action: String, arguments: Array)
var visual_context
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
var _last_selected := -2

func setup(context, from_history := false) -> void:
	visual_context = context
	var return_to_history: bool = from_history
	add_theme_constant_override("separation", 10)
	var header := HBoxContainer.new()
	header.add_child(AppTheme.title(I18n.text("JOURNEY REPLAY"), 30))
	var push := Control.new()
	push.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	header.add_child(push)
	header.add_child(_button(I18n.text("BACK TO HISTORY") if return_to_history else I18n.text("BACK TO RESULT"), "back"))
	add_child(header)
	add_child(AppTheme.muted(I18n.text("This is the immutable recording produced by the engine. Playback only moves a local viewing cursor.")))
	var body := HSplitContainer.new()
	body.size_flags_vertical = Control.SIZE_EXPAND_FILL
	add_child(body)
	var left := VBoxContainer.new()
	left.custom_minimum_size.x = 390
	left.add_child(AppTheme.muted(I18n.text("COMMAND TIMELINE"), 13))
	list = ItemList.new()
	list.name = "ReplayCommandList"
	list.size_flags_vertical = Control.SIZE_EXPAND_FILL
	list.item_selected.connect(func(index): intent_requested.emit("select", [index]))
	left.add_child(list)
	more_button = _button(I18n.text("LOAD MORE"), "more")
	left.add_child(more_button)
	body.add_child(AppTheme.panel(left))
	var right := VBoxContainer.new()
	right.add_theme_constant_override("separation", 8)
	right.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	heading = AppTheme.title(I18n.text("Loading replay…"), 24, AppTheme.GOLD)
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
	controls.add_child(_button(I18n.text("RESTART"), "restart"))
	previous_button = _button(I18n.text("PREVIOUS"), "previous")
	controls.add_child(previous_button)
	play_button = _button(I18n.text("PLAY"), "toggle_play")
	play_button.name = "ReplayPlayButton"
	controls.add_child(play_button)
	next_button = _button(I18n.text("NEXT"), "next")
	controls.add_child(next_button)
	verify_button = _button(I18n.text("VERIFY INTEGRITY"), "verify")
	controls.add_child(verify_button)
	right.add_child(controls)
	body.add_child(AppTheme.panel(right))
	previous_button.disabled = true; next_button.disabled = true; play_button.disabled = true


func apply_model(model: Dictionary) -> void:
	var entries: Array = model.entries
	while list.item_count < entries.size():
		var entry: Dictionary = entries[list.item_count]
		list.add_item("#%03d  •  %s\n%s %s" % [int(entry.sequence),
			I18n.text(str(entry.get("commandType", "")).replace("_", " ").capitalize()), I18n.text("Step"), int(entry.get("step", 0))])
	if model.selected_index >= 0 and model.selected_index != _last_selected:
		list.select(model.selected_index); list.ensure_current_is_visible()
		_last_selected = model.selected_index
	progress.max_value = maxi(int(model.through_sequence) - 1, 1)
	progress.value = maxi(model.selected_index, 0)
	heading.text = I18n.text("Loading replay…") if entries.is_empty() else I18n.text("Command %s of %s") % [model.selected_index + 1, model.through_sequence]
	state_text.clear(); state_text.add_text(model.state_text)
	frame_text.clear(); frame_text.add_text(model.frame_text)
	previous_button.disabled = model.selected_index <= 0
	next_button.disabled = entries.is_empty() or (not model.has_more and model.selected_index >= entries.size() - 1)
	play_button.disabled = entries.is_empty()
	play_button.text = I18n.text("PAUSE REPLAY" if model.playing else "PLAY")
	verify_button.disabled = entries.is_empty() or model.verifying
	more_button.visible = model.has_more
	more_button.disabled = model.page_loading
func _button(text: String, action: String) -> Button:
	var value := AppTheme.button(text)
	value.pressed.connect(func(): intent_requested.emit(action, []))
	return value

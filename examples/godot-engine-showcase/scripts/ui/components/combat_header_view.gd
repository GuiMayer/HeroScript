extends HBoxContainer
signal timeline_requested
signal tools_requested
signal pause_requested
var tool_button: Button
func apply_model(model: Dictionary) -> void:
	for child in get_children(): remove_child(child); child.queue_free()
	var title := AppTheme.title(str(model.title), 24)
	title.tooltip_text = str(model.context)
	add_child(title)
	var spacer := Control.new()
	spacer.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	add_child(spacer)
	add_child(AppTheme.title(str(model.round), 18, AppTheme.GOLD))
	var timeline := AppTheme.button(str(model.timeline), 165)
	timeline.custom_minimum_size.y = 44
	timeline.pressed.connect(func(): timeline_requested.emit())
	add_child(timeline)
	tool_button = AppTheme.button(str(model.tools), 125)
	tool_button.custom_minimum_size.y = 44
	tool_button.visible = bool(model.allow_tools)
	tool_button.pressed.connect(func(): tools_requested.emit())
	add_child(tool_button)
	var pause := AppTheme.button(str(model.pause), 100)
	pause.custom_minimum_size.y = 44
	pause.pressed.connect(func(): pause_requested.emit())
	add_child(pause)

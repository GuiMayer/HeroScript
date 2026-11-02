extends Control
## Owns visual hosts and modality, not navigation or engine commands.
signal settings_requested
signal menu_requested
signal abandon_requested(choice: Dictionary)
signal resume_requested
const Layers = preload("res://scripts/ui/shell/overlay_layers.gd")
var visual_context
var run_scenery
var host: MarginContainer
var toast: Label
var pause_layer: Control
var pause_canvas: CanvasLayer
var _previous_focus: WeakRef
var _toast_motion: Tween
var _toast_surface: StyleBoxFlat
const PauseProfile = preload("res://data/themes/ivory_theme.tres")
var pause_context
var _history_dialog: AcceptDialog
func mount(context) -> void:
	visual_context = context
	run_scenery = get_node("%RunSceneryHost")
	run_scenery.mount(context)
	host = get_node("ScreenHost")
	for side in ["left","right"]: host.add_theme_constant_override("margin_"+side,roundi(context.profile.screen_margin.x))
	for side in ["top","bottom"]: host.add_theme_constant_override("margin_"+side,roundi(context.profile.screen_margin.y))
	toast = get_node("%Toast")
	if _toast_surface == null: _toast_surface = toast.get_theme_stylebox("normal").duplicate()
	_refresh_toast_style()
func _refresh_toast_style() -> void:
	_toast_surface.bg_color = visual_context.styles.color("surface.toast")
	_toast_surface.border_color = visual_context.styles.EMBER
	toast.add_theme_stylebox_override("normal",_toast_surface)
	if toast.has_meta("base_style_normal"): toast.remove_meta("base_style_normal")
	if toast.has_meta("visual_preferences_revision"): toast.remove_meta("visual_preferences_revision")
	visual_context.styles.apply_view_preferences(toast,visual_context.text_scale,visual_context.high_contrast)
func show_pause(abandon: Dictionary, conversation_history: Array = []) -> void:
	var viewport := get_viewport()
	var focused = viewport.gui_get_focus_owner() if viewport != null else null
	_previous_focus = weakref(focused) if focused != null else null
	pause_context = visual_context.derive(PauseProfile)
	AppTheme.scoped_profile(PauseProfile, func(): _build_pause(abandon, conversation_history.duplicate(true)))
	var resume: Button = pause_layer.find_child("ResumeButton", true, false)
	if resume.is_inside_tree():
		resume.grab_focus()
		preload("res://scripts/ui/focus_navigation.gd").queue(pause_layer)
func _build_pause(abandon: Dictionary, conversation_history: Array) -> void:
	pause_canvas = preload("res://scenes/ui/overlays/pause_menu.tscn").instantiate()
	pause_canvas.setup(pause_context,not abandon.is_empty(),not conversation_history.is_empty())
	pause_layer = pause_canvas.get_node("%PauseOverlay")
	pause_canvas.intent_requested.connect(func(action):
		match action:
			"resume": resume_requested.emit()
			"settings": settings_requested.emit()
			"history": _open_conversation_history(conversation_history)
			"abandon": confirm_abandon(abandon)
			"menu": menu_requested.emit()
	)
	add_child(pause_canvas)
func refresh_pause_visuals() -> void:
	if not is_instance_valid(pause_layer): return
	pause_context = visual_context.derive(PauseProfile)
	pause_canvas.apply_context(pause_context)
func close_pause() -> void:
	# Windows detach from their embedded viewport immediately with the pause canvas.
	if is_instance_valid(_history_dialog):
		_history_dialog.get_parent().remove_child(_history_dialog)
		_history_dialog.queue_free()
	_history_dialog = null
	if is_instance_valid(pause_canvas):
		remove_child(pause_canvas)
		pause_canvas.queue_free()
	pause_canvas = null
	pause_layer = null
	pause_context = null
	if is_inside_tree() and _previous_focus and is_instance_valid(_previous_focus.get_ref()) and _previous_focus.get_ref().is_inside_tree():
		_previous_focus.get_ref().grab_focus()
	_previous_focus = null
func _open_conversation_history(entries: Array) -> void:
	if not is_instance_valid(pause_layer) or entries.is_empty(): return
	if is_instance_valid(_history_dialog): return
	var lines: Array[String] = []
	for entry in entries:
		var speaker := str(entry.get("speaker", ""))
		lines.append((speaker + ": " if not speaker.is_empty() else "") + str(entry.get("text", "")))
	var dialog = preload("res://scenes/ui/overlays/conversation_history.tscn").instantiate()
	dialog.visual_context = pause_context
	_history_dialog = dialog
	dialog.name = "ConversationHistoryDialog"
	dialog.process_mode = Node.PROCESS_MODE_ALWAYS
	dialog.exclusive = true
	dialog.setup(pause_context.translator.text("CONVERSATION HISTORY"), "\n\n".join(lines))
	dialog.ok_button_text = pause_context.translator.text("BACK")
	dialog.tree_exited.connect(func():
		if is_instance_valid(pause_layer):
			var button: Button = pause_layer.find_child("ConversationHistoryButton", true, false)
			if is_instance_valid(button) and button.is_inside_tree(): button.grab_focus())
	pause_layer.add_child(dialog)
	dialog.popup_centered()
func confirm_abandon(choice: Dictionary) -> void:
	if not is_instance_valid(pause_layer): return
	var dialog := (preload("res://scenes/ui/overlays/confirmation_dialog.tscn").instantiate() as ConfirmationDialog)
	dialog.theme = pause_layer.theme
	dialog.process_mode = Node.PROCESS_MODE_ALWAYS
	dialog.title = visual_context.translator.text("Confirm choice")
	dialog.dialog_text = visual_context.translator.text("This ends the current journey. Its history remains available.")
	dialog.ok_button_text = visual_context.translator.text("ABANDON RUN")
	dialog.cancel_button_text = visual_context.translator.text("CANCEL")
	dialog.confirmed.connect(func(): abandon_requested.emit(choice.duplicate(true)))
	dialog.visibility_changed.connect(func(): if not dialog.visible: dialog.queue_free())
	pause_layer.add_child(dialog)
	dialog.popup_centered(Vector2i(480, 190))
func show_toast(message: String, error: bool) -> void:
	if _toast_motion: _toast_motion.kill()
	toast.text = message
	toast.add_theme_color_override("font_color", AppTheme.BLOOD if error else AppTheme.INK)
	toast.visible = true
	_toast_motion = create_tween()
	_toast_motion.tween_interval(2.5)
	_toast_motion.tween_callback(func(): toast.visible = false)
func apply_visual_profile() -> void:
	# Notices live outside the screen host; avoid stale dark styles on menu paper.
	for side in ["left", "right"]: host.add_theme_constant_override("margin_" + side, roundi(visual_context.profile.screen_margin.x))
	for side in ["top", "bottom"]: host.add_theme_constant_override("margin_" + side, roundi(visual_context.profile.screen_margin.y))
	_refresh_toast_style()
func dispose() -> void:
	if _toast_motion: _toast_motion.kill()
	close_pause()
func _exit_tree() -> void: dispose()

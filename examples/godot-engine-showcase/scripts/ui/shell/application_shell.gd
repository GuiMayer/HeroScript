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
const PauseProfile = preload("res://data/themes/ivory_theme.tres")
var pause_context
func mount(context) -> void:
	visual_context = context
	run_scenery = preload("res://scripts/ui/shell/run_scenery_host.gd").new()
	add_child(run_scenery)
	run_scenery.mount(context)
	move_child(run_scenery, 0)
	set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	host = get_node("ScreenHost")
	host.process_mode = Node.PROCESS_MODE_PAUSABLE
	for side in ["left", "right"]: host.add_theme_constant_override("margin_" + side, roundi(context.profile.screen_margin.x))
	for side in ["top", "bottom"]: host.add_theme_constant_override("margin_" + side, roundi(context.profile.screen_margin.y))
	var notices := CanvasLayer.new()
	notices.layer = Layers.NOTIFICATION
	add_child(notices)
	toast = Label.new()
	toast.visible = false
	toast.mouse_filter = Control.MOUSE_FILTER_IGNORE
	toast.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	toast.set_anchors_preset(Control.PRESET_CENTER_TOP)
	toast.position = Vector2(-260, 18)
	toast.size = Vector2(520, 52)
	toast.add_theme_stylebox_override("normal", AppTheme.box(AppTheme.color("surface.toast"), 10, AppTheme.EMBER, 1))
	toast.add_theme_font_size_override("font_size", 16)
	notices.add_child(toast)
func show_pause(abandon: Dictionary) -> void:
	_previous_focus = weakref(get_viewport().gui_get_focus_owner()) if get_viewport().gui_get_focus_owner() else null
	pause_context = visual_context.derive(PauseProfile)
	AppTheme.scoped_profile(PauseProfile, func(): _build_pause(abandon))
	var resume: Button = pause_layer.find_child("ResumeButton", true, false)
	resume.grab_focus()
	preload("res://scripts/ui/focus_navigation.gd").queue(pause_layer)
func _build_pause(abandon: Dictionary) -> void:
	pause_canvas = CanvasLayer.new()
	pause_canvas.name = "PauseCanvas"
	pause_canvas.layer = Layers.MODAL
	pause_canvas.process_mode = Node.PROCESS_MODE_ALWAYS
	add_child(pause_canvas)
	pause_layer = ColorRect.new()
	pause_layer.name = "PauseOverlay"
	pause_layer.theme = AppTheme.build(pause_context.high_contrast)
	pause_layer.process_mode = Node.PROCESS_MODE_ALWAYS
	pause_layer.color = AppTheme.color("surface.modal_scrim")
	pause_layer.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	pause_canvas.add_child(pause_layer)
	var center := CenterContainer.new()
	center.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	pause_layer.add_child(center)
	var content := VBoxContainer.new()
	content.custom_minimum_size = Vector2(420, 0)
	content.add_theme_constant_override("separation", 13)
	var trace := preload("res://scripts/ui/components/circuit_trace.gd").new()
	trace.name = "PauseCircuit"
	trace.custom_minimum_size.y = 80
	trace.simplified = pause_context.high_contrast
	content.add_child(trace)
	var i18n = pause_context.translator
	content.add_child(AppTheme.title(i18n.text("PAUSE"), 42))
	content.add_child(AppTheme.muted(i18n.text("Take your time. Your progress is preserved while the game is paused."), 15, 380))
	var resume := _button(i18n.text("RESUME"), resume_requested.emit)
	resume.name = "ResumeButton"
	resume.theme_type_variation = "PrimaryAction"
	content.add_child(resume)
	content.add_child(_button(i18n.text("SETTINGS"), settings_requested.emit))
	if not abandon.is_empty():
		var button := _button(i18n.text("ABANDON RUN"), func(): confirm_abandon(abandon))
		button.name = "AbandonRunButton"
		button.theme_type_variation = "DangerAction"
		content.add_child(button)
	content.add_child(_button(i18n.text("MAIN MENU"), menu_requested.emit))
	center.add_child(AppTheme.panel(content))
	AppTheme.apply_view_preferences(pause_layer, pause_context.text_scale, pause_context.high_contrast)
func refresh_pause_visuals() -> void:
	if not is_instance_valid(pause_layer): return
	pause_context = visual_context.derive(PauseProfile)
	AppTheme.scoped_profile(PauseProfile, func():
		pause_layer.theme = AppTheme.build(pause_context.high_contrast)
		pause_layer.color = AppTheme.color("surface.modal_scrim")
		AppTheme.apply_view_preferences(pause_layer, pause_context.text_scale, pause_context.high_contrast)
		pause_layer.find_child("PauseCircuit", true, false).simplified = pause_context.high_contrast)
func close_pause() -> void:
	if is_instance_valid(pause_canvas):
		remove_child(pause_canvas)
		pause_canvas.queue_free()
	pause_canvas = null
	pause_layer = null
	pause_context = null
	if is_inside_tree() and _previous_focus and is_instance_valid(_previous_focus.get_ref()) and _previous_focus.get_ref().is_inside_tree():
		_previous_focus.get_ref().grab_focus()
	_previous_focus = null
func confirm_abandon(choice: Dictionary) -> void:
	if not is_instance_valid(pause_layer): return
	var dialog := ConfirmationDialog.new()
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
	toast.add_theme_stylebox_override("normal", AppTheme.box(AppTheme.color("surface.toast"), AppTheme.profile.corner_radius, AppTheme.EMBER, 1))
	if toast.has_meta("base_style_normal"): toast.remove_meta("base_style_normal")
	AppTheme.apply_view_preferences(toast, visual_context.text_scale, visual_context.high_contrast)
func _button(text: String, action: Callable) -> Button:
	var value := AppTheme.button(text, 360)
	value.pressed.connect(action)
	return value
func dispose() -> void:
	if _toast_motion: _toast_motion.kill()
	close_pause()
func _exit_tree() -> void: dispose()

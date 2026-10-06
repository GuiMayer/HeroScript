extends Control

const SettingsScreen = preload("res://scripts/ui/settings_screen.gd")
const SandboxScreen = preload("res://scripts/ui/sandbox_screen.gd")
const CodexScreen = preload("res://scripts/ui/codex_screen.gd")
const ActivityScreen = preload("res://scripts/ui/activity_screen.gd")
const CombatScreen = preload("res://scripts/ui/combat_screen.gd")
const TimelineScreen = preload("res://scripts/ui/timeline_screen.gd")
const RunHistoryScreen = preload("res://scripts/ui/run_history_screen.gd")
const RunReplayScreen = preload("res://scripts/ui/run_replay_screen.gd")

var host: MarginContainer
var toast: Label
var pause_layer: Control
var presentation: Dictionary = {}
var current_screen := "menu"
var navigation_epoch := 0
var previous_focus: WeakRef
var settings_return_to_gameplay := false

func _ready() -> void:
	process_mode = Node.PROCESS_MODE_ALWAYS
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--locale="):
			I18n.set_locale(argument.trim_prefix("--locale="))
	theme = AppTheme.build(Preferences.high_contrast)
	_load_presentation()
	var background := EmberBackground.new()
	background.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(background)
	host = MarginContainer.new()
	host.process_mode = Node.PROCESS_MODE_PAUSABLE
	host.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	host.add_theme_constant_override("margin_left", 48)
	host.add_theme_constant_override("margin_top", 24)
	host.add_theme_constant_override("margin_right", 48)
	host.add_theme_constant_override("margin_bottom", 24)
	add_child(host)
	host.child_entered_tree.connect(_queue_focus_preparation)
	toast = Label.new()
	toast.visible = false
	toast.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	toast.set_anchors_preset(Control.PRESET_CENTER_TOP)
	toast.position = Vector2(-260, 18)
	toast.size = Vector2(520, 52)
	toast.add_theme_stylebox_override("normal", AppTheme.box(Color("#281e2be8"), 10, AppTheme.EMBER, 1))
	toast.add_theme_font_size_override("font_size", 16)
	add_child(toast)
	GameSession.failed.connect(func(error): show_error(I18n.error(error)))
	GameSession.changed.connect(_on_session_changed)
	Preferences.changed.connect(_apply_preferences)
	I18n.locale_changed.connect(_on_locale_changed)
	_show_main_menu()
	resized.connect(_refresh_layout_navigation)
	if "--layout-smoke" in OS.get_cmdline_user_args(): return
	if "--ui-smoke" in OS.get_cmdline_user_args():
		add_child(preload("res://tests/usability.gd").new())
		return
	if "--smoke" in OS.get_cmdline_user_args():
		add_child(preload("res://tests/smoke.gd").new())
		return
	if "--capture-combat" in OS.get_cmdline_user_args():
		call_deferred("_prepare_combat_capture")
		return
	call_deferred("_probe_engine")

func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("pause_game") and current_screen in ["activity", "combat", "timeline"]:
		toggle_pause()
		get_viewport().set_input_as_handled()
	elif event.is_action_pressed("open_timeline") and current_screen == "combat":
		show_timeline()
		get_viewport().set_input_as_handled()

func _load_presentation() -> void:
	var file := FileAccess.open("res://data/presentation.json", FileAccess.READ)
	if file:
		var value = JSON.parse_string(file.get_as_text())
		file.close()
		if value is Dictionary:
			var core_file := "res://data/volatile_core_presentation.json"
			if FileAccess.file_exists(core_file):
				var core = JSON.parse_string(FileAccess.get_file_as_string(core_file))
				if core is Dictionary:
					for category in core:
						if not value.has(category): value[category] = {}
						value[category].merge(core[category], true)
			presentation = I18n.presentation(value)

func _probe_engine() -> void:
	await GameSession.connect_engine()
	if current_screen == "menu":
		_show_main_menu()

func _prepare_combat_capture() -> void:
	if await GameSession.connect_engine() and await GameSession.start_campaign(int(Time.get_unix_time_from_system()) % 80000000 + 10000000):
		var start := GameSession.command("START_ENCOUNTER")
		if not start.is_empty() and await GameSession.execute_run_command("START_ENCOUNTER", start.get("validPayload", {})):
			show_combat()
	set_meta("capture_ready", true)

func _screen() -> VBoxContainer:
	_clear_host()
	var root := VBoxContainer.new()
	root.add_theme_constant_override("separation", 20)
	host.add_child(root)
	return root

func _clear_host() -> void:
	navigation_epoch += 1
	for child in host.get_children():
		host.remove_child(child)
		child.queue_free()

func _show_main_menu() -> void:
	current_screen = "menu"
	var root := _screen()
	var status_row := HBoxContainer.new()
	status_row.alignment = BoxContainer.ALIGNMENT_END
	var status := Label.new()
	status.text = I18n.text("●  ENGINE ONLINE") if GameSession.available else I18n.text("●  CONNECTING")
	status.add_theme_color_override("font_color", AppTheme.TEAL if GameSession.available else AppTheme.GOLD)
	status.add_theme_font_size_override("font_size", 13)
	status_row.add_child(status)
	root.add_child(status_row)
	var spacer := Control.new()
	spacer.size_flags_vertical = Control.SIZE_EXPAND_FILL
	root.add_child(spacer)
	var center := HBoxContainer.new()
	center.alignment = BoxContainer.ALIGNMENT_CENTER
	root.add_child(center)
	var card_content := VBoxContainer.new()
	card_content.custom_minimum_size = Vector2(540, 0)
	card_content.add_theme_constant_override("separation", 12)
	var eyebrow := AppTheme.muted(I18n.text("A DATA-DRIVEN SHOWCASE"), 14)
	eyebrow.add_theme_color_override("font_color", AppTheme.EMBER)
	card_content.add_child(eyebrow)
	card_content.add_child(AppTheme.title("EMBER ARCHIVE", 52, AppTheme.INK))
	card_content.add_child(AppTheme.muted(
		I18n.text("A short ascent powered by HeroScript. Cards, enemies, resources, map and flow live in JSON; Godot brings each decision to life."), 17))
	var line := HSeparator.new()
	card_content.add_child(line)
	card_content.add_child(_button(I18n.text("NEW JOURNEY"), _new_campaign, 420))
	var continue_button := _button(I18n.text("CONTINUE"), _continue_campaign, 420)
	continue_button.disabled = Preferences.last_run_id.is_empty()
	card_content.add_child(continue_button)
	if GameSession.has_pending_command:
		card_content.add_child(_button(I18n.text("RECONNECT"), _recover_session, 420))
	var history_button := _button(I18n.text("JOURNEY HISTORY"), show_history, 420)
	history_button.name = "HistoryButton"
	history_button.disabled = not GameSession.available
	card_content.add_child(history_button)
	card_content.add_child(_button(I18n.text("RULES LAB"), show_sandbox, 420))
	card_content.add_child(_button(I18n.text("CONTENT CODEX"), show_codex, 420))
	card_content.add_child(_button(I18n.text("SETTINGS"), show_settings, 420))
	card_content.add_child(_button(I18n.text("QUIT"), get_tree().quit, 420))
	center.add_child(AppTheme.panel(card_content, Color("#171929e8")))
	var bottom := AppTheme.muted(I18n.text("HeroScript decides  •  REST connects  •  Godot presents"), 13)
	bottom.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	root.add_child(bottom)

func _new_campaign() -> void:
	var epoch := navigation_epoch
	if GameSession.busy or GameSession.has_pending_command:
		return
	show_toast(I18n.text("Preparing your journey…"))
	var ok := await GameSession.start_campaign(Preferences.next_campaign_seed())
	if ok and epoch == navigation_epoch:
		open_game()

func _continue_campaign() -> void:
	var epoch := navigation_epoch
	show_toast(I18n.text("Restoring your journey…"))
	if await GameSession.continue_run(Preferences.last_run_id) and epoch == navigation_epoch:
		open_game()

func _recover_session() -> void:
	var epoch := navigation_epoch
	var accepted := await GameSession.refresh()
	if accepted and epoch == navigation_epoch: open_game()

func open_game() -> void:
	if GameSession.run.is_empty():
		_show_main_menu()
		return
	if not GameSession.combat.is_empty() and str(GameSession.combat.get("status", "ACTIVE")) == "ACTIVE":
		show_combat()
	else:
		show_activity()

func show_activity() -> void:
	if current_screen == "activity" and host.get_child_count() == 1:
		var existing = host.get_child(0)
		if existing.has_method("refresh_state"):
			existing.refresh_state(presentation)
			return
	current_screen = "activity"
	_clear_host()
	var screen = ActivityScreen.new()
	screen.setup(self, presentation)
	host.add_child(screen)

func show_combat() -> void:
	if current_screen == "combat" and host.get_child_count() == 1:
		var existing = host.get_child(0)
		if existing.has_method("refresh_state"):
			existing.refresh_state(presentation)
			return
	current_screen = "combat"
	_clear_host()
	var screen = CombatScreen.new()
	screen.setup(self, presentation)
	host.add_child(screen)

func show_timeline() -> void:
	if get_tree().paused or GameSession.busy:
		return
	current_screen = "timeline"
	_clear_host()
	var screen = TimelineScreen.new()
	screen.setup(self)
	host.add_child(screen)

func show_history() -> void:
	if GameSession.busy: return
	current_screen = "history"
	_clear_host()
	var screen = RunHistoryScreen.new()
	screen.setup(self)
	host.add_child(screen)

func show_run_replay(run_id: String, from_history := false) -> void:
	if GameSession.busy or run_id.is_empty(): return
	current_screen = "run_replay"
	_clear_host()
	var screen = RunReplayScreen.new()
	screen.setup(self, run_id, from_history)
	host.add_child(screen)

func show_sandbox() -> void:
	current_screen = "sandbox"
	_clear_host()
	var screen = SandboxScreen.new()
	screen.setup(self, presentation)
	host.add_child(screen)

func show_codex() -> void:
	current_screen = "codex"
	_clear_host()
	var screen = CodexScreen.new()
	screen.setup(self)
	host.add_child(screen)

func show_settings(return_to_gameplay := false) -> void:
	settings_return_to_gameplay = return_to_gameplay
	current_screen = "settings"
	_clear_host()
	var screen = SettingsScreen.new()
	screen.setup(self, open_game if return_to_gameplay else _show_main_menu)
	host.add_child(screen)

func back_to_menu() -> void:
	_show_main_menu()

func toggle_pause() -> void:
	if is_instance_valid(pause_layer):
		get_tree().paused = false
		pause_layer.queue_free()
		pause_layer = null
		if previous_focus and is_instance_valid(previous_focus.get_ref()): previous_focus.get_ref().grab_focus()
		return
	previous_focus = weakref(get_viewport().gui_get_focus_owner()) if get_viewport().gui_get_focus_owner() else null
	pause_layer = ColorRect.new()
	pause_layer.process_mode = Node.PROCESS_MODE_ALWAYS
	pause_layer.color = Color("#080912dd")
	pause_layer.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(pause_layer)
	get_tree().paused = true
	var center := CenterContainer.new()
	center.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	pause_layer.add_child(center)
	var content := VBoxContainer.new()
	content.custom_minimum_size = Vector2(420, 0)
	content.add_theme_constant_override("separation", 13)
	content.add_child(AppTheme.title(I18n.text("PAUSE"), 42, AppTheme.GOLD))
	content.add_child(AppTheme.muted(I18n.text("Take your time. Your progress is preserved while the game is paused.")))
	content.add_child(_button(I18n.text("RESUME"), toggle_pause, 360))
	content.add_child(_button(I18n.text("SETTINGS"), _pause_settings, 360))
	var abandon := _abandon_choice()
	if not abandon.is_empty():
		var abandon_button := _button(I18n.text("ABANDON RUN"), func(): _confirm_pause_abandon(abandon), 360)
		abandon_button.name = "AbandonRunButton"
		abandon_button.add_theme_color_override("font_color", AppTheme.BLOOD)
		content.add_child(abandon_button)
	content.add_child(_button(I18n.text("MAIN MENU"), _pause_menu, 360))
	center.add_child(AppTheme.panel(content))
	_queue_focus_preparation(pause_layer)

func _pause_settings() -> void:
	toggle_pause()
	show_settings(true)

func _pause_menu() -> void:
	toggle_pause()
	_show_main_menu()

func _abandon_choice() -> Dictionary:
	if GameSession.run.is_empty() or str(GameSession.run.get("lifecycle", "Active")).to_lower() != "active": return {}
	for choice in GameSession.activity_choices():
		if str(choice.get("type", "")) == "ABANDON_RUN": return choice.duplicate(true)
	return {}

func _confirm_pause_abandon(choice: Dictionary) -> void:
	if not is_instance_valid(pause_layer) or GameSession.busy: return
	var dialog := ConfirmationDialog.new()
	dialog.process_mode = Node.PROCESS_MODE_ALWAYS
	dialog.title = I18n.text("Confirm choice")
	dialog.dialog_text = I18n.text("This ends the current journey. Its history remains available.")
	dialog.ok_button_text = I18n.text("ABANDON RUN")
	dialog.cancel_button_text = I18n.text("CANCEL")
	dialog.confirmed.connect(func(): _abandon_from_pause(choice))
	dialog.visibility_changed.connect(func(): if not dialog.visible: dialog.queue_free())
	pause_layer.add_child(dialog)
	dialog.popup_centered(Vector2i(480, 190))

func _abandon_from_pause(choice: Dictionary) -> void:
	if GameSession.busy: return
	toggle_pause()
	show_toast(I18n.text("Ending the current journey…"))
	if await GameSession.submit_activity(choice) and is_inside_tree(): open_game()

func show_toast(message: String, error := false) -> void:
	toast.text = message
	toast.add_theme_color_override("font_color", AppTheme.BLOOD if error else AppTheme.INK)
	toast.visible = true
	var tween := create_tween()
	tween.tween_interval(2.5)
	tween.tween_callback(func(): toast.visible = false)

func show_error(message: String) -> void:
	show_toast(message, true)

func _button(text: String, action: Callable, width := 0) -> Button:
	var value := AppTheme.button(text, width)
	value.pressed.connect(func():
		GameAudio.ui()
		action.call())
	return value

func _on_session_changed() -> void:
	if current_screen == "combat" and host.get_child_count() > 0:
		var screen = host.get_child(0)
		if screen.has_method("_update_controls"):
			screen._update_controls()

func _apply_preferences() -> void:
	theme = AppTheme.build(Preferences.high_contrast)
	AppTheme.apply_view_preferences(host)
	if is_instance_valid(pause_layer): AppTheme.apply_view_preferences(pause_layer)

func _on_locale_changed() -> void:
	_load_presentation()
	call_deferred("_rebuild_localized_screen")

func _rebuild_localized_screen() -> void:
	match current_screen:
		"settings": show_settings(settings_return_to_gameplay)
		"combat": show_combat()
		"activity": show_activity()
		"menu": _show_main_menu()

func _queue_focus_preparation(screen: Node) -> void:
	_prepare_focus.call_deferred(weakref(screen))

func _prepare_focus(reference: WeakRef) -> void:
	var screen = reference.get_ref()
	if not is_instance_valid(screen) or not screen is Control: return
	await get_tree().process_frame
	if not is_instance_valid(screen) or not screen.is_inside_tree(): return
	AppTheme.apply_view_preferences(screen)
	await get_tree().process_frame
	if is_instance_valid(screen) and screen.is_inside_tree():
		preload("res://scripts/ui/focus_navigation.gd").wire(screen, true)

func _refresh_layout_navigation() -> void:
	await get_tree().process_frame
	if not is_inside_tree(): return
	var scope: Control = pause_layer if is_instance_valid(pause_layer) else host
	preload("res://scripts/ui/focus_navigation.gd").wire(scope)

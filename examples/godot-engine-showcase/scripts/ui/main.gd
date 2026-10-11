extends Control

const SettingsScreen = preload("res://scripts/ui/settings_screen.gd")
const SandboxScreen = preload("res://scripts/ui/sandbox_screen.gd")
const CodexScreen = preload("res://scripts/ui/codex_screen.gd")
const ActivityScreen = preload("res://scripts/ui/activity_screen.gd")
const CombatScreen = preload("res://scripts/ui/combat_screen.gd")
const TimelineScreen = preload("res://scripts/ui/timeline_screen.gd")
const RunHistoryScreen = preload("res://scripts/ui/run_history_screen.gd")
const RunReplayScreen = preload("res://scripts/ui/run_replay_screen.gd")

var visual_context
var shell
var host: MarginContainer:
	get: return shell.host
var toast: Label:
	get: return shell.toast
var pause_layer: Control:
	get: return shell.pause_layer if shell else null
var pause_canvas: CanvasLayer:
	get: return shell.pause_canvas if shell else null
var presentation: Dictionary = {}
var current_screen := "menu"
var navigation_epoch := 0
var settings_return_to_gameplay := false
var continuation_run_id := ""

func _ready() -> void:
	visual_context = preload("res://scripts/ui/visual_context.gd").from_nodes(Preferences, I18n)
	AppTheme.configure_preferences(Preferences.text_scale, Preferences.high_contrast)
	process_mode = Node.PROCESS_MODE_ALWAYS
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--locale="):
			I18n.set_locale(argument.trim_prefix("--locale="))
	theme = AppTheme.build(Preferences.high_contrast)
	_load_presentation()
	var background := EmberBackground.new()
	background.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(background)
	shell = preload("res://scenes/ui/application_shell.tscn").instantiate()
	add_child(shell)
	shell.mount(visual_context)
	shell.theme = theme
	shell.resume_requested.connect(toggle_pause)
	shell.settings_requested.connect(_pause_settings)
	shell.menu_requested.connect(_pause_menu)
	shell.abandon_requested.connect(_abandon_from_pause)
	host.child_entered_tree.connect(_queue_focus_preparation)
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
			for overlay_path in ["res://data/volatile_core_presentation.json", "res://data/world_one_presentation.json"]:
				if not FileAccess.file_exists(overlay_path): continue
				var overlay = JSON.parse_string(FileAccess.get_file_as_string(overlay_path))
				if overlay is Dictionary:
					for category in overlay:
						if not value.has(category): value[category] = {}
						value[category].merge(overlay[category], true)
			presentation = I18n.presentation(value)

func _probe_engine() -> void:
	if await GameSession.connect_engine(Preferences.selected_setting_id):
		Preferences.selected_setting_id = GameSession.selected_setting_id
		Preferences.save_session()
	if current_screen == "menu":
		_show_main_menu()

func _prepare_combat_capture() -> void:
	if await GameSession.connect_engine(Preferences.selected_setting_id) and await GameSession.start_campaign(int(Time.get_unix_time_from_system()) % 80000000 + 10000000):
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
	continuation_run_id = ""
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
	var chosen_setting := GameSession.selected_setting
	var setting_name := I18n.text(str(chosen_setting.get("displayName", "Ember Archive")))
	var setting_description := str(chosen_setting.get("description",
		"A short ascent powered by HeroScript. Cards, enemies, resources, map and flow live in JSON; Godot brings each decision to life."))
	card_content.add_child(AppTheme.title(setting_name.to_upper(), 52, AppTheme.INK))
	card_content.add_child(AppTheme.muted(I18n.text(setting_description), 17))
	var line := HSeparator.new()
	card_content.add_child(line)
	card_content.add_child(AppTheme.muted(I18n.text("GAME SETTING"), 13))
	var setting_selector := OptionButton.new()
	setting_selector.name = "SettingSelector"
	setting_selector.custom_minimum_size = Vector2(420, 46)
	for setting in GameSession.settings:
		setting_selector.add_item(I18n.text(str(setting.get("displayName", setting.get("settingId", "Setting")))))
		setting_selector.set_item_metadata(setting_selector.item_count - 1, str(setting.get("settingId", "")))
		if str(setting.get("settingId", "")) == GameSession.selected_setting_id:
			setting_selector.select(setting_selector.item_count - 1)
	setting_selector.disabled = not GameSession.available or setting_selector.item_count < 2
	setting_selector.item_selected.connect(_select_setting_from_menu.bind(setting_selector))
	card_content.add_child(setting_selector)
	card_content.add_child(AppTheme.muted(I18n.text("A setting swaps the complete rules and content package used by a new journey."), 13))
	card_content.add_child(_button(I18n.text("NEW JOURNEY"), _new_campaign, 420))
	var continue_button := _button(I18n.text("CONTINUE"), _continue_campaign, 420)
	continue_button.name = "ContinueButton"
	continue_button.disabled = true
	continue_button.tooltip_text = I18n.text("Continue the selected setting's journey.")
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
	center.add_child(AppTheme.panel(card_content, AppTheme.color("surface.menu")))
	var bottom := AppTheme.muted(I18n.text("HeroScript decides  •  REST connects  •  Godot presents"), 13)
	bottom.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	root.add_child(bottom)
	if GameSession.available:
		_resolve_menu_resume.call_deferred(weakref(continue_button), navigation_epoch)

func _resolve_menu_resume(reference: WeakRef, epoch: int) -> void:
	var before := JSON.stringify(Preferences.resume_index.runs)
	var result: Dictionary = await GameSession.find_resume(Preferences.resume_index, Preferences.api_url, Preferences.last_run_id)
	if epoch != navigation_epoch or current_screen != "menu": return
	var button = reference.get_ref()
	if not is_instance_valid(button): return
	if not result.ok:
		button.tooltip_text = I18n.error(result)
		return
	if bool(result.get("legacyChecked", false)): Preferences.last_run_id = ""
	if before != JSON.stringify(Preferences.resume_index.runs) or bool(result.get("legacyChecked", false)):
		Preferences.save_session()
	continuation_run_id = str(result.get("runId", ""))
	button.disabled = continuation_run_id.is_empty()
	if button.disabled: button.tooltip_text = I18n.text("No active journey in this setting. Start a new journey.")

func _select_setting_from_menu(index: int, selector: OptionButton) -> void:
	var setting_id := str(selector.get_item_metadata(index))
	if setting_id == GameSession.selected_setting_id:
		return
	if not GameSession.select_setting(setting_id):
		for item in selector.item_count:
			if str(selector.get_item_metadata(item)) == GameSession.selected_setting_id: selector.select(item)
		show_error(I18n.text("Finish or recover the current operation before changing the setting."))
		return
	Preferences.selected_setting_id = setting_id
	Preferences.save_session()
	call_deferred("_show_main_menu")

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
	if continuation_run_id.is_empty() or GameSession.busy: return
	show_toast(I18n.text("Restoring your journey…"))
	var mode := str(GameSession.selected_setting.get("launch", {}).get("modeId", ""))
	if await GameSession.continue_run(continuation_run_id, mode) and epoch == navigation_epoch:
		open_game()
	elif epoch == navigation_epoch:
		_show_main_menu()

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
		shell.close_pause()
		return
	get_tree().paused = true
	shell.show_pause(_abandon_choice())

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
	if not GameSession.busy: shell.confirm_abandon(choice)

func _abandon_from_pause(choice: Dictionary) -> void:
	if GameSession.busy: return
	toggle_pause()
	show_toast(I18n.text("Ending the current journey…"))
	if await GameSession.submit_activity(choice) and is_inside_tree(): open_game()

func show_toast(message: String, error := false) -> void:
	shell.show_toast(message, error)

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
	visual_context.update_from(Preferences, I18n, AppTheme.profile)
	AppTheme.configure_preferences(Preferences.text_scale, Preferences.high_contrast)
	theme = AppTheme.build(Preferences.high_contrast)
	shell.theme = theme
	AppTheme.apply_view_preferences(host)
	if is_instance_valid(pause_layer):
		pause_layer.theme = theme
		AppTheme.apply_view_preferences(pause_layer)

func _on_locale_changed() -> void:
	visual_context.update_from(Preferences, I18n, AppTheme.profile)
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


func _exit_tree() -> void:
	if GameSession.changed.is_connected(_on_session_changed): GameSession.changed.disconnect(_on_session_changed)
	if Preferences.changed.is_connected(_apply_preferences): Preferences.changed.disconnect(_apply_preferences)
	if I18n.locale_changed.is_connected(_on_locale_changed): I18n.locale_changed.disconnect(_on_locale_changed)
	if get_tree(): get_tree().paused = false

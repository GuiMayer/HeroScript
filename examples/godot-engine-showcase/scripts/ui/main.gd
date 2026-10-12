extends Control

const SettingsScreen = preload("res://scripts/ui/settings_screen.gd")
const SandboxScreen = preload("res://scripts/ui/sandbox_screen.gd")
const CodexScreen = preload("res://scripts/ui/codex_screen.gd")
const ActivityScreen = preload("res://scripts/ui/activity_screen.gd")
const DialogueScreen = preload("res://scripts/ui/dialogue_screen.gd")
const CombatScreen = preload("res://scripts/ui/combat_screen.gd")
const TimelineScreen = preload("res://scripts/ui/timeline_screen.gd")
const RunHistoryScreen = preload("res://scripts/ui/run_history_screen.gd")
const RunReplayScreen = preload("res://scripts/ui/run_replay_screen.gd")
const MainMenuView = preload("res://scripts/ui/main_menu_view.gd")
const MenuProfile = preload("res://data/themes/ivory_theme.tres")

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
var replay_from_history := false
var _dialogue_return_scope := ""
var dialogue_return_barrier: bool:
	get: return _dialogue_return_scope == _run_navigation_scope() and not _dialogue_return_scope.is_empty()
	set(value): _dialogue_return_scope = _run_navigation_scope() if value else ""
func _run_navigation_scope() -> String:
	return JSON.stringify([GameSession.run.get("runId", ""), GameSession.run.get("branchId", ""), GameSession.run.get("currentNodeId", "")])
var _probing_engine := false
const MENU_ROUTES := ["menu", "settings", "history", "sandbox", "codex"]
var _entry_profile: Resource
var _gameplay_profile: Resource
var _menu_paper: ColorRect

func _ready() -> void:
	_entry_profile = AppTheme.profile
	_gameplay_profile = _entry_profile
	visual_context = preload("res://scripts/ui/visual_context.gd").from_nodes(Preferences, I18n)
	AppTheme.configure_preferences(Preferences.text_scale, Preferences.high_contrast)
	process_mode = Node.PROCESS_MODE_ALWAYS
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--locale="):
			I18n.set_locale(argument.trim_prefix("--locale="))
	theme = AppTheme.build(Preferences.high_contrast)
	_load_presentation()
	visual_context.setting_id = GameSession.selected_setting_id
	_menu_paper = ColorRect.new()
	_menu_paper.name = "MenuPaper"
	_menu_paper.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_menu_paper.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(_menu_paper)
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
	if event.is_action_pressed("pause_game") and current_screen in ["activity", "combat", "timeline", "dialogue"]:
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
	visual_context.set_resource_styles(presentation.get("resources", {}))

func _probe_engine() -> void:
	if _probing_engine or GameSession.busy or GameSession.has_pending_command: return
	_probing_engine = true
	if current_screen == "menu": _show_main_menu()
	if await GameSession.connect_engine(Preferences.selected_setting_id):
		Preferences.selected_setting_id = GameSession.selected_setting_id
		visual_context.setting_id = GameSession.selected_setting_id
		Preferences.save_session()
	_probing_engine = false
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
		if child.has_method("dispose"): child.dispose()
		host.remove_child(child)
		child.queue_free()
	_apply_route_profile()

func _apply_route_profile() -> void:
	var menu_route := _is_menu_route()
	var desired: Resource = MenuProfile if menu_route else _gameplay_profile
	if AppTheme.profile != desired:
		AppTheme.set_profile(desired)
		_apply_preferences()
	shell.run_scenery.set_active(not menu_route)
	_menu_paper.visible = menu_route
	_menu_paper.color = AppTheme.NIGHT

func _is_menu_route() -> bool:
	return current_screen in MENU_ROUTES or (current_screen == "run_replay" and replay_from_history)

func _show_main_menu() -> void:
	current_screen = "menu"
	continuation_run_id = ""
	_clear_host()
	var view = MainMenuView.new()
	view.mount(visual_context)
	var chosen_setting := GameSession.selected_setting
	var setting_name := I18n.text(str(chosen_setting.get("displayName", "Ember Archive")))
	var setting_description := str(chosen_setting.get("description",
		"A short ascent powered by HeroScript. Cards, enemies, resources, map and flow live in JSON; Godot brings each decision to life."))
	view.apply_model({"settingName": setting_name, "description": I18n.text(setting_description),
		"settings": GameSession.settings, "selectedSettingId": GameSession.selected_setting_id,
		"available": GameSession.available, "pending": GameSession.has_pending_command, "connecting": _probing_engine})
	view.setting_requested.connect(_select_setting_from_menu.bind(view.setting_selector))
	view.action_requested.connect(_menu_action)
	AppTheme.apply_view_preferences(view, visual_context.text_scale, visual_context.high_contrast)
	host.add_child(view)
	if GameSession.available:
		_resolve_menu_resume.call_deferred(weakref(view.continue_button), navigation_epoch)

func _menu_action(action: String) -> void:
	GameAudio.ui()
	match action:
		"new": _new_campaign()
		"continue": _continue_campaign()
		"reconnect": _recover_session()
		"history": show_history()
		"sandbox": show_sandbox()
		"codex": show_codex()
		"settings": show_settings()
		"quit": get_tree().quit()

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
	preload("res://scripts/ui/focus_navigation.gd").queue(host)

func _select_setting_from_menu(index: int, selector: OptionButton) -> void:
	var setting_id := str(selector.get_item_metadata(index))
	if setting_id == GameSession.selected_setting_id:
		return
	if not GameSession.select_setting(setting_id):
		for item in selector.item_count:
			if str(selector.get_item_metadata(item)) == GameSession.selected_setting_id: selector.select(item)
		show_error(I18n.text("Finish or recover the current operation before changing the setting."))
		return
	visual_context.setting_id = setting_id
	visual_context.changed.emit()
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
	if GameSession.busy: return
	if GameSession.run.is_empty() and not GameSession.has_pending_command:
		await _probe_engine()
		return
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
		if _has_dialogue_activity(): show_dialogue()
		else: show_activity()

func _has_dialogue_activity() -> bool:
	var run: Dictionary = GameSession.run
	if str(run.get("lifecycle", "Active")).to_lower() != "active": return false
	var id := str(run.get("currentNodeId", ""))
	if id in run.get("map", {}).get("resolvedNodeIds", []): return false
	for node in run.get("map", {}).get("nodes", []):
		if str(node.get("nodeId", "")) == id: return str(node.get("activity", {}).get("type", "")) == "Dialogue"
	return false

func show_dialogue() -> void:
	if current_screen == "dialogue" and host.get_child_count() == 1:
		host.get_child(0).refresh_state(presentation); return
	current_screen = "dialogue"; _clear_host()
	var screen = DialogueScreen.new(); screen.setup(self, presentation); host.add_child(screen)

func show_activity(inspect_map := false) -> void:
	if not inspect_map and _has_dialogue_activity(): show_dialogue(); return
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
	replay_from_history = from_history
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
	if current_screen in ["combat", "dialogue"] and host.get_child_count() > 0:
		var screen = host.get_child(0)
		if screen.has_method("apply_interaction"):
			screen.apply_interaction(null)

func _apply_preferences() -> void:
	visual_context.update_from(Preferences, I18n, AppTheme.profile)
	AppTheme.configure_preferences(Preferences.text_scale, Preferences.high_contrast)
	theme = AppTheme.build(Preferences.high_contrast)
	shell.theme = theme
	shell.apply_visual_profile()
	AppTheme.apply_view_preferences(host)
	if _is_menu_route():
		_menu_paper.color = AppTheme.NIGHT
		for screen in host.get_children():
			if screen.has_method("apply_visual_profile"): screen.apply_visual_profile(AppTheme.profile)
	if is_instance_valid(pause_layer):
		shell.refresh_pause_visuals()

func _on_locale_changed() -> void:
	visual_context.update_from(Preferences, I18n, AppTheme.profile)
	_load_presentation()
	call_deferred("_rebuild_localized_screen")

func _rebuild_localized_screen() -> void:
	match current_screen:
		"dialogue": show_dialogue()
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
	if _entry_profile:
		AppTheme.set_profile(_entry_profile)
		AppTheme.configure_preferences(Preferences.text_scale, Preferences.high_contrast)


func apply_visual_profile(profile: Resource) -> void:
	_gameplay_profile = profile
	AppTheme.set_profile(profile)
	_apply_preferences()
	for screen in host.get_children():
		if screen.has_method("apply_visual_profile"): screen.apply_visual_profile(profile)

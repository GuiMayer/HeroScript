extends Control

const SettingsScreen = preload("res://scripts/settings_screen.gd")
const SandboxScreen = preload("res://scripts/sandbox_screen.gd")
const CodexScreen = preload("res://scripts/codex_screen.gd")
const ActivityScreen = preload("res://scripts/activity_screen.gd")
const CombatScreen = preload("res://scripts/combat_screen.gd")
const TimelineScreen = preload("res://scripts/timeline_screen.gd")

var host: MarginContainer
var toast: Label
var pause_layer: Control
var presentation: Dictionary = {}
var current_screen := "menu"
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
	toast = Label.new()
	toast.visible = false
	toast.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	toast.set_anchors_preset(Control.PRESET_CENTER_TOP)
	toast.position = Vector2(-260, 18)
	toast.size = Vector2(520, 52)
	toast.add_theme_stylebox_override("normal", AppTheme.box(Color("#281e2be8"), 10, AppTheme.EMBER, 1))
	toast.add_theme_font_size_override("font_size", 16)
	add_child(toast)
	GameSession.failed.connect(show_error)
	GameSession.changed.connect(_on_session_changed)
	Preferences.changed.connect(_apply_preferences)
	I18n.locale_changed.connect(_on_locale_changed)
	_show_main_menu()
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
	for child in host.get_children():
		host.remove_child(child)
		child.queue_free()

func _show_main_menu() -> void:
	current_screen = "menu"
	var root := _screen()
	var status_row := HBoxContainer.new()
	status_row.alignment = BoxContainer.ALIGNMENT_END
	var status := Label.new()
	status.text = I18n.text("●  ENGINE ONLINE") if HeroAPI.available else I18n.text("●  AGUARDANDO ENGINE")
	status.add_theme_color_override("font_color", AppTheme.TEAL if HeroAPI.available else AppTheme.GOLD)
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
	var eyebrow := AppTheme.muted(I18n.text("UMA DEMONSTRAÇÃO DATA-DRIVEN"), 14)
	eyebrow.add_theme_color_override("font_color", AppTheme.EMBER)
	card_content.add_child(eyebrow)
	card_content.add_child(AppTheme.title("EMBER ARCHIVE", 52, AppTheme.INK))
	card_content.add_child(AppTheme.muted(
		I18n.text("Uma pequena ascensão regida pela HeroScript. Cartas, inimigos, recursos, mapa e fluxo vivem em JSON; a Godot apresenta cada decisão."), 17))
	var line := HSeparator.new()
	card_content.add_child(line)
	card_content.add_child(_button(I18n.text("NOVA JORNADA"), _new_campaign, 420))
	var continue_button := _button(I18n.text("CONTINUAR"), _continue_campaign, 420)
	continue_button.disabled = Preferences.last_run_id.is_empty()
	card_content.add_child(continue_button)
	card_content.add_child(_button(I18n.text("LABORATÓRIO DE REGRAS"), show_sandbox, 420))
	card_content.add_child(_button(I18n.text("CÓDICE DE CONTEÚDO"), show_codex, 420))
	card_content.add_child(_button(I18n.text("CONFIGURAÇÕES"), show_settings, 420))
	card_content.add_child(_button(I18n.text("SAIR"), get_tree().quit, 420))
	center.add_child(AppTheme.panel(card_content, Color("#171929e8")))
	var bottom := AppTheme.muted(I18n.text("HeroScript decide  •  REST transporta  •  Godot apresenta"), 13)
	bottom.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	root.add_child(bottom)

func _new_campaign() -> void:
	show_toast(I18n.text("Preparando uma run imutável…"))
	var ok := await GameSession.start_campaign(Preferences.next_campaign_seed())
	if ok:
		open_game()

func _continue_campaign() -> void:
	show_toast(I18n.text("Reconstruindo a run pelo journal…"))
	if await GameSession.continue_run():
		open_game()

func open_game() -> void:
	if GameSession.run.is_empty():
		_show_main_menu()
		return
	if not GameSession.combat.is_empty() and str(GameSession.combat.get("status", "ACTIVE")) == "ACTIVE":
		show_combat()
	else:
		show_activity()

func show_activity() -> void:
	current_screen = "activity"
	_clear_host()
	var screen = ActivityScreen.new()
	screen.setup(self, presentation)
	host.add_child(screen)

func show_combat() -> void:
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
		return
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
	content.add_child(AppTheme.title(I18n.text("PAUSA"), 42, AppTheme.GOLD))
	content.add_child(AppTheme.muted(I18n.text("A engine já calculou o estado. A apresentação pode aguardar sem alterar a run.")))
	content.add_child(_button(I18n.text("RETOMAR"), toggle_pause, 360))
	content.add_child(_button(I18n.text("CONFIGURAÇÕES"), _pause_settings, 360))
	content.add_child(_button(I18n.text("MENU PRINCIPAL"), _pause_menu, 360))
	center.add_child(AppTheme.panel(content))

func _pause_settings() -> void:
	toggle_pause()
	show_settings(true)

func _pause_menu() -> void:
	toggle_pause()
	_show_main_menu()

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
	pass

func _apply_preferences() -> void:
	theme = AppTheme.build(Preferences.high_contrast)

func _on_locale_changed() -> void:
	_load_presentation()
	call_deferred("_rebuild_localized_screen")

func _rebuild_localized_screen() -> void:
	match current_screen:
		"settings": show_settings(settings_return_to_gameplay)
		"combat": show_combat()
		"activity": show_activity()
		"menu": _show_main_menu()

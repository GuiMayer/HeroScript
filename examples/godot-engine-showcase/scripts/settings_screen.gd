extends VBoxContainer

var router
var close_action: Callable
var capture_action := ""
var capture_button: Button

func setup(owner, on_close: Callable) -> void:
	router = owner
	close_action = on_close
	add_theme_constant_override("separation", 18)
	var header := HBoxContainer.new()
	header.add_child(AppTheme.title("CONFIGURAÇÕES", 34))
	var push := Control.new()
	push.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	header.add_child(push)
	header.add_child(_button("VOLTAR", _close, 130))
	add_child(header)
	var columns := HBoxContainer.new()
	columns.size_flags_vertical = Control.SIZE_EXPAND_FILL
	columns.add_theme_constant_override("separation", 20)
	add_child(columns)
	columns.add_child(_audio_panel())
	columns.add_child(_control_panel())

func _audio_panel() -> Control:
	var content := VBoxContainer.new()
	content.custom_minimum_size = Vector2(440, 0)
	content.add_theme_constant_override("separation", 12)
	content.add_child(AppTheme.title("Áudio e vídeo", 24, AppTheme.GOLD))
	content.add_child(_slider("Volume geral", Preferences.master_volume, func(v): Preferences.master_volume = v))
	content.add_child(_slider("Música", Preferences.music_volume, func(v): Preferences.music_volume = v))
	content.add_child(_slider("Efeitos", Preferences.sfx_volume, func(v): Preferences.sfx_volume = v))
	content.add_child(_slider("Velocidade das animações", Preferences.animation_speed / 2.0,
		func(v): Preferences.animation_speed = maxf(.25, v * 2.0)))
	var fullscreen := CheckButton.new()
	fullscreen.text = "Tela cheia"
	fullscreen.button_pressed = Preferences.fullscreen
	fullscreen.toggled.connect(func(value): Preferences.fullscreen = value; Preferences.save())
	content.add_child(fullscreen)
	var contrast := CheckButton.new()
	contrast.text = "Contraste reforçado"
	contrast.button_pressed = Preferences.high_contrast
	contrast.toggled.connect(func(value): Preferences.high_contrast = value; Preferences.save())
	content.add_child(contrast)
	content.add_child(AppTheme.muted("As preferências ficam em user:// e nunca entram no estado determinístico do jogo."))
	return AppTheme.panel(content)

func _control_panel() -> Control:
	var content := VBoxContainer.new()
	content.custom_minimum_size = Vector2(520, 0)
	content.add_theme_constant_override("separation", 12)
	content.add_child(AppTheme.title("Controles e conexão", 24, AppTheme.TEAL))
	var labels := {
		"pause_game": "Pausar",
		"end_turn": "Encerrar turno",
		"open_timeline": "Abrir timeline",
		"confirm_action": "Próximo frame"
	}
	for action in Preferences.ACTIONS:
		var row := HBoxContainer.new()
		var name := Label.new()
		name.text = labels[action]
		name.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		row.add_child(name)
		var button := AppTheme.button(Preferences.action_label(action), 150)
		button.pressed.connect(func(): GameAudio.ui(); _capture(action, button))
		row.add_child(button)
		content.add_child(row)
	var api_label := AppTheme.muted("Endereço da HeroScript")
	content.add_child(api_label)
	var api := LineEdit.new()
	api.text = Preferences.api_url
	api.text_submitted.connect(func(value): Preferences.api_url = value; Preferences.save())
	api.focus_exited.connect(func(): Preferences.api_url = api.text; Preferences.save())
	content.add_child(api)
	content.add_child(AppTheme.muted("A Godot usa somente a API REST. Nenhuma regra de combate é executada neste projeto."))
	return AppTheme.panel(content)

func _slider(label_text: String, value: float, setter: Callable) -> Control:
	var row := VBoxContainer.new()
	var label := Label.new()
	label.text = label_text
	row.add_child(label)
	var slider := HSlider.new()
	slider.min_value = 0.0
	slider.max_value = 1.0
	slider.step = .01
	slider.value = value
	slider.value_changed.connect(func(v): setter.call(float(v)); Preferences.save())
	row.add_child(slider)
	return row

func _capture(action: String, button: Button) -> void:
	capture_action = action
	capture_button = button
	button.text = "PRESSIONE UMA TECLA"
	set_process_unhandled_key_input(true)

func _unhandled_key_input(event: InputEvent) -> void:
	if capture_action.is_empty() or not event.pressed or event.echo:
		return
	Preferences.remap(capture_action, event.physical_keycode)
	capture_button.text = Preferences.action_label(capture_action)
	capture_action = ""
	set_process_unhandled_key_input(false)
	get_viewport().set_input_as_handled()

func _close() -> void:
	Preferences.save()
	close_action.call()

func _button(text: String, action: Callable, width := 0) -> Button:
	var value := AppTheme.button(text, width)
	value.pressed.connect(func(): GameAudio.ui(); action.call())
	return value
